using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SplitzBackend.Models;
using SplitzBackend.Services;
using Xunit;

namespace SplitzBackend.Tests;

public class IdentityEmailSenderTests
{
    [Fact]
    public void BuildConfirmationUrlMapsIdentityParametersToFrontendRoute()
    {
        var options = new EmailOptions
        {
            FrontendBaseUrl = "https://app.example.com",
            ConfirmEmailPath = "/confirm-email"
        };
        var backendLink = "https://api.example.com/account/confirmEmail?userId=user-1&code=abc%2B123&changedEmail=new%40example.com";

        var frontendLink = ResendIdentityEmailSender.BuildConfirmationUrl(options, backendLink);
        var uri = new Uri(frontendLink);
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("https://app.example.com/confirm-email", frontendLink.Split('?')[0]);
        Assert.Equal("user-1", query["userId"]);
        Assert.Equal("abc+123", query["code"]);
        Assert.Equal("new@example.com", query["changedEmail"]);
    }

    [Fact]
    public void BuildPasswordResetUrlMapsIdentityLinkParametersToFrontendRoute()
    {
        var options = new EmailOptions
        {
            FrontendBaseUrl = "https://app.example.com/",
            ResetPasswordPath = "reset-password"
        };
        var backendLink = "https://api.example.com/account/resetPassword?email=alice%40example.com&resetCode=token%2Fvalue";

        var frontendLink = ResendIdentityEmailSender.BuildPasswordResetUrl(options, "alice@example.com", backendLink, null);
        var uri = new Uri(frontendLink);
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("https://app.example.com/reset-password", frontendLink.Split('?')[0]);
        Assert.Equal("alice@example.com", query["email"]);
        Assert.Equal("token/value", query["resetCode"]);
    }

    [Fact]
    public void BuildPasswordResetUrlCanUseResetCodeWhenNoBackendLinkIsAvailable()
    {
        var options = new EmailOptions
        {
            FrontendBaseUrl = "https://app.example.com",
            ResetPasswordPath = "/reset-password"
        };

        var frontendLink = ResendIdentityEmailSender.BuildPasswordResetUrl(options, "alice@example.com", null, "reset-token");
        var uri = new Uri(frontendLink);
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("alice@example.com", query["email"]);
        Assert.Equal("reset-token", query["resetCode"]);
    }

    [Fact]
    public async Task DisabledSenderDoesNotRequireResendClient()
    {
        var sender = new ResendIdentityEmailSender(
            serviceScopeFactory: null,
            emailOptions: Options.Create(new EmailOptions()),
            logger: NullLogger<ResendIdentityEmailSender>.Instance);

        await sender.SendConfirmationLinkAsync(new SplitzUser(), "alice@example.com", "https://api.example.com/account/confirmEmail?userId=1&code=token");
        await sender.SendPasswordResetLinkAsync(new SplitzUser(), "alice@example.com", "https://api.example.com/account/resetPassword?email=alice%40example.com&resetCode=token");
        await sender.SendPasswordResetCodeAsync(new SplitzUser(), "alice@example.com", "token");
    }

    [Fact]
    public void IdentityOptionsRequireConfirmedEmail()
    {
        var options = new IdentityOptions();

        Program.ConfigureIdentityOptions(options);

        Assert.True(options.SignIn.RequireConfirmedEmail);
    }
}