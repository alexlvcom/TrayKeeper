using TrayKeeper.Matching;

namespace TrayKeeper.Tests;

public class PathNormalizerTests
{
    [Theory]
    [InlineData("app-1.5.1095", "app-#")]
    [InlineData("app-1.6.224", "app-#")]
    [InlineData("1.6.224", "#")]
    [InlineData("v2.0", "#")]
    [InlineData("PhpStorm 2025.1.7", "phpstorm #")]
    [InlineData("Claude_1.22209.3.0_x64__pzs8sxrjxfjjc", "claude_#_x64__pzs8sxrjxfjjc")]
    public void CollapsesVersionTokens(string segment, string expected) =>
        Assert.Equal(expected, PathNormalizer.NormalizeSegment(segment));

    [Theory]
    [InlineData("Office16")]
    [InlineData("WisprFlow")]
    [InlineData("current")]
    [InlineData("bin")]
    [InlineData("win32")]
    [InlineData("x64")]
    public void LeavesNonVersionSegmentsAlone(string segment) =>
        Assert.Equal(segment.ToLowerInvariant(), PathNormalizer.NormalizeSegment(segment));

    [Fact]
    public void DoesNotTreatASingleNumberAsAVersion()
    {
        // A bare integer is far more likely to be part of a product name than a version.
        Assert.Equal("office16", PathNormalizer.NormalizeSegment("Office16"));
        Assert.Equal("v2", PathNormalizer.NormalizeSegment("v2"));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.0.0")]
    [InlineData("1.0.0.0")]
    [InlineData("v1.2.3")]
    public void RecognisesPureVersionSegments(string segment) =>
        Assert.True(PathNormalizer.IsVersionSegment(segment));

    [Fact]
    public void NormalizesTwoVersionedDirectoriesToTheSameValue()
    {
        var first = PathNormalizer.NormalizeDirectory(@"C:\Users\Alex\AppData\Local\WisprFlow\app-1.5.1095");
        var second = PathNormalizer.NormalizeDirectory(@"c:\users\alex\appdata\local\wisprflow\app-1.6.224");

        Assert.Equal(first, second);
        Assert.Equal(@"c:\users\alex\appdata\local\wisprflow\app-#", first);
    }

    [Fact]
    public void KeepsDistinctApplicationsDistinct()
    {
        var alpha = PathNormalizer.NormalizeDirectory(@"C:\Program Files\Vendor\Alpha");
        var beta = PathNormalizer.NormalizeDirectory(@"C:\Program Files\Vendor\Beta");

        Assert.NotEqual(alpha, beta);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(@"\\", "")]
    public void HandlesMalformedDirectories(string? input, string expected) =>
        Assert.Equal(expected, PathNormalizer.NormalizeDirectory(input));

    [Fact]
    public void NormalizesFullExecutablePaths()
    {
        Assert.Equal(@"c:\app\app-#\appname.exe",
            PathNormalizer.NormalizeExecutablePath(@"C:\App\app-1.2.3\AppName.exe"));
    }

    [Fact]
    public void NormalizesUncPaths()
    {
        var result = PathNormalizer.NormalizeDirectory(@"\\server\share\App\app-1.2.3");
        Assert.Equal(@"server\share\app\app-#", result);
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_1.22209.3.0_x64__pzs8sxrjxfjjc\app\claude.exe", "claude_pzs8sxrjxfjjc")]
    [InlineData(@"C:\Program Files\WindowsApps\MSTeams_26163.405.4842.717_x64__8wekyb3d8bbwe\ms-teams.exe", "msteams_8wekyb3d8bbwe")]
    [InlineData(@"C:\Program Files\WindowsApps\SAMSUNGELECTRONICSCoLtd.SamsungNotes_4.3.743.0_x64__wyx1vj98g3asy\a.exe",
        "samsungelectronicscoltd.samsungnotes_wyx1vj98g3asy")]
    public void ExtractsPackageFamilyName(string path, string expected) =>
        Assert.Equal(expected, PathNormalizer.TryGetPackageFamilyName(path));

    [Theory]
    [InlineData(@"C:\Users\Alex\AppData\Local\Discord\app-1.0.9248\Discord.exe")]
    [InlineData(@"C:\Program Files\Vendor\App\app.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\NotAPackageFullName\app.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void ReturnsNoPackageFamilyForUnpackagedPaths(string? path) =>
        Assert.Null(PathNormalizer.TryGetPackageFamilyName(path));

    [Fact]
    public void PackageFamilyIgnoresVersionAndArchitecture()
    {
        var v1 = PathNormalizer.TryGetPackageFamilyName(
            @"C:\Program Files\WindowsApps\Claude_1.13576.4.0_x64__pzs8sxrjxfjjc\app\claude.exe");
        var v2 = PathNormalizer.TryGetPackageFamilyName(
            @"C:\Program Files\WindowsApps\Claude_1.22209.3.0_x64__pzs8sxrjxfjjc\app\claude.exe");

        Assert.Equal(v1, v2);
    }
}
