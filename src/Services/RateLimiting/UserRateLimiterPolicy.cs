using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

/// <summary>
/// Sliding window keyed by the authenticated user id. <paramref name="selectWindow"/> picks which
/// <c>RateLimitOptions</c> window applies, so one class serves every per-user workflow.
/// Unauthenticated requests are not limited here; authorization rejects them first.
/// </summary>
internal sealed class UserRateLimiterPolicy(
    string category,
    Func<RateLimitOptions, SlidingWindowRateLimitOptions> selectWindow) : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected => WriteRejectionAsync;

    public RateLimitPartition<string> GetPartition(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return RateLimitPartition.GetNoLimiter("unauthenticated");

        var rateLimitOptions = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        if (!rateLimitOptions.Enabled)
            return RateLimitPartition.GetNoLimiter("disabled");

        return RateLimitPartition.GetSlidingWindowLimiter(
            userId,
            _ => selectWindow(rateLimitOptions).ToLimiterOptions());
    }

    private ValueTask WriteRejectionAsync(OnRejectedContext rejectionContext, CancellationToken cancellationToken)
    {
        return rejectionContext.HttpContext.RequestServices.GetRequiredService<RateLimitRejectionWriter>().WriteAsync(
            rejectionContext.HttpContext,
            rejectionContext.Lease,
            category,
            RateLimitPartitionTypes.User,
            cancellationToken: cancellationToken);
    }
}