using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

public static class EmailDeliveryAccountRateLimitEndpointFilter
{
    public static EndpointFilterDelegate Create(
        EndpointFilterFactoryContext _,
        EndpointFilterDelegate next)
    {
        return async invocationContext =>
        {
            var email = GetEmail(invocationContext.Arguments);
            if (email is null)
                return await next(invocationContext);

            var services = invocationContext.HttpContext.RequestServices;
            var normalizer = services.GetRequiredService<ILookupNormalizer>();
            var normalizedEmail = normalizer.NormalizeEmail(email.Trim()) ?? string.Empty;
            var limiter = services.GetRequiredService<EmailDeliveryAccountRateLimiter>();

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
                category: "email-delivery",
                partitionType: "account",
                invocationContext.HttpContext.RequestAborted);
            return null;
        };
    }

    private static string? GetEmail(IList<object?> arguments)
    {
        var forgotPassword = arguments.OfType<ForgotPasswordRequest>().SingleOrDefault();
        if (forgotPassword is not null)
            return forgotPassword.Email;

        return arguments.OfType<ResendConfirmationEmailRequest>().SingleOrDefault()?.Email;
    }
}