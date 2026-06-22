using Microsoft.Extensions.Options;
using SplitzBackend.Services;
using Xunit;

namespace SplitzBackend.Tests;

public class EmailOptionsTests
{
    [Fact]
    public void DefaultsKeepLocalDevelopmentEmailUnavailable()
    {
        var options = new EmailOptions();

        Assert.Equal("Resend", options.Provider);
        Assert.False(options.Enabled);
        Assert.Equal("http://localhost:5173", options.FrontendBaseUrl);
        Assert.Equal("/confirm-email", options.ConfirmEmailPath);
        Assert.Equal("/reset-password", options.ResetPasswordPath);
        Assert.False(options.IsAvailable);
    }

    [Fact]
    public void IsAvailableRequiresEnabledAndAllDeliverySettings()
    {
        var options = new EmailOptions
        {
            Enabled = true,
            ApiKey = "re_test_key",
            FromEmail = "noreply@example.com",
            FrontendBaseUrl = "https://app.example.com"
        };

        Assert.True(options.IsAvailable);
    }

    [Fact]
    public void ProductionValidationFailsWhenEnabledConfigurationIsIncomplete()
    {
        var options = new EmailOptions
        {
            Enabled = true,
            ApiKey = "",
            FromEmail = "noreply@example.com",
            FrontendBaseUrl = "https://app.example.com"
        };

        Assert.Throws<OptionsValidationException>(() => EmailOptions.ValidateForProduction(options));
    }
}