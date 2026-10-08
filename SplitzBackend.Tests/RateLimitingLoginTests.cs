using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitingLoginTests
{
    [Fact]
    public async Task LoginRejectsEquivalentNormalizedEmailAndKeepsOtherEmailsIsolated()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var client = factory.CreateClient();
        var firstResponse = await PostLoginAsync(client, "  person@example.com  ");

        Assert.Equal(HttpStatusCode.Unauthorized, firstResponse.StatusCode);

        var limitedRequest = CreateLoginRequest("PERSON@EXAMPLE.COM");
        limitedRequest.Headers.Add("Origin", "http://localhost:5173");
        var limitedResponse = await client.SendAsync(limitedRequest);

        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.NotNull(limitedResponse.Headers.RetryAfter?.Delta);
        Assert.Contains(
            "Retry-After",
            limitedResponse.Headers.GetValues("Access-Control-Expose-Headers"));

        using var problem = JsonDocument.Parse(await limitedResponse.Content.ReadAsStringAsync());
        Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("rate_limit_exceeded", problem.RootElement.GetProperty("code").GetString());

        var otherEmailResponse = await PostLoginAsync(client, "other@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, otherEmailResponse.StatusCode);
    }

    [Fact]
    public async Task TrustedForwardedAddressesUseSeparateIpPartitions()
    {
        using var factory = CreateIpPartitionFactory("10.0.0.1");
        using var client = factory.CreateClient();

        var firstClientResponse = await PostLoginAsync(
            client,
            "person@example.com",
            remoteIp: "10.0.0.1",
            forwardedFor: "198.51.100.10");
        var secondClientResponse = await PostLoginAsync(
            client,
            "person@example.com",
            remoteIp: "10.0.0.1",
            forwardedFor: "198.51.100.11");
        var repeatedFirstClientResponse = await PostLoginAsync(
            client,
            "person@example.com",
            remoteIp: "10.0.0.1",
            forwardedFor: "198.51.100.10");

        Assert.Equal(HttpStatusCode.Unauthorized, firstClientResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondClientResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, repeatedFirstClientResponse.StatusCode);
    }

    [Fact]
    public async Task TrustedForwardedNetworkUsesSeparateIpPartitions()
    {
        using var factory = new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Login:Ip:PermitLimit"] = "1",
            ["RateLimiting:Login:Account:PermitLimit"] = "100",
            ["RateLimiting:TrustedNetworks:0"] = "10.0.0.0/24"
        });
        using var client = factory.CreateClient();

        var firstClientResponse = await PostLoginAsync(
            client,
            "person@example.com",
            remoteIp: "10.0.0.1",
            forwardedFor: "198.51.100.10");
        var secondClientResponse = await PostLoginAsync(
            client,
            "person@example.com",
            remoteIp: "10.0.0.1",
            forwardedFor: "198.51.100.11");
        var repeatedFirstClientResponse = await PostLoginAsync(
            client,
            "person@example.com",
            remoteIp: "10.0.0.1",
            forwardedFor: "198.51.100.10");

        Assert.Equal(HttpStatusCode.Unauthorized, firstClientResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondClientResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, repeatedFirstClientResponse.StatusCode);
    }

    [Fact]
    public async Task UntrustedForwardedAddressesCannotChangeTheIpPartition()
    {
        using var factory = CreateIpPartitionFactory("10.0.0.1");
        using var client = factory.CreateClient();

        var firstResponse = await PostLoginAsync(
            client,
            "person@example.com",
            remoteIp: "203.0.113.20",
            forwardedFor: "198.51.100.10");
        var secondResponse = await PostLoginAsync(
            client,
            "person@example.com",
            remoteIp: "203.0.113.20",
            forwardedFor: "198.51.100.11");

        Assert.Equal(HttpStatusCode.Unauthorized, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
    }

    [Fact]
    public async Task ForwardedAddressesAreIgnoredWhenNoProxyTrustIsConfigured()
    {
        using var factory = new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Login:Ip:PermitLimit"] = "1",
            ["RateLimiting:Login:Account:PermitLimit"] = "100"
        });
        using var client = factory.CreateClient();

        var firstResponse = await PostLoginAsync(
            client,
            "person@example.com",
            remoteIp: "203.0.113.20",
            forwardedFor: "198.51.100.10");
        var secondResponse = await PostLoginAsync(
            client,
            "person@example.com",
            remoteIp: "203.0.113.20",
            forwardedFor: "198.51.100.11");

        Assert.Equal(HttpStatusCode.Unauthorized, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
    }

    private static RateLimitingWebApplicationFactory CreateIpPartitionFactory(string trustedProxy)
    {
        return new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Login:Ip:PermitLimit"] = "1",
            ["RateLimiting:Login:Account:PermitLimit"] = "100",
            ["RateLimiting:TrustedProxies:0"] = trustedProxy
        });
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string? remoteIp = null,
        string? forwardedFor = null)
    {
        var request = CreateLoginRequest(email);
        if (remoteIp is not null)
            request.Headers.Add("X-Test-Remote-IP", remoteIp);
        if (forwardedFor is not null)
            request.Headers.Add("X-Forwarded-For", forwardedFor);

        return await client.SendAsync(request);
    }

    private static HttpRequestMessage CreateLoginRequest(string email)
    {
        return new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = JsonContent.Create(new
            {
                email,
                password = "WrongPassword123!"
            })
        };
    }
}