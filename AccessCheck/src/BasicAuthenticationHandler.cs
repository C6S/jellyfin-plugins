using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.AccessCheck;

public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AccessCheckBasic";

    private readonly IUserManager _userManager;
    private readonly IServerApplicationHost _appHost;
    private readonly INetworkManager _networkManager;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IUserManager userManager,
        IServerApplicationHost appHost,
        INetworkManager networkManager)
        : base(options, logger, encoder)
    {
        _userManager = userManager;
        _appHost = appHost;
        _networkManager = networkManager;
    }

    // The realm is the server name from Jellyfin's settings. Kestrel rejects
    // non-ASCII response header values, so diacritics are stripped ("Café" ->
    // "Cafe"); a name that still isn't printable ASCII falls back to a fixed
    // realm rather than being shown with letters missing.
    internal static string Realm(string serverName)
    {
        var name = new string(serverName
            .Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Trim();

        return name.Length > 0 && name.All(c => c is >= ' ' and <= '~') ? name : "Jellyfin";
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        string credentials;
        try
        {
            credentials = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader["Basic ".Length..]));
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("Invalid Basic auth encoding");
        }

        var colonIdx = credentials.IndexOf(':');
        if (colonIdx < 0)
            return AuthenticateResult.Fail("Invalid Basic auth format");

        var username = credentials[..colonIdx];
        var password = credentials[(colonIdx + 1)..];

        var rateLimiter = Plugin.Instance?.RateLimiter;
        if (rateLimiter?.IsBlocked(username) == true)
        {
            Logger.LogDebug("Rate limit active for user {Username}", username);
            Context.Items["Forbidden"] = true;
            return AuthenticateResult.Fail("Rate limited");
        }

        var authCache = Plugin.Instance?.AuthCache;
        var user = authCache?.Get(username, password);

        if (user is null)
        {
            var authLocks = Plugin.Instance?.AuthLocks;
            var authLock = authLocks is null
                ? null
                : await authLocks.TryAcquireAsync(username, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (authLocks is not null && authLock is null)
            {
                Context.Items["Forbidden"] = true;
                return AuthenticateResult.Fail("Auth lock timeout");
            }

            try
            {
                user = authCache?.Get(username, password);

                if (user is null)
                {
                    try
                    {
                        user = await _userManager.AuthenticateUser(
                            username,
                            password,
                            Context.GetNormalizedRemoteIP().ToString(),
                            false).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug(ex, "Authentication failed for user {Username}", username);
                        rateLimiter?.RecordFailure(username, Plugin.Instance!.Configuration.RateLimitBaseSeconds, Plugin.Instance!.Configuration.RateLimitMaxSeconds, Plugin.Instance!.Configuration.RateLimitExponent);
                        return AuthenticateResult.Fail("Authentication failed");
                    }

                    if (user is null)
                    {
                        Logger.LogDebug("Authentication returned null for user {Username}", username);
                        rateLimiter?.RecordFailure(username, Plugin.Instance!.Configuration.RateLimitBaseSeconds, Plugin.Instance!.Configuration.RateLimitMaxSeconds, Plugin.Instance!.Configuration.RateLimitExponent);
                        return AuthenticateResult.Fail("Invalid credentials");
                    }

                    rateLimiter?.RecordSuccess(username);
                    authCache?.Set(username, password, user, 600);
                }
            }
            finally
            {
                authLock?.Dispose();
            }
        }

        // AuthenticateUser enforces these, but a cached login skips it. Changes
        // to the user reach the cache through UserEventConsumer; these two
        // depend on the request's origin and the clock instead, so they are
        // repeated per request as Jellyfin's own authorization does. The
        // password is known good here, so a denial is 403, not a new prompt.
        string? denial = null;
        if (!user.HasPermission(PermissionKind.EnableRemoteAccess)
            && !_networkManager.IsInLocalNetwork(Context.GetNormalizedRemoteIP()))
            denial = "Remote access disabled";
        else if (!user.IsParentalScheduleAllowed())
            denial = "Outside access schedule";

        if (denial is not null)
        {
            Logger.LogDebug("Access denied to {Username}: {Reason}", username, denial);
            Context.Items["Forbidden"] = true;
            return AuthenticateResult.Fail(denial);
        }

        Context.Items["JellyfinUser"] = user;

        var claims = new[] { new Claim(ClaimTypes.Name, username) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey("Forbidden"))
            Response.StatusCode = 403;
        else
        {
            Response.StatusCode = 401;
            Response.Headers.WWWAuthenticate = $"Basic realm={HeaderUtilities.EscapeAsQuotedString(Realm(_appHost.FriendlyName))}";
        }

        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        return Task.CompletedTask;
    }
}
