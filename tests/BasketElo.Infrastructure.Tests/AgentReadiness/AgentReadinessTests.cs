using System.Text.Json;
using BasketElo.Web.AgentReadiness;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BasketElo.Infrastructure.Tests.AgentReadiness;

public class AgentReadinessTests
{
    [Fact]
    public async Task Homepage_ReturnsMarkdownWhenExplicitlyRequested()
    {
        var nextWasCalled = false;
        var middleware = new MarkdownNegotiationMiddleware(_ =>
        {
            nextWasCalled = true;
            return Task.CompletedTask;
        });
        var context = NewContext("/", "text/markdown");

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.False(nextWasCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("text/markdown; charset=utf-8", context.Response.ContentType);
        Assert.Contains("Accept", context.Response.Headers.Vary.ToString());
        Assert.StartsWith("# Basketball ELO rankings", body);
        Assert.True(body.Length >= 500);
    }

    [Fact]
    public async Task Homepage_PreservesHtmlHandlingAndVariesOnAccept()
    {
        var nextWasCalled = false;
        var middleware = new MarkdownNegotiationMiddleware(_ =>
        {
            nextWasCalled = true;
            return Task.CompletedTask;
        });
        var context = NewContext("/", "text/html");

        await middleware.InvokeAsync(context);

        Assert.True(nextWasCalled);
        Assert.Contains("Accept", context.Response.Headers.Vary.ToString());
    }

    [Fact]
    public void HomepageOverview_IsMeaningfulServerRenderedContent()
    {
        Assert.True(AgentContent.HomepageOverviewEnglish.Length >= 500);
        Assert.Contains("independent basketball rating service", AgentContent.HomepageOverviewEnglish);
        Assert.Contains("not an official", AgentContent.HomepageOverviewEnglish);
    }

    [Fact]
    public void Robots_ExplicitlyAllowsMajorAgentCrawlers()
    {
        var robots = AgentContent.Robots("https://basketelo.com/");

        Assert.Contains("User-agent: ChatGPT-User\nAllow: /", robots);
        Assert.Contains("User-agent: OAI-SearchBot\nAllow: /", robots);
        Assert.Contains("User-agent: Claude-User\nAllow: /", robots);
        Assert.Contains("User-agent: PerplexityBot\nAllow: /", robots);
        Assert.Contains("Sitemap: https://basketelo.com/sitemap.xml", robots);
    }

    [Fact]
    public void OpenApiDocument_IsValidAndDescribesPublicEndpointsAndErrors()
    {
        using var document = JsonDocument.Parse(AgentApi.OpenApiDocument);
        var root = document.RootElement;

        Assert.Equal("3.1.0", root.GetProperty("openapi").GetString());
        Assert.True(root.GetProperty("paths").TryGetProperty("/api/v1/status", out _));
        Assert.True(root.GetProperty("paths").TryGetProperty("/health", out _));
        Assert.True(root.GetProperty("components").GetProperty("schemas").TryGetProperty("Error", out _));
    }

    [Fact]
    public void AgentApiStatus_LinksToDiscoveryAndMarkdownResources()
    {
        Assert.Equal("ok", AgentApi.Status.Status);
        Assert.Equal("BasketElo Agent API", AgentApi.Status.Name);
        Assert.Equal("/openapi.json", AgentApi.Status.Openapi);
        Assert.Equal("/", AgentApi.Status.MarkdownHomepage);
    }

    [Fact]
    public async Task ApiFailures_AreStructuredJsonWithResolutionHint()
    {
        var context = NewContext("/api/unknown", "application/json");
        context.TraceIdentifier = "agent-test-trace";

        await MachineReadableErrors.WriteAsync(context, StatusCodes.Status404NotFound);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var error = document.RootElement;
        Assert.Equal("application/problem+json; charset=utf-8", context.Response.ContentType);
        Assert.Equal("about:blank", error.GetProperty("type").GetString());
        Assert.Equal(StatusCodes.Status404NotFound, error.GetProperty("status").GetInt32());
        Assert.Equal("not_found", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.Contains("/openapi.json", error.GetProperty("resolutionHint").GetString());
        Assert.Equal("agent-test-trace", error.GetProperty("traceId").GetString());
    }

    private static DefaultHttpContext NewContext(string path, string accept)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.Headers.Accept = accept;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
