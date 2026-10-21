using TrayKeeper.Ui;

namespace TrayKeeper.Tests;

public sealed class WhatsNewSinceTests
{
    private const string Sample =
        "# Changelog\n\n## 1.4.2 - 2026-10-21\n\n### Main changes\n\n- Fix\n\n" +
        "## 1.4.1 - 2026-10-20\n\n### Main changes\n\n- Highlights\n\n### Other updates\n\n- Detail\n\n" +
        "## [1.4.0] - 2026-10-20\n\n### Main changes\n\n- Redesign\n\n" +
        "## [1.3.1] - 2026-10-19\n\n- Untitled fix\n";

    [Fact]
    public void UpgradeShowsEveryReleaseSinceThePreviousOneNewestFirst()
    {
        var releases = Changelog.MainChangesSince(Changelog.Parse(Sample), new Version(1, 4, 0), "1.4.2");

        Assert.Equal(new[] { "1.4.2", "1.4.1" }, releases.Select(r => r.Version));
        var notes = releases.SelectMany(r => r.Notes).ToArray();
        Assert.Contains("Highlights", notes);
        Assert.DoesNotContain("Detail", notes);
        Assert.DoesNotContain("Redesign", notes);
    }

    [Fact]
    public void FreshInstallShowsOnlyTheCurrentRelease()
    {
        var releases = Changelog.MainChangesSince(Changelog.Parse(Sample), null, "1.4.2");

        Assert.Equal("1.4.2", Assert.Single(releases).Version);
    }

    [Fact]
    public void ReleaseWithoutMainChangesKeepsItsNotes()
    {
        var releases = Changelog.MainChangesSince(Changelog.Parse(Sample), new Version(1, 3, 0), "1.3.1");

        Assert.Equal(new[] { "Untitled fix" }, Assert.Single(releases).Notes);
    }

    [Fact]
    public void PreviousReleaseComesFromTheRecordOrFromSettingsOfOlderReleases()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TrayKeeperTests", Guid.NewGuid().ToString("N"));
        var seen = Path.Combine(directory, "last-seen-release.txt");
        var settings = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            var state = new WhatsNewState(seen, settings);
            Assert.Null(state.PreviousRelease());
            File.WriteAllText(settings, "{ \"AutoRefreshEnabled\": true }");
            Assert.Equal(new Version(1, 3, 2), state.PreviousRelease());
            File.WriteAllText(settings, "{ \"Theme\": 0 }");
            Assert.Equal(new Version(1, 4, 0), state.PreviousRelease());
            state.MarkShown("1.4.1");
            Assert.Equal(new Version(1, 4, 1), state.PreviousRelease());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
