using Microsoft.Net.Http.Headers;
using Xunit;

namespace Jellyfin.Plugin.AccessCheck.Tests;

public class RealmTests
{
    [Theory]
    [InlineData("My Server", "My Server")]
    [InlineData("Café Müller", "Cafe Muller")]
    [InlineData("  padded  ", "padded")]
    [InlineData("Łódź", "Jellyfin")]
    [InlineData("東京", "Jellyfin")]
    [InlineData("tab\there", "Jellyfin")]
    [InlineData("", "Jellyfin")]
    public void UsesTheServerNameAsPlainAscii(string serverName, string expected) =>
        Assert.Equal(expected, BasicAuthenticationHandler.Realm(serverName));

    [Fact]
    public void QuotesAndBackslashesAreEscapedInTheHeader() =>
        Assert.Equal(
            "\"say \\\"hi\\\" \\\\o/\"",
            HeaderUtilities.EscapeAsQuotedString(BasicAuthenticationHandler.Realm("say \"hi\" \\o/")).ToString());
}
