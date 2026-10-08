using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

/// <summary>
/// Sliding window keyed by the client IP address. <paramref name="selectWindow"/> picks which
/// <c>RateLimitOptions</c> window applies, so one class serves every anonymous account workflow.
/// </summary>
internal sealed class IpRateLimiterPolicy(
    string category,
    Func<RateLimitOptions, SlidingWindowRateLimitOptions> selectWindow) : IRateLimiterPolicy<string>
{
    private const string UnknownAddressPartition = "unknown";

    public Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected => WriteRejectionAsync;

    public RateLimitPartition<string> GetPartition(HttpContext context)
    {
        var rateLimitOptions = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        if (!rateLimitOptions.Enabled)
            return RateLimitPartition.GetNoLimiter("disabled");

        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is null)
        {
            context.RequestServices.GetRequiredService<ILogger<IpRateLimiterPolicy>>().LogWarning(
                "Client IP address is unavailable; using the shared {Partition} rate-limit partition.",
                UnknownAddressPartition);
        }

        return RateLimitPartition.GetSlidingWindowLimiter(
            remoteAddress?.ToString() ?? UnknownAddressPartition,
            _ => selectWindow(rateLimitOptions).ToLimiterOptions());
    }

    private ValueTask WriteRejectionAsync(OnRejectedContext rejectionContext, CancellationToken cancellationToken)
    {
        return rejectionContext.HttpContext.RequestServices.GetRequiredService<RateLimitRejectionWriter>().WriteAsync(
            rejectionContext.HttpContext,
            rejectionContext.Lease,
            category,
            RateLimitPartitionTypes.Ip,
            cancellationToken: cancellationToken);
    }
}