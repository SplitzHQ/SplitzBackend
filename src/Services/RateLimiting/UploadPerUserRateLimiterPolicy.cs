using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

/// <summary>Hourly sliding window per authenticated user for image uploads.</summary>
internal sealed class UploadPerUserRateLimiterPolicy : IRateLimiterPolicy<string>
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
            _ => rateLimitOptions.Upload.HourlyPerUser.ToLimiterOptions());
    }

    private static ValueTask WriteRejectionAsync(OnRejectedContext rejectionContext, CancellationToken cancellationToken)
    {
        return rejectionContext.HttpContext.RequestServices.GetRequiredService<RateLimitRejectionWriter>().WriteAsync(
            rejectionContext.HttpContext,
            rejectionContext.Lease,
            RateLimitCategories.Upload,
            RateLimitPartitionTypes.User,
            cancellationToken: cancellationToken);
    }
}