using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

public static class EmailConfirmationAccountRateLimitEndpointFilter
{
    public static EndpointFilterDelegate Create(
        EndpointFilterFactoryContext factoryContext,
        EndpointFilterDelegate next)
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

            var services = invocationContext.HttpContext.RequestServices;
            var limiter = services.GetRequiredService<EmailConfirmationAccountRateLimiter>();

            using var lease = await limiter.AcquireAsync(
                userId,
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
                category: "email-confirmation",
                partitionType: "account",
                invocationContext.HttpContext.RequestAborted);
            return null;
        };
    }
}