using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SplitzBackend.Models;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitingEmailDeliveryTests
{
    [Fact]
    public async Task EmailDeliveryAccountQuotaIsSharedAcrossAllThreeRoutes()
    {
        using var factory = CreateEmailDeliveryFactory(ipPermitLimit: 100, accountPermitLimit: 2);
        using var client = factory.CreateClient();

        var recoveryResponse = await PostEmailAsync(client, "/account/recovery/request", "  shared@example.com  ");
        var forgotResponse = await PostEmailAsync(client, "/account/forgotPassword", "SHARED@EXAMPLE.COM");
        var resendResponse = await PostEmailAsync(client, "/account/resendConfirmationEmail", "shared@example.com");

        Assert.Equal(HttpStatusCode.OK, recoveryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);
        await AssertRateLimitedAsync(resendResponse);
    }

    [Fact]
    public async Task EmailDeliveryIpQuotaIsSharedAcrossRoutesAndIsolatedByAddress()
    {
        using var factory = CreateEmailDeliveryFactory(ipPermitLimit: 2, accountPermitLimit: 100);
        using var client = factory.CreateClient();

        var recoveryResponse = await PostEmailAsync(
            client,
            "/account/recovery/request",
            "first@example.com",
            remoteIp: "203.0.113.20");
        var forgotResponse = await PostEmailAsync(
            client,
            "/account/forgotPassword",
            "second@example.com",
            remoteIp: "203.0.113.20");
        var limitedResponse = await PostEmailAsync(
            client,
            "/account/resendConfirmationEmail",
            "third@example.com",
            remoteIp: "203.0.113.20");
        var otherIpResponse = await PostEmailAsync(
            client,
            "/account/resendConfirmationEmail",
            "fourth@example.com",
            remoteIp: "203.0.113.21");

        Assert.Equal(HttpStatusCode.OK, recoveryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);
        await AssertRateLimitedAsync(limitedResponse);
        Assert.Equal(HttpStatusCode.OK, otherIpResponse.StatusCode);
    }

    [Fact]
    public async Task RejectedEmailDeliveryRequestDoesNotInvokeTheSender()
    {
        using var factory = CreateEmailDeliveryFactory(ipPermitLimit: 100, accountPermitLimit: 1);
        await CreateConfirmedUserAsync(factory, "known@example.com");
        var sender = factory.Services.GetRequiredService<RecordingIdentityEmailSender>();
        sender.Reset();
        using var client = factory.CreateClient();

        var recoveryResponse = await PostEmailAsync(client, "/account/recovery/request", "known@example.com");
        var limitedResponse = await PostEmailAsync(client, "/account/forgotPassword", "KNOWN@EXAMPLE.COM");

        Assert.Equal(HttpStatusCode.OK, recoveryResponse.StatusCode);
        Assert.Equal(1, sender.DeliveryCount);
        await AssertRateLimitedAsync(limitedResponse);
        Assert.Equal(1, sender.DeliveryCount);
    }

    [Fact]
    public async Task IpRejectedEmailDeliveryRequestDoesNotInvokeTheSender()
    {
        using var factory = CreateEmailDeliveryFactory(ipPermitLimit: 1, accountPermitLimit: 100);
        await CreateConfirmedUserAsync(factory, "first-known@example.com");
        await CreateConfirmedUserAsync(factory, "second-known@example.com");
        var sender = factory.Services.GetRequiredService<RecordingIdentityEmailSender>();
        sender.Reset();
        using var client = factory.CreateClient();

        var firstResponse = await PostEmailAsync(
            client,
            "/account/recovery/request",
            "first-known@example.com",
            remoteIp: "203.0.113.20");
        var limitedResponse = await PostEmailAsync(
            client,
            "/account/forgotPassword",
            "second-known@example.com",
            remoteIp: "203.0.113.20");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(1, sender.DeliveryCount);
        await AssertRateLimitedAsync(limitedResponse);
        Assert.Equal(1, sender.DeliveryCount);
    }

    [Fact]
    public async Task KnownAndUnknownEmailsHaveEquivalentRateLimitResponses()
    {
        using var factory = CreateEmailDeliveryFactory(ipPermitLimit: 100, accountPermitLimit: 1);
        await CreateConfirmedUserAsync(factory, "known@example.com");
        using var client = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.OK,
            (await PostEmailAsync(client, "/account/recovery/request", "known@example.com")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await PostEmailAsync(client, "/account/recovery/request", "unknown@example.com")).StatusCode);

        var known = await MeasureAsync(() =>
            PostEmailAsync(client, "/account/forgotPassword", "KNOWN@EXAMPLE.COM"));
        var unknown = await MeasureAsync(() =>
            PostEmailAsync(client, "/account/resendConfirmationEmail", "UNKNOWN@EXAMPLE.COM"));

        Assert.Equal(HttpStatusCode.TooManyRequests, known.Response.StatusCode);
        Assert.Equal(known.Response.StatusCode, unknown.Response.StatusCode);
        Assert.Equal(
            await known.Response.Content.ReadAsStringAsync(),
            await unknown.Response.Content.ReadAsStringAsync());
        Assert.Equal(
            known.Response.Content.Headers.ContentType?.ToString(),
            unknown.Response.Content.Headers.ContentType?.ToString());
        Assert.Equal(known.Response.Headers.RetryAfter?.Delta, unknown.Response.Headers.RetryAfter?.Delta);
        Assert.True((known.Elapsed - unknown.Elapsed).Duration() < TimeSpan.FromSeconds(2));
    }

    private static RateLimitingWebApplicationFactory CreateEmailDeliveryFactory(
        int ipPermitLimit,
        int accountPermitLimit)
    {
        return new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:EmailDelivery:Ip:PermitLimit"] = ipPermitLimit.ToString(),
            ["RateLimiting:EmailDelivery:Account:PermitLimit"] = accountPermitLimit.ToString()
        });
    }

    private static async Task<HttpResponseMessage> PostEmailAsync(
        HttpClient client,
        string path,
        string email,
        string? remoteIp = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { email })
        };
        if (remoteIp is not null)
            request.Headers.Add("X-Test-Remote-IP", remoteIp);

        return await client.SendAsync(request);
    }

    private static async Task CreateConfirmedUserAsync(
        RateLimitingWebApplicationFactory factory,
        string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SplitzUser>>();
        var user = new SplitzUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        Assert.True((await userManager.CreateAsync(user, "Password1234")).Succeeded);
    }

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter?.Delta);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("rate_limit_exceeded", body, StringComparison.Ordinal);
        Assert.DoesNotContain("email", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(HttpResponseMessage Response, TimeSpan Elapsed)> MeasureAsync(
        Func<Task<HttpResponseMessage>> request)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await request();
        stopwatch.Stop();
        return (response, stopwatch.Elapsed);
    }
}