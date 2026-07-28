using Microsoft.Win32;
using TrayKeeper.Logging;

namespace TrayKeeper.Services;

/// <summary>
/// Current-user autostart via the standard Run key. A scheduled task would be needed only to
/// gain elevation or to set a delay, neither of which applies here, so the Run key is used.
/// </summary>
public sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TrayKeeper";

    private readonly ILog _log;

    public StartupManager(ILog log) => _log = log;

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                _log.Warn($"Could not read the startup registration. {ex.Message}");
                return false;
            }
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrEmpty(path))
                {
                    _log.Warn("Could not determine the executable path; startup registration skipped.");
                    return false;
                }

                key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
                _log.Info($"Registered for startup: {path}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                _log.Info("Removed the startup registration.");
            }

            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _log.Error("Could not change the startup registration.", ex);
            return false;
        }
    }
}
