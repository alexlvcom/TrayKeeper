using TrayKeeper.Logging;
using TrayKeeper.Services;
using TrayKeeper.Settings;

namespace TrayKeeper.Ui;

/// <summary>
/// Owns the notification-area icon and its menu. There is no main window and no timer: the app
/// only does work in response to a menu command, a double-click, or a second launch.
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

    private AppSettings _settings;
    private ResultsForm? _resultsForm;
    private AboutForm? _aboutForm;

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

        var refreshItem = new ToolStripMenuItem("&Refresh tray icons", null, (_, _) => RunRefresh(showBalloon: true));
        var showResultsItem = new ToolStripMenuItem("&Show last results", null, (_, _) => ShowResults());

        _startWithWindowsItem = new ToolStripMenuItem("Start with &Windows", null, (_, _) => ToggleStartWithWindows())
        {
            CheckOnClick = false,
            Checked = _startupManager.IsEnabled,
        };

        _refreshOnStartupItem = new ToolStripMenuItem("Run refresh when TrayKeeper &starts", null,
            (_, _) => ToggleRefreshOnStartup())
        {
            CheckOnClick = false,
            Checked = _settings.RunRefreshOnStartup,
        };

        var aboutItem = new ToolStripMenuItem("&About TrayKeeper", null, (_, _) => ShowAbout());
        var exitItem = new ToolStripMenuItem("E&xit", null, (_, _) => ExitApplication());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            refreshItem,
            showResultsItem,
            new ToolStripSeparator(),
            _startWithWindowsItem,
            _refreshOnStartupItem,
            new ToolStripSeparator(),
            aboutItem,
            exitItem,
        });

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = $"TrayKeeper v{Changelog.MarketingVersion}",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => RunRefresh(showBalloon: true);
        _notifyIcon.BalloonTipClicked += (_, _) => ShowResults();

        if (_settings.RunRefreshOnStartup)
        {
            _log.Info("Running the startup refresh.");
            RunRefresh(showBalloon: true);
        }
    }

    /// <summary>Entry point for the single-instance listener; marshals onto the UI thread.</summary>
    public void RefreshFromOtherInstance()
    {
        if (_notifyIcon.ContextMenuStrip is { } menu && menu.InvokeRequired)
        {
            menu.BeginInvoke(() => RunRefresh(showBalloon: true));
            return;
        }

        RunRefresh(showBalloon: true);
    }

    private void RunRefresh(bool showBalloon)
    {
        RefreshResult result;

        try
        {
            result = _refreshService.Refresh();
        }
        catch (Exception ex)
        {
            _log.Error("Unhandled failure during refresh.", ex);
            ShowBalloon("TrayKeeper", "Refresh failed. See the log for details.", ToolTipIcon.Error);
            return;
        }

        if (showBalloon && _settings.ShowBalloonNotifications)
        {
            var iconKind = result.Outcome switch
            {
                RefreshOutcome.Failed => ToolTipIcon.Error,
                RefreshOutcome.ChangesApplied => ToolTipIcon.Info,
                _ => ToolTipIcon.None,
            };

            ShowBalloon("TrayKeeper", result.Summary, iconKind);
        }

        if (_resultsForm is { IsDisposed: false, Visible: true }) _resultsForm.LoadReport();
    }

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
        }
        else
        {
            _startWithWindowsItem.Checked = _startupManager.IsEnabled;
            MessageBox.Show("Could not change the startup setting. See the log for details.", "TrayKeeper",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ToggleRefreshOnStartup()
    {
        _settings.RunRefreshOnStartup = !_settings.RunRefreshOnStartup;
        _refreshOnStartupItem.Checked = _settings.RunRefreshOnStartup;
        _settingsStore.Save(_settings);
    }

    private void ExitApplication()
    {
        _log.Info("Exiting.");
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
