using System.Runtime.InteropServices;

namespace TrayKeeper.Registry;

public interface IKnownFolderResolver
{
    /// <summary>Returns the filesystem path for a KNOWNFOLDERID, or <c>null</c> when unavailable.</summary>
    string? Resolve(Guid folderId);
}

/// <summary>
/// Resolves KNOWNFOLDERID values through the shell, caching results for the process lifetime.
/// Explorer stores <c>ExecutablePath</c> with a known-folder prefix for anything below a
/// well-known root, so this expansion is a prerequisite for reading file metadata at all.
/// </summary>
public sealed class ShellKnownFolderResolver : IKnownFolderResolver
{
    private readonly Dictionary<Guid, string?> _cache = new();
    private readonly object _gate = new();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    public string? Resolve(Guid folderId)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(folderId, out var cached)) return cached;

            var resolved = ResolveCore(folderId);
            _cache[folderId] = resolved;
            return resolved;
        }
    }

    private static string? ResolveCore(Guid folderId)
    {
        var buffer = IntPtr.Zero;
        try
        {
            // KF_FLAG_DONT_VERIFY (0x4000) so a folder that is not currently present still resolves.
            var hr = SHGetKnownFolderPath(in folderId, 0x4000, IntPtr.Zero, out buffer);
            return hr == 0 && buffer != IntPtr.Zero ? Marshal.PtrToStringUni(buffer) : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeCoTaskMem(buffer);
        }
    }
}

/// <summary>A fixed lookup table, used by tests and as a fallback when the shell call fails.</summary>
public sealed class StaticKnownFolderResolver : IKnownFolderResolver
{
    private readonly IReadOnlyDictionary<Guid, string> _map;

    public StaticKnownFolderResolver(IReadOnlyDictionary<Guid, string> map) => _map = map;

    public string? Resolve(Guid folderId) => _map.TryGetValue(folderId, out var path) ? path : null;
}

public static class ExecutablePathResolver
{
    /// <summary>
    /// Expands a stored <c>ExecutablePath</c>. Values may be a plain path
    /// (<c>C:\Users\...\app.exe</c>) or a known-folder reference
    /// (<c>{6D809377-6AF0-444B-8957-A3773F02200E}\WindowsApps\...\app.exe</c>).
    /// </summary>
    /// <returns>
    /// The expanded path, and whether a prefix was present and successfully expanded.
    /// An unresolvable prefix yields the original string so the entry stays reportable.
    /// </returns>
    public static (string Path, bool HadPrefix, bool Resolved) Resolve(string? raw, IKnownFolderResolver resolver)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (string.Empty, false, false);

        var value = raw.Trim();
        if (value[0] != '{') return (value, false, true);

        var close = value.IndexOf('}');
        if (close < 0) return (value, false, false);

        if (!Guid.TryParse(value[..(close + 1)], out var folderId)) return (value, false, false);

        var remainder = value[(close + 1)..].TrimStart('\\', '/');
        var root = resolver.Resolve(folderId);

        if (string.IsNullOrEmpty(root)) return (value, true, false);

        return (remainder.Length == 0 ? root : Path.Combine(root, remainder), true, true);
    }
}
