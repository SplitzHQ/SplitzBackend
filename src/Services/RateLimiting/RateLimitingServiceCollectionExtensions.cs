using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

public static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddSplitzRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection(RateLimitOptions.SectionName);

        services.AddSingleton<IValidateOptions<RateLimitOptions>>(
            new RateLimitOptionsValidator(environment.EnvironmentName));
        services.AddOptions<RateLimitOptions>()
            .Bind(section)
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<RateLimitOptions>>((options, rateLimitOptions) =>
            {
                options.ForwardLimit = 1;
                var trustedProxies = rateLimitOptions.Value.TrustedProxies;
                var trustedNetworks = rateLimitOptions.Value.TrustedNetworks;
                if (trustedProxies.Count == 0 && trustedNetworks.Count == 0)
                {
                    options.ForwardedHeaders = ForwardedHeaders.None;
                    return;
                }

                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();

                foreach (var proxy in trustedProxies)
                    if (IPAddress.TryParse(proxy, out var address))
                        options.KnownProxies.Add(address);

                foreach (var network in trustedNetworks)
                    if (System.Net.IPNetwork.TryParse(network, out var parsedNetwork))
                        options.KnownIPNetworks.Add(parsedNetwork);
            });

        services.AddSingleton<RateLimitRejectionWriter>();
        services.AddSingleton<EmailConfirmationAccountRateLimiter>();
        services.AddSingleton<EmailDeliveryAccountRateLimiter>();
        services.AddSingleton<LoginAccountRateLimiter>();
        services.AddSingleton<PasswordResetAccountRateLimiter>();
        services.AddSingleton<RegistrationAccountRateLimiter>();
        services.AddRateLimiter(options =>
        {
            options.OnRejected = async (rejectionContext, cancellationToken) =>
            {
                var requestServices = rejectionContext.HttpContext.RequestServices;
                var rateLimitOptions = requestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                var endpoint = rejectionContext.HttpContext.GetEndpoint();
                var isUpload = endpoint?.Metadata.GetMetadata<UploadRateLimitEndpointMetadataAttribute>() is not null;
                var retryAfter = rejectionContext.Lease.TryGetMetadata(
                    MetadataName.RetryAfter,
                    out var calculatedRetryAfter)
                    ? calculatedRetryAfter
                    : TimeSpan.FromSeconds(isUpload
                        ? rateLimitOptions.Upload.ConcurrencyRetryAfterSeconds
                        : rateLimitOptions.DefaultRetryAfterSeconds);
                var metadata = endpoint?.Metadata.GetMetadata<RateLimitEndpointMetadata>();

                await requestServices.GetRequiredService<RateLimitRejectionWriter>().WriteAsync(
                    rejectionContext.HttpContext,
                    retryAfter,
                    metadata?.Category ?? (isUpload ? "upload" : "unknown"),
                    metadata?.PartitionType ?? (isUpload ? "global" : "ip"),
                    cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var uploadMetadata = context.GetEndpoint()?
                    .Metadata.GetMetadata<UploadRateLimitEndpointMetadataAttribute>();
                var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (uploadMetadata is null || string.IsNullOrEmpty(userId))
                    return RateLimitPartition.GetNoLimiter("not-authenticated-upload");

                var rateLimitOptions = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitOptions>>()
                    .Value;
                if (!rateLimitOptions.Enabled)
                    return RateLimitPartition.GetNoLimiter("disabled");

                return RateLimitPartition.GetConcurrencyLimiter("authenticated-upload", _ =>
                    new ConcurrencyLimiterOptions
                    {
                        PermitLimit = rateLimitOptions.Upload.GlobalConcurrencyPermitLimit,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            options.AddPolicy(RateLimitPolicyNames.LoginIp, context =>
            {
                var rateLimitOptions = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitOptions>>()
                    .Value;
                if (!rateLimitOptions.Enabled)
                    return RateLimitPartition.GetNoLimiter("disabled");

                var remoteAddress = context.Connection.RemoteIpAddress;
                var partitionKey = remoteAddress?.ToString() ?? "unknown";
                if (remoteAddress is null)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("SplitzBackend.RateLimiting")
                        .LogWarning("Client IP address is unavailable; using the shared unknown rate-limit partition.");
                }

                return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
                    CreateSlidingWindowOptions(rateLimitOptions.Login.Ip));
            });

            options.AddPolicy(RateLimitPolicyNames.EmailDeliveryIp, context =>
            {
                var rateLimitOptions = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitOptions>>()
                    .Value;
                if (!rateLimitOptions.Enabled)
                    return RateLimitPartition.GetNoLimiter("disabled");

                var remoteAddress = context.Connection.RemoteIpAddress;
                var partitionKey = remoteAddress?.ToString() ?? "unknown";
                if (remoteAddress is null)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("SplitzBackend.RateLimiting")
                        .LogWarning("Client IP address is unavailable; using the shared unknown rate-limit partition.");
                }

                return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
                    CreateSlidingWindowOptions(rateLimitOptions.EmailDelivery.Ip));
            });

            options.AddPolicy(RateLimitPolicyNames.EmailConfirmationIp, context =>
            {
                var rateLimitOptions = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitOptions>>()
                    .Value;
                if (!rateLimitOptions.Enabled)
                    return RateLimitPartition.GetNoLimiter("disabled");

                var remoteAddress = context.Connection.RemoteIpAddress;
                var partitionKey = remoteAddress?.ToString() ?? "unknown";
                if (remoteAddress is null)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("SplitzBackend.RateLimiting")
                        .LogWarning("Client IP address is unavailable; using the shared unknown rate-limit partition.");
                }

                return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
                    CreateSlidingWindowOptions(rateLimitOptions.EmailConfirmation.Ip));
            });

            options.AddPolicy(RateLimitPolicyNames.RegistrationIp, context =>
            {
                var rateLimitOptions = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitOptions>>()
                    .Value;
                if (!rateLimitOptions.Enabled)
                    return RateLimitPartition.GetNoLimiter("disabled");

                var remoteAddress = context.Connection.RemoteIpAddress;
                var partitionKey = remoteAddress?.ToString() ?? "unknown";
                if (remoteAddress is null)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("SplitzBackend.RateLimiting")
                        .LogWarning("Client IP address is unavailable; using the shared unknown rate-limit partition.");
                }

                return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
                    CreateSlidingWindowOptions(rateLimitOptions.Registration.Ip));
            });

            options.AddPolicy(RateLimitPolicyNames.PasswordResetIp, context =>
            {
                var rateLimitOptions = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitOptions>>()
                    .Value;
                if (!rateLimitOptions.Enabled)
                    return RateLimitPartition.GetNoLimiter("disabled");

                var remoteAddress = context.Connection.RemoteIpAddress;
                var partitionKey = remoteAddress?.ToString() ?? "unknown";
                if (remoteAddress is null)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("SplitzBackend.RateLimiting")
                        .LogWarning("Client IP address is unavailable; using the shared unknown rate-limit partition.");
                }

                return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
                    CreateSlidingWindowOptions(rateLimitOptions.PasswordReset.Ip));
            });

            options.AddPolicy(
                RateLimitPolicyNames.UploadPerUser,
                new UploadPerUserRateLimiterPolicy());
        });

        return services;
    }

    internal static SlidingWindowRateLimiterOptions CreateSlidingWindowOptions(
        SlidingWindowRateLimitOptions options)
    {
        return new SlidingWindowRateLimiterOptions
        {
            PermitLimit = options.PermitLimit,
            Window = TimeSpan.FromSeconds(options.WindowSeconds),
            SegmentsPerWindow = options.SegmentsPerWindow,
            QueueLimit = options.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        };
    }
}

