using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using SplitzBackend.Models;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitingPasswordResetTests
{
    [Fact]
    public async Task PasswordResetAccountQuotaIsSharedAcrossBothRoutesAndNormalizesEmail()
    {
        using var factory = CreatePasswordResetFactory(ipPermitLimit: 100, accountPermitLimit: 1);
        using var client = factory.CreateClient();

        var customResponse = await PostResetAsync(
            client,
            "/account/recovery/reset",
            "  shared@example.com  ",
            "first-code",
            "Password1234");
        var limitedResponse = await PostResetAsync(
            client,
            "/account/resetPassword",
            "SHARED@EXAMPLE.COM",
            "second-secret-code",
            "Password1234");
        var otherEmailResponse = await PostResetAsync(
            client,
            "/account/resetPassword",
            "other@example.com",
            "second-secret-code",
            "Password1234");

        Assert.Equal(HttpStatusCode.BadRequest, customResponse.StatusCode);
        await AssertRateLimitedAsync(limitedResponse, "second-secret-code", "Password1234");
        Assert.Equal(HttpStatusCode.BadRequest, otherEmailResponse.StatusCode);
    }

    [Fact]
    public async Task PasswordResetIpQuotaIsSharedAcrossRoutesAndIsolatedByAddress()
    {
        using var factory = CreatePasswordResetFactory(ipPermitLimit: 1, accountPermitLimit: 100);
        using var client = factory.CreateClient();

        var customResponse = await PostResetAsync(
            client,
            "/account/recovery/reset",
            "first@example.com",
            "first-code",
            "Password1234",
            "203.0.113.20");
        var limitedResponse = await PostResetAsync(
            client,
            "/account/resetPassword",
            "second@example.com",
            "second-code",
            "Password1234",
            "203.0.113.20");
        var otherIpResponse = await PostResetAsync(
            client,
            "/account/resetPassword",
            "third@example.com",
            "third-code",
            "Password1234",
            "203.0.113.21");

        Assert.Equal(HttpStatusCode.BadRequest, customResponse.StatusCode);
        await AssertRateLimitedAsync(limitedResponse, "second-code", "Password1234");
        Assert.Equal(HttpStatusCode.BadRequest, otherIpResponse.StatusCode);
    }

    [Fact]
    public async Task AccountRejectedValidCustomResetDoesNotChangeThePasswordOrConfirmationState()
    {
        using var factory = CreatePasswordResetFactory(ipPermitLimit: 100, accountPermitLimit: 1);
        var (email, validCode) = await CreateUserAndResetCodeAsync(factory);
        using var client = factory.CreateClient();

        var invalidResponse = await PostResetAsync(
            client,
            "/account/resetPassword",
            email,
            "invalid-code",
            "Replacement1234");
        var limitedResponse = await PostResetAsync(
            client,
            "/account/recovery/reset",
            email.ToUpperInvariant(),
            validCode,
            "Replacement1234");

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        await AssertRateLimitedAsync(limitedResponse, validCode, "Replacement1234");
        var passwordState = await GetPasswordStateAsync(factory, email);
        Assert.True(passwordState.OriginalPasswordWorks);
        Assert.False(passwordState.ReplacementPasswordWorks);
        Assert.False(passwordState.EmailConfirmed);
    }

    [Fact]
    public async Task IpRejectedValidCustomResetDoesNotChangeThePasswordOrConfirmationState()
    {
        using var factory = CreatePasswordResetFactory(ipPermitLimit: 1, accountPermitLimit: 100);
        var (email, validCode) = await CreateUserAndResetCodeAsync(factory);
        using var client = factory.CreateClient();

        var invalidResponse = await PostResetAsync(
            client,
            "/account/resetPassword",
            "other@example.com",
            "invalid-code",
            "Replacement1234",
            "203.0.113.20");
        var limitedResponse = await PostResetAsync(
            client,
            "/account/recovery/reset",
            email,
            validCode,
            "Replacement1234",
            "203.0.113.20");

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        await AssertRateLimitedAsync(limitedResponse, validCode, "Replacement1234");
        var passwordState = await GetPasswordStateAsync(factory, email);
        Assert.True(passwordState.OriginalPasswordWorks);
        Assert.False(passwordState.ReplacementPasswordWorks);
        Assert.False(passwordState.EmailConfirmed);
    }

    private static RateLimitingWebApplicationFactory CreatePasswordResetFactory(
        int ipPermitLimit,
        int accountPermitLimit)
    {
        return new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:PasswordReset:Ip:PermitLimit"] = ipPermitLimit.ToString(),
            ["RateLimiting:PasswordReset:Account:PermitLimit"] = accountPermitLimit.ToString()
        });
    }

    private static async Task<HttpResponseMessage> PostResetAsync(
        HttpClient client,
        string path,
        string email,
        string resetCode,
        string newPassword,
        string? remoteIp = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { email, resetCode, newPassword })
        };
        if (remoteIp is not null)
            request.Headers.Add("X-Test-Remote-IP", remoteIp);

        return await client.SendAsync(request);
    }

    private static async Task<(string Email, string ResetCode)> CreateUserAndResetCodeAsync(
        RateLimitingWebApplicationFactory factory)
    {
        const string email = "reset-me@example.com";
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SplitzUser>>();
        var user = new SplitzUser { UserName = email, Email = email };
        Assert.True((await userManager.CreateAsync(user, "Original1234")).Succeeded);
        var code = await userManager.GeneratePasswordResetTokenAsync(user);
        return (email, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code)));
    }

    private static async Task<(bool OriginalPasswordWorks, bool ReplacementPasswordWorks, bool EmailConfirmed)>
        GetPasswordStateAsync(RateLimitingWebApplicationFactory factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SplitzUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return (
            await userManager.CheckPasswordAsync(user, "Original1234"),
            await userManager.CheckPasswordAsync(user, "Replacement1234"),
            user.EmailConfirmed);
    }

    private static async Task AssertRateLimitedAsync(
        HttpResponseMessage response,
        string resetCode,
        string newPassword)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter?.Delta);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("rate_limit_exceeded", body, StringComparison.Ordinal);
        Assert.DoesNotContain(resetCode, body, StringComparison.Ordinal);
        Assert.DoesNotContain(newPassword, body, StringComparison.Ordinal);
    }
}