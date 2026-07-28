namespace TrayKeeper.Model;

/// <summary>
/// Version metadata read from the executable on disk.
/// </summary>
public sealed record FileIdentity
{
    /// <summary>
    /// <c>true</c> when the executable was found, <c>false</c> when it is confirmed missing,
    /// and <c>null</c> when existence could not be determined (for example a packaged
    /// application under <c>%ProgramFiles%\WindowsApps</c>, where the ACL denies access).
    /// Only a definite <c>false</c> is treated as a stale entry.
    /// </summary>
    public bool? Exists { get; init; }

    public string? ProductName { get; init; }
    public string? FileDescription { get; init; }
    public string? CompanyName { get; init; }
    public string? ProductVersion { get; init; }

    public static readonly FileIdentity Unknown = new();
}

/// <summary>
/// One entry under <c>HKCU\Control Panel\NotifyIconSettings</c>.
/// </summary>
public sealed record NotifyIconEntry
{
    /// <summary>Registry subkey name: an opaque decimal 64-bit hash produced by Explorer.</summary>
    public required string KeyName { get; init; }

    /// <summary>
    /// The <c>ExecutablePath</c> value exactly as stored. May begin with a KNOWNFOLDERID
    /// in brace form, e.g. <c>{6D809377-6AF0-444B-8957-A3773F02200E}\WindowsApps\...</c>.
    /// </summary>
    public required string RawExecutablePath { get; init; }

    /// <summary>The executable path after any KNOWNFOLDERID prefix has been expanded.</summary>
    public required string ResolvedExecutablePath { get; init; }

    /// <summary>
    /// The <c>IsPromoted</c> DWORD. <c>null</c> means the value is absent, which Explorer
    /// treats as "not promoted" — the icon lives in the overflow flyout.
    /// </summary>
    public bool? IsPromoted { get; init; }

    /// <summary>The <c>uID</c> field of the owning NOTIFYICONDATA, when the icon is identified by UID.</summary>
    public uint? Uid { get; init; }

    /// <summary>The <c>guidItem</c> of the owning NOTIFYICONDATA, when the icon is identified by GUID.</summary>
    public string? IconGuid { get; init; }

    public string? InitialTooltip { get; init; }

    /// <summary>Publisher string recorded by Explorer, when present.</summary>
    public string? Publisher { get; init; }

    public FileIdentity File { get; init; } = FileIdentity.Unknown;

    public bool IsEnabled => IsPromoted == true;

    public bool IsDisabled => IsPromoted != true;

    public string FileName => PathUtilities.GetFileNameSafe(ResolvedExecutablePath);

    public string DirectoryPath => PathUtilities.GetDirectoryNameSafe(ResolvedExecutablePath);

    /// <summary>Display label preferring the richest human-readable name available.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(File.ProductName)) return File.ProductName!.Trim();
            if (!string.IsNullOrWhiteSpace(File.FileDescription)) return File.FileDescription!.Trim();
            if (!string.IsNullOrWhiteSpace(InitialTooltip)) return InitialTooltip!.Trim();
            var name = FileName;
            return string.IsNullOrEmpty(name) ? KeyName : name;
        }
    }
}
