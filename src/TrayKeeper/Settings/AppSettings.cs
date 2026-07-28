using System.Text.Json;
using TrayKeeper.Logging;
using TrayKeeper.Services;

namespace TrayKeeper.Settings;

public sealed class AppSettings
{
    /// <summary>Run one refresh automatically when TrayKeeper starts.</summary>
    public bool RunRefreshOnStartup { get; set; }

    /// <summary>Show a balloon tip after each refresh.</summary>
    public bool ShowBalloonNotifications { get; set; } = true;

    /// <summary>Leave entries alone when their executable is confirmed missing.</summary>
    public bool SkipMissingExecutables { get; set; } = true;
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
