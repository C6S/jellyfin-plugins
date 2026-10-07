using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AccessCheck.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public int RateLimitBaseSeconds { get; set; } = 2;

    public int RateLimitMaxSeconds { get; set; } = 600;

    public double RateLimitExponent { get; set; } = 1.7;

    // The only part of the tree the plugin answers for; everything outside it
    // is denied, including the folders above it.
    public string BrowseRoot { get; set; } = "/media";
}
