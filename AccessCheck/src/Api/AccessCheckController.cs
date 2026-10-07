using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AccessCheck.Api;

[ApiController]
[Route("AccessCheck")]
public class AccessCheckController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<AccessCheckController> _logger;

    public AccessCheckController(ILibraryManager libraryManager, ILogger<AccessCheckController> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    [HttpGet("{*routeUri}")]
    [Authorize(Policy = BasicAuthenticationHandler.SchemeName)]
    public ActionResult Authorize([FromRoute] string? routeUri)
    {
        var headerUri = Request.Headers["X-Request-URI"].FirstOrDefault();

        // Exactly one path source. A missing one is a proxy misconfiguration
        // (nginx drops headers whose value is empty), and answering it as "/"
        // would grant whatever the proxy is about to serve; 400 makes nginx
        // fail the request with a 500 and log it instead.
        if (string.IsNullOrEmpty(routeUri) == string.IsNullOrEmpty(headerUri))
            return BadRequest();

        var user = HttpContext.Items["JellyfinUser"] as User;
        if (user is null)
            return Unauthorized();

        // Route values arrive already decoded (except %2F); the header is raw.
        var path = string.IsNullOrEmpty(routeUri)
            ? RequestPath.FromRawUri(headerUri!)
            : RequestPath.FromDecoded(routeUri);

        if (path is null)
        {
            _logger.LogDebug("Access denied to {Username} for unsafe path {Uri}", user.Username, routeUri ?? headerUri);
            return Forbid();
        }

        var decision = AccessPolicy.Evaluate(
            user,
            path,
            Plugin.Instance?.Configuration.BrowseRoot,
            _libraryManager.GetVirtualFolders());

        _logger.LogDebug(
            "Access {Result} to {Username} for {Path} ({Reason}, library {Library})",
            decision.Allowed ? "granted" : "denied",
            user.Username,
            path,
            decision.Reason,
            decision.Library);

        return decision.Allowed ? Ok() : Forbid();
    }
}
