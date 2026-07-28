using TrayKeeper.Logging;
using TrayKeeper.Matching;
using TrayKeeper.Registry;
using TrayKeeper.Settings;

namespace TrayKeeper.Services;

/// <summary>
/// Orchestrates one refresh: read the registry, decide, back up, then write.
/// </summary>
public sealed class RefreshService
{
    private readonly INotifyIconRegistry _registry;
    private readonly BackupService _backup;
    private readonly ResultStore _results;
    private readonly SettingsStore _settings;
    private readonly ILog _log;
    private readonly Func<DateTimeOffset> _clock;

    public RefreshService(
        INotifyIconRegistry registry,
        BackupService backup,
        ResultStore results,
        SettingsStore settings,
        ILog log,
        Func<DateTimeOffset>? clock = null)
    {
        _registry = registry;
        _backup = backup;
        _results = results;
        _settings = settings;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public RefreshResult Refresh()
    {
        var timestamp = _clock();
        var errors = new List<string>();

        try
        {
            var settings = _settings.Load();
            var entries = _registry.Read();
            _log.Info($"Read {entries.Count} notification-area entries.");

            if (entries.Count == 0)
            {
                return Finish(new RefreshResult
                {
                    Timestamp = timestamp,
                    Outcome = RefreshOutcome.Failed,
                    Errors = new[]
                    {
                        @"No entries were found under HKCU\Control Panel\NotifyIconSettings. " +
                        "This Windows build may store notification-area settings elsewhere.",
                    },
                });
            }

            var matcher = new DuplicateMatcher(new MatcherOptions
            {
                SkipMissingExecutables = settings.SkipMissingExecutables,
            });

            var analysis = matcher.Analyze(entries);

            var skipped = analysis.Skipped
                .Select(s => new SkippedEntry
                {
                    DisplayName = s.Target.DisplayName,
                    TargetPath = s.Target.ResolvedExecutablePath,
                    ClosestSourcePath = s.ClosestSource?.ResolvedExecutablePath,
                    Reason = s.Description,
                })
                .ToList();

            if (analysis.Matches.Count == 0)
            {
                _log.Info("No disabled duplicates qualified for promotion.");
                return Finish(new RefreshResult
                {
                    Timestamp = timestamp,
                    Outcome = RefreshOutcome.NoDuplicatesFound,
                    TotalEntries = entries.Count,
                    EnabledEntries = analysis.EnabledEntries.Count,
                    Skipped = skipped,
                });
            }

            // Nothing is written until a backup exists.
            var backupPath = _backup.Create(timestamp);
            if (backupPath is null)
            {
                return Finish(new RefreshResult
                {
                    Timestamp = timestamp,
                    Outcome = RefreshOutcome.Failed,
                    TotalEntries = entries.Count,
                    EnabledEntries = analysis.EnabledEntries.Count,
                    Skipped = skipped,
                    Errors = new[] { "Could not back up the notification-area settings, so no changes were made." },
                });
            }

            var applied = new List<AppliedChange>();

            foreach (var match in analysis.Matches)
            {
                try
                {
                    _registry.SetPromoted(match.Target.KeyName, promoted: true);

                    applied.Add(new AppliedChange
                    {
                        KeyName = match.Target.KeyName,
                        OriginalIsPromoted = match.Target.IsPromoted,
                        TargetPath = match.Target.ResolvedExecutablePath,
                        SourcePath = match.Source.ResolvedExecutablePath,
                        DisplayName = match.Target.DisplayName,
                        Reason = match.Reason,
                    });
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException
                                               or InvalidOperationException or IOException)
                {
                    var message = $"Could not enable '{match.Target.DisplayName}' ({match.Target.KeyName}): {ex.Message}";
                    _log.Error(message, ex);
                    errors.Add(message);
                }
            }

            if (applied.Count > 0) _results.SaveChanges(applied);

            var outcome = applied.Count > 0
                ? RefreshOutcome.ChangesApplied
                : RefreshOutcome.Failed;

            return Finish(new RefreshResult
            {
                Timestamp = timestamp,
                Outcome = outcome,
                TotalEntries = entries.Count,
                EnabledEntries = analysis.EnabledEntries.Count,
                BackupPath = backupPath,
                Sources = analysis.MatchingSources
                    .Select(s => new MatchingSource { DisplayName = s.DisplayName, Path = s.ResolvedExecutablePath })
                    .ToList(),
                Applied = applied,
                Skipped = skipped,
                Errors = errors,
            });
        }
        catch (Exception ex)
        {
            _log.Error("Refresh failed.", ex);
            return Finish(new RefreshResult
            {
                Timestamp = timestamp,
                Outcome = RefreshOutcome.Failed,
                Errors = new[] { ex.Message },
            });
        }
    }

    /// <summary>
    /// Reverts the changes from the most recent refresh, restoring each entry to the exact prior
    /// state — including deleting <c>IsPromoted</c> where it did not previously exist.
    /// </summary>
    public (int Restored, IReadOnlyList<string> Errors) RestoreLastChanges()
    {
        var changes = _results.LoadChanges();
        var errors = new List<string>();
        var restored = 0;

        foreach (var change in changes)
        {
            try
            {
                if (change.OriginalIsPromoted is null) _registry.RemovePromoted(change.KeyName);
                else _registry.SetPromoted(change.KeyName, change.OriginalIsPromoted.Value);

                restored++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException
                                           or InvalidOperationException or IOException)
            {
                var message = $"Could not restore '{change.DisplayName}' ({change.KeyName}): {ex.Message}";
                _log.Error(message, ex);
                errors.Add(message);
            }
        }

        if (errors.Count == 0) _results.ClearChanges();

        _log.Info($"Restore completed: {restored} of {changes.Count} entries reverted.");
        return (restored, errors);
    }

    public bool HasRestorableChanges() => _results.LoadChanges().Count > 0;

    /// <summary>
    /// Logs and persists the result. This runs on the failure path too, so it must never throw —
    /// an exception here would escape <see cref="Refresh"/> and take the tray icon down with it.
    /// </summary>
    private RefreshResult Finish(RefreshResult result)
    {
        try
        {
            _log.Info($"Refresh result: {result.Summary}");
            _results.SaveResult(result);
        }
        catch (Exception ex)
        {
            _log.Error("Could not persist the refresh result.", ex);
        }

        return result;
    }
}
