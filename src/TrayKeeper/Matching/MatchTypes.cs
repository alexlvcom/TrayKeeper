using TrayKeeper.Model;

namespace TrayKeeper.Matching;

/// <summary>Named evidence that two entries belong to the same application.</summary>
public enum MatchSignal
{
    /// <summary>Identical MSIX package family name — version independent, strongest available.</summary>
    PackageFamily,

    /// <summary>Identical directory once version segments are collapsed.</summary>
    NormalizedDirectory,

    /// <summary>Identical file product name and company name.</summary>
    ProductAndCompany,

    /// <summary>Identical file description and company name.</summary>
    DescriptionAndCompany,

    /// <summary>Identical initial tooltip and company name.</summary>
    TooltipAndCompany,
}

/// <summary>Why a plausible-looking entry was not enabled.</summary>
public enum SkipReason
{
    SameExecutablePath,
    DifferentIconIdentity,
    IconIdentityNotComparable,
    ExecutableMissing,
    GenericFileNameWithWeakEvidence,
    InsufficientEvidence,
    AlreadyEnabled,
    MalformedEntry,
}

public sealed record MatchDecision
{
    public bool IsMatch { get; init; }
    public IReadOnlyList<MatchSignal> Signals { get; init; } = Array.Empty<MatchSignal>();
    public SkipReason? SkipReason { get; init; }
    public string? Detail { get; init; }

    /// <summary>Higher wins when several enabled entries could act as the source.</summary>
    public int Strength { get; init; }

    public static MatchDecision Match(int strength, params MatchSignal[] signals) =>
        new() { IsMatch = true, Strength = strength, Signals = signals };

    public static MatchDecision Skip(SkipReason reason, string? detail = null) =>
        new() { IsMatch = false, SkipReason = reason, Detail = detail };
}

/// <summary>A disabled entry that TrayKeeper decided to enable, plus the entry that justified it.</summary>
public sealed record DuplicateMatch
{
    public required NotifyIconEntry Target { get; init; }
    public required NotifyIconEntry Source { get; init; }
    public required MatchDecision Decision { get; init; }
    public string Reason => MatchReasonFormatter.Describe(Decision.Signals);
}

/// <summary>A disabled entry that resembled an enabled one but was deliberately left alone.</summary>
public sealed record SkippedCandidate
{
    public required NotifyIconEntry Target { get; init; }
    public NotifyIconEntry? ClosestSource { get; init; }
    public required SkipReason Reason { get; init; }
    public string? Detail { get; init; }
    public string Description => MatchReasonFormatter.Describe(Reason, Detail);
}

public sealed record MatchAnalysis
{
    public IReadOnlyList<NotifyIconEntry> AllEntries { get; init; } = Array.Empty<NotifyIconEntry>();
    public IReadOnlyList<NotifyIconEntry> EnabledEntries { get; init; } = Array.Empty<NotifyIconEntry>();
    public IReadOnlyList<DuplicateMatch> Matches { get; init; } = Array.Empty<DuplicateMatch>();
    public IReadOnlyList<SkippedCandidate> Skipped { get; init; } = Array.Empty<SkippedCandidate>();

    /// <summary>The enabled entries that actually justified at least one change.</summary>
    public IReadOnlyList<NotifyIconEntry> MatchingSources =>
        Matches.Select(m => m.Source).DistinctBy(e => e.KeyName).ToList();
}

public static class MatchReasonFormatter
{
    public static string Describe(IReadOnlyList<MatchSignal> signals)
    {
        var parts = new List<string> { "same executable name" };

        foreach (var signal in signals)
        {
            parts.Add(signal switch
            {
                MatchSignal.PackageFamily => "same package family name",
                MatchSignal.NormalizedDirectory => "same normalized application directory",
                MatchSignal.ProductAndCompany => "same product name and company",
                MatchSignal.DescriptionAndCompany => "same file description and company",
                MatchSignal.TooltipAndCompany => "same tooltip and company",
                _ => signal.ToString(),
            });
        }

        parts.Add("same notification icon identity");
        return string.Join(", ", parts);
    }

    public static string Describe(SkipReason reason, string? detail)
    {
        var text = reason switch
        {
            SkipReason.SameExecutablePath =>
                "another entry uses the identical executable path, so this is a separate icon of the same process, not an update duplicate",
            SkipReason.DifferentIconIdentity =>
                "the notification icon identity (UID or GUID) differs, so these are different icons of the same application",
            SkipReason.IconIdentityNotComparable =>
                "the notification icon identity could not be compared between the two entries",
            SkipReason.ExecutableMissing =>
                "the executable no longer exists on disk, so this is a leftover entry from an uninstalled version",
            SkipReason.GenericFileNameWithWeakEvidence =>
                "the executable name is too generic and the remaining evidence was not strong enough",
            SkipReason.InsufficientEvidence =>
                "no enabled entry matched with enough confidence",
            SkipReason.AlreadyEnabled => "already enabled",
            SkipReason.MalformedEntry => "the registry entry is malformed",
            _ => reason.ToString(),
        };

        return string.IsNullOrWhiteSpace(detail) ? text : $"{text} ({detail})";
    }
}