internal sealed class UploadPerUserRateLimiterPolicy : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected => WriteRejectionAsync;

    public RateLimitPartition<string> GetPartition(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return RateLimitPartition.GetNoLimiter("unauthenticated");

        var rateLimitOptions = context.RequestServices
            .GetRequiredService<IOptions<RateLimitOptions>>()
            .Value;
        if (!rateLimitOptions.Enabled)
            return RateLimitPartition.GetNoLimiter("disabled");

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ =>
            RateLimitingServiceCollectionExtensions.CreateSlidingWindowOptions(
                rateLimitOptions.Upload.HourlyPerUser));
    }

    private static async ValueTask WriteRejectionAsync(
        OnRejectedContext rejectionContext,
        CancellationToken cancellationToken)
    {
        var requestServices = rejectionContext.HttpContext.RequestServices;
        var rateLimitOptions = requestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        var retryAfter = rejectionContext.Lease.TryGetMetadata(
            MetadataName.RetryAfter,
            out var calculatedRetryAfter)
            ? calculatedRetryAfter
            : TimeSpan.FromSeconds(rateLimitOptions.DefaultRetryAfterSeconds);

        await requestServices.GetRequiredService<RateLimitRejectionWriter>().WriteAsync(
            rejectionContext.HttpContext,
            retryAfter,
            "upload",
            "user",
            cancellationToken);
    }
}