using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

/// <summary>
/// Per-account sliding window limiters for the anonymous account workflows, one per
/// <see cref="RateLimitCategories"/> entry. The account key is a normalized email or a user id.
/// </summary>
public sealed class AccountRateLimiter : IDisposable
{
    private readonly Dictionary<string, PartitionedRateLimiter<string>> limitersByCategory;

    public AccountRateLimiter(IOptions<RateLimitOptions> options)
    {
        var rateLimitOptions = options.Value;
        limitersByCategory = new Dictionary<string, PartitionedRateLimiter<string>>(StringComparer.Ordinal)
        {
            [RateLimitCategories.EmailConfirmation] = Create(rateLimitOptions, rateLimitOptions.EmailConfirmation.Account),
            [RateLimitCategories.EmailDelivery] = Create(rateLimitOptions, rateLimitOptions.EmailDelivery.Account),
            [RateLimitCategories.Login] = Create(rateLimitOptions, rateLimitOptions.Login.Account),
            [RateLimitCategories.PasswordReset] = Create(rateLimitOptions, rateLimitOptions.PasswordReset.Account),
            [RateLimitCategories.Registration] = Create(rateLimitOptions, rateLimitOptions.Registration.Account)
        };
    }

    public ValueTask<RateLimitLease> AcquireAsync(string category, string accountKey, CancellationToken cancellationToken)
    {
        return limitersByCategory[category].AcquireAsync(accountKey, permitCount: 1, cancellationToken);
    }

    public void Dispose()
    {
        foreach (var limiter in limitersByCategory.Values)
            limiter.Dispose();
    }

    private static PartitionedRateLimiter<string> Create(
        RateLimitOptions rateLimitOptions,
        SlidingWindowRateLimitOptions windowOptions)
    {
        return PartitionedRateLimiter.Create<string, string>(accountKey =>
            rateLimitOptions.Enabled
                ? RateLimitPartition.GetSlidingWindowLimiter(accountKey, _ => windowOptions.ToLimiterOptions())
                : RateLimitPartition.GetNoLimiter("disabled"));
    }
}