using System.Diagnostics;
using System.Text.Json;
using TrayKeeper.Logging;
using TrayKeeper.Registry;

namespace TrayKeeper.Services;

/// <summary>
/// Captures the current-user notification-area settings before any write. Two artefacts are
/// produced: a JSON snapshot of the values TrayKeeper understands, and a best-effort
/// <c>reg.exe export</c> of the whole key that can be re-imported by hand.
/// </summary>
public sealed class BackupService
{
    private const int RetainedBackups = 10;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly INotifyIconRegistry _registry;
    private readonly ILog _log;
    private readonly string _directory;

    public BackupService(INotifyIconRegistry registry, ILog log, string? directory = null)
    {
        _registry = registry;
        _log = log;
        _directory = string.IsNullOrWhiteSpace(directory) ? AppPaths.BackupDirectory : directory;
    }

    /// <summary>Writes the backup pair and returns the JSON snapshot path, or <c>null</c> on failure.</summary>
    public string? Create(DateTimeOffset timestamp)
    {
        try
        {
            Directory.CreateDirectory(_directory);

            var stamp = timestamp.ToString("yyyyMMdd-HHmmss");
            var jsonPath = Path.Combine(_directory, $"notifyiconsettings-{stamp}.json");

            var snapshot = _registry.ReadRawForBackup();
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(snapshot, Options));

            ExportRegFile(Path.Combine(_directory, $"notifyiconsettings-{stamp}.reg"));
            Prune();

            _log.Info($"Backup written to {jsonPath}.");
            return jsonPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Error("Backup failed; no changes will be applied.", ex);
            return null;
        }
    }

    private void ExportRegFile(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "reg.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("export");
            startInfo.ArgumentList.Add($@"HKCU\{NotifyIconRegistry.SubKeyPath}");
            startInfo.ArgumentList.Add(path);
            startInfo.ArgumentList.Add("/y");

            using var process = Process.Start(startInfo);
            if (process is null) return;

            if (!process.WaitForExit(10_000))
            {
                _log.Warn("reg.exe export did not finish in time; the JSON snapshot is still available.");
                return;
            }

            if (process.ExitCode != 0)
            {
                _log.Warn($"reg.exe export returned {process.ExitCode}; the JSON snapshot is still available.");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _log.Warn($"reg.exe export unavailable: {ex.Message}");
        }
    }

    private void Prune()
    {
        try
        {
            foreach (var pattern in new[] { "notifyiconsettings-*.json", "notifyiconsettings-*.reg" })
            {
                var stale = new DirectoryInfo(_directory)
                    .GetFiles(pattern)
                    .OrderByDescending(f => f.Name)
                    .Skip(RetainedBackups);

                foreach (var file in stale) file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Debug($"Backup pruning skipped: {ex.Message}");
        }
    }
}
