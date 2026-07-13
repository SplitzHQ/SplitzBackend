using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

public static class LoginAccountRateLimitEndpointFilter
{
    public static EndpointFilterDelegate Create(
        EndpointFilterFactoryContext _,
        EndpointFilterDelegate next)
    {
        return async invocationContext =>
        {
            var login = invocationContext.Arguments.OfType<LoginRequest>().SingleOrDefault();
            if (login is null)
                return await next(invocationContext);

            var services = invocationContext.HttpContext.RequestServices;
            var normalizer = services.GetRequiredService<ILookupNormalizer>();
            var normalizedEmail = normalizer.NormalizeEmail(login.Email?.Trim() ?? string.Empty) ?? string.Empty;
            var limiter = services.GetRequiredService<LoginAccountRateLimiter>();

            using var lease = await limiter.AcquireAsync(
                normalizedEmail,
                invocationContext.HttpContext.RequestAborted);
            if (lease.IsAcquired)
                return await next(invocationContext);

            var options = services.GetRequiredService<IOptions<RateLimitOptions>>().Value;
            var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var calculatedRetryAfter)
                ? calculatedRetryAfter
                : TimeSpan.FromSeconds(options.DefaultRetryAfterSeconds);
            var writer = services.GetRequiredService<RateLimitRejectionWriter>();

            await writer.WriteAsync(
                invocationContext.HttpContext,
                retryAfter,
                category: "login",
                partitionType: "account",
                invocationContext.HttpContext.RequestAborted);
            return Results.Empty;
        };
    }
}