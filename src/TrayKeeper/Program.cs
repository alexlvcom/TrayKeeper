using TrayKeeper.Logging;
using TrayKeeper.Registry;
using TrayKeeper.Services;
using TrayKeeper.Settings;
using TrayKeeper.Ui;

namespace TrayKeeper;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        using var instance = new SingleInstance();

        if (!instance.IsFirstInstance)
        {
            // Another TrayKeeper already owns the tray icon. Ask it to refresh, then exit quietly
            // rather than adding a second icon.
            instance.SignalExistingInstance();
            return 0;
        }

        AppPaths.EnsureCreated();

        using var log = new FileLog(AppPaths.LogDirectory);
        log.Info($"{AppInfo.DisplayName} starting (pid {Environment.ProcessId}).");

        ApplicationConfiguration.Initialize();

        var settingsStore = new SettingsStore(log);
        var knownFolders = new ShellKnownFolderResolver();
        var fileIdentities = new FileIdentityProvider();
        var registry = new NotifyIconRegistry(knownFolders, fileIdentities, log);
        var resultStore = new ResultStore(log);
        var backupService = new BackupService(registry, log);
        var refreshService = new RefreshService(registry, backupService, resultStore, settingsStore, log);
        var startupManager = new StartupManager(log);
        var explorerRestarter = new ExplorerRestarter(log);

        Application.ThreadException += (_, e) => log.Error("Unhandled UI exception.", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Error("Unhandled exception.", e.ExceptionObject as Exception);

        using var context = new TrayApplicationContext(
            refreshService, resultStore, explorerRestarter, startupManager, settingsStore, log);

        instance.StartListening(context.RefreshFromOtherInstance);

        try
        {
            Application.Run(context);
        }
        catch (Exception ex)
        {
            log.Error($"{AppInfo.DisplayName} terminated unexpectedly.", ex);
            return 1;
        }

        log.Info($"{AppInfo.DisplayName} stopped.");
        return 0;
    }
}
