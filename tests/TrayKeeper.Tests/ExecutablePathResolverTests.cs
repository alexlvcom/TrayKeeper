using TrayKeeper.Registry;

namespace TrayKeeper.Tests;

public class ExecutablePathResolverTests
{
    private static readonly Guid ProgramFilesX64 = new("6D809377-6AF0-444B-8957-A3773F02200E");
    private static readonly Guid WindowsFolder = new("F38BF404-1D43-42F2-9305-67DE0B28FC23");
    private static readonly Guid SystemFolder = new("1AC14E77-02E7-4E5D-B744-2EB1AE5198B7");

    private static readonly IKnownFolderResolver Resolver = new StaticKnownFolderResolver(
        new Dictionary<Guid, string>
        {
            [ProgramFilesX64] = @"C:\Program Files",
            [WindowsFolder] = @"C:\WINDOWS",
            [SystemFolder] = @"C:\WINDOWS\System32",
        });

    [Fact]
    public void ExpandsAKnownFolderPrefix()
    {
        var (path, hadPrefix, resolved) = ExecutablePathResolver.Resolve(
            @"{6D809377-6AF0-444B-8957-A3773F02200E}\WindowsApps\Claude_1.0.0.0_x64__abc\app\claude.exe", Resolver);

        Assert.Equal(@"C:\Program Files\WindowsApps\Claude_1.0.0.0_x64__abc\app\claude.exe", path);
        Assert.True(hadPrefix);
        Assert.True(resolved);
    }

    [Fact]
    public void ExpandsTheSystemFolderPrefix()
    {
        var (path, _, resolved) = ExecutablePathResolver.Resolve(
            @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\Taskmgr.exe", Resolver);

        Assert.Equal(@"C:\WINDOWS\System32\Taskmgr.exe", path);
        Assert.True(resolved);
    }

    [Fact]
    public void LeavesAPlainPathUnchanged()
    {
        const string original = @"C:\Users\Alex\AppData\Local\WisprFlow\app-1.6.224\Wispr Flow.exe";

        var (path, hadPrefix, resolved) = ExecutablePathResolver.Resolve(original, Resolver);

        Assert.Equal(original, path);
        Assert.False(hadPrefix);
        Assert.True(resolved);
    }

    [Fact]
    public void ReportsAnUnknownFolderIdWithoutLosingTheValue()
    {
        const string original = @"{00000000-0000-0000-0000-000000000000}\app.exe";

        var (path, hadPrefix, resolved) = ExecutablePathResolver.Resolve(original, Resolver);

        Assert.Equal(original, path);
        Assert.True(hadPrefix);
        Assert.False(resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void HandlesEmptyValues(string? raw)
    {
        var (path, hadPrefix, resolved) = ExecutablePathResolver.Resolve(raw, Resolver);

        Assert.Equal(string.Empty, path);
        Assert.False(hadPrefix);
        Assert.False(resolved);
    }

    [Theory]
    [InlineData("{not-a-guid}\\app.exe")]
    [InlineData("{unterminated")]
    public void HandlesMalformedPrefixes(string raw)
    {
        var (path, _, resolved) = ExecutablePathResolver.Resolve(raw, Resolver);

        Assert.Equal(raw, path);
        Assert.False(resolved);
    }

    [Fact]
    public void ResolvesAPrefixWithNoRemainder()
    {
        var (path, _, resolved) = ExecutablePathResolver.Resolve(
            "{F38BF404-1D43-42F2-9305-67DE0B28FC23}", Resolver);

        Assert.Equal(@"C:\WINDOWS", path);
        Assert.True(resolved);
    }

    [Fact]
    public void ShellResolverReturnsRealPathsOnWindows()
    {
        var shell = new ShellKnownFolderResolver();

        var windows = shell.Resolve(WindowsFolder);
        var programFiles = shell.Resolve(ProgramFilesX64);

        Assert.False(string.IsNullOrEmpty(windows));
        Assert.False(string.IsNullOrEmpty(programFiles));
        Assert.True(Directory.Exists(windows));

        // Cached second call must agree.
        Assert.Equal(windows, shell.Resolve(WindowsFolder));
    }
}
