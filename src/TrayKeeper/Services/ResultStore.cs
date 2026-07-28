using System.Text.Json;
using TrayKeeper.Logging;
using TrayKeeper.Model;

namespace TrayKeeper.Services;

/// <summary>Persists the most recent refresh result and its undo record.</summary>
public sealed class ResultStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly ILog _log;
    private readonly string _resultPath;
    private readonly string _changesPath;

    public ResultStore(ILog log, string? resultPath = null, string? changesPath = null)
    {
        _log = log;
        _resultPath = string.IsNullOrWhiteSpace(resultPath) ? AppPaths.LastResultFile : resultPath;
        _changesPath = string.IsNullOrWhiteSpace(changesPath) ? AppPaths.LastChangesFile : changesPath;
    }

    public void SaveResult(RefreshResult result) => Write(_resultPath, result);

    public RefreshResult? LoadResult() => Read<RefreshResult>(_resultPath);

    /// <summary>
    /// Stores the undo record separately from the result, so viewing history never risks
    /// discarding a pending restore.
    /// </summary>
    public void SaveChanges(IReadOnlyList<AppliedChange> changes) => Write(_changesPath, changes);

    public IReadOnlyList<AppliedChange> LoadChanges() =>
        Read<List<AppliedChange>>(_changesPath) ?? (IReadOnlyList<AppliedChange>)Array.Empty<AppliedChange>();

    public void ClearChanges()
    {
        try
        {
            if (File.Exists(_changesPath)) File.Delete(_changesPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Could not clear the undo record. {ex.Message}");
        }
    }

    private void Write<T>(string path, T value)
    {
        try
        {
            // A path with no directory component is legitimate, so this must not be assumed
            // non-null. Persisting the report is never allowed to break a refresh.
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException or JsonException)
        {
            _log.Warn($"Could not write {PathUtilities.GetFileNameSafe(path)}. {ex.Message}");
        }
    }

    private T? Read<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or JsonException or ArgumentException or NotSupportedException)
        {
            _log.Warn($"Could not read {PathUtilities.GetFileNameSafe(path)}. {ex.Message}");
            return null;
        }
    }
}
