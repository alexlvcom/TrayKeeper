namespace TrayKeeper.Model;

/// <summary>
/// Path helpers that never throw. Registry values are attacker-adjacent data written by
/// arbitrary applications, so malformed or empty paths must degrade to empty strings
/// rather than bringing the refresh down.
/// </summary>
public static class PathUtilities
{
    public static string GetFileNameSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        try
        {
            return Path.GetFileName(path.TrimEnd('\\', '/')) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            var index = path.LastIndexOfAny(new[] { '\\', '/' });
            return index >= 0 && index < path.Length - 1 ? path[(index + 1)..] : path;
        }
    }

    public static string GetDirectoryNameSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        try
        {
            return Path.GetDirectoryName(path) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            var index = path.LastIndexOfAny(new[] { '\\', '/' });
            return index > 0 ? path[..index] : string.Empty;
        }
    }
}
