using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AccessCheck;

public enum AccessReason
{
    OutsideRoot,
    AboveLibrary,
    NotInLibrary,
    DownloadsDisabled,
    AllLibrariesEnabled,
    LibraryEnabled,
    LibraryNotEnabled,
}

public sealed record AccessDecision(AccessReason Reason, string? Library = null)
{
    public bool Allowed => Reason is AccessReason.AboveLibrary
        or AccessReason.AllLibrariesEnabled
        or AccessReason.LibraryEnabled;
}

// Decides whether a user may reach a path, given Jellyfin's libraries. The
// path must already be normalized by RequestPath.
public static class AccessPolicy
{
    public static AccessDecision Evaluate(
        User user,
        string path,
        string? browseRoot,
        IReadOnlyCollection<VirtualFolderInfo> libraries)
    {
        // An unusable root denies everything rather than opening everything.
        var root = string.IsNullOrWhiteSpace(browseRoot) ? null : RequestPath.FromDecoded(browseRoot);
        if (root is null || !RequestPath.IsWithin(path, root))
            return new(AccessReason.OutsideRoot);

        var matched = libraries.FirstOrDefault(f =>
            f.Locations.Any(loc => RequestPath.IsWithin(path, TrimLocation(loc))));

        if (matched is null)
        {
            // Folders leading down to a library can be listed so clients can
            // navigate to it.
            var isParent = libraries.Any(f =>
                f.Locations.Any(loc => RequestPath.IsWithin(TrimLocation(loc), path)));

            return new(isParent ? AccessReason.AboveLibrary : AccessReason.NotInLibrary);
        }

        // Anything inside a library is its files, which the proxy hands out
        // directly: that is downloading, whatever the client does with them.
        // Jellyfin applies this permission to admins too.
        if (!user.HasPermission(PermissionKind.EnableContentDownloading))
            return new(AccessReason.DownloadsDisabled, matched.Name);

        if (user.HasPermission(PermissionKind.EnableAllFolders))
            return new(AccessReason.AllLibrariesEnabled, matched.Name);

        // Compared as Guids: Jellyfin stores the policy's ids with dashes, while
        // VirtualFolderInfo.ItemId has none.
        var enabled = Guid.TryParse(matched.ItemId, out var libraryId)
            && user.GetPreferenceValues<Guid>(PreferenceKind.EnabledFolders).Contains(libraryId);
        return enabled
            ? new(AccessReason.LibraryEnabled, matched.Name)
            : new(AccessReason.LibraryNotEnabled, matched.Name);
    }

    private static string TrimLocation(string location) =>
        location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
