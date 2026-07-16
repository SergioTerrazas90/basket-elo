using System.Net;

namespace BasketElo.Infrastructure.Backfill;

public sealed class BackfillHttpRequestException(
    string message,
    int attempts,
    HttpStatusCode? statusCode = null,
    Exception? innerException = null)
    : HttpRequestException(message, innerException, statusCode)
{
    public int Attempts { get; } = attempts;
}

internal static class BackfillHttpRetryPolicy
{
    public static async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        int maxRetries,
        int baseDelayMilliseconds,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, maxRetries + 1);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await sendAsync(cancellationToken);
                if (!IsTransient(response.StatusCode))
                {
                    return response;
                }

                if (attempt == maxAttempts)
                {
                    var statusCode = response.StatusCode;
                    response.Dispose();
                    throw new BackfillHttpRequestException(
                        $"Provider returned transient HTTP {(int)statusCode} ({statusCode}) after {attempt} attempts.",
                        attempt,
                        statusCode);
                }

                var retryAfter = response.Headers.RetryAfter?.Delta;
                response.Dispose();
                await DelayAsync(attempt, baseDelayMilliseconds, retryAfter, cancellationToken);
            }
            catch (BackfillHttpRequestException)
            {
                throw;
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await DelayAsync(attempt, baseDelayMilliseconds, null, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new BackfillHttpRequestException(
                    $"Provider HTTP request failed after {attempt} attempts: {exception.Message}",
                    attempt,
                    exception.StatusCode,
                    exception);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                await DelayAsync(attempt, baseDelayMilliseconds, null, cancellationToken);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new BackfillHttpRequestException(
                    $"Provider HTTP request timed out after {attempt} attempts.",
                    attempt,
                    innerException: exception);
            }
        }

        throw new InvalidOperationException("HTTP retry policy completed without a response.");
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static Task DelayAsync(
        int failedAttempt,
        int baseDelayMilliseconds,
        TimeSpan? retryAfter,
        CancellationToken cancellationToken)
    {
        var exponentialDelay = TimeSpan.FromMilliseconds(
            Math.Min(30_000, Math.Max(0, baseDelayMilliseconds) * Math.Pow(2, failedAttempt - 1)));
        var delay = retryAfter > exponentialDelay ? retryAfter.Value : exponentialDelay;
        return delay > TimeSpan.Zero ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;
    }
}
