using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

public sealed class EmailConfirmationAccountRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter;

    public EmailConfirmationAccountRateLimiter(IOptions<RateLimitOptions> options)
    {
        var rateLimitOptions = options.Value;
        limiter = PartitionedRateLimiter.Create<string, string>(userId =>
        {
            if (!rateLimitOptions.Enabled)
                return RateLimitPartition.GetNoLimiter("disabled");

            return RateLimitPartition.GetSlidingWindowLimiter(userId, _ =>
                RateLimitingServiceCollectionExtensions.CreateSlidingWindowOptions(
                    rateLimitOptions.EmailConfirmation.Account));
        });
    }

    public ValueTask<RateLimitLease> AcquireAsync(string userId, CancellationToken cancellationToken)
    {
        return limiter.AcquireAsync(userId, permitCount: 1, cancellationToken);
    }

    public void Dispose()
    {
        limiter.Dispose();
    }
}