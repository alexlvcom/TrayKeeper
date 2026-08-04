using TrayKeeper.Logging;
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
