using TrayKeeper.Matching;
using TrayKeeper.Model;
using static TrayKeeper.Tests.EntryBuilder;

namespace TrayKeeper.Tests;

public class DuplicateMatcherTests
{
    private static readonly DuplicateMatcher Matcher = new();

    private static MatchAnalysis Analyze(params NotifyIconEntry[] entries) => Matcher.Analyze(entries);

    // ---------------------------------------------------------------- versioned directories

    [Fact]
    public void EnablesNewVersionedDirectoryWhenOldOneIsEnabled()
    {
        var analysis = Analyze(
            Enabled(@"C:\Users\Alex\AppData\Local\WisprFlow\app-1.5.1095\Wispr Flow.exe",
                product: "Wispr Flow", company: "Wispr AI"),
            Disabled(@"C:\Users\Alex\AppData\Local\WisprFlow\app-1.6.224\Wispr Flow.exe",
                product: "Wispr Flow", company: "Wispr AI"));

        var match = Assert.Single(analysis.Matches);
        Assert.EndsWith(@"app-1.6.224\Wispr Flow.exe", match.Target.ResolvedExecutablePath);
        Assert.Contains(MatchSignal.NormalizedDirectory, match.Decision.Signals);
        Assert.Contains("normalized application directory", match.Reason);
    }

    [Fact]
    public void EnablesOldVersionWhenTheNewOneIsTheEnabledEntry()
    {
        // Direction must not matter: whichever entry the user enabled acts as the source.
        var analysis = Analyze(
            Disabled(@"C:\App\app-1.4.0\AppName.exe", product: "AppName", company: "Vendor"),
            Enabled(@"C:\App\app-1.5.0\AppName.exe", product: "AppName", company: "Vendor"));

        var match = Assert.Single(analysis.Matches);
        Assert.EndsWith(@"app-1.4.0\AppName.exe", match.Target.ResolvedExecutablePath);
    }

    [Fact]
    public void TreatsAbsentIsPromotedValueAsDisabled()
    {
        // Windows omits IsPromoted entirely for icons the user has never toggled.
        var analysis = Analyze(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            Unset(@"C:\App\app-2.0.0\AppName.exe", product: "AppName", company: "Vendor"));

        var match = Assert.Single(analysis.Matches);
        Assert.Null(match.Target.IsPromoted);
    }

    [Fact]
    public void MatchesVersionDirectoryWithoutAppPrefix()
    {
        var analysis = Analyze(
            Enabled(@"C:\Program Files\Vendor\Tool\2.10.4\tool.exe", product: "Tool", company: "Vendor"),
            Disabled(@"C:\Program Files\Vendor\Tool\2.11.0\tool.exe", product: "Tool", company: "Vendor"));

        Assert.Single(analysis.Matches);
    }

    [Fact]
    public void MatchesVersionEmbeddedInAProductNamedDirectory()
    {
        var analysis = Analyze(
            Enabled(@"C:\Program Files\JetBrains\PhpStorm 2025.1.7\bin\phpstorm64.exe",
                product: "PhpStorm", company: "JetBrains s.r.o."),
            Disabled(@"C:\Program Files\JetBrains\PhpStorm 2025.2.1\bin\phpstorm64.exe",
                product: "PhpStorm", company: "JetBrains s.r.o."));

        Assert.Single(analysis.Matches);
    }

    // ---------------------------------------------------------------- same company, different product

    [Fact]
    public void DoesNotMatchDifferentProductsFromTheSameCompany()
    {
        // Company agreement alone must never be sufficient.
        var analysis = Analyze(
            Enabled(@"C:\Program Files\Vendor\Alpha\app.exe", product: "Alpha", company: "Vendor Inc"),
            Disabled(@"C:\Program Files\Vendor\Beta\app.exe", product: "Beta", company: "Vendor Inc"));

        Assert.Empty(analysis.Matches);
        var skipped = Assert.Single(analysis.Skipped);
        Assert.Equal(SkipReason.GenericFileNameWithWeakEvidence, skipped.Reason);
    }

    [Fact]
    public void DoesNotMatchDistinctiveNamesFromTheSameCompanyInUnrelatedDirectories()
    {
        var analysis = Analyze(
            Enabled(@"C:\Program Files\Vendor\Alpha\shared.exe", product: "Alpha Suite", company: "Vendor Inc"),
            Disabled(@"D:\Tools\Beta\shared.exe", product: "Beta Suite", company: "Vendor Inc"));

        Assert.Empty(analysis.Matches);
        Assert.Single(analysis.Skipped);
    }

    // ---------------------------------------------------------------- generic executable names

