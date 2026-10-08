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
        services.AddSingleton<IValidateOptions<RateLimitOptions>>(
            new RateLimitOptionsValidator(environment.EnvironmentName));
        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<RateLimitOptions>>(ConfigureForwardedHeaders);

        services.AddSingleton<RateLimitRejectionWriter>();
        services.AddSingleton<AccountRateLimiter>();
        services.AddRateLimiter(options =>
        {
            // Named policies write their own rejections, so this handler only sees the global limiter.
            options.OnRejected = WriteGlobalUploadRejectionAsync;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(GetGlobalUploadPartition);

            options.AddPolicy(
                RateLimitPolicyNames.LoginIp,
                new IpRateLimiterPolicy(RateLimitCategories.Login, o => o.Login.Ip));
            options.AddPolicy(
                RateLimitPolicyNames.RegistrationIp,
                new IpRateLimiterPolicy(RateLimitCategories.Registration, o => o.Registration.Ip));
            options.AddPolicy(
                RateLimitPolicyNames.EmailDeliveryIp,
                new IpRateLimiterPolicy(RateLimitCategories.EmailDelivery, o => o.EmailDelivery.Ip));
            options.AddPolicy(
                RateLimitPolicyNames.EmailConfirmationIp,
                new IpRateLimiterPolicy(RateLimitCategories.EmailConfirmation, o => o.EmailConfirmation.Ip));
            options.AddPolicy(
                RateLimitPolicyNames.PasswordResetIp,
                new IpRateLimiterPolicy(RateLimitCategories.PasswordReset, o => o.PasswordReset.Ip));
            options.AddPolicy(
                RateLimitPolicyNames.UploadPerUser,
                new UploadPerUserRateLimiterPolicy());
        });

        return services;
    }

    /// <summary>
    /// Only honours X-Forwarded-* headers when a trusted proxy or network is configured; otherwise the
    /// connection's own address is the client address.
    /// </summary>
    private static void ConfigureForwardedHeaders(
        ForwardedHeadersOptions options,
        IOptions<RateLimitOptions> rateLimitOptions)
    {
        var trustedProxies = rateLimitOptions.Value.TrustedProxies;
        var trustedNetworks = rateLimitOptions.Value.TrustedNetworks;

        options.ForwardLimit = 1;
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
    }

    /// <summary>
    /// Caps how many authenticated uploads run at once across the whole server. Every other request,
    /// including unauthenticated calls to upload routes, shares a single unlimited partition.
    /// </summary>
    private static RateLimitPartition<string> GetGlobalUploadPartition(HttpContext context)
    {
        var isUploadEndpoint = context.GetEndpoint()?
            .Metadata.GetMetadata<UploadRateLimitEndpointMetadataAttribute>() is not null;
        var isAuthenticated = !string.IsNullOrEmpty(context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (!isUploadEndpoint || !isAuthenticated)
            return RateLimitPartition.GetNoLimiter("unlimited");

        var rateLimitOptions = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        if (!rateLimitOptions.Enabled)
            return RateLimitPartition.GetNoLimiter("disabled");

        return RateLimitPartition.GetConcurrencyLimiter("authenticated-upload", _ =>
            new ConcurrencyLimiterOptions
            {
                PermitLimit = rateLimitOptions.Upload.GlobalConcurrencyPermitLimit,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    }

    private static ValueTask WriteGlobalUploadRejectionAsync(
        OnRejectedContext rejectionContext,
        CancellationToken cancellationToken)
    {
        var services = rejectionContext.HttpContext.RequestServices;
        var rateLimitOptions = services.GetRequiredService<IOptions<RateLimitOptions>>().Value;

        // A concurrency lease carries no retry-after metadata, so the configured value is always used.
        return services.GetRequiredService<RateLimitRejectionWriter>().WriteAsync(
            rejectionContext.HttpContext,
            rejectionContext.Lease,
            RateLimitCategories.Upload,
            RateLimitPartitionTypes.Global,
            TimeSpan.FromSeconds(rateLimitOptions.Upload.ConcurrencyRetryAfterSeconds),
            cancellationToken);
    }
}