using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

public static class PasswordResetAccountRateLimitEndpointFilter
{
    public static EndpointFilterDelegate Create(
        EndpointFilterFactoryContext _,
        EndpointFilterDelegate next)
    {
        return async invocationContext =>
        {
            var reset = invocationContext.Arguments.OfType<ResetPasswordRequest>().SingleOrDefault();
            if (reset is null)
                return await next(invocationContext);

            var services = invocationContext.HttpContext.RequestServices;
            var normalizer = services.GetRequiredService<ILookupNormalizer>();
            var normalizedEmail = normalizer.NormalizeEmail(reset.Email?.Trim() ?? string.Empty) ?? string.Empty;
            var limiter = services.GetRequiredService<PasswordResetAccountRateLimiter>();

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
                category: "password-reset",
                partitionType: "account",
                invocationContext.HttpContext.RequestAborted);
            return Results.Empty;
        };
    }
}