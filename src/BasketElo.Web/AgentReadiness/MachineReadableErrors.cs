using System.Text.Json;

namespace BasketElo.Web.AgentReadiness;

public static class MachineReadableErrors
{
    public static bool IsMachinePath(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path.Equals("/openapi.json");

    public static Task WriteAsync(HttpContext context, int statusCode)
    {
        var (code, message, hint) = statusCode switch
        {
            StatusCodes.Status400BadRequest => ("bad_request", "The request is invalid.", "Check the OpenAPI document and correct the submitted parameters."),
            StatusCodes.Status401Unauthorized => ("unauthorized", "Authentication is required.", "Provide valid credentials and retry the request."),
            StatusCodes.Status403Forbidden => ("forbidden", "The caller is not allowed to perform this operation.", "Use credentials with the required access or choose a public endpoint."),
            StatusCodes.Status404NotFound => ("not_found", "The requested machine-readable endpoint does not exist.", "Consult /openapi.json for supported paths and methods."),
            StatusCodes.Status405MethodNotAllowed => ("method_not_allowed", "The HTTP method is not supported for this endpoint.", "Consult /openapi.json and use one of the documented methods."),
            StatusCodes.Status429TooManyRequests => ("rate_limited", "The request rate limit has been exceeded.", "Wait for the Retry-After interval before retrying."),
            _ when statusCode >= 500 => ("internal_error", "The service could not complete the request.", "Retry later. If the failure persists, report the traceId to the site operator."),
            _ => ("request_failed", "The request could not be completed.", "Consult /openapi.json and revise the request.")
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title = message,
            status = statusCode,
            detail = hint,
            code,
            message,
            resolutionHint = hint,
            traceId = context.TraceIdentifier
        }), context.RequestAborted);
    }
}
