using TrayKeeper.Model;

namespace TrayKeeper.Matching;

public sealed record MatcherOptions
{
    /// <summary>
    /// Do not promote an entry whose executable is confirmed absent. Windows keeps entries for
    /// versions that have since been deleted; enabling those produces phantom icons that can
    /// never appear. Existence that cannot be determined is not treated as absent.
    /// </summary>
    public bool SkipMissingExecutables { get; init; } = true;

    /// <summary>
    /// Executable names that identify a role rather than an application. A shared name here is
    /// worth nothing on its own, so such entries need directory- or package-level evidence.
    /// </summary>
    public IReadOnlySet<string> GenericFileNames { get; init; } = DefaultGenericFileNames;

    public static readonly IReadOnlySet<string> DefaultGenericFileNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "update.exe", "updater.exe", "autoupdate.exe", "setup.exe", "install.exe", "installer.exe",
            "helper.exe", "service.exe", "host.exe", "launcher.exe", "app.exe", "main.exe", "run.exe",
            "start.exe", "startup.exe", "agent.exe", "monitor.exe", "notify.exe", "notifier.exe",
            "tray.exe", "systray.exe", "trayapp.exe", "background.exe", "backgroundtask.exe",
            "electron.exe", "node.exe", "java.exe", "javaw.exe", "python.exe", "pythonw.exe",
            "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe",
            "rundll32.exe", "dllhost.exe", "explorer.exe", "svchost.exe", "conhost.exe",
        };

    public static readonly MatcherOptions Default = new();
}

/// <summary>
/// Decides which disabled notification-area entries are update duplicates of an entry the user
/// has already enabled. Deliberately biased toward false negatives: leaving an icon hidden is a
/// minor annoyance, whereas surfacing an unrelated application's icon is a visible defect.
/// </summary>
public sealed class DuplicateMatcher
{
    private readonly MatcherOptions _options;

    public DuplicateMatcher(MatcherOptions? options = null) => _options = options ?? MatcherOptions.Default;

