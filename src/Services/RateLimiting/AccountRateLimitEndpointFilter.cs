using Microsoft.AspNetCore.Identity;

namespace SplitzBackend.Services.RateLimiting;

/// <summary>
/// Endpoint filters that apply the per-account limit from <see cref="AccountRateLimiter"/>. They run
/// after the per-IP policy, so a request only reaches them once it has passed the IP window.
/// </summary>
public static class AccountRateLimitEndpointFilter
{
    /// <summary>
    /// Limits by the email address found in the endpoint's bound arguments. Requests without an email
    /// are passed through; they fail validation downstream and cannot target an account.
    /// </summary>
    public static Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> ForEmail(
        string category,
        Func<IList<object?>, string?> selectEmail)
    {
        return (_, next) => async invocationContext =>
        {
            var email = selectEmail(invocationContext.Arguments);
            if (string.IsNullOrWhiteSpace(email))
                return await next(invocationContext);

            var normalizer = invocationContext.HttpContext.RequestServices.GetRequiredService<ILookupNormalizer>();
            var accountKey = normalizer.NormalizeEmail(email.Trim()) ?? string.Empty;
            return await LimitAsync(invocationContext, next, category, accountKey);
        };
    }

    /// <summary>
    /// Limits by the <c>userId</c> argument. Identity's confirmEmail handler binds it straight from the
    /// query string rather than through a request DTO, so the argument is located by parameter name.
    /// </summary>
    public static Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> ForUserId(
        string category)
    {
        return (factoryContext, next) =>
        {
            var userIdArgumentIndex = Array.FindIndex(
                factoryContext.MethodInfo.GetParameters(),
                parameter => string.Equals(parameter.Name, "userId", StringComparison.OrdinalIgnoreCase));

            return async invocationContext =>
            {
                if (userIdArgumentIndex < 0
                    || userIdArgumentIndex >= invocationContext.Arguments.Count
                    || invocationContext.Arguments[userIdArgumentIndex] is not string userId
                    || string.IsNullOrWhiteSpace(userId))
                    return await next(invocationContext);

                return await LimitAsync(invocationContext, next, category, userId);
            };
        };
    }

    private static async ValueTask<object?> LimitAsync(
        EndpointFilterInvocationContext invocationContext,
        EndpointFilterDelegate next,
        string category,
        string accountKey)
    {
        var httpContext = invocationContext.HttpContext;
        var limiter = httpContext.RequestServices.GetRequiredService<AccountRateLimiter>();

        using var lease = await limiter.AcquireAsync(category, accountKey, httpContext.RequestAborted);
        if (lease.IsAcquired)
            return await next(invocationContext);

        await httpContext.RequestServices.GetRequiredService<RateLimitRejectionWriter>().WriteAsync(
            httpContext,
            lease,
            category,
            RateLimitPartitionTypes.Account,
            cancellationToken: httpContext.RequestAborted);
        return Results.Empty;
    }
}