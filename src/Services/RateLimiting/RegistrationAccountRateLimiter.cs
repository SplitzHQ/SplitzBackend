using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

public sealed class RegistrationAccountRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter;

    public RegistrationAccountRateLimiter(IOptions<RateLimitOptions> options)
    {
        var rateLimitOptions = options.Value;
        limiter = PartitionedRateLimiter.Create<string, string>(accountKey =>
        {
            if (!rateLimitOptions.Enabled)
                return RateLimitPartition.GetNoLimiter("disabled");

            return RateLimitPartition.GetSlidingWindowLimiter(accountKey, _ =>
                RateLimitingServiceCollectionExtensions.CreateSlidingWindowOptions(
                    rateLimitOptions.Registration.Account));
        });
    }

    public ValueTask<RateLimitLease> AcquireAsync(string accountKey, CancellationToken cancellationToken)
    {
        return limiter.AcquireAsync(accountKey, permitCount: 1, cancellationToken);
    }

    public void Dispose()
    {
        limiter.Dispose();
    }
}