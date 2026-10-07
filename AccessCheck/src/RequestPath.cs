namespace Jellyfin.Plugin.AccessCheck;

// Turns the requested URI into the path the proxy will actually serve, so
// the library check runs against the same path. nginx's $request_uri is raw:
// it still carries the query string, percent-encoding, duplicate slashes and
// dot segments, all of which nginx resolves before mapping to a file.
public static class RequestPath
{
    // A raw request URI, as forwarded in X-Request-URI.
    public static string? FromRawUri(string raw)
    {
        // nginx splits off the query at the first literal '?', before decoding.
        var queryIdx = raw.IndexOf('?');
        if (queryIdx >= 0)
            raw = raw[..queryIdx];

        return FromDecoded(Uri.UnescapeDataString(raw));
    }

    // An already-decoded path, such as an ASP.NET route value.
    public static string? FromDecoded(string path)
    {
        if (path.Contains('\0'))
            return null;

        // Dot segments are rejected rather than resolved: no legitimate
        // client sends them, and resolving them differently from the proxy
        // is exactly how a check gets bypassed. Backslashes count as
        // separators here in case the proxy treats them as such.
        if (path.Split('/', '\\').Any(s => s is "." or ".."))
            return null;

        return "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    // Whether a normalized path is the directory or somewhere beneath it.
    public static bool IsWithin(string path, string dir) =>
        dir == "/"
        || path.Equals(dir, StringComparison.Ordinal)
        || path.StartsWith(dir + "/", StringComparison.Ordinal);
}
