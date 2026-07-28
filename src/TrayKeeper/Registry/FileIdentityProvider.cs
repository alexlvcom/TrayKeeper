using System.Diagnostics;
using TrayKeeper.Model;

namespace TrayKeeper.Registry;

public interface IFileIdentityProvider
{
    FileIdentity Get(string executablePath);
}

/// <summary>
/// Reads version metadata from executables, caching per path. Failures are absorbed: a file we
/// cannot open contributes no signals rather than aborting the refresh.
/// </summary>
public sealed class FileIdentityProvider : IFileIdentityProvider
{
    private readonly Dictionary<string, FileIdentity> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public FileIdentity Get(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return FileIdentity.Unknown;

        if (_cache.TryGetValue(executablePath, out var cached)) return cached;

        var identity = Read(executablePath);
        _cache[executablePath] = identity;
        return identity;
    }

    private static FileIdentity Read(string executablePath)
    {
        bool? exists;
        try
        {
            exists = File.Exists(executablePath);

            // A packaged application lives under an ACL that can deny probing. Treat that as
            // "unknown" rather than "missing", otherwise every MSIX entry looks stale.
            if (exists == false && IsAccessRestricted(executablePath)) exists = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            exists = null;
        }

        if (exists != true)
        {
            return new FileIdentity { Exists = exists };
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return new FileIdentity
            {
                Exists = true,
                ProductName = Clean(info.ProductName),
                FileDescription = Clean(info.FileDescription),
                CompanyName = Clean(info.CompanyName),
                ProductVersion = Clean(info.ProductVersion),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new FileIdentity { Exists = true };
        }
    }

    private static bool IsAccessRestricted(string path) =>
        path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
