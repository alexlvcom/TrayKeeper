namespace TrayKeeper.Services;

/// <summary>One entry TrayKeeper promoted, recorded so it can be undone.</summary>
public sealed record AppliedChange
{
    public required string KeyName { get; init; }

    /// <summary>The original <c>IsPromoted</c> value: <c>null</c> means the value was absent.</summary>
    public bool? OriginalIsPromoted { get; init; }

    public required string TargetPath { get; init; }
    public required string SourcePath { get; init; }
    public required string DisplayName { get; init; }
    public required string Reason { get; init; }
}

public sealed record SkippedEntry
{
    public required string DisplayName { get; init; }
    public required string TargetPath { get; init; }
    public string? ClosestSourcePath { get; init; }
    public required string Reason { get; init; }
}

public sealed record MatchingSource
{
    public required string DisplayName { get; init; }
    public required string Path { get; init; }
}

/// <summary>What asked for a refresh. Automatic ones run every minute, so they stay quiet.</summary>
public enum RefreshTrigger
{
    Manual,
    Automatic,
}

public enum RefreshOutcome
{
    ChangesApplied,
    NoDuplicatesFound,
    Failed,
}

/// <summary>The outcome of one refresh, persisted so "Show last results" survives a restart.</summary>
public sealed record RefreshResult
{
    public DateTimeOffset Timestamp { get; init; }
    public RefreshOutcome Outcome { get; init; }
    public int TotalEntries { get; init; }
    public int EnabledEntries { get; init; }
    public string? BackupPath { get; init; }

    public IReadOnlyList<MatchingSource> Sources { get; init; } = Array.Empty<MatchingSource>();
    public IReadOnlyList<AppliedChange> Applied { get; init; } = Array.Empty<AppliedChange>();
    public IReadOnlyList<SkippedEntry> Skipped { get; init; } = Array.Empty<SkippedEntry>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>Short text for the balloon tip.</summary>
    public string Summary => Outcome switch
    {
        RefreshOutcome.Failed => "Refresh failed. See the log for details.",
        RefreshOutcome.NoDuplicatesFound => "No disabled duplicate tray icons were found.",
        _ => Applied.Count == 1
            ? "Enabled 1 duplicate tray icon entry."
            : $"Enabled {Applied.Count} duplicate tray icon entries.",
    };
}
