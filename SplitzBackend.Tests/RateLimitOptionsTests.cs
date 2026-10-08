using Microsoft.Extensions.Options;
using SplitzBackend.Services.RateLimiting;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitOptionsTests
{
    [Fact]
    public void DefaultsMatchApprovedLimits()
    {
        var options = new RateLimitOptions();

        Assert.True(options.Enabled);
        Assert.Equal(60, options.DefaultRetryAfterSeconds);

        AssertPolicy(options.Login.Ip, 20, 300, 5);
        AssertPolicy(options.Login.Account, 10, 300, 5);
        AssertPolicy(options.Registration.Ip, 10, 3600, 12);
        AssertPolicy(options.Registration.Account, 6, 3600, 12);
        AssertPolicy(options.EmailDelivery.Ip, 10, 3600, 12);
        AssertPolicy(options.EmailDelivery.Account, 6, 3600, 12);
        AssertPolicy(options.EmailConfirmation.Ip, 20, 900, 15);
        AssertPolicy(options.EmailConfirmation.Account, 10, 900, 15);
        AssertPolicy(options.PasswordReset.Ip, 20, 900, 15);
        AssertPolicy(options.PasswordReset.Account, 10, 900, 15);

        AssertPolicy(options.Upload.HourlyPerUser, 200, 3600, 12);
        Assert.Equal(5, options.Upload.GlobalConcurrencyPermitLimit);
        Assert.Equal(5, options.Upload.ConcurrencyRetryAfterSeconds);
    }

    [Fact]
    public void ProductionValidationRequiresTrustedProxyConfiguration()
    {
        var validator = new RateLimitOptionsValidator("Production");

        var result = validator.Validate(Options.DefaultName, new RateLimitOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("trusted proxy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DevelopmentValidationAllowsDirectClientConnections()
    {
        var validator = new RateLimitOptionsValidator("Development");

        var result = validator.Validate(Options.DefaultName, new RateLimitOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("not-an-ip", null)]
    [InlineData(null, "not-a-network")]
    public void ValidationRejectsMalformedProxyConfiguration(string? proxy, string? network)
    {
        var options = new RateLimitOptions();
        if (proxy is not null)
            options.TrustedProxies.Add(proxy);
        if (network is not null)
            options.TrustedNetworks.Add(network);

        var result = new RateLimitOptionsValidator("Development").Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("default-retry", "RateLimiting:DefaultRetryAfterSeconds")]
    [InlineData("permit-limit", "RateLimiting:Login:Ip:PermitLimit")]
    [InlineData("window", "RateLimiting:Login:Account:WindowSeconds")]
    [InlineData("segments", "RateLimiting:Registration:Ip:SegmentsPerWindow")]
    [InlineData("upload-concurrency", "RateLimiting:Upload:GlobalConcurrencyPermitLimit")]
    [InlineData("upload-retry", "RateLimiting:Upload:ConcurrencyRetryAfterSeconds")]
    public void ValidationRejectsEachInvalidPolicySetting(string setting, string expectedPath)
    {
        var options = new RateLimitOptions();
        switch (setting)
        {
            case "default-retry":
                options.DefaultRetryAfterSeconds = 0;
                break;
            case "permit-limit":
                options.Login.Ip.PermitLimit = 0;
                break;
            case "window":
                options.Login.Account.WindowSeconds = 0;
                break;
            case "segments":
                options.Registration.Ip.SegmentsPerWindow = 0;
                break;
            case "upload-concurrency":
                options.Upload.GlobalConcurrencyPermitLimit = 0;
                break;
            case "upload-retry":
                options.Upload.ConcurrencyRetryAfterSeconds = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(setting), setting, null);
        }

        var result = new RateLimitOptionsValidator("Development").Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.StartsWith(expectedPath, StringComparison.Ordinal));
    }

    private static void AssertPolicy(
        SlidingWindowRateLimitOptions options,
        int permitLimit,
        int windowSeconds,
        int segmentsPerWindow)
    {
        Assert.Equal(permitLimit, options.PermitLimit);
        Assert.Equal(windowSeconds, options.WindowSeconds);
        Assert.Equal(segmentsPerWindow, options.SegmentsPerWindow);
        Assert.Equal(0, options.ToLimiterOptions().QueueLimit);
    }
}