using TrayKeeper.Model;

namespace TrayKeeper.Tests;

/// <summary>Concise construction of <see cref="NotifyIconEntry"/> values for tests.</summary>
internal static class EntryBuilder
{
    private static int _nextKey = 1000;

    public static NotifyIconEntry Entry(
        string path,
        bool? promoted = null,
        uint? uid = 3,
        string? iconGuid = null,
        string? product = null,
        string? description = null,
        string? company = null,
        string? tooltip = null,
        string? publisher = null,
        bool? exists = true,
        string? keyName = null)
    {
        return new NotifyIconEntry
        {
            KeyName = keyName ?? (_nextKey++).ToString(),
            RawExecutablePath = path,
            ResolvedExecutablePath = path,
            IsPromoted = promoted,
            Uid = uid,
            IconGuid = iconGuid,
            InitialTooltip = tooltip,
            Publisher = publisher,
            File = new FileIdentity
            {
                Exists = exists,
                ProductName = product,
                FileDescription = description,
                CompanyName = company,
            },
        };
    }

    public static NotifyIconEntry Enabled(
        string path,
        uint? uid = 3,
        string? iconGuid = null,
        string? product = null,
        string? description = null,
        string? company = null,
        string? tooltip = null,
        string? publisher = null,
        bool? exists = true) =>
        Entry(path, true, uid, iconGuid, product, description, company, tooltip, publisher, exists);

    public static NotifyIconEntry Disabled(
        string path,
        uint? uid = 3,
        string? iconGuid = null,
        string? product = null,
        string? description = null,
        string? company = null,
        string? tooltip = null,
        string? publisher = null,
        bool? exists = true) =>
        Entry(path, false, uid, iconGuid, product, description, company, tooltip, publisher, exists);

    /// <summary>A disabled entry whose <c>IsPromoted</c> value is absent, as Windows leaves it.</summary>
    public static NotifyIconEntry Unset(
        string path,
        uint? uid = 3,
        string? iconGuid = null,
        string? product = null,
        string? description = null,
        string? company = null,
        string? tooltip = null,
        string? publisher = null,
        bool? exists = true) =>
        Entry(path, null, uid, iconGuid, product, description, company, tooltip, publisher, exists);
}
