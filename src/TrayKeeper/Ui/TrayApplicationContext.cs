using TrayKeeper.Logging;
using TrayKeeper.Services;
using TrayKeeper.Settings;

namespace TrayKeeper.Ui;

/// <summary>
/// Owns the notification-area icon and its menu. There is no main window; work happens on a menu
/// command, a double-click, a second launch, or — when auto-refresh is on — a timer tick.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly RefreshService _refreshService;
    private readonly ResultStore _resultStore;
    private readonly ExplorerRestarter _explorerRestarter;
    private readonly StartupManager _startupManager;
    private readonly SettingsStore _settingsStore;
    private readonly ILog _log;
    private readonly Icon _icon;

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startWithWindowsItem;
    private readonly ToolStripMenuItem _refreshOnStartupItem;
    private readonly ToolStripMenuItem _autoRefreshItem;
    private readonly System.Windows.Forms.Timer _autoRefreshTimer;

    private AppSettings _settings;
    private ResultsForm? _resultsForm;
    private AboutForm? _aboutForm;

    /// <summary>True while a refresh runs or a dialog this class opened is on screen.</summary>
    private bool _busy;

    public TrayApplicationContext(
        RefreshService refreshService,
        ResultStore resultStore,
        ExplorerRestarter explorerRestarter,
        StartupManager startupManager,
        SettingsStore settingsStore,
        ILog log)
    {
        _refreshService = refreshService;
        _resultStore = resultStore;
        _explorerRestarter = explorerRestarter;
        _startupManager = startupManager;
        _settingsStore = settingsStore;
        _log = log;
        _settings = settingsStore.Load();
        _icon = AppIcon.Load();

        var refreshItem = new ToolStripMenuItem("&Refresh tray icons", null, (_, _) => RunRefresh(RefreshTrigger.Manual));
        var showResultsItem = new ToolStripMenuItem("&Show last results", null, (_, _) => ShowResults());

        _startWithWindowsItem = new ToolStripMenuItem("Start with &Windows", null, (_, _) => ToggleStartWithWindows())
        {
            CheckOnClick = false,
            Checked = _startupManager.IsEnabled,
        };

        _refreshOnStartupItem = new ToolStripMenuItem($"Run refresh when {AppInfo.DisplayName} &starts", null,
            (_, _) => ToggleRefreshOnStartup())
        {
            CheckOnClick = false,
            Checked = _settings.RunRefreshOnStartup,
        };

        _autoRefreshItem = new ToolStripMenuItem(AutoRefreshItemText(), null, (_, _) => ToggleAutoRefresh())
        {
            CheckOnClick = false,
            Checked = _settings.AutoRefreshEnabled,
        };

        // A WinForms timer ticks on the UI thread, so a refresh needs no marshalling and cannot
        // overlap the message pump's other work.
        _autoRefreshTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)_settings.AutoRefreshInterval.TotalMilliseconds,
        };
        _autoRefreshTimer.Tick += (_, _) => OnAutoRefreshTick();
        _autoRefreshTimer.Enabled = _settings.AutoRefreshEnabled;

        var aboutItem = new ToolStripMenuItem($"&About {AppInfo.DisplayName}", null, (_, _) => ShowAbout());
        var exitItem = new ToolStripMenuItem("E&xit", null, (_, _) => ExitApplication());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            refreshItem,
            showResultsItem,
            new ToolStripSeparator(),
            _startWithWindowsItem,
            _refreshOnStartupItem,
            _autoRefreshItem,
            new ToolStripSeparator(),
            aboutItem,
            exitItem,
        });

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = TooltipText(),
            Visible = true,
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => RunRefresh(RefreshTrigger.Manual);
        _notifyIcon.BalloonTipClicked += (_, _) => ShowResults();

        _log.Info(_settings.AutoRefreshEnabled
            ? $"Auto-refresh is on, every {DescribeInterval()}."
            : "Auto-refresh is off.");

        if (_settings.RunRefreshOnStartup)
        {
            _log.Info("Running the startup refresh.");
            RunRefresh(RefreshTrigger.Manual);
        }
    }

    /// <summary>Entry point for the single-instance listener; marshals onto the UI thread.</summary>
    public void RefreshFromOtherInstance()
    {
        if (_notifyIcon.ContextMenuStrip is { } menu && menu.InvokeRequired)
        {
            menu.BeginInvoke(() => RunRefresh(RefreshTrigger.Manual));
            return;
        }

        RunRefresh(RefreshTrigger.Manual);
    }

    /// <summary>
    /// Skips the tick when a refresh is already running or a dialog is open. Modal dialogs pump
    /// messages, so without this a tick could fire while the user is confirming a restore.
    /// </summary>
    private void OnAutoRefreshTick()
    {
        if (_busy || _resultsForm is { IsDisposed: false, IsBusy: true }) return;

        RunRefresh(RefreshTrigger.Automatic);
    }

    private void RunRefresh(RefreshTrigger trigger)
    {
        RefreshResult result;
        _busy = true;

        try
        {
            result = _refreshService.Refresh(trigger);
        }
        catch (Exception ex)
        {
            _log.Error("Unhandled failure during refresh.", ex);
            if (trigger == RefreshTrigger.Manual)
            {
                ShowBalloon(AppInfo.DisplayName, "Refresh failed. See the log for details.", ToolTipIcon.Error);
            }

            return;
        }
        finally
        {
            _busy = false;
        }

        if (_settings.ShowBalloonNotifications && ShouldShowBalloon(trigger, result))
        {
            var iconKind = result.Outcome switch
            {
                RefreshOutcome.Failed => ToolTipIcon.Error,
                RefreshOutcome.ChangesApplied => ToolTipIcon.Info,
                _ => ToolTipIcon.None,
            };

            ShowBalloon(AppInfo.DisplayName, result.Summary, iconKind);
        }

        if (_resultsForm is { IsDisposed: false, Visible: true }) _resultsForm.LoadReport();
    }

    /// <summary>
    /// A manual refresh always reports back. An automatic one speaks up only when it actually
    /// enabled something — a balloon every minute saying "nothing to do" would be noise.
    /// </summary>
    private static bool ShouldShowBalloon(RefreshTrigger trigger, RefreshResult result) =>
        trigger == RefreshTrigger.Manual || result.Outcome == RefreshOutcome.ChangesApplied;

    private void ShowBalloon(string title, string message, ToolTipIcon iconKind) =>
        _notifyIcon.ShowBalloonTip(5000, title, message, iconKind);

    private void ShowResults()
    {
        if (_resultsForm is { IsDisposed: false })
        {
            _resultsForm.LoadReport();
            _resultsForm.Show();
            _resultsForm.WindowState = FormWindowState.Normal;
            _resultsForm.Activate();
            return;
        }

        _resultsForm = new ResultsForm(_refreshService, _resultStore, _explorerRestarter, _icon);
        _resultsForm.FormClosed += (_, _) => _resultsForm = null;
        _resultsForm.Show();
        _resultsForm.Activate();
    }

    private void ShowAbout()
    {
        if (_aboutForm is { IsDisposed: false })
        {
            _aboutForm.Show();
            _aboutForm.WindowState = FormWindowState.Normal;
            _aboutForm.Activate();
            return;
        }

        _aboutForm = new AboutForm(_icon);
        _aboutForm.FormClosed += (_, _) => _aboutForm = null;
        _aboutForm.Show();
        _aboutForm.Activate();
    }

    private void ToggleStartWithWindows()
    {
        var desired = !_startupManager.IsEnabled;

        if (_startupManager.TrySetEnabled(desired))
        {
            _startWithWindowsItem.Checked = desired;
            return;
        }

        _startWithWindowsItem.Checked = _startupManager.IsEnabled;

        // The dialog pumps messages, so hold off the auto-refresh timer while it is up.
        _busy = true;

        try
        {
            MessageBox.Show("Could not change the startup setting. See the log for details.", AppInfo.DisplayName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ToggleRefreshOnStartup()
    {
        _settings.RunRefreshOnStartup = !_settings.RunRefreshOnStartup;
        _refreshOnStartupItem.Checked = _settings.RunRefreshOnStartup;
        _settingsStore.Save(_settings);
    }

    private void ToggleAutoRefresh()
    {
        _settings.AutoRefreshEnabled = !_settings.AutoRefreshEnabled;
        _autoRefreshItem.Checked = _settings.AutoRefreshEnabled;
        _autoRefreshTimer.Enabled = _settings.AutoRefreshEnabled;
        _notifyIcon.Text = TooltipText();
        _settingsStore.Save(_settings);

        _log.Info(_settings.AutoRefreshEnabled
            ? $"Auto-refresh turned on, every {DescribeInterval()}."
            : "Auto-refresh turned off.");
    }

    /// <summary>"minute" or "N minutes", from the clamped interval the timer actually uses.</summary>
    private string DescribeInterval()
    {
        var minutes = (int)_settings.AutoRefreshInterval.TotalMinutes;
        return minutes == 1 ? "minute" : $"{minutes} minutes";
    }

    private string AutoRefreshItemText() => $"&Auto-refresh every {DescribeInterval()}";

    private string TooltipText()
    {
        var text = $"{AppInfo.DisplayName} v{Changelog.MarketingVersion}";
        if (!_settings.AutoRefreshEnabled) return text;

        var withInterval = $"{text} — auto-refresh every {DescribeInterval()}";

        // NotifyIcon.Text rejects anything longer than 63 characters.
        return withInterval.Length <= 63 ? withInterval : text;
    }

    private void ExitApplication()
    {
        _log.Info("Exiting.");
        _autoRefreshTimer.Stop();
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _resultsForm?.Dispose();
            _aboutForm?.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }
}
