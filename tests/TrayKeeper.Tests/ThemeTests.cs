using AlexLV.WindowsUi;
using TrayKeeper.Logging;
using TrayKeeper.Settings;

namespace TrayKeeper.Tests;

public sealed class ThemeTests : IDisposable
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
    public void TheThemeFollowsWindowsByDefault() => Assert.Equal(AppTheme.System, new AppSettings().Theme);

    [Theory]
    [InlineData(AppTheme.System, true, false)]
    [InlineData(AppTheme.System, false, true)]
    [InlineData(AppTheme.Light, false, false)]
    [InlineData(AppTheme.Dark, true, true)]
    public void SystemResolvesFromTheWindowsAppMode(AppTheme choice, bool windowsUsesLight, bool expectDark) =>
        Assert.Equal(expectDark, ThemeManager.Resolve(choice, windowsUsesLight));

    [Fact]
    public void TheChosenThemeSurvivesASaveAndLoad()
    {
        var store = new SettingsStore(NullLog.Instance, SettingsPath);
        store.Save(new AppSettings { Theme = AppTheme.Dark });

        Assert.Equal(AppTheme.Dark, store.Load().Theme);
    }

    [Fact]
    public void AHandEditedUnknownThemeFallsBackToSystem()
    {
        File.WriteAllText(SettingsPath, "{ \"Theme\": 42 }");

        Assert.Equal(AppTheme.System, new SettingsStore(NullLog.Instance, SettingsPath).Load().Theme);
    }

    [Fact]
    public void ADraftThemeLeavesTheRunningSettingsUnchanged()
    {
        var running = new AppSettings { Theme = AppTheme.Light };
        var draft = running.Clone();

        draft.Theme = AppTheme.Dark;

        Assert.Equal(AppTheme.Light, running.Theme);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
