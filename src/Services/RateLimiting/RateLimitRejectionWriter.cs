using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

/// <summary>Writes the shared 429 response: a neutral problem document plus a <c>Retry-After</c> header.</summary>
public sealed class RateLimitRejectionWriter(
    IOptions<RateLimitOptions> options,
    ILogger<RateLimitRejectionWriter> logger)
{
    /// <summary>
    /// Writes the rejection using the lease's retry-after metadata, or <paramref name="fallbackRetryAfter"/>
    /// (default: <c>DefaultRetryAfterSeconds</c>) when the lease carries none.
    /// </summary>
    public ValueTask WriteAsync(
        HttpContext context,
        RateLimitLease lease,
        string category,
        string partitionType,
        TimeSpan? fallbackRetryAfter = null,
        CancellationToken cancellationToken = default)
    {
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter)
            ? leaseRetryAfter
            : fallbackRetryAfter ?? TimeSpan.FromSeconds(options.Value.DefaultRetryAfterSeconds);

        return WriteAsync(context, retryAfter, category, partitionType, cancellationToken);
    }

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
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

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