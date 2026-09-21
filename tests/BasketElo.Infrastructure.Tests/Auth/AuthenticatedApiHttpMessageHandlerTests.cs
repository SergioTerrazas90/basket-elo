using System.Net;
using BasketElo.Web.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Auth;

public sealed class AuthenticatedApiHttpMessageHandlerTests
{
    [Fact]
    public async Task SendsClientIpOnlyWithInternalSecret()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        var capture = new CaptureHandler();
        var handler = new AuthenticatedApiHttpMessageHandler(
            new HttpContextAccessor { HttpContext = context },
            BuildConfiguration("shared-secret"))
        {
            InnerHandler = capture
        };

        using var client = new HttpClient(handler);
        using var response = await client.GetAsync("https://api.example.test/teams");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("shared-secret", capture.Request!.Headers.GetValues(InternalAuthHeaders.SharedSecret).Single());
        Assert.Equal("203.0.113.9", capture.Request.Headers.GetValues(InternalAuthHeaders.ClientIp).Single());
    }

    [Fact]
    public async Task DoesNotTrustOrForwardClientIpWithoutInternalSecret()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        var capture = new CaptureHandler();
        var handler = new AuthenticatedApiHttpMessageHandler(
            new HttpContextAccessor { HttpContext = context },
            BuildConfiguration(null))
        {
            InnerHandler = capture
        };

        using var client = new HttpClient(handler);
        using var response = await client.GetAsync("https://api.example.test/teams");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(capture.Request!.Headers.Contains(InternalAuthHeaders.SharedSecret));
        Assert.False(capture.Request.Headers.Contains(InternalAuthHeaders.ClientIp));
    }

    private static IConfiguration BuildConfiguration(string? sharedSecret)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalAuth:SharedSecret"] = sharedSecret
            })
            .Build();

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
