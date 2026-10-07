using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.AccessCheck.Tests;

public class AccessPolicyTests
{
    private const string Root = "/media";

    private static readonly Guid MoviesId = Guid.NewGuid();
    private static readonly Guid ShowsId = Guid.NewGuid();

    // ItemId as Jellyfin reports it: without dashes.
    private static readonly VirtualFolderInfo[] Libraries =
    [
        new() { Name = "Movies", ItemId = MoviesId.ToString("N"), Locations = ["/media/Movies"] },
        new() { Name = "Shows", ItemId = ShowsId.ToString("N"), Locations = ["/media/Shows/"] },
        new() { Name = "Music", ItemId = Guid.NewGuid().ToString("N"), Locations = ["/media/deep/nested/Music"] },
    ];

    private static User NewUser(bool download = true, bool allFolders = false, params Guid[] enabledFolders)
    {
        var user = new User("alice", "auth", "reset");
        user.SetPermission(PermissionKind.EnableContentDownloading, download);
        user.SetPermission(PermissionKind.EnableAllFolders, allFolders);
        // Stored the way Jellyfin's UpdatePolicyAsync stores it: Guids, which
        // join with dashes.
        user.SetPreference(PreferenceKind.EnabledFolders, enabledFolders);
        return user;
    }

    private static AccessDecision Evaluate(User user, string path, string? root = Root) =>
        AccessPolicy.Evaluate(user, path, root, Libraries);

    [Theory]
    [InlineData("/mnt")]
    [InlineData("/")]
    [InlineData("/mediaX/Movies/x.mkv")]
    public void PathsOutsideTheRootAreDenied(string path)
    {
        var decision = Evaluate(NewUser(allFolders: true), path);

        Assert.Equal(AccessReason.OutsideRoot, decision.Reason);
        Assert.False(decision.Allowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/media/../etc")]
    public void AnUnusableRootDeniesEverything(string? root) =>
        Assert.Equal(AccessReason.OutsideRoot, Evaluate(NewUser(allFolders: true), "/media/Movies/x.mkv", root).Reason);

    [Fact]
    public void ARootOfSlashAllowsListingFromTheTop()
    {
        var decision = Evaluate(NewUser(), "/", root: "/");

        Assert.Equal(AccessReason.AboveLibrary, decision.Reason);
        Assert.True(decision.Allowed);
    }

    [Theory]
    [InlineData("/media")]
    [InlineData("/media/deep")]
    [InlineData("/media/deep/nested")]
    public void FoldersAboveLibrariesCanBeListedWithoutAnyPermission(string path)
    {
        var decision = Evaluate(NewUser(download: false), path);

        Assert.Equal(AccessReason.AboveLibrary, decision.Reason);
        Assert.True(decision.Allowed);
    }

    [Theory]
    [InlineData("/media/Other")]
    [InlineData("/media/deep/other")]
    [InlineData("/media/Movi")]
    [InlineData("/media/MoviesX/x.mkv")]
    [InlineData("/media/movies/x.mkv")]
    public void PathsInNoLibraryAreDenied(string path)
    {
        var decision = Evaluate(NewUser(allFolders: true), path);

        Assert.Equal(AccessReason.NotInLibrary, decision.Reason);
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void WithoutTheDownloadPermissionLibrariesAreDenied()
    {
        var decision = Evaluate(NewUser(download: false, allFolders: true), "/media/Movies/x.mkv");

        Assert.Equal(new AccessDecision(AccessReason.DownloadsDisabled, "Movies"), decision);
        Assert.False(decision.Allowed);
    }

    [Theory]
    [InlineData("/media/Movies")]
    [InlineData("/media/Movies/x.mkv")]
    [InlineData("/media/Shows/S01/e01.mkv")]
    [InlineData("/media/deep/nested/Music/a.flac")]
    public void AllLibrariesEnabledAllowsEveryLibrary(string path)
    {
        var decision = Evaluate(NewUser(allFolders: true), path);

        Assert.Equal(AccessReason.AllLibrariesEnabled, decision.Reason);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void AnEnabledLibraryIsAllowed()
    {
        var decision = Evaluate(NewUser(enabledFolders: MoviesId), "/media/Movies/x.mkv");

        Assert.Equal(new AccessDecision(AccessReason.LibraryEnabled, "Movies"), decision);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void ALibraryNotEnabledIsDenied()
    {
        var decision = Evaluate(NewUser(enabledFolders: MoviesId), "/media/Shows/S01/e01.mkv");

        Assert.Equal(new AccessDecision(AccessReason.LibraryNotEnabled, "Shows"), decision);
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void EachOfSeveralEnabledLibrariesIsAllowed()
    {
        var user = NewUser(enabledFolders: [MoviesId, ShowsId]);

        Assert.Equal(AccessReason.LibraryEnabled, Evaluate(user, "/media/Movies/x.mkv").Reason);
        Assert.Equal(AccessReason.LibraryEnabled, Evaluate(user, "/media/Shows/S01/e01.mkv").Reason);
        Assert.Equal(AccessReason.LibraryNotEnabled, Evaluate(user, "/media/deep/nested/Music/a.flac").Reason);
    }
}