    public MatchAnalysis Analyze(IReadOnlyList<NotifyIconEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var enabled = entries.Where(e => e.IsEnabled).ToList();
        var disabled = entries.Where(e => e.IsDisabled).ToList();

        var matches = new List<DuplicateMatch>();
        var skipped = new List<SkippedCandidate>();

        foreach (var target in disabled)
        {
            if (string.IsNullOrWhiteSpace(target.ResolvedExecutablePath) || target.FileName.Length == 0)
            {
                skipped.Add(new SkippedCandidate
                {
                    Target = target,
                    Reason = SkipReason.MalformedEntry,
                    Detail = $"unusable ExecutablePath '{target.RawExecutablePath}'",
                });
                continue;
            }

            // Only entries sharing the executable file name are even considered. Anything else is
            // not a plausible duplicate and is not worth reporting as "similar".
            var candidates = enabled
                .Where(source => string.Equals(source.FileName, target.FileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0) continue;

            DuplicateMatch? best = null;
            SkippedCandidate? bestSkip = null;

            foreach (var source in candidates)
            {
                var decision = Evaluate(source, target);

                if (decision.IsMatch)
                {
                    if (best is null || decision.Strength > best.Decision.Strength)
                    {
                        best = new DuplicateMatch { Target = target, Source = source, Decision = decision };
                    }
                }
                else if (bestSkip is null || RankSkip(decision.SkipReason) > RankSkip(bestSkip.Reason))
                {
                    bestSkip = new SkippedCandidate
                    {
                        Target = target,
                        ClosestSource = source,
                        Reason = decision.SkipReason ?? SkipReason.InsufficientEvidence,
                        Detail = decision.Detail,
                    };
                }
            }

            if (best is not null) matches.Add(best);
            else if (bestSkip is not null) skipped.Add(bestSkip);
        }

        return new MatchAnalysis
        {
            AllEntries = entries,
            EnabledEntries = enabled,
            Matches = matches,
            Skipped = skipped,
        };
    }

    /// <summary>
    /// Evaluates a single (enabled source, disabled target) pair. Callers have already
    /// established that the executable file names are equal.
    /// </summary>
    internal MatchDecision Evaluate(NotifyIconEntry source, NotifyIconEntry target)
    {
        if (string.Equals(source.KeyName, target.KeyName, StringComparison.Ordinal))
        {
            return MatchDecision.Skip(SkipReason.SameExecutablePath, "same registry entry");
        }

        // Gate 1 — the two entries must describe the same executable location, not the same file.
        // Two entries with an identical path are two distinct icons owned by one process
        // (Outlook registers separate icons for new mail and for the main window), never an
        // update duplicate.
        if (string.Equals(source.ResolvedExecutablePath, target.ResolvedExecutablePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return MatchDecision.Skip(SkipReason.SameExecutablePath);
        }

        // Gate 2 — the icon slot identity must match. Explorer keys each entry on the
        // NOTIFYICONDATA uID or guidItem; an application that shows several different icons has
        // several identities, and carrying a visibility setting across them is wrong.
        var identity = CompareIconIdentity(source, target);
        if (identity is not null) return identity;

        // Gate 3 — never promote an entry whose executable is confirmed gone.
        if (_options.SkipMissingExecutables && target.File.Exists == false)
        {
            return MatchDecision.Skip(SkipReason.ExecutableMissing, target.ResolvedExecutablePath);
        }

        var signals = CollectSignals(source, target);
        var isGeneric = _options.GenericFileNames.Contains(target.FileName);

        // Strong evidence is location based: the same package family, or the same directory once
        // version segments are collapsed. Metadata-only agreement is accepted for a distinctive
        // executable name but never for a generic one, where unrelated vendors routinely ship
        // files with matching product strings.
        var hasStrongEvidence = signals.Contains(MatchSignal.PackageFamily)
                                || signals.Contains(MatchSignal.NormalizedDirectory);

        if (isGeneric && !hasStrongEvidence)
        {
            return MatchDecision.Skip(SkipReason.GenericFileNameWithWeakEvidence, target.FileName);
        }

        if (signals.Count == 0)
        {
            return MatchDecision.Skip(SkipReason.InsufficientEvidence,
                $"only the executable name '{target.FileName}' is shared");
        }

        return MatchDecision.Match(Strength(signals), signals.ToArray());
    }

    /// <summary>
    /// Returns a skip decision when the icon identities differ or cannot be compared,
    /// or <c>null</c> when they agree.
    /// </summary>
    private static MatchDecision? CompareIconIdentity(NotifyIconEntry source, NotifyIconEntry target)
    {
        var haveUid = source.Uid.HasValue && target.Uid.HasValue;
        var haveGuid = !string.IsNullOrWhiteSpace(source.IconGuid) && !string.IsNullOrWhiteSpace(target.IconGuid);

        if (!haveUid && !haveGuid)
        {
            return MatchDecision.Skip(SkipReason.IconIdentityNotComparable,
                "neither entry exposes a comparable UID or icon GUID");
        }

        if (haveUid && source.Uid!.Value != target.Uid!.Value)
        {
            return MatchDecision.Skip(SkipReason.DifferentIconIdentity,
                $"UID {source.Uid.Value} vs {target.Uid.Value}");
        }

        if (haveGuid && !string.Equals(NormalizeGuid(source.IconGuid), NormalizeGuid(target.IconGuid),
                StringComparison.OrdinalIgnoreCase))
        {
            return MatchDecision.Skip(SkipReason.DifferentIconIdentity, "icon GUIDs differ");
        }

        return null;
    }

    private static string NormalizeGuid(string? guid) =>
        Guid.TryParse(guid, out var parsed) ? parsed.ToString("B") : guid?.Trim() ?? string.Empty;

    private static List<MatchSignal> CollectSignals(NotifyIconEntry source, NotifyIconEntry target)
    {
        var signals = new List<MatchSignal>();

        var sourceFamily = PathNormalizer.TryGetPackageFamilyName(source.ResolvedExecutablePath);
        var targetFamily = PathNormalizer.TryGetPackageFamilyName(target.ResolvedExecutablePath);

        if (sourceFamily is not null && sourceFamily == targetFamily)
        {
            signals.Add(MatchSignal.PackageFamily);
        }

        var sourceDirectory = PathNormalizer.NormalizeDirectory(source.DirectoryPath);
        var targetDirectory = PathNormalizer.NormalizeDirectory(target.DirectoryPath);

        if (sourceDirectory.Length > 0 && sourceDirectory == targetDirectory)
        {
            signals.Add(MatchSignal.NormalizedDirectory);
        }

        var sameCompany = EqualNonEmpty(source.File.CompanyName, target.File.CompanyName)
                          || EqualNonEmpty(source.Publisher, target.Publisher);

        if (sameCompany)
        {
            if (EqualNonEmpty(source.File.ProductName, target.File.ProductName))
            {
                signals.Add(MatchSignal.ProductAndCompany);
            }

            if (EqualNonEmpty(source.File.FileDescription, target.File.FileDescription))
            {
                signals.Add(MatchSignal.DescriptionAndCompany);
            }

            if (EqualNonEmpty(source.InitialTooltip, target.InitialTooltip))
            {
                signals.Add(MatchSignal.TooltipAndCompany);
            }
        }

        return signals;
    }

    private static bool EqualNonEmpty(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static int Strength(IReadOnlyCollection<MatchSignal> signals) =>
        signals.Sum(signal => signal switch
        {
            MatchSignal.PackageFamily => 100,
            MatchSignal.NormalizedDirectory => 80,
            MatchSignal.ProductAndCompany => 40,
            MatchSignal.DescriptionAndCompany => 30,
            MatchSignal.TooltipAndCompany => 20,
            _ => 0,
        });

    /// <summary>
    /// Orders skip reasons by how informative they are, so the reported reason is the most
    /// specific one encountered rather than whichever candidate happened to be evaluated last.
    /// </summary>
    private static int RankSkip(SkipReason? reason) => reason switch
    {
        SkipReason.ExecutableMissing => 5,
        SkipReason.GenericFileNameWithWeakEvidence => 4,
        SkipReason.DifferentIconIdentity => 3,
        SkipReason.InsufficientEvidence => 2,
        SkipReason.IconIdentityNotComparable => 1,
        _ => 0,
    };
}
