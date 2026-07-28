namespace TrayKeeper.Services;

/// <summary>All TrayKeeper state lives under %LocalAppData%\TrayKeeper.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayKeeper");

    public static string LogDirectory => Path.Combine(Root, "Logs");
    public static string BackupDirectory => Path.Combine(Root, "Backups");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string LastResultFile => Path.Combine(Root, "last-result.json");
    public static string LastChangesFile => Path.Combine(Root, "last-changes.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }
}