    [Fact]
    public void DoesNotMatchGenericNameOnMetadataAlone()
    {
        var analysis = Analyze(
            Enabled(@"C:\Users\Alex\AppData\Local\AlphaApp\Update.exe",
                product: "Squirrel", company: "GitHub"),
            Disabled(@"C:\Users\Alex\AppData\Local\BetaApp\Update.exe",
                product: "Squirrel", company: "GitHub"));

        Assert.Empty(analysis.Matches);
        var skipped = Assert.Single(analysis.Skipped);
        Assert.Equal(SkipReason.GenericFileNameWithWeakEvidence, skipped.Reason);
    }

    [Fact]
    public void MatchesGenericNameWhenTheNormalizedDirectoryAgrees()
    {
        // update.exe inside the same versioned application folder is still the same application.
        var analysis = Analyze(
            Enabled(@"C:\Users\Alex\AppData\Local\AlphaApp\app-1.0.0\Update.exe",
                product: "Squirrel", company: "GitHub"),
            Disabled(@"C:\Users\Alex\AppData\Local\AlphaApp\app-1.1.0\Update.exe",
                product: "Squirrel", company: "GitHub"));

        var match = Assert.Single(analysis.Matches);
        Assert.Contains(MatchSignal.NormalizedDirectory, match.Decision.Signals);
    }

    [Fact]
    public void DoesNotMatchHelperExecutablesAcrossApplications()
    {
        var analysis = Analyze(
            Enabled(@"C:\Program Files\Alpha\helper.exe", product: "Helper", company: "Alpha Ltd"),
            Disabled(@"C:\Program Files\Beta\helper.exe", product: "Helper", company: "Alpha Ltd"));

        Assert.Empty(analysis.Matches);
    }

    // ---------------------------------------------------------------- case insensitivity

    [Fact]
    public void ComparesPathsCaseInsensitively()
    {
        var analysis = Analyze(
            Enabled(@"C:\Users\Alex\AppData\Local\WisprFlow\app-1.5.1095\Wispr Flow.exe",
                product: "Wispr Flow", company: "Wispr AI"),
            Disabled(@"c:\users\alex\appdata\local\wisprflow\APP-1.6.224\WISPR FLOW.EXE",
                product: "wispr flow", company: "WISPR AI"));

        Assert.Single(analysis.Matches);
    }

    [Fact]
    public void TreatsIdenticalPathsDifferingOnlyByCaseAsTheSameEntry()
    {
        // Same file, two icons — a different UID would be the only legitimate difference, and the
        // paths being equal means this is not an update duplicate.
        var analysis = Analyze(
            Enabled(@"C:\Program Files\Vendor\App\app.exe", uid: 1, product: "App", company: "Vendor"),
            Disabled(@"c:\program files\vendor\app\APP.EXE", uid: 1, product: "App", company: "Vendor"));

        Assert.Empty(analysis.Matches);
        var skipped = Assert.Single(analysis.Skipped);
        Assert.Equal(SkipReason.SameExecutablePath, skipped.Reason);
    }

    // ---------------------------------------------------------------- malformed paths

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\")]
    [InlineData("{not-a-guid}")]
    public void ReportsMalformedTargetsWithoutThrowing(string path)
    {
        var analysis = Analyze(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            Disabled(path, product: "AppName", company: "Vendor"));

        Assert.Empty(analysis.Matches);
    }

    [Fact]
    public void MalformedTargetIsReportedAsMalformed()
    {
        var analysis = Analyze(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            Disabled("   ", product: "AppName", company: "Vendor"));

        var skipped = Assert.Single(analysis.Skipped);
        Assert.Equal(SkipReason.MalformedEntry, skipped.Reason);
    }

