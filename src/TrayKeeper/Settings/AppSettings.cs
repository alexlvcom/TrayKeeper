using System.Text.Json;
using TrayKeeper.Logging;
using TrayKeeper.Services;

namespace TrayKeeper.Settings;

public sealed class AppSettings
{
    public const int DefaultAutoRefreshIntervalMinutes = 1;
    public const int MinAutoRefreshIntervalMinutes = 1;
    public const int MaxAutoRefreshIntervalMinutes = 24 * 60;

    /// <summary>Run one refresh automatically when TrayKeeper starts.</summary>
    public bool RunRefreshOnStartup { get; set; }

    /// <summary>Re-scan on a timer while TrayKeeper runs, so hidden icons return without a manual refresh.</summary>
    public bool AutoRefreshEnabled { get; set; } = true;

    /// <summary>Minutes between automatic refreshes. Read it through <see cref="AutoRefreshInterval"/>.</summary>
    public int AutoRefreshIntervalMinutes { get; set; } = DefaultAutoRefreshIntervalMinutes;

    /// <summary>Show a balloon tip after each refresh.</summary>
    public bool ShowBalloonNotifications { get; set; } = true;

    /// <summary>Leave entries alone when their executable is confirmed missing.</summary>
    public bool SkipMissingExecutables { get; set; } = true;

    /// <summary>How often Tray Keeper checks GitHub for releases.</summary>
    public UpdateCheckFrequency CheckFrequency { get; set; } = UpdateCheckFrequency.Daily;

    /// <summary>Last time an update check was performed in UTC.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>A release version the user chose to skip for background notifications.</summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>
    /// The interval the timer actually uses. settings.json is hand-editable, so the stored value is
    /// clamped — zero or a negative number would throw when assigned to a timer.
    /// </summary>
    public TimeSpan AutoRefreshInterval => TimeSpan.FromMinutes(Math.Clamp(
        AutoRefreshIntervalMinutes, MinAutoRefreshIntervalMinutes, MaxAutoRefreshIntervalMinutes));
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILog _log;

    public SettingsStore(ILog log, string? path = null)
    {
        _log = log;
        _path = string.IsNullOrWhiteSpace(path) ? AppPaths.SettingsFile : path;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Warn($"Could not read settings; using defaults. {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            _log.Warn($"Could not save settings. {ex.Message}");
        }
    }
}
