using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SplitzBackend.Models;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitingRegistrationTests
{
    [Fact]
    public async Task RegistrationBelowThresholdPreservesSuccessfulIdentityBehavior()
    {
        using var factory = CreateRegistrationFactory(ipPermitLimit: 10, accountPermitLimit: 6);
        using var client = factory.CreateClient();

        var response = await PostRegistrationAsync(client, "person@example.com", "Password1234");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RegistrationRejectsEquivalentNormalizedUnknownEmailBeforeIdentityExecutes()
    {
        using var factory = CreateRegistrationFactory(ipPermitLimit: 100, accountPermitLimit: 1);
        using var client = factory.CreateClient();

        var firstResponse = await PostRegistrationAsync(client, "person@example.com", "weak");
        var limitedResponse = await PostRegistrationAsync(client, "PERSON@EXAMPLE.COM", "Password1234");
        var otherEmailResponse = await PostRegistrationAsync(client, "other@example.com", "Password1234");

        Assert.Equal(HttpStatusCode.BadRequest, firstResponse.StatusCode);
        await AssertRateLimitedAsync(limitedResponse);
        Assert.False(await UserExistsAsync(factory, "person@example.com"));
        Assert.Equal(HttpStatusCode.OK, otherEmailResponse.StatusCode);
    }

    [Fact]
    public async Task RegistrationRejectsSecondIpRequestBeforeCreatingAnotherAccount()
    {
        using var factory = CreateRegistrationFactory(ipPermitLimit: 1, accountPermitLimit: 100);
        using var client = factory.CreateClient();

        var firstResponse = await PostRegistrationAsync(
            client,
            "first@example.com",
            "Password1234",
            remoteIp: "203.0.113.20");
        var limitedResponse = await PostRegistrationAsync(
            client,
            "second@example.com",
            "Password1234",
            remoteIp: "203.0.113.20");
        var otherIpResponse = await PostRegistrationAsync(
            client,
            "third@example.com",
            "Password1234",
            remoteIp: "203.0.113.21");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        await AssertRateLimitedAsync(limitedResponse);
        Assert.False(await UserExistsAsync(factory, "second@example.com"));
        Assert.Equal(HttpStatusCode.OK, otherIpResponse.StatusCode);
    }

    [Fact]
    public async Task LoginAttemptsDoNotConsumeRegistrationAccountQuota()
    {
        using var factory = new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Login:Ip:PermitLimit"] = "100",
            ["RateLimiting:Login:Account:PermitLimit"] = "1",
            ["RateLimiting:Registration:Ip:PermitLimit"] = "100",
            ["RateLimiting:Registration:Account:PermitLimit"] = "1"
        });
        using var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/account/login", new
        {
            email = "independent@example.com",
            password = "WrongPassword123!"
        });
        var registrationResponse = await PostRegistrationAsync(
            client,
            "independent@example.com",
            "Password1234");

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, registrationResponse.StatusCode);
    }

    private static RateLimitingWebApplicationFactory CreateRegistrationFactory(
        int ipPermitLimit,
        int accountPermitLimit)
    {
        return new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Registration:Ip:PermitLimit"] = ipPermitLimit.ToString(),
            ["RateLimiting:Registration:Account:PermitLimit"] = accountPermitLimit.ToString()
        });
    }

    private static async Task<HttpResponseMessage> PostRegistrationAsync(
        HttpClient client,
        string email,
        string password,
        string? remoteIp = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/account/register")
        {
            Content = JsonContent.Create(new { email, password })
        };
        if (remoteIp is not null)
            request.Headers.Add("X-Test-Remote-IP", remoteIp);

        return await client.SendAsync(request);
    }

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter?.Delta);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("rate_limit_exceeded", problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("registration", problem.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", problem.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> UserExistsAsync(
        RateLimitingWebApplicationFactory factory,
        string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SplitzUser>>();
        return await userManager.FindByEmailAsync(email) is not null;
    }
}