    [Fact]
    public void MalformedEnabledEntryDoesNotDisruptOtherMatches()
    {
        var analysis = Analyze(
            Enabled(""),
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            Disabled(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor"));

        Assert.Single(analysis.Matches);
    }

    // ---------------------------------------------------------------- missing executables

    [Fact]
    public void SkipsEntriesWhoseExecutableIsGone()
    {
        // Windows keeps an entry per version forever; promoting a deleted version produces an
        // icon that can never appear.
        var analysis = Analyze(
            Enabled(@"C:\App\app-1.5.0\AppName.exe", product: "AppName", company: "Vendor"),
            Disabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor", exists: false));

        Assert.Empty(analysis.Matches);
        var skipped = Assert.Single(analysis.Skipped);
        Assert.Equal(SkipReason.ExecutableMissing, skipped.Reason);
    }

    [Fact]
    public void EnablesEntriesWhoseExistenceIsUnknown()
    {
        // A packaged application's path may be unreadable; unknown must not mean missing.
        var analysis = Analyze(
            Enabled(@"C:\App\app-1.5.0\AppName.exe", product: "AppName", company: "Vendor", exists: null),
            Disabled(@"C:\App\app-1.6.0\AppName.exe", product: "AppName", company: "Vendor", exists: null));

        Assert.Single(analysis.Matches);
    }

    [Fact]
    public void PromotesMissingExecutablesWhenTheGuardIsDisabled()
    {
        var matcher = new DuplicateMatcher(new MatcherOptions { SkipMissingExecutables = false });

        var analysis = matcher.Analyze(new[]
        {
            Enabled(@"C:\App\app-1.5.0\AppName.exe", product: "AppName", company: "Vendor"),
            Disabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor", exists: false),
        });

        Assert.Single(analysis.Matches);
    }

    // ---------------------------------------------------------------- packaged applications

    [Fact]
    public void MatchesPackagedApplicationAcrossVersionsByPackageFamily()
    {
        const string root = @"C:\Program Files\WindowsApps";

        var analysis = Analyze(
            Enabled($@"{root}\Claude_1.13576.4.0_x64__pzs8sxrjxfjjc\app\claude.exe", exists: null),
            Disabled($@"{root}\Claude_1.22209.3.0_x64__pzs8sxrjxfjjc\app\claude.exe", exists: null));

        var match = Assert.Single(analysis.Matches);
        Assert.Contains(MatchSignal.PackageFamily, match.Decision.Signals);
        Assert.Contains("package family", match.Reason);
    }

    [Fact]
    public void DoesNotMatchPackagedApplicationsFromDifferentPublishers()
    {
        const string root = @"C:\Program Files\WindowsApps";

        var analysis = Analyze(
            Enabled($@"{root}\Thing_1.0.0.0_x64__aaaaaaaaaaaaa\app\thing.exe", exists: null),
            Disabled($@"{root}\Thing_1.0.0.0_x64__bbbbbbbbbbbbb\app\thing.exe", exists: null));

        Assert.Empty(analysis.Matches);
    }

    [Fact]
    public void DoesNotMatchPackagedAgainstUnpackagedInstall()
    {
        var analysis = Analyze(
            Enabled(@"C:\Program Files\WindowsApps\Thing_1.0.0.0_x64__aaaaaaaaaaaaa\thing.exe", exists: null),
            Disabled(@"C:\Users\Alex\AppData\Local\Thing\thing.exe", product: "Thing", company: "Vendor"));

        Assert.Empty(analysis.Matches);
    }

    [Fact]
    public void MatchesUnpackagedApplicationWithNoFileMetadataByDirectory()
    {
        // Electron builds frequently ship without version resources.
        var analysis = Analyze(
            Enabled(@"C:\Users\Alex\AppData\Local\Discord\app-1.0.9248\Discord.exe"),
            Disabled(@"C:\Users\Alex\AppData\Local\Discord\app-1.0.9270\Discord.exe"));

        var match = Assert.Single(analysis.Matches);
        Assert.Contains(MatchSignal.NormalizedDirectory, match.Decision.Signals);
    }

    // ---------------------------------------------------------------- icon identity

    [Fact]
    public void DoesNotCarryVisibilityAcrossDifferentIconUids()
    {
        // One executable can own several icons; each has its own visibility setting.
        var analysis = Analyze(
            Enabled(@"C:\Program Files\Office\OUTLOOK.EXE", uid: 12345, product: "Outlook", company: "Microsoft"),
            Disabled(@"C:\Program Files\Office16\OUTLOOK.EXE", uid: 0, product: "Outlook", company: "Microsoft"));

        Assert.Empty(analysis.Matches);
        var skipped = Assert.Single(analysis.Skipped);
        Assert.Equal(SkipReason.DifferentIconIdentity, skipped.Reason);
    }

    [Fact]
    public void MatchesGuidIdentifiedIconsWhenTheGuidAgrees()
    {
        var analysis = Analyze(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", uid: null,
                iconGuid: "{AF0ADBF8-4B35-4FFD-B1DB-18DE16ABB0F4}", product: "AppName", company: "Vendor"),
            Disabled(@"C:\App\app-1.1.0\AppName.exe", uid: null,
                iconGuid: "{af0adbf8-4b35-4ffd-b1db-18de16abb0f4}", product: "AppName", company: "Vendor"));

        Assert.Single(analysis.Matches);
    }

    [Fact]
    public void DoesNotMatchWhenIconGuidsDiffer()
    {
        var analysis = Analyze(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", uid: null,
                iconGuid: "{11111111-1111-1111-1111-111111111111}", product: "AppName", company: "Vendor"),
            Disabled(@"C:\App\app-1.1.0\AppName.exe", uid: null,
                iconGuid: "{22222222-2222-2222-2222-222222222222}", product: "AppName", company: "Vendor"));

        Assert.Empty(analysis.Matches);
        Assert.Equal(SkipReason.DifferentIconIdentity, Assert.Single(analysis.Skipped).Reason);
    }

    [Fact]
    public void DoesNotMatchWhenNeitherEntryExposesAnIdentity()
    {
        var analysis = Analyze(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", uid: null, product: "AppName", company: "Vendor"),
            Disabled(@"C:\App\app-1.1.0\AppName.exe", uid: null, product: "AppName", company: "Vendor"));

        Assert.Empty(analysis.Matches);
        Assert.Equal(SkipReason.IconIdentityNotComparable, Assert.Single(analysis.Skipped).Reason);
    }

    // ---------------------------------------------------------------- duplicate and already-enabled entries

    [Fact]
    public void EnablesEveryQualifyingDuplicateOfOneApplication()
    {
        var analysis = Analyze(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            Disabled(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor"),
            Disabled(@"C:\App\app-1.2.0\AppName.exe", product: "AppName", company: "Vendor"));

        Assert.Equal(2, analysis.Matches.Count);
        Assert.Single(analysis.MatchingSources);
    }

    [Fact]
    public void LeavesAlreadyEnabledDuplicatesUntouched()
    {
        var analysis = Analyze(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            Enabled(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor"));

        Assert.Empty(analysis.Matches);
        Assert.Empty(analysis.Skipped);
    }

    [Fact]
    public void PicksTheStrongestSourceWhenSeveralQualify()
    {
        var weakSource = Enabled(@"D:\Other\AppName.exe", product: "AppName", company: "Vendor");
        var strongSource = Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor");
        var target = Disabled(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor");

        var analysis = Analyze(weakSource, strongSource, target);

        var match = Assert.Single(analysis.Matches);
        Assert.Equal(strongSource.KeyName, match.Source.KeyName);
    }

    // ---------------------------------------------------------------- unrelated icons

    [Fact]
    public void IgnoresUnrelatedHiddenIcons()
    {
        var analysis = Analyze(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            Disabled(@"C:\Windows\System32\Taskmgr.exe", uid: 0, product: "Task Manager", company: "Microsoft"),
            Disabled(@"C:\Program Files\Tailscale\tailscale-ipn.exe", product: "Tailscale", company: "Tailscale Inc"));

        Assert.Empty(analysis.Matches);

        // Nothing shares a file name with an enabled entry, so nothing is even reported as similar.
        Assert.Empty(analysis.Skipped);
    }

    [Fact]
    public void ProducesNoMatchesWhenNothingIsEnabled()
    {
        var analysis = Analyze(
            Disabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            Disabled(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor"));

        Assert.Empty(analysis.Matches);
        Assert.Empty(analysis.Skipped);
    }

    [Fact]
    public void HandlesAnEmptyRegistry()
    {
        var analysis = Analyze();

        Assert.Empty(analysis.Matches);
        Assert.Empty(analysis.Skipped);
        Assert.Empty(analysis.EnabledEntries);
    }

    // ---------------------------------------------------------------- publisher and tooltip fallbacks

    [Fact]
    public void UsesRegistryPublisherWhenFileMetadataIsUnavailable()
    {
        var analysis = Analyze(
            Enabled(@"C:\App\v1.0\AppName.exe", tooltip: "AppName Sync", publisher: "Vendor Inc", exists: null),
            Disabled(@"C:\App\v1.1\AppName.exe", tooltip: "AppName Sync", publisher: "Vendor Inc", exists: null));

        var match = Assert.Single(analysis.Matches);
        Assert.Contains(MatchSignal.NormalizedDirectory, match.Decision.Signals);
        Assert.Contains(MatchSignal.TooltipAndCompany, match.Decision.Signals);
    }

    [Fact]
    public void RealWorldWisprFlowScenario()
    {
        // Reproduces the machine that motivated TrayKeeper: six entries, two enabled, and three of
        // the hidden ones pointing at versions that have since been deleted.
        const string root = @"C:\Users\Alex\AppData\Local\WisprFlow";

        var analysis = Analyze(
            Unset($@"{root}\app-1.5.1095\Wispr Flow.exe", exists: false),
            Unset($@"{root}\app-1.6.30\Wispr Flow.exe", exists: false),
            Unset($@"{root}\app-1.6.122\Wispr Flow.exe", exists: false),
            Unset($@"{root}\app-1.6.168\Wispr Flow.exe", product: "Wispr Flow", company: "Wispr AI"),
            Enabled($@"{root}\app-1.6.182\Wispr Flow.exe", product: "Wispr Flow", company: "Wispr AI"),
            Enabled($@"{root}\app-1.6.224\Wispr Flow.exe", product: "Wispr Flow", company: "Wispr AI"));

        var match = Assert.Single(analysis.Matches);
        Assert.EndsWith(@"app-1.6.168\Wispr Flow.exe", match.Target.ResolvedExecutablePath);

        Assert.Equal(3, analysis.Skipped.Count);
        Assert.All(analysis.Skipped, s => Assert.Equal(SkipReason.ExecutableMissing, s.Reason));
    }
}
