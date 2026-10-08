using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

/// <summary>
/// Bound from the <c>RateLimiting</c> configuration section. The property initializers are the
/// production defaults, so appsettings only needs to list values that differ.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;
    public int DefaultRetryAfterSeconds { get; set; } = 60;
    public List<string> TrustedProxies { get; set; } = [];
    public List<string> TrustedNetworks { get; set; } = [];
    public EndpointRateLimitOptions Login { get; set; } = EndpointRateLimitOptions.Create(20, 10, 300, 5);
    public EndpointRateLimitOptions Registration { get; set; } = EndpointRateLimitOptions.Create(10, 6, 3600, 12);
    public EndpointRateLimitOptions EmailDelivery { get; set; } = EndpointRateLimitOptions.Create(10, 6, 3600, 12);
    public EndpointRateLimitOptions EmailConfirmation { get; set; } = EndpointRateLimitOptions.Create(20, 10, 900, 15);
    public EndpointRateLimitOptions PasswordReset { get; set; } = EndpointRateLimitOptions.Create(20, 10, 900, 15);
    public UploadRateLimitOptions Upload { get; set; } = new();
}

/// <summary>Limits for one anonymous account workflow: a per-IP window and a per-account (email or user id) window.</summary>
public sealed class EndpointRateLimitOptions
{
    public SlidingWindowRateLimitOptions Ip { get; set; } = new();
    public SlidingWindowRateLimitOptions Account { get; set; } = new();

    public static EndpointRateLimitOptions Create(
        int ipPermitLimit,
        int accountPermitLimit,
        int windowSeconds,
        int segmentsPerWindow)
    {
        return new EndpointRateLimitOptions
        {
            Ip = SlidingWindowRateLimitOptions.Create(ipPermitLimit, windowSeconds, segmentsPerWindow),
            Account = SlidingWindowRateLimitOptions.Create(accountPermitLimit, windowSeconds, segmentsPerWindow)
        };
    }
}

public sealed class SlidingWindowRateLimitOptions
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public int SegmentsPerWindow { get; set; }

    public static SlidingWindowRateLimitOptions Create(int permitLimit, int windowSeconds, int segmentsPerWindow)
    {
        return new SlidingWindowRateLimitOptions
        {
            PermitLimit = permitLimit,
            WindowSeconds = windowSeconds,
            SegmentsPerWindow = segmentsPerWindow
        };
    }

    /// <summary>Requests are never queued: a request over the limit is rejected immediately.</summary>
    public SlidingWindowRateLimiterOptions ToLimiterOptions()
    {
        return new SlidingWindowRateLimiterOptions
        {
            PermitLimit = PermitLimit,
            Window = TimeSpan.FromSeconds(WindowSeconds),
            SegmentsPerWindow = SegmentsPerWindow,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        };
    }
}

public sealed class UploadRateLimitOptions
{
    public SlidingWindowRateLimitOptions HourlyPerUser { get; set; } =
        SlidingWindowRateLimitOptions.Create(200, 3600, 12);

    public int GlobalConcurrencyPermitLimit { get; set; } = 5;
    public int ConcurrencyRetryAfterSeconds { get; set; } = 5;
}

public sealed class RateLimitOptionsValidator(string environmentName) : IValidateOptions<RateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitOptions options)
    {
        var failures = new List<string>();

        if (options.DefaultRetryAfterSeconds <= 0)
            failures.Add("RateLimiting:DefaultRetryAfterSeconds must be greater than zero.");

        ValidatePolicy(options.Login.Ip, "RateLimiting:Login:Ip", failures);
        ValidatePolicy(options.Login.Account, "RateLimiting:Login:Account", failures);
        ValidatePolicy(options.Registration.Ip, "RateLimiting:Registration:Ip", failures);
        ValidatePolicy(options.Registration.Account, "RateLimiting:Registration:Account", failures);
        ValidatePolicy(options.EmailDelivery.Ip, "RateLimiting:EmailDelivery:Ip", failures);
        ValidatePolicy(options.EmailDelivery.Account, "RateLimiting:EmailDelivery:Account", failures);
        ValidatePolicy(options.EmailConfirmation.Ip, "RateLimiting:EmailConfirmation:Ip", failures);
        ValidatePolicy(options.EmailConfirmation.Account, "RateLimiting:EmailConfirmation:Account", failures);
        ValidatePolicy(options.PasswordReset.Ip, "RateLimiting:PasswordReset:Ip", failures);
        ValidatePolicy(options.PasswordReset.Account, "RateLimiting:PasswordReset:Account", failures);
        ValidatePolicy(options.Upload.HourlyPerUser, "RateLimiting:Upload:HourlyPerUser", failures);

        if (options.Upload.GlobalConcurrencyPermitLimit <= 0)
            failures.Add("RateLimiting:Upload:GlobalConcurrencyPermitLimit must be greater than zero.");
        if (options.Upload.ConcurrencyRetryAfterSeconds <= 0)
            failures.Add("RateLimiting:Upload:ConcurrencyRetryAfterSeconds must be greater than zero.");

        foreach (var proxy in options.TrustedProxies)
            if (!IPAddress.TryParse(proxy, out _))
                failures.Add($"RateLimiting:TrustedProxies contains invalid IP address '{proxy}'.");

        foreach (var network in options.TrustedNetworks)
            if (!IPNetwork.TryParse(network, out _))
                failures.Add($"RateLimiting:TrustedNetworks contains invalid CIDR network '{network}'.");

        // Behind an unconfigured proxy every client shares the proxy's address, so one busy user would
        // lock the whole site out of login. Refusing to start is safer than silently limiting by proxy IP.
        if (options.Enabled
            && environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase)
            && options.TrustedProxies.Count == 0
            && options.TrustedNetworks.Count == 0)
            failures.Add(
                "Rate limiting in Production requires at least one trusted proxy or trusted network. "
                + "Set RateLimiting:TrustedProxies / RateLimiting:TrustedNetworks, or RateLimiting:Enabled=false when Kestrel is exposed directly.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePolicy(
        SlidingWindowRateLimitOptions options,
        string path,
        ICollection<string> failures)
    {
        if (options.PermitLimit <= 0)
            failures.Add($"{path}:PermitLimit must be greater than zero.");
        if (options.WindowSeconds <= 0)
            failures.Add($"{path}:WindowSeconds must be greater than zero.");
        if (options.SegmentsPerWindow <= 0)
            failures.Add($"{path}:SegmentsPerWindow must be greater than zero.");
    }
}