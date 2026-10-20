using TrayKeeper.Logging;
using TrayKeeper.Services;
using TrayKeeper.Settings;

namespace TrayKeeper.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "TrayKeeperTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath
    {
        get
        {
            Directory.CreateDirectory(_directory);
            return Path.Combine(_directory, "settings.json");
        }
    }

    [Fact]
    public void AutoRefreshIsOnEveryMinuteByDefault()
    {
        var settings = new AppSettings();

        Assert.True(settings.AutoRefreshEnabled);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.AutoRefreshInterval);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(15, 15)]
    [InlineData(10_000, 1440)]
    public void AHandEditedIntervalIsClamped(int stored, int expectedMinutes)
    {
        // settings.json is editable, and a zero or negative interval would throw on the timer.
        var settings = new AppSettings { AutoRefreshIntervalMinutes = stored };

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), settings.AutoRefreshInterval);
    }

    [Fact]
    public void TheAutoRefreshToggleSurvivesASaveAndLoad()
    {
        var store = new SettingsStore(NullLog.Instance, SettingsPath);
        store.Save(new AppSettings { AutoRefreshEnabled = false, AutoRefreshIntervalMinutes = 5 });

        var loaded = store.Load();

        Assert.False(loaded.AutoRefreshEnabled);
        Assert.Equal(TimeSpan.FromMinutes(5), loaded.AutoRefreshInterval);
    }

    [Fact]
    public void EditingASettingsDraftLeavesTheRunningSettingsUnchanged()
    {
        var running = new AppSettings { CheckFrequency = UpdateCheckFrequency.Daily, AutoRefreshIntervalMinutes = 5 };

        var draft = running.Clone();
        draft.CheckFrequency = UpdateCheckFrequency.Hourly;
        draft.AutoRefreshIntervalMinutes = 30;

        Assert.Equal(UpdateCheckFrequency.Daily, running.CheckFrequency);
        Assert.Equal(5, running.AutoRefreshIntervalMinutes);
    }

    [Fact]
    public void ASuccessfulSaveReportsNoErrorAndLeavesNoTempFile()
    {
        var path = SettingsPath;
        var store = new SettingsStore(NullLog.Instance, path);

        Assert.True(store.Save(new AppSettings { CheckFrequency = UpdateCheckFrequency.Hourly }, out var error));
        Assert.Null(error);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(UpdateCheckFrequency.Hourly, store.Load().CheckFrequency);
    }

    [Fact]
    public void AFailedSaveIsReportedSoSettingsCanShowAnError()
    {
        // A directory in place of settings.json makes the write fail.
        var path = SettingsPath;
        Directory.CreateDirectory(path);
        var store = new SettingsStore(NullLog.Instance, path);

        Assert.False(store.Save(new AppSettings(), out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void SettingsFilesFromAnEarlierVersionGetAutoRefreshOn()
    {
        // A 1.1.0 settings.json has no auto-refresh keys; the upgrade must enable the feature.
        var path = SettingsPath;
        File.WriteAllText(path, """{"RunRefreshOnStartup":true}""");

        var loaded = new SettingsStore(NullLog.Instance, path).Load();

        Assert.True(loaded.RunRefreshOnStartup);
        Assert.True(loaded.AutoRefreshEnabled);
        Assert.Equal(TimeSpan.FromMinutes(1), loaded.AutoRefreshInterval);
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
