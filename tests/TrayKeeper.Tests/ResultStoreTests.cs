using TrayKeeper.Logging;
using TrayKeeper.Services;

namespace TrayKeeper.Tests;

public sealed class ResultStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "TrayKeeperTests", Guid.NewGuid().ToString("N"));

    private string Path_(string name) => Path.Combine(_directory, name);

    [Fact]
    public void RoundTripsAResult()
    {
        var store = new ResultStore(NullLog.Instance, Path_("r.json"), Path_("c.json"));

        var original = new RefreshResult
        {
            Timestamp = new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero),
            Outcome = RefreshOutcome.ChangesApplied,
            TotalEntries = 57,
            EnabledEntries = 9,
            Applied = new[]
            {
                new AppliedChange
                {
                    KeyName = "123",
                    OriginalIsPromoted = null,
                    TargetPath = @"C:\App\app-1.1.0\App.exe",
                    SourcePath = @"C:\App\app-1.0.0\App.exe",
                    DisplayName = "App",
                    Reason = "same executable name",
                },
            },
        };

        store.SaveResult(original);
        var reloaded = store.LoadResult();

        Assert.NotNull(reloaded);
        Assert.Equal(original.Outcome, reloaded!.Outcome);
        Assert.Equal(57, reloaded.TotalEntries);

        // The distinction between "absent" and "0" must survive serialisation, or an undo
        // would write an explicit 0 where Windows had no value at all.
        Assert.Null(Assert.Single(reloaded.Applied).OriginalIsPromoted);
    }

    [Fact]
    public void ReturnsNullWhenNoResultHasBeenSaved()
    {
        var store = new ResultStore(NullLog.Instance, Path_("missing.json"), Path_("c.json"));
        Assert.Null(store.LoadResult());
    }

    [Fact]
    public void ReturnsAnEmptyListWhenThereIsNothingToUndo()
    {
        var store = new ResultStore(NullLog.Instance, Path_("r.json"), Path_("missing.json"));
        Assert.Empty(store.LoadChanges());
    }

    [Fact]
    public void SurvivesCorruptJson()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_("r.json"), "{ this is not json");

        var store = new ResultStore(NullLog.Instance, Path_("r.json"), Path_("c.json"));

        Assert.Null(store.LoadResult());
    }

    [Fact]
    public void WritingToAPathWithNoDirectoryComponentDoesNotThrow()
    {
        // Regression: Path.GetDirectoryName returns null for a bare file name, which previously
        // escaped as an ArgumentNullException and aborted the whole refresh.
        var store = new ResultStore(NullLog.Instance, "traykeeper-test-result.json", "traykeeper-test-changes.json");

        store.SaveResult(new RefreshResult { Outcome = RefreshOutcome.NoDuplicatesFound });

        try
        {
            Assert.NotNull(store.LoadResult());
        }
        finally
        {
            foreach (var name in new[] { "traykeeper-test-result.json", "traykeeper-test-changes.json" })
            {
                if (File.Exists(name)) File.Delete(name);
            }
        }
    }

    [Fact]
    public void FallsBackToTheDefaultLocationForBlankPaths()
    {
        // Regression: a blank path must fall back to the default location rather than being kept
        // verbatim, which produced a path whose directory resolved to null and threw on first use.
        // Only reachability is asserted — the default location is real user state, so its
        // contents are not this test's business.
        var store = new ResultStore(NullLog.Instance, "   ", string.Empty);

        store.LoadResult();
        store.LoadChanges();
    }

    [Fact]
    public void ClearChangesRemovesTheUndoRecord()
    {
        var store = new ResultStore(NullLog.Instance, Path_("r.json"), Path_("c.json"));

        store.SaveChanges(new[]
        {
            new AppliedChange
            {
                KeyName = "1", TargetPath = "t", SourcePath = "s", DisplayName = "d", Reason = "r",
            },
        });

        Assert.Single(store.LoadChanges());

        store.ClearChanges();

        Assert.Empty(store.LoadChanges());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
