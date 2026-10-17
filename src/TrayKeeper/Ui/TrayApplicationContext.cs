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
    private readonly System.Windows.Forms.Timer _autoRefreshTimer;

    private AppSettings _settings;
    private ResultsForm? _resultsForm;
    private SettingsForm? _settingsForm;
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

        var refreshItem = new ToolStripMenuItem("&Refresh tray icons", null, (_, _) => RunRefresh(RefreshTrigger.Manual));
        var showResultsItem = new ToolStripMenuItem("&Show last results", null, (_, _) => ShowResults());

        // A WinForms timer ticks on the UI thread, so a refresh needs no marshalling and cannot
        // overlap the message pump's other work.
        _autoRefreshTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)_settings.AutoRefreshInterval.TotalMilliseconds,
        };
        _autoRefreshTimer.Tick += (_, _) => OnAutoRefreshTick();
        _autoRefreshTimer.Enabled = _settings.AutoRefreshEnabled;

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

    private void ShowSettings(string? initialTab = null)
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Show();
            _settingsForm.WindowState = FormWindowState.Normal;
            if (initialTab != null) _settingsForm.SelectTab(initialTab);
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(
            _icon,
            _settings,
            settings =>
            {
                _settings = settings;
                _settingsStore.Save(_settings);
                ApplyUpdateSchedule();
                _autoRefreshTimer.Interval = (int)_settings.AutoRefreshInterval.TotalMilliseconds;
                _autoRefreshTimer.Enabled = _settings.AutoRefreshEnabled;
                _notifyIcon.Text = TooltipText();
            },
            () => CheckForUpdatesAsync(manual: true),
            _startupManager);
        if (initialTab != null) _settingsForm.SelectTab(initialTab);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void ShowAbout() => ShowSettings("About");

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

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checkingUpdate || (!manual && _settings.CheckFrequency == UpdateCheckFrequency.Never)) return;
        _checkingUpdate = true;
        try
        {
            var update = await _updates.CheckAsync(Version.Parse(Changelog.MarketingVersion), _updateCancellation.Token);
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settingsStore.Save(_settings);

            if (update is null)
            {
                if (manual) MessageBox.Show($"You are running version {Changelog.MarketingVersion}.\n\nNo newer installer version is available.", AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!manual && _lastOfferedUpdate == update.Version) return;
            _lastOfferedUpdate = update.Version;
            ShowUpdateDialog(update);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Error("Update check failed.", ex);
            if (manual) MessageBox.Show($"Could not check for updates. Please try again later.\n\n{ex.Message}", AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _checkingUpdate = false; }
    }

    private void ShowUpdateDialog(AppUpdate update)
    {
        using var dialog = new Form
        {
            Text = $"Update {AppInfo.DisplayName} (v{Changelog.MarketingVersion})",
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = true,
            ClientSize = new Size(470, 200),
            Icon = _icon,
        };
        var message = new Label
        {
            Text = $"Version {update.Version} is available. You have {Changelog.MarketingVersion}.\n\nUpdate will download and open the installer. Setup will close the app and restart it after updating.",
            Location = new Point(18, 18),
            Size = new Size(434, 115),
        };
        var install = new Button { Text = "Update", Location = new Point(268, 152), Size = new Size(88, 30) };
        var later = new Button { Text = "Later", Location = new Point(364, 152), Size = new Size(88, 30), DialogResult = DialogResult.Cancel };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateCancellation.Token);
        cancellation.CancelAfter(TimeSpan.FromMinutes(10));
        dialog.FormClosing += (_, _) => cancellation.Cancel();
        install.Click += async (_, _) =>
        {
            install.Enabled = false;
            message.Text = "Downloading and verifying the installer…";
            try
            {
                var cache = Path.Combine(AppPaths.Root, "Updates");
                var installer = await _updates.DownloadAsync(update, cache, cancellation.Token);
                if (dialog.IsDisposed) return;
                _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = installer,
                    UseShellExecute = true,
                }) ?? throw new InvalidOperationException("Windows could not open the installer.");
                dialog.Close();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (dialog.IsDisposed) return;
                message.Text = $"Update failed. Please try again.\n\n{ex.Message}";
                install.Enabled = true;
            }
        };
        dialog.Controls.AddRange(new Control[] { message, install, later });
        dialog.AcceptButton = install;
        dialog.CancelButton = later;
        dialog.ShowDialog();
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
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _updateCancellation.Cancel();
            _updateCancellation.Dispose();
            _updates.Dispose();
            _shutdownListener.Dispose();
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _resultsForm?.Dispose();
            _settingsForm?.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }
}
