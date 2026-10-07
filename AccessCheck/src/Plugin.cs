using Jellyfin.Plugin.AccessCheck.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AccessCheck;

public class Plugin : BasePlugin<PluginConfiguration>, IDisposable
{
    public static Plugin? Instance { get; private set; }

    public RateLimiter RateLimiter { get; }

    public AuthCache AuthCache { get; } = new();

    public KeyedLock AuthLocks { get; } = new();

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        RateLimiter = new RateLimiter(() => Configuration.RateLimitMaxSeconds);
    }

    public void Dispose()
    {
        RateLimiter.Dispose();
        GC.SuppressFinalize(this);
    }

    public override string Name => "AccessCheck";

    public override Guid Id => new("cb207969-cc84-4f02-bfd5-25cab26d681d");

    public override string Description => "Validates user credentials and library access for external auth subrequests.";
}
