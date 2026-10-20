using TrayKeeper.Ui;

namespace TrayKeeper.Tests;

public class UpdateResultTests
{
    private static readonly DateTime CheckedAt = new(2026, 10, 20, 13, 30, 0);

    [Fact]
    public void BeingCurrentSaysTheLatestVersionInPlace()
    {
        var status = UpdateStatus.Latest("1.4.0", CheckedAt);

        Assert.Equal(UpdateState.UpToDate, status.State);
        Assert.Equal("You're running the latest version (v1.4.0)", status.Title);
        Assert.Equal("Last checked 2026-10-20 13:30", status.Detail);
    }

    [Fact]
    public void AFailedCheckShowsItsReasonInPlace()
    {
        var status = UpdateStatus.CheckFailed("1.4.0", "No such host is known.");

        Assert.Equal(UpdateState.Failed, status.State);
        Assert.Contains("No such host is known.", status.Detail);
        Assert.Contains("v1.4.0", status.Detail);
    }

    [Fact]
    public void NoStatusUsesInstallerWording()
    {
        var all = new[]
        {
            UpdateStatus.Latest("1.4.0", CheckedAt),
            UpdateStatus.CheckFailed("1.4.0", "offline"),
            UpdateStatus.Newer("1.4.0", new Version(1, 4, 1), CheckedAt),
        };

        Assert.All(all, s => Assert.DoesNotContain("installer", s.Title + s.Detail, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BeingCurrentOrFailingNeverOpensADialog(bool manual) =>
        Assert.False(TrayApplicationContext.OffersUpdateDialog(available: null, manual, lastOffered: null));

    [Fact]
    public void AManualCheckOffersANewerReleaseEveryTime() =>
        Assert.True(TrayApplicationContext.OffersUpdateDialog(new Version(1, 4, 1), manual: true, lastOffered: new Version(1, 4, 1)));

    [Fact]
    public void AnAutomaticCheckOffersEachNewerReleaseOnce()
    {
        Assert.True(TrayApplicationContext.OffersUpdateDialog(new Version(1, 4, 1), manual: false, lastOffered: null));
        Assert.False(TrayApplicationContext.OffersUpdateDialog(new Version(1, 4, 1), manual: false, lastOffered: new Version(1, 4, 1)));
    }
}
