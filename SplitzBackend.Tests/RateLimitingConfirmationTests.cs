using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using SplitzBackend.Models;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitingConfirmationTests
{
    [Fact]
    public async Task ConfirmationAccountQuotaUsesUserIdAndIgnoresConfirmationCode()
    {
        using var factory = CreateConfirmationFactory(ipPermitLimit: 100, accountPermitLimit: 1);
        using var client = factory.CreateClient();

        var firstResponse = await ConfirmAsync(client, "user-1", "first-code");
        var limitedResponse = await ConfirmAsync(client, "user-1", "second-secret-code");
        var otherUserResponse = await ConfirmAsync(client, "user-2", "second-secret-code");

        Assert.NotEqual(HttpStatusCode.TooManyRequests, firstResponse.StatusCode);
        await AssertRateLimitedAsync(limitedResponse, "second-secret-code");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherUserResponse.StatusCode);
    }

    [Fact]
    public async Task ConfirmationIpQuotaIsIsolatedByRemoteAddress()
    {
        using var factory = CreateConfirmationFactory(ipPermitLimit: 1, accountPermitLimit: 100);
        using var client = factory.CreateClient();

        var firstResponse = await ConfirmAsync(client, "user-1", "first-code", "203.0.113.20");
        var limitedResponse = await ConfirmAsync(client, "user-2", "second-code", "203.0.113.20");
        var otherIpResponse = await ConfirmAsync(client, "user-3", "third-code", "203.0.113.21");

        Assert.NotEqual(HttpStatusCode.TooManyRequests, firstResponse.StatusCode);
        await AssertRateLimitedAsync(limitedResponse, "second-code");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherIpResponse.StatusCode);
    }

    [Fact]
    public async Task RejectedConfirmationDoesNotConfirmTheUser()
    {
        using var factory = CreateConfirmationFactory(ipPermitLimit: 100, accountPermitLimit: 1);
        var (userId, validCode) = await CreateUserAndConfirmationCodeAsync(factory, "confirm-me@example.com");
        using var client = factory.CreateClient();

        var invalidResponse = await ConfirmAsync(client, userId, "invalid-code");
        var limitedResponse = await ConfirmAsync(client, userId, validCode);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, invalidResponse.StatusCode);
        await AssertRateLimitedAsync(limitedResponse, validCode);
        Assert.False(await IsEmailConfirmedAsync(factory, userId));
    }

    [Fact]
    public async Task MissingUserIdKeepsIdentityValidationBehaviorOutsideTheAccountPartition()
    {
        using var factory = CreateConfirmationFactory(ipPermitLimit: 100, accountPermitLimit: 1);
        using var client = factory.CreateClient();

        var firstResponse = await client.GetAsync("/account/confirmEmail?code=first-code");
        var secondResponse = await client.GetAsync("/account/confirmEmail?code=second-code");

        Assert.NotEqual(HttpStatusCode.TooManyRequests, firstResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
    }

    private static RateLimitingWebApplicationFactory CreateConfirmationFactory(
        int ipPermitLimit,
        int accountPermitLimit)
    {
        return new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:EmailConfirmation:Ip:PermitLimit"] = ipPermitLimit.ToString(),
            ["RateLimiting:EmailConfirmation:Account:PermitLimit"] = accountPermitLimit.ToString()
        });
    }

    private static async Task<HttpResponseMessage> ConfirmAsync(
        HttpClient client,
        string userId,
        string code,
        string? remoteIp = null)
    {
        var path = QueryHelpers.AddQueryString(
            "/account/confirmEmail",
            new Dictionary<string, string?>
            {
                ["userId"] = userId,
                ["code"] = code
            });
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (remoteIp is not null)
            request.Headers.Add("X-Test-Remote-IP", remoteIp);

        return await client.SendAsync(request);
    }

    private static async Task<(string UserId, string Code)> CreateUserAndConfirmationCodeAsync(
        RateLimitingWebApplicationFactory factory,
        string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SplitzUser>>();
        var user = new SplitzUser { UserName = email, Email = email };
        Assert.True((await userManager.CreateAsync(user, "Password1234")).Succeeded);
        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        return (user.Id, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code)));
    }

    private static async Task<bool> IsEmailConfirmedAsync(
        RateLimitingWebApplicationFactory factory,
        string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SplitzUser>>();
        var user = await userManager.FindByIdAsync(userId);
        Assert.NotNull(user);
        return await userManager.IsEmailConfirmedAsync(user);
    }

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response, string secretCode)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter?.Delta);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("rate_limit_exceeded", body, StringComparison.Ordinal);
        Assert.DoesNotContain(secretCode, body, StringComparison.Ordinal);
    }
}