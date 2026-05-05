using System;

namespace Jellyfin.Api.Auth;

/// <summary>
/// Issues and validates short-lived tokens that authorize reads from
/// <c>/web/ConfigurationPage</c>. Used to authenticate browser-driven
/// sub-resource fetches (dynamic <c>import()</c>, <c>&lt;script&gt;</c>,
/// <c>&lt;link&gt;</c>, <c>&lt;img&gt;</c>) that cannot carry the
/// <c>Authorization</c> header.
/// </summary>
public interface IPluginPageTokenService
{
    /// <summary>
    /// Issues a new token valid for the given lifetime.
    /// </summary>
    /// <param name="ttl">Time the token remains valid for.</param>
    /// <returns>An opaque, URL-safe token string.</returns>
    string Issue(TimeSpan ttl);

    /// <summary>
    /// Returns <c>true</c> if the supplied token is currently valid.
    /// </summary>
    /// <param name="token">The token string to validate, or <c>null</c>/empty.</param>
    /// <returns><c>true</c> if the token is known and unexpired.</returns>
    bool Validate(string? token);
}
