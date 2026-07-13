using System.Net;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.RateLimiting;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;
    public int DefaultRetryAfterSeconds { get; set; } = 60;
    public List<string> TrustedProxies { get; set; } = [];
    public List<string> TrustedNetworks { get; set; } = [];
    public AccountRateLimitOptions Login { get; set; } = AccountRateLimitOptions.Create(20, 10, 300, 5);
    public AccountRateLimitOptions Registration { get; set; } = AccountRateLimitOptions.Create(10, 6, 3600, 12);
    public AccountRateLimitOptions EmailDelivery { get; set; } = AccountRateLimitOptions.Create(10, 6, 3600, 12);
    public AccountRateLimitOptions EmailConfirmation { get; set; } = AccountRateLimitOptions.Create(20, 10, 900, 15);
    public AccountRateLimitOptions PasswordReset { get; set; } = AccountRateLimitOptions.Create(20, 10, 900, 15);
    public UploadRateLimitOptions Upload { get; set; } = new();
}

public sealed class AccountRateLimitOptions
{
    public SlidingWindowRateLimitOptions Ip { get; set; } = new();
    public SlidingWindowRateLimitOptions Account { get; set; } = new();

    public static AccountRateLimitOptions Create(
        int ipPermitLimit,
        int accountPermitLimit,
        int windowSeconds,
        int segmentsPerWindow)
    {
        return new AccountRateLimitOptions
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
    public int QueueLimit { get; set; }

    public static SlidingWindowRateLimitOptions Create(int permitLimit, int windowSeconds, int segmentsPerWindow)
    {
        return new SlidingWindowRateLimitOptions
        {
            PermitLimit = permitLimit,
            WindowSeconds = windowSeconds,
            SegmentsPerWindow = segmentsPerWindow,
            QueueLimit = 0
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

        if (options.Enabled
            && environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase)
            && options.TrustedProxies.Count == 0
            && options.TrustedNetworks.Count == 0)
            failures.Add("Rate limiting in Production requires at least one trusted proxy or trusted network.");

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
        if (options.QueueLimit != 0)
            failures.Add($"{path}:QueueLimit must be zero.");
    }
}