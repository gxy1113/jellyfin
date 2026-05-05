using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Api.Attributes;
using Jellyfin.Api.Auth;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Models;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// The dashboard controller.
/// </summary>
[Route("")]
public class DashboardController : BaseJellyfinApiController
{
    private static readonly Regex _configPageUrlRegex = new(
        @"/web/ConfigurationPage\?[^""'\s<>]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly TimeSpan _pageTokenLifetime = TimeSpan.FromMinutes(5);

    private readonly ILogger<DashboardController> _logger;
    private readonly IPluginManager _pluginManager;
    private readonly IPluginPageTokenService _pageTokenService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardController"/> class.
    /// </summary>
    /// <param name="logger">Instance of <see cref="ILogger{DashboardController}"/> interface.</param>
    /// <param name="pluginManager">Instance of <see cref="IPluginManager"/> interface.</param>
    /// <param name="pageTokenService">Instance of <see cref="IPluginPageTokenService"/> interface.</param>
    public DashboardController(
        ILogger<DashboardController> logger,
        IPluginManager pluginManager,
        IPluginPageTokenService pageTokenService)
    {
        _logger = logger;
        _pluginManager = pluginManager;
        _pageTokenService = pageTokenService;
    }

    /// <summary>
    /// Gets the configuration pages.
    /// </summary>
    /// <param name="enableInMainMenu">Whether to enable in the main menu.</param>
    /// <response code="200">ConfigurationPages returned.</response>
    /// <response code="404">Server still loading.</response>
    /// <returns>An <see cref="IEnumerable{ConfigurationPageInfo}"/> with infos about the plugins.</returns>
    [HttpGet("web/ConfigurationPages")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IEnumerable<ConfigurationPageInfo>> GetConfigurationPages(
        [FromQuery] bool? enableInMainMenu)
    {
        var configPages = _pluginManager.Plugins.SelectMany(GetConfigPages).ToList();

        if (enableInMainMenu.HasValue)
        {
            configPages = configPages.Where(p => p.EnableInMainMenu == enableInMainMenu.Value).ToList();
        }

        return configPages;
    }

    /// <summary>
    /// Gets a dashboard configuration page.
    /// </summary>
    /// <param name="name">The name of the page.</param>
    /// <param name="pageToken">A short-lived token previously embedded in the page HTML; supplied by the browser when fetching sub-resources via tags that cannot carry the standard auth header.</param>
    /// <response code="200">ConfigurationPage returned.</response>
    /// <response code="401">Caller is not an administrator and supplied no valid page token.</response>
    /// <response code="404">Plugin configuration page not found.</response>
    /// <returns>The configuration page.</returns>
    [HttpGet("web/ConfigurationPage")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesFile(MediaTypeNames.Text.Html, "application/x-javascript")]
    public async Task<ActionResult> GetDashboardConfigurationPage(
        [FromQuery] string? name,
        [FromQuery(Name = "page_token")] string? pageToken)
    {
        // Authorize first so unauthenticated callers cannot enumerate which
        // plugins are installed via the 404-vs-401 response oracle. Caller
        // must be admin OR present a valid page token. Sub-resources
        // referenced from a plugin's HTML (script/link/img/dynamic import)
        // cannot carry our custom Authorization header, so they authenticate
        // via the short-lived token that was embedded into the page when the
        // admin initially fetched it.
        var isAdmin = User.IsInRole(UserRoles.Administrator);
        var hasValidToken = _pageTokenService.Validate(pageToken);
        if (!isAdmin && !hasValidToken)
        {
            return Unauthorized();
        }

        var altPage = GetPluginPages().FirstOrDefault(p => string.Equals(p.Item1.Name, name, StringComparison.OrdinalIgnoreCase));
        if (altPage is null)
        {
            return NotFound();
        }

        IPlugin plugin = altPage.Item2;
        string resourcePath = altPage.Item1.EmbeddedResourcePath;
        Stream? stream = plugin.GetType().Assembly.GetManifestResourceStream(resourcePath);
        if (stream is null)
        {
            _logger.LogError("Failed to get resource {Resource} from plugin {Plugin}", resourcePath, plugin.Name);
            return NotFound();
        }

        var mimeType = MimeTypes.GetMimeType(resourcePath);

        // For HTML pages requested by an admin, mint a fresh token and rewrite
        // /web/ConfigurationPage URLs in the body so that browser-driven
        // sub-resource fetches authenticate via the page_token query parameter.
        // Note: GetMimeType returns "text/html; charset=UTF-8" for .html files,
        // so we match the type prefix rather than full equality.
        if (isAdmin && mimeType.StartsWith(MediaTypeNames.Text.Html, StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync().ConfigureAwait(false);
            var token = _pageTokenService.Issue(_pageTokenLifetime);
            return Content(InjectPageToken(html, token), mimeType);
        }

        return File(stream, mimeType);
    }

    private static string InjectPageToken(string html, string token)
    {
        var encoded = Uri.EscapeDataString(token);
        var rewritten = _configPageUrlRegex.Replace(html, m => m.Value + "&page_token=" + encoded);

        return rewritten.Replace(
            "data-role=\"page\"",
            "data-role=\"page\" data-plugin-page-token=\"" + token + "\"",
            StringComparison.Ordinal);
    }

    private IEnumerable<ConfigurationPageInfo> GetConfigPages(LocalPlugin plugin)
    {
        return GetPluginPages(plugin).Select(i => new ConfigurationPageInfo(plugin.Instance, i.Item1));
    }

    private IEnumerable<Tuple<PluginPageInfo, IPlugin>> GetPluginPages(LocalPlugin plugin)
    {
        if (plugin.Instance is not IHasWebPages hasWebPages)
        {
            return Enumerable.Empty<Tuple<PluginPageInfo, IPlugin>>();
        }

        return hasWebPages.GetPages().Select(i => new Tuple<PluginPageInfo, IPlugin>(i, plugin.Instance));
    }

    private IEnumerable<Tuple<PluginPageInfo, IPlugin>> GetPluginPages()
    {
        return _pluginManager.Plugins.SelectMany(GetPluginPages);
    }
}
