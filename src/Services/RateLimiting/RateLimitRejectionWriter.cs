using Microsoft.AspNetCore.Mvc;

namespace SplitzBackend.Services.RateLimiting;

public sealed class RateLimitRejectionWriter(ILogger<RateLimitRejectionWriter> logger)
{
    public async ValueTask WriteAsync(
        HttpContext context,
        TimeSpan retryAfter,
        string category,
        string partitionType,
        CancellationToken cancellationToken = default)
    {
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        logger.LogWarning(
            "Rate limit rejected {Method} {RoutePattern} for {Category} partition type {PartitionType}; retry after {RetryAfterSeconds} seconds.",
            context.Request.Method,
            GetRoutePattern(context),
            category,
            partitionType,
            retryAfterSeconds);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Detail = "Too many requests were received. Please try again later."
        };
        problem.Extensions["code"] = "rate_limit_exceeded";

        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
    }

    private static string GetRoutePattern(HttpContext context)
    {
        return context.GetEndpoint() is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText ?? context.Request.Path
            : context.Request.Path;
    }
}