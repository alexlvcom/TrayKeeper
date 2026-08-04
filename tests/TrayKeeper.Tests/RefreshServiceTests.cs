using TrayKeeper.Logging;
using TrayKeeper.Model;
using TrayKeeper.Registry;
using TrayKeeper.Services;
using TrayKeeper.Settings;
using static TrayKeeper.Tests.EntryBuilder;

namespace TrayKeeper.Tests;

/// <summary>
/// A registry substitute that records every write, so tests can assert on ordering, undo
/// fidelity, and failure handling without touching HKCU.
/// </summary>
internal sealed class FakeNotifyIconRegistry : INotifyIconRegistry
{
    private readonly List<NotifyIconEntry> _entries;

    public FakeNotifyIconRegistry(params NotifyIconEntry[] entries) => _entries = entries.ToList();

    public List<(string KeyName, bool? Promoted)> Writes { get; } = new();

    public int BackupReads { get; private set; }

    /// <summary>Keys whose write should fail, simulating a deleted or locked entry.</summary>
    public HashSet<string> FailingKeys { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<NotifyIconEntry> Read() => _entries;

    public void SetPromoted(string keyName, bool promoted)
    {
        if (FailingKeys.Contains(keyName))
        {
            throw new InvalidOperationException($"Entry {keyName} no longer exists.");
        }

        Writes.Add((keyName, promoted));
    }

    public void RemovePromoted(string keyName)
    {
        if (FailingKeys.Contains(keyName))
        {
            throw new InvalidOperationException($"Entry {keyName} no longer exists.");
        }

        Writes.Add((keyName, null));
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadRawForBackup()
    {
        BackupReads++;
        return _entries
            .Select(IReadOnlyDictionary<string, object?> (e) => new Dictionary<string, object?>
            {
                ["Key"] = e.KeyName,
                ["ExecutablePath"] = e.RawExecutablePath,
                ["IsPromoted"] = e.IsPromoted is null ? null : e.IsPromoted.Value ? 1 : 0,
            })
            .ToList();
    }
}

/// <summary>Captures log lines so the quiet-automatic-refresh policy can be asserted.</summary>
internal sealed class RecordingLog : ILog
{
    public List<string> Lines { get; } = new();

    public void Debug(string message) => Lines.Add(message);
    public void Info(string message) => Lines.Add(message);
    public void Warn(string message) => Lines.Add(message);
    public void Error(string message, Exception? exception = null) => Lines.Add(message);

    public int Count(string fragment) => Lines.Count(line => line.Contains(fragment, StringComparison.Ordinal));
}

public sealed class RefreshServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "TrayKeeperTests", Guid.NewGuid().ToString("N"));

    private RefreshService CreateService(FakeNotifyIconRegistry registry, out ResultStore resultStore) =>
        CreateService(registry, NullLog.Instance, out resultStore);

