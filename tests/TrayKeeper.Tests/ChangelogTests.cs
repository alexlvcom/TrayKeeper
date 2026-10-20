using System.Reflection;
using TrayKeeper;

namespace TrayKeeper.Tests;

public class ChangelogTests
{
    [Fact]
    public void HasAtLeastOneEntry() => Assert.NotEmpty(Changelog.Entries);

    [Fact]
    public void MarketingVersionComesFromTheNewestEntry() =>
        Assert.Equal(Changelog.Entries[0].Version, Changelog.MarketingVersion);

    [Fact]
    public void EveryVersionIsParsableAndUnique()
    {
        var versions = Changelog.Entries.Select(e => Version.Parse(e.Version)).ToList();
        Assert.Equal(versions.Count, versions.Distinct().Count());
    }

    [Fact]
    public void EntriesAreOrderedNewestFirst()
    {
        var versions = Changelog.Entries.Select(e => Version.Parse(e.Version)).ToList();
        var descending = versions.OrderByDescending(v => v).ToList();

        Assert.Equal(descending, versions);
    }

    [Fact]
    public void EveryDateIsAValidIsoDate()
    {
        foreach (var (version, date, _) in Changelog.Entries)
        {
            Assert.True(
                DateTime.TryParseExact(date, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out _),
                $"Entry v{version} has a date that is not yyyy-MM-dd: '{date}'");
        }
    }

    [Fact]
    public void EveryEntryHasNotes()
    {
        foreach (var (version, _, notes) in Changelog.Entries)
        {
            Assert.NotEmpty(notes);
            Assert.All(notes, note => Assert.False(string.IsNullOrWhiteSpace(note),
                $"Entry v{version} has a blank note."));
        }
    }

    [Fact]
    public void TheRunningVersionHasTheNewestChangelogHeading()
    {
        // 1.3.2 shipped reporting 1.3.1 from a stale coded history. All three parts must agree:
        // the assembly version, the newest CHANGELOG.md heading and the release notes About shows.
        var assembly = typeof(Changelog).Assembly.GetName().Version!;

        Assert.Equal(Changelog.Entries[0].Version, Changelog.MarketingVersion);
        Assert.Equal($"{assembly.Major}.{assembly.Minor}.{assembly.Build}", Changelog.MarketingVersion);
        Assert.NotNull(Changelog.Current);
    }

    [Fact]
    public void ParsesHeadingsSectionsAndBullets()
    {
        var entries = Changelog.Parse("""
            # Changelog

            ## [2.0.0] - 2026-11-01

            ### Main changes
            - First `big` change
              continued on the next line.

            ### Other updates
            - Small fix

            ## [1.9.0] - 2026-10-01
            - Plain bullet
            """);

        Assert.Equal(new[] { "2.0.0", "1.9.0" }, entries.Select(e => e.Version));
        Assert.Equal("2026-11-01", entries[0].Date);
        Assert.Equal(new[] { "Main changes", "Other updates" }, entries[0].Sections.Select(s => s.Heading));
        Assert.Equal("First big change continued on the next line.", entries[0].Sections[0].Bullets[0]);
        Assert.Null(entries[1].Sections[0].Heading);
        Assert.Equal(new[] { "Plain bullet" }, entries[1].Notes);
    }

    [Fact]
    public void ReleaseNotesForAMissingVersionSayThereAreNone() =>
        Assert.Equal("No release notes available for this version.", Changelog.GetReleaseNotes("0.0.1"));

    [Fact]
    public void BuildVersionIsReported() =>
        Assert.False(string.IsNullOrWhiteSpace(Changelog.BuildVersion));

    [Fact]
    public void FormattedTextContainsTheKeyFacts()
    {
        var text = Changelog.FormatFull();

        Assert.Contains($"{AppInfo.DisplayName}  v{Changelog.MarketingVersion}", text);
        Assert.Contains("What's changed", text);
        Assert.Contains(@"HKCU\Control Panel\NotifyIconSettings", text);

        // Every version must appear in the rendered history.
        foreach (var (version, _, _) in Changelog.Entries)
        {
            Assert.Contains($"v{version}", text);
        }
    }

    [Fact]
    public void FormattedTextRendersEveryNote()
    {
        var text = Changelog.FormatFull();

        foreach (var (_, _, notes) in Changelog.Entries)
        {
            foreach (var note in notes) Assert.Contains(note, text);
        }
    }

    [Fact]
    public void BodyOmitsTheVersionHeaderTheAboutWindowAlreadyShows()
    {
        var body = Changelog.FormatBody();

        Assert.DoesNotContain($"{AppInfo.DisplayName}  v{Changelog.MarketingVersion}", body);
        Assert.DoesNotContain("Build ", body);

        // …but still carries the substance.
        Assert.Contains("What's changed", body);
        Assert.Contains(@"HKCU\Control Panel\NotifyIconSettings", body);
    }

    [Fact]
    public void FullTextIsTheHeaderPlusTheBody() =>
        Assert.EndsWith(Changelog.FormatBody(), Changelog.FormatFull());

    [Fact]
    public void FullTextCreditsTheAuthor()
    {
        var text = Changelog.FormatFull();

        Assert.Contains(Changelog.Author, text);
        Assert.Contains(Changelog.AuthorEmail, text);
        Assert.Contains(Changelog.Copyright, text);
    }

    [Fact]
    public void AssemblyMetadataMatchesTheDeclaredAuthor()
    {
        // Guards against the .csproj and Changelog.cs drifting apart on authorship.
        var assembly = typeof(Changelog).Assembly;

        Assert.Equal(Changelog.Author,
            assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
        Assert.Equal(Changelog.Copyright,
            assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
    }
}
