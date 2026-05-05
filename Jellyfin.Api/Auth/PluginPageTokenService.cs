using System;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Api.Auth;

/// <inheritdoc />
public sealed class PluginPageTokenService : IPluginPageTokenService
{
    private const string KeyPrefix = "plugin-page-token:";
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginPageTokenService"/> class.
    /// </summary>
    /// <param name="cache">Memory cache used as token store.</param>
    public PluginPageTokenService(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public string Issue(TimeSpan ttl)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        _cache.Set(KeyPrefix + token, true, ttl);
        return token;
    }

    /// <inheritdoc />
    public bool Validate(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        return _cache.TryGetValue(KeyPrefix + token, out _);
    }
}
