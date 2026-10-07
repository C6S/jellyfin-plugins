using Xunit;

namespace Jellyfin.Plugin.AccessCheck.Tests;

public class RequestPathTests
{
    [Theory]
    [InlineData("/media/A/file.mkv", "/media/A/file.mkv")]
    [InlineData("/media/A/?C=M;O=D", "/media/A")]
    [InlineData("/media/A/x%3Fy?q=1", "/media/A/x?y")]
    [InlineData("/media/Caf%C3%A9%20films/", "/media/Café films")]
    [InlineData("//media///A//f", "/media/A/f")]
    [InlineData("/media/A/...hidden", "/media/A/...hidden")]
    [InlineData("/media/A/%252e%252e/B", "/media/A/%2e%2e/B")]
    [InlineData("/media/A/a+b", "/media/A/a+b")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData("?only=query", "/")]
    public void FromRawUri_NormalizesLikeNginx(string raw, string expected) =>
        Assert.Equal(expected, RequestPath.FromRawUri(raw));

    [Theory]
    [InlineData("/media/A/../B/file.mkv")]
    [InlineData("/media/A/%2e%2e/B/file.mkv")]
    [InlineData("/media/A/%2E%2E/B/")]
    [InlineData("/media/A/./file")]
    [InlineData("/media/A/..%2fB")]
    [InlineData("/media/A/..%5cB")]
    [InlineData("/media/A/..\\B")]
    [InlineData("/..")]
    [InlineData("/media/A/x%00")]
    public void FromRawUri_RejectsDotSegmentsAndNul(string raw) =>
        Assert.Null(RequestPath.FromRawUri(raw));

    [Fact]
    public void FromRawUri_DecodesOnlyOnce() =>
        Assert.Equal("/media/%41", RequestPath.FromRawUri("/media/%2541"));

    [Theory]
    [InlineData("media/A/f", "/media/A/f")]
    [InlineData("media/A/x?y", "/media/A/x?y")]
    [InlineData("media/A/x%2e%2e", "/media/A/x%2e%2e")]
    public void FromDecoded_KeepsQueryCharsAndPercents(string decoded, string expected) =>
        Assert.Equal(expected, RequestPath.FromDecoded(decoded));

    [Theory]
    [InlineData("media/A/../B")]
    [InlineData("media/./A")]
    public void FromDecoded_RejectsDotSegments(string decoded) =>
        Assert.Null(RequestPath.FromDecoded(decoded));

    [Theory]
    [InlineData("/media", "/media", true)]
    [InlineData("/media/A/f", "/media", true)]
    [InlineData("/mediaX/A", "/media", false)]
    [InlineData("/mnt", "/media", false)]
    [InlineData("/", "/media", false)]
    [InlineData("/Media/A", "/media", false)]
    [InlineData("/anything", "/", true)]
    [InlineData("/", "/", true)]
    public void IsWithin_IsFolderAwareAndCaseSensitive(string path, string dir, bool expected) =>
        Assert.Equal(expected, RequestPath.IsWithin(path, dir));
}
