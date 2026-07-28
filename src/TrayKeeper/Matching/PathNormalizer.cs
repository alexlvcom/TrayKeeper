using System.Text.RegularExpressions;
using TrayKeeper.Model;

namespace TrayKeeper.Matching;

/// <summary>
/// Collapses version-specific detail out of executable paths so that two installs of the
/// same application land on the same string.
/// </summary>
public static partial class PathNormalizer
{
    /// <summary>
    /// A dotted version token that is delimited by something other than a letter or digit,
    /// so that <c>app-1.5.1095</c> and <c>Claude_1.13576.4.0_x64</c> both match while
    /// <c>Office16</c> and <c>win32</c> do not.
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])v?\d+(?:\.\d+){1,3}(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex VersionTokenRegex();

    /// <summary>MSIX package full name: <c>Name_Version_Arch__PublisherId</c>.</summary>
    [GeneratedRegex(@"^(?<name>[^_]+)_(?<version>\d+(?:\.\d+){0,3})_(?<arch>[^_]*)__(?<publisher>[A-Za-z0-9]+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PackageFullNameRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex CollapseSpacesRegex();

    private const string VersionPlaceholder = "#";

    /// <summary>
    /// Replaces every version token in a single path segment with a placeholder.
    /// <c>app-1.5.1095</c> and <c>app-1.6.224</c> both become <c>app-#</c>;
    /// <c>PhpStorm 2025.1.7</c> becomes <c>phpstorm #</c>.
    /// </summary>
    public static string NormalizeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return string.Empty;

        var replaced = VersionTokenRegex().Replace(segment, VersionPlaceholder);
        replaced = CollapseSpacesRegex().Replace(replaced, " ");
        return replaced.Trim().ToLowerInvariant();
    }

    /// <summary>True when a segment consists only of a version, e.g. <c>1.6.224</c> or <c>v2.0</c>.</summary>
    public static bool IsVersionSegment(string segment) => NormalizeSegment(segment) == VersionPlaceholder;

    /// <summary>
    /// Normalizes a directory path for comparison: lower-cased, version tokens replaced,
    /// separators unified, trailing separator removed.
    /// </summary>
    public static string NormalizeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return string.Empty;

        var unified = directory.Replace('/', '\\').TrimEnd('\\');
        var segments = unified.Split('\\', StringSplitOptions.None);

        for (var i = 0; i < segments.Length; i++)
        {
            // Preserve a drive specifier such as "C:" verbatim.
            segments[i] = i == 0 && segments[i].EndsWith(':')
                ? segments[i].ToLowerInvariant()
                : NormalizeSegment(segments[i]);
        }

        return string.Join('\\', segments.Where(s => s.Length > 0));
    }

    /// <summary>Normalizes a full executable path (directory plus file name).</summary>
    public static string NormalizeExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return string.Empty;

        var directory = NormalizeDirectory(PathUtilities.GetDirectoryNameSafe(executablePath));
        var file = PathUtilities.GetFileNameSafe(executablePath).ToLowerInvariant();

        return directory.Length == 0 ? file : $"{directory}\\{file}";
    }

    /// <summary>
    /// Extracts the MSIX package family name (<c>Name_PublisherId</c>) from a path under
    /// <c>WindowsApps</c>. Returns <c>null</c> for unpackaged applications. The family name is
    /// version independent by construction, which makes it the strongest available signal.
    /// </summary>
    public static string? TryGetPackageFamilyName(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;

        var segments = executablePath.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (!segments[i].Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;

            var match = PackageFullNameRegex().Match(segments[i + 1]);
            if (match.Success)
            {
                var name = match.Groups["name"].Value;
                var publisher = match.Groups["publisher"].Value;
                return $"{name}_{publisher}".ToLowerInvariant();
            }
        }

        return null;
    }
}
