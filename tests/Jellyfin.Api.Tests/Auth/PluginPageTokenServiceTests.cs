using System;
using Jellyfin.Api.Auth;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Jellyfin.Api.Tests.Auth
{
    public class PluginPageTokenServiceTests
    {
        private static PluginPageTokenService NewService()
            => new PluginPageTokenService(new MemoryCache(new MemoryCacheOptions()));

        [Fact]
        public void Issue_ReturnsNonEmptyUrlSafeToken()
        {
            var service = NewService();

            var token = service.Issue(TimeSpan.FromMinutes(1));

            Assert.False(string.IsNullOrEmpty(token));
            Assert.DoesNotContain('+', token);
            Assert.DoesNotContain('/', token);
            Assert.DoesNotContain('=', token);
        }

        [Fact]
        public void Validate_ReturnsTrueForFreshlyIssuedToken()
        {
            var service = NewService();
            var token = service.Issue(TimeSpan.FromMinutes(1));

            Assert.True(service.Validate(token));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-real-token")]
        public void Validate_ReturnsFalseForUnknownTokens(string? token)
        {
            var service = NewService();

            Assert.False(service.Validate(token));
        }

        [Fact]
        public void Validate_ReturnsFalseAfterExpiry()
        {
            // MemoryCache only checks TTL on access, so we issue with a tiny
            // TTL and sleep just past it.
            var service = NewService();
            var token = service.Issue(TimeSpan.FromMilliseconds(50));
            System.Threading.Thread.Sleep(150);

            Assert.False(service.Validate(token));
        }

        [Fact]
        public void Issue_ProducesDistinctTokens()
        {
            var service = NewService();

            var a = service.Issue(TimeSpan.FromMinutes(1));
            var b = service.Issue(TimeSpan.FromMinutes(1));

            Assert.NotEqual(a, b);
        }
    }
}
