using Microsoft.Net.Http.Headers;

namespace BasketElo.Web.AgentReadiness;

public sealed class MarkdownNegotiationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) ||
            context.Request.Path != "/")
        {
            await next(context);
            return;
        }

        context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.Accept);
        if (!AcceptsMarkdown(context.Request.GetTypedHeaders().Accept))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/markdown; charset=utf-8";
        context.Response.Headers.CacheControl = "public, max-age=300";
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.WriteAsync(AgentContent.MarkdownHomepage, context.RequestAborted);
        }
    }

    private static bool AcceptsMarkdown(IList<MediaTypeHeaderValue>? acceptedTypes) =>
        acceptedTypes?.Any(value =>
            value.Quality.GetValueOrDefault(1) > 0 &&
            string.Equals(value.MediaType.Value, "text/markdown", StringComparison.OrdinalIgnoreCase)) == true;
}