    private RefreshService CreateService(FakeNotifyIconRegistry registry, ILog log, out ResultStore resultStore)
    {
        Directory.CreateDirectory(_directory);

        resultStore = new ResultStore(log,
            Path.Combine(_directory, "last-result.json"),
            Path.Combine(_directory, "last-changes.json"));

        var backup = new BackupService(registry, log, Path.Combine(_directory, "Backups"));
        var settings = new SettingsStore(log, Path.Combine(_directory, "settings.json"));

        return new RefreshService(registry, backup, resultStore, settings, log,
            () => new DateTimeOffset(2026, 7, 28, 10, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void PromotesADuplicateAndReportsIt()
    {
        var source = Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor");
        var target = Unset(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor");
        var registry = new FakeNotifyIconRegistry(source, target);

        var result = CreateService(registry, out _).Refresh();

        Assert.Equal(RefreshOutcome.ChangesApplied, result.Outcome);
        Assert.Equal("Enabled 1 duplicate tray icon entry.", result.Summary);

        var write = Assert.Single(registry.Writes);
        Assert.Equal(target.KeyName, write.KeyName);
        Assert.True(write.Promoted);

        var applied = Assert.Single(result.Applied);
        Assert.Null(applied.OriginalIsPromoted);
        Assert.Equal(source.ResolvedExecutablePath, applied.SourcePath);
    }

    [Fact]
    public void WritesABackupBeforeChangingAnything()
    {
        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            Unset(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor"));

        var result = CreateService(registry, out _).Refresh();

        Assert.Equal(1, registry.BackupReads);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
    }

    [Fact]
    public void DoesNotBackUpWhenThereIsNothingToChange()
    {
        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"));

        var result = CreateService(registry, out _).Refresh();

        Assert.Equal(RefreshOutcome.NoDuplicatesFound, result.Outcome);
        Assert.Equal("No disabled duplicate tray icons were found.", result.Summary);
        Assert.Equal(0, registry.BackupReads);
        Assert.Empty(registry.Writes);
    }

    [Fact]
    public void ReportsFailureWhenTheRegistryIsEmpty()
    {
        var result = CreateService(new FakeNotifyIconRegistry(), out _).Refresh();

        Assert.Equal(RefreshOutcome.Failed, result.Outcome);
        Assert.Equal("Refresh failed. See the log for details.", result.Summary);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void RecordsAnErrorWhenOneEntryCannotBeWritten()
    {
        var target = Unset(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor");
        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            target);
        registry.FailingKeys.Add(target.KeyName);

        var result = CreateService(registry, out _).Refresh();

        Assert.Equal(RefreshOutcome.Failed, result.Outcome);
        Assert.Empty(result.Applied);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void RestoreReturnsEachEntryToItsExactPriorState()
    {
        var absentValue = Unset(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor");
        var explicitZero = Disabled(@"C:\App\app-1.2.0\AppName.exe", product: "AppName", company: "Vendor");
        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            absentValue,
            explicitZero);

        var service = CreateService(registry, out _);
        service.Refresh();
        registry.Writes.Clear();

        Assert.True(service.HasRestorableChanges());

        var (restored, errors) = service.RestoreLastChanges();

        Assert.Equal(2, restored);
        Assert.Empty(errors);

        // The entry that had no IsPromoted value must have the value deleted, not set to 0.
        Assert.Contains((absentValue.KeyName, (bool?)null), registry.Writes);
        Assert.Contains((explicitZero.KeyName, (bool?)false), registry.Writes);

        Assert.False(service.HasRestorableChanges());
    }

    [Fact]
    public void RestoreKeepsTheUndoRecordWhenItPartiallyFails()
    {
        var target = Unset(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor");
        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            target);

        var service = CreateService(registry, out _);
        service.Refresh();

        registry.FailingKeys.Add(target.KeyName);
        var (restored, errors) = service.RestoreLastChanges();

        Assert.Equal(0, restored);
        Assert.Single(errors);
        Assert.True(service.HasRestorableChanges());
    }

    [Fact]
    public void PersistsTheResultForTheResultsWindow()
    {
        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            Unset(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor"));

        var service = CreateService(registry, out var resultStore);
        service.Refresh();

        var reloaded = resultStore.LoadResult();

        Assert.NotNull(reloaded);
        Assert.Equal(RefreshOutcome.ChangesApplied, reloaded!.Outcome);
        Assert.Single(reloaded.Applied);
        Assert.Contains("normalized application directory", reloaded.Applied[0].Reason);
    }

    [Fact]
    public void RespectsTheSkipMissingExecutablesSetting()
    {
        Directory.CreateDirectory(_directory);
        var settingsPath = Path.Combine(_directory, "settings.json");
        new SettingsStore(NullLog.Instance, settingsPath)
            .Save(new AppSettings { SkipMissingExecutables = false });

        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.5.0\AppName.exe", product: "AppName", company: "Vendor"),
            Unset(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor", exists: false));

        var service = new RefreshService(
            registry,
            new BackupService(registry, NullLog.Instance, Path.Combine(_directory, "Backups")),
            new ResultStore(NullLog.Instance,
                Path.Combine(_directory, "last-result.json"),
                Path.Combine(_directory, "last-changes.json")),
            new SettingsStore(NullLog.Instance, settingsPath),
            NullLog.Instance);

        var result = service.Refresh();

        Assert.Equal(RefreshOutcome.ChangesApplied, result.Outcome);
        Assert.Single(result.Applied);
    }

    [Fact]
    public void AnAutomaticRefreshAppliesChangesJustLikeAManualOne()
    {
        var target = Unset(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor");
        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            target);

        var result = CreateService(registry, out var resultStore).Refresh(RefreshTrigger.Automatic);

        Assert.Equal(RefreshOutcome.ChangesApplied, result.Outcome);
        Assert.Equal((target.KeyName, (bool?)true), Assert.Single(registry.Writes));
        Assert.NotNull(resultStore.LoadResult());
    }

    [Fact]
    public void RepeatedAutomaticRefreshesWithTheSameOutcomeAreLoggedOnce()
    {
        // The timer fires every minute; identical no-op results must not fill the log.
        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"));

        var log = new RecordingLog();
        var service = CreateService(registry, log, out _);

        service.Refresh(RefreshTrigger.Automatic);
        service.Refresh(RefreshTrigger.Automatic);
        service.Refresh(RefreshTrigger.Automatic);

        Assert.Equal(1, log.Count("Refresh result:"));
    }

    [Fact]
    public void EveryManualRefreshIsLogged()
    {
        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"));

        var log = new RecordingLog();
        var service = CreateService(registry, log, out _);

        service.Refresh();
        service.Refresh();

        Assert.Equal(2, log.Count("Refresh result:"));
    }

    [Fact]
    public void AnAutomaticRefreshIsLoggedAgainWhenTheOutcomeChanges()
    {
        var target = Unset(@"C:\App\app-1.1.0\AppName.exe", product: "AppName", company: "Vendor");
        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"),
            target);
        registry.FailingKeys.Add(target.KeyName);

        var log = new RecordingLog();
        var service = CreateService(registry, log, out _);

        service.Refresh(RefreshTrigger.Automatic);
        service.Refresh(RefreshTrigger.Automatic);

        Assert.Equal(1, log.Count("Refresh result:"));
        Assert.Equal(1, log.Count("Could not enable"));

        // The entry becomes writable, so the next tick has something new to say.
        registry.FailingKeys.Clear();
        service.Refresh(RefreshTrigger.Automatic);

        Assert.Equal(2, log.Count("Refresh result:"));
    }

    [Fact]
    public void ARepeatedAutomaticNoOpDoesNotRewriteTheStoredResult()
    {
        var registry = new FakeNotifyIconRegistry(
            Enabled(@"C:\App\app-1.0.0\AppName.exe", product: "AppName", company: "Vendor"));

        var service = CreateService(registry, out var resultStore);

        // The first no-op is a new outcome, so it is recorded.
        service.Refresh(RefreshTrigger.Automatic);
        Assert.Equal(RefreshOutcome.NoDuplicatesFound, resultStore.LoadResult()!.Outcome);

        // A marker standing in for a report written elsewhere: an identical tick must leave it alone
        // rather than rewriting last-result.json every minute.
        resultStore.SaveResult(new RefreshResult
        {
            Timestamp = new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero),
            Outcome = RefreshOutcome.ChangesApplied,
        });

        service.Refresh(RefreshTrigger.Automatic);

        Assert.Equal(RefreshOutcome.ChangesApplied, resultStore.LoadResult()!.Outcome);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best effort.
        }
    }
}
