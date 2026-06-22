using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SplitzBackend.Controllers;
using SplitzBackend.Services;
using Xunit;

namespace SplitzBackend.Tests;

public class EmailCapabilitiesControllerTests
{
    [Fact]
    public void EmailCapabilitiesIsAnonymous()
    {
        var method = typeof(AccountController).GetMethod(nameof(AccountController.GetEmailCapabilities));

        Assert.NotNull(method);
        Assert.Contains(method.GetCustomAttributes(inherit: true), attribute => attribute is Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute);
    }

    [Fact]
    public void EmailCapabilitiesReportsUnavailableWhenEmailIsDisabled()
    {
        var controller = new AccountController(
            db: null!,
            userManager: null!,
            mapper: null!,
            imageStorage: null!,
            emailOptions: Options.Create(new EmailOptions()));

        var result = controller.GetEmailCapabilities();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var capabilities = Assert.IsType<EmailCapabilitiesDto>(ok.Value);
        Assert.False(capabilities.EmailEnabled);
        Assert.False(capabilities.PasswordResetEnabled);
    }

    [Fact]
    public void EmailCapabilitiesReportsAvailableWhenEmailIsConfigured()
    {
        var controller = new AccountController(
            db: null!,
            userManager: null!,
            mapper: null!,
            imageStorage: null!,
            emailOptions: Options.Create(new EmailOptions
            {
                Enabled = true,
                ApiKey = "re_test_key",
                FromEmail = "noreply@example.com",
                FrontendBaseUrl = "https://app.example.com"
            }));

        var result = controller.GetEmailCapabilities();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var capabilities = Assert.IsType<EmailCapabilitiesDto>(ok.Value);
        Assert.True(capabilities.EmailEnabled);
        Assert.True(capabilities.PasswordResetEnabled);
    }
}