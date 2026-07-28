using System.Reflection;

namespace TrayKeeper;

/// <summary>
/// App version info and human-readable change history shown in the tray menu → About.
/// Add a new entry at the top of <see cref="Entries"/> whenever behaviour changes,
/// and bump &lt;VersionPrefix&gt; in the .csproj to match.
/// </summary>
public static class Changelog
{
    /// <summary>Newest first. Each entry: version, date, and bullet notes.</summary>
    public static readonly (string Version, string Date, string[] Notes)[] Entries =
    {
        ("1.1.0", "2026-07-28", new[]
        {
            "New: About window showing the version, build date, the registry key",
            "     TrayKeeper writes to, and this change history.",
        }),
        ("1.0.0", "2026-07-28", new[]
        {
            "Initial release.",
            "Finds notification-area entries that Windows hid after an app update and",
            "     enables them when another entry for the same app is already enabled.",
            "Expands KNOWNFOLDERID prefixes in ExecutablePath, so packaged apps and",
            "     anything under a well-known root are matched correctly.",
            "Requires a matching notification icon identity (UID or IconGuid), so a",
            "     visibility setting is never carried across two different icons of one app.",
            "Skips entries whose executable no longer exists, so deleted versions are",
            "     not promoted into icons that can never appear.",
            "Company name alone, or a generic exe name like update.exe, is never enough",
            "     to declare a match; conservative by design.",
            "Backs up the whole key (JSON + .reg) before writing, and records the exact",
            "     prior value of every change so 'Restore last changes' is lossless.",
            "Writes only IsPromoted, only under HKCU, and never requires elevation.",
            "Changes apply live — no Explorer restart, no settings broadcast.",
        }),
    };

    public const string Author = "Alex LV";
    public const string AuthorEmail = "alex@alexlv.com";
    public const string Copyright = "Copyright © 2026 Alex LV";

    /// <summary>Author line shown in the About window header.</summary>
    public static string AuthorLine => $"{Author}  ·  {AuthorEmail}";

    /// <summary>Marketing version, e.g. "1.1.0" (from the newest changelog entry).</summary>
    public static string MarketingVersion => Entries[0].Version;

    /// <summary>Date of the newest changelog entry.</summary>
    public static string MarketingDate => Entries[0].Date;

    /// <summary>Full build version including the auto-incrementing build/revision.</summary>
    public static string BuildVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? MarketingVersion;
        }
    }

    /// <summary>Timestamp this executable was built.</summary>
    public static DateTime BuildDate
    {
        get
        {
            try
            {
                // Environment.ProcessPath is the only reliable source in a single-file publish,
                // where Assembly.Location is empty.
                var path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return File.GetLastWriteTime(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Fall through to the sentinel.
            }

            return DateTime.MinValue;
        }
    }

    /// <summary>One-line summary of what the app is for.</summary>
    public static readonly string[] Description =
    {
        "Re-enables notification-area icons that Windows hid after an application",
        "update moved its executable into a new versioned folder.",
        "",
        "Only entries that confidently match an already-enabled entry are touched —",
        "hidden icons in general are left alone.",
    };

    /// <summary>The single registry value the app writes, surfaced for transparency.</summary>
    public static readonly string[] RegistryNotes =
    {
        @"Reads:  HKCU\Control Panel\NotifyIconSettings",
        @"Writes: IsPromoted (0 -> 1) on matched entries only",
        "        Nothing machine-wide. No elevation. No scheduled task.",
    };

    /// <summary>
    /// Everything except the version header. Used for the About window body, which already shows
    /// the version and build stamp in its header.
    /// </summary>
    public static string FormatBody()
    {
        var lines = new List<string>();
        lines.AddRange(Description);
        lines.Add("");
        lines.AddRange(RegistryNotes);
        lines.Add("");
        lines.Add("What's changed");
        lines.Add("──────────────");

        foreach (var (version, date, notes) in Entries)
        {
            lines.Add("");
            lines.Add($"v{version}  ({date})");
            foreach (var note in notes) lines.Add("  • " + note);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The version header plus <see cref="FormatBody"/> — the form used for the clipboard, so a
    /// pasted copy identifies the exact build it came from.
    /// </summary>
    public static string FormatFull()
    {
        var header = new[]
        {
            $"TrayKeeper  v{MarketingVersion}",
            $"Build {BuildVersion}" +
                (BuildDate == DateTime.MinValue ? "" : $"   (built {BuildDate:yyyy-MM-dd HH:mm})"),
            $"{Author} <{AuthorEmail}>",
            Copyright,
            "",
        };

        return string.Join(Environment.NewLine, header) + FormatBody();
    }
}
