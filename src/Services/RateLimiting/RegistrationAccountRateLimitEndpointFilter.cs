using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

public static class RegistrationAccountRateLimitEndpointFilter
{
    public static EndpointFilterDelegate Create(
        EndpointFilterFactoryContext _,
        EndpointFilterDelegate next)
    {
        return async invocationContext =>
        {
            var registration = invocationContext.Arguments.OfType<RegisterRequest>().SingleOrDefault();
            if (registration is null)
                return await next(invocationContext);

            var services = invocationContext.HttpContext.RequestServices;
            var normalizer = services.GetRequiredService<ILookupNormalizer>();
            var normalizedEmail = normalizer.NormalizeEmail(registration.Email?.Trim() ?? string.Empty) ?? string.Empty;
            var limiter = services.GetRequiredService<RegistrationAccountRateLimiter>();

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
                category: "registration",
                partitionType: "account",
                invocationContext.HttpContext.RequestAborted);
            return null;
        };
    }
}