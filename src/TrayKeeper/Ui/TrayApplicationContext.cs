using System.Drawing;
using System.Windows.Forms;
using AlexLV.WindowsUi;
using TrayKeeper.Logging;
using TrayKeeper.Services;
using TrayKeeper.Settings;

namespace TrayKeeper.Ui;

/// <summary>
/// Owns the notification-area icon and its menu. There is no main window; work happens on a menu
/// command, a double-click, a second launch, or — when auto-refresh is on — a timer tick. The tray
/// stays WinForms (NotifyIcon and its message loop); every window is WPF with the shared kit.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    /// <summary>Tray Keeper's accent, the Windows blue of its icon.</summary>
    public static readonly AccentColors Accent = AccentColors.FromHex("#0067B8", "#6CB8F6", onDark: "#0B1A26");

    private readonly RefreshService _refreshService;
    private readonly ResultStore _resultStore;
    private readonly ExplorerRestarter _explorerRestarter;
    private readonly StartupManager _startupManager;
    private readonly SettingsStore _settingsStore;
    private readonly ILog _log;
    private readonly Icon _icon;

    private readonly NotifyIcon _notifyIcon;
    private readonly TrayMenuStyler _menuStyler;
    private readonly System.Windows.Forms.Timer _autoRefreshTimer;

    private AppSettings _settings;
    private ResultsWindow? _resultsWindow;
    private SettingsWindow? _settingsWindow;
    private readonly ShutdownListenerWindow _shutdownListener;
    private readonly UpdateService _updates = new();
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly System.Windows.Forms.Timer _updateTimer = new();
    private DateTime _nextUpdateCheckUtc = DateTime.MaxValue;
    private bool _checkingUpdate;
    private Version? _lastOfferedUpdate;

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

        ThemeManager.Initialize(AppInfo.DisplayName, Accent, _settings.Theme);
        ThemeManager.WindowIcon = WinFormsHosting.ToImageSource(_icon);

        // A WinForms timer ticks on the UI thread, so a refresh needs no marshalling and cannot
        // overlap the message pump's other work.
        _autoRefreshTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)_settings.AutoRefreshInterval.TotalMilliseconds,
        };
        _autoRefreshTimer.Tick += (_, _) => OnAutoRefreshTick();
        _autoRefreshTimer.Enabled = _settings.AutoRefreshEnabled;

        var refreshItem = new ToolStripMenuItem("&Refresh tray icons", null, (_, _) => RunRefresh(RefreshTrigger.Manual));
        var showResultsItem = new ToolStripMenuItem("&Show last results", null, (_, _) => ShowResults());
        var settingsItem = new ToolStripMenuItem("Se&ttings…", null, (_, _) => ShowSettings());
        var exitItem = new ToolStripMenuItem("E&xit", null, (_, _) => ExitApplication());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            refreshItem,
            showResultsItem,
            new ToolStripSeparator(),
            settingsItem,
            exitItem,
        });

        _menuStyler = new TrayMenuStyler(menu);
        _menuStyler.SetGlyph(refreshItem, "");
        _menuStyler.SetGlyph(showResultsItem, "");
        _menuStyler.SetGlyph(settingsItem, "");
        _menuStyler.SetGlyph(exitItem, "");
        // Double-clicking the icon runs this item, so it is drawn as the default.
        refreshItem.Font = new Font(menu.Font, System.Drawing.FontStyle.Bold);

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

        _shutdownListener = new ShutdownListenerWindow(Shutdown);
        _shutdownListener.Show();

        _updateTimer.Interval = (int)TimeSpan.FromMinutes(15).TotalMilliseconds;
        _updateTimer.Tick += async (_, _) =>
        {
            if (DateTime.UtcNow >= _nextUpdateCheckUtc)
            {
                await CheckForUpdatesAsync(manual: false);
                _nextUpdateCheckUtc = UpdateSchedule.NextCheck(DateTime.UtcNow, _settings.CheckFrequency);
            }
        };

        ApplyUpdateSchedule();
        if (_settings.CheckFrequency != UpdateCheckFrequency.Never)
        {
            _ = CheckForUpdatesAsync(manual: false);
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
        if (_busy || _resultsWindow is { IsBusy: true }) return;

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

        if (_resultsWindow is { IsVisible: true }) _resultsWindow.LoadReport();
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
        if (_resultsWindow is not null)
        {
            _resultsWindow.LoadReport();
            WinFormsHosting.ShowModeless(_resultsWindow);
            return;
        }

        _resultsWindow = new ResultsWindow(_refreshService, _resultStore, _explorerRestarter);
        _resultsWindow.Closed += (_, _) => _resultsWindow = null;
        WinFormsHosting.ShowModeless(_resultsWindow);
    }

    private void ShowSettings(string? initialTab = null)
    {
        if (_settingsWindow is not null)
        {
            if (initialTab != null) _settingsWindow.SelectTab(initialTab);
            WinFormsHosting.ShowModeless(_settingsWindow);
            return;
        }

        // A copy, so Cancel really cancels.
        var draft = _settings.Clone();

        _settingsWindow = new SettingsWindow(
            draft,
            saved =>
            {
                // Keep background state written since the dialog opened.
                saved.LastUpdateCheckUtc = _settings.LastUpdateCheckUtc;
                if (!_settingsStore.Save(saved, out var error)) return error;

                _settings = saved;
                ApplyUpdateSchedule();
                _autoRefreshTimer.Interval = (int)_settings.AutoRefreshInterval.TotalMilliseconds;
                _autoRefreshTimer.Enabled = _settings.AutoRefreshEnabled;
                _notifyIcon.Text = TooltipText();
                ThemeManager.Apply(_settings.Theme);
                return null;
            },
            () => CheckForUpdatesAsync(manual: true),
            _startupManager);
        if (initialTab != null) _settingsWindow.SelectTab(initialTab);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        WinFormsHosting.ShowModeless(_settingsWindow);
    }

    /// <summary>"minute" or "N minutes", from the clamped interval the timer actually uses.</summary>
    private string DescribeInterval()
    {
        var minutes = (int)_settings.AutoRefreshInterval.TotalMinutes;
        return minutes == 1 ? "minute" : $"{minutes} minutes";
    }

    private string TooltipText()
    {
        var text = $"{AppInfo.DisplayName} v{Changelog.MarketingVersion}";
        if (!_settings.AutoRefreshEnabled) return text;

        var withInterval = $"{text} — auto-refresh every {DescribeInterval()}";

        // NotifyIcon.Text rejects anything longer than 63 characters.
        return withInterval.Length <= 63 ? withInterval : text;
    }

    public void Shutdown()
    {
        _log.Info("Shutdown requested by listener.");
        ExitApplication();
    }

    private void ApplyUpdateSchedule()
    {
        _settings.CheckFrequency = UpdateSchedule.Normalize(_settings.CheckFrequency);
        _updateTimer.Stop();
        _nextUpdateCheckUtc = UpdateSchedule.NextCheck(DateTime.UtcNow, _settings.CheckFrequency);
        if (_settings.CheckFrequency != UpdateCheckFrequency.Never) _updateTimer.Start();
    }

    /// <summary>Checks GitHub. Manual checks report every outcome; automatic ones only offer an update.</summary>
    private async Task<UpdateStatus> CheckForUpdatesAsync(bool manual)
    {
        var current = Changelog.MarketingVersion;
        if (_checkingUpdate) return new UpdateStatus(UpdateState.Checking, "Checking for updates…", "A check is already running");
        if (!manual && _settings.CheckFrequency == UpdateCheckFrequency.Never)
        {
            return new UpdateStatus(UpdateState.NotChecked, "Automatic checks are off", $"Version {current}");
        }

        _checkingUpdate = true;
        try
        {
            var update = await _updates.CheckAsync(Version.Parse(current), _updateCancellation.Token);
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settingsStore.Save(_settings);
            var checkedAt = $"Last checked {DateTime.Now:yyyy-MM-dd HH:mm}";

            if (update is null)
            {
                if (manual) MessageDialog.Show($"You are running version {current}.\n\nNo newer installer version is available.", MessageKind.Info);
                return new UpdateStatus(UpdateState.UpToDate, $"{AppInfo.DisplayName} is up to date", $"Version {current} · {checkedAt}");
            }

            var available = new UpdateStatus(UpdateState.Available, $"Version {update.Version.ToString(3)} is available",
                $"You have {current} · {checkedAt}");
            if (!manual && _lastOfferedUpdate == update.Version) return available;
            _lastOfferedUpdate = update.Version;
            ShowUpdateDialog(update);
            return available;
        }
        catch (OperationCanceledException)
        {
            return new UpdateStatus(UpdateState.Failed, "Update check stopped", $"Version {current}");
        }
        catch (Exception ex)
        {
            _log.Error("Update check failed.", ex);
            if (manual) MessageDialog.Show($"Could not check for updates. Please try again later.\n\n{ex.Message}", MessageKind.Warning);
            return new UpdateStatus(UpdateState.Failed, "Could not check for updates", $"Version {current} · {ex.Message}");
        }
        finally { _checkingUpdate = false; }
    }

    private void ShowUpdateDialog(AppUpdate update)
    {
        _busy = true;
        try
        {
            new UpdateWindow(update, _updates, _updateCancellation.Token).ShowDialog();
        }
        finally
        {
            _busy = false;
        }
    }

    private void ExitApplication()
    {
        _log.Info("Exiting.");
        _autoRefreshTimer.Stop();
        _notifyIcon.Visible = false;
        _settingsWindow?.Close();
        _resultsWindow?.Close();
        ExitThread();
    }

    private bool _disposed;

    protected override void Dispose(bool disposing)
    {
        // A crash path can dispose twice (ExitThread, then Program.Main); cancelling a disposed source throws.
        if (_disposed) return;
        _disposed = true;
        if (disposing)
        {
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _updateCancellation.Cancel();
            _updateCancellation.Dispose();
            _updates.Dispose();
            _shutdownListener.Dispose();
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Dispose();
            _notifyIcon.Visible = false;
            _menuStyler.Dispose();
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }
}
