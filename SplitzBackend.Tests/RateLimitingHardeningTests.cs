using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SplitzBackend.Models;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitingHardeningTests
{
    [Fact]
    public async Task DisabledRateLimitingBypassesAnonymousAndUploadPolicies()
    {
        using var factory = new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "false",
            ["RateLimiting:Login:Ip:PermitLimit"] = "1",
            ["RateLimiting:Login:Account:PermitLimit"] = "1",
            ["RateLimiting:Upload:GlobalConcurrencyPermitLimit"] = "1",
            ["RateLimiting:Upload:HourlyPerUser:PermitLimit"] = "1"
        });
        using var client = factory.CreateClient();

        for (var requestNumber = 0; requestNumber < 3; requestNumber++)
        {
            var response = await PostLoginAsync(client, "disabled@example.com");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var token = await CreateUserAndLoginAsync(factory, "disabled-upload@example.com");
        var storage = factory.Services.GetRequiredService<BlockingImageStorageService>();
        storage.StartBlocking();
        var concurrentUploads = new[]
        {
            PostAvatarAsync(client, token, 1),
            PostAvatarAsync(client, token, 2)
        };

        try
        {
            await storage.WaitForUploadsAsync(2, TimeSpan.FromSeconds(5));
        }
        finally
        {
            storage.Release();
            await Task.WhenAll(concurrentUploads);
        }

        var thirdUpload = await PostAvatarAsync(client, token, 3);

        Assert.All(concurrentUploads, upload => Assert.Equal(HttpStatusCode.OK, upload.Result.StatusCode));
        Assert.Equal(HttpStatusCode.OK, thirdUpload.StatusCode);
        Assert.Equal(3, storage.UploadCount);
        Assert.Empty(factory.Logs.Messages);
    }

    [Fact]
    public async Task FreshHostStartsWithEmptyLimiterState()
    {
        var configuration = new Dictionary<string, string?>
        {
            ["RateLimiting:Login:Ip:PermitLimit"] = "100",
            ["RateLimiting:Login:Account:PermitLimit"] = "1"
        };

        using (var firstFactory = new RateLimitingWebApplicationFactory(configuration))
        using (var firstClient = firstFactory.CreateClient())
        {
            var firstResponse = await PostLoginAsync(firstClient, "restart@example.com");
            var limitedResponse = await PostLoginAsync(firstClient, "RESTART@EXAMPLE.COM");

            Assert.Equal(HttpStatusCode.Unauthorized, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        }

        using var secondFactory = new RateLimitingWebApplicationFactory(configuration);
        using var secondClient = secondFactory.CreateClient();

        var responseAfterRestart = await PostLoginAsync(secondClient, "restart@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, responseAfterRestart.StatusCode);
    }

    [Fact]
    public async Task AnonymousAccountRejectionLogOmitsSubmittedAndConnectionValues()
    {
        using var factory = new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Login:Ip:PermitLimit"] = "100",
            ["RateLimiting:Login:Account:PermitLimit"] = "1"
        });
        using var client = factory.CreateClient();
        const string email = "secret-account@example.com";
        const string password = "SecretPassword1234!";
        const string remoteIp = "203.0.113.20";

        await PostLoginAsync(client, email, password, remoteIp);
        factory.Logs.Reset();
        var limitedResponse = await PostLoginAsync(client, email, password, remoteIp);

        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        AssertAnonymousRejectionLog(factory.Logs, "account", email, password, remoteIp);
    }

    [Fact]
    public async Task AnonymousIpRejectionLogOmitsSubmittedAndConnectionValues()
    {
        using var factory = new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Login:Ip:PermitLimit"] = "1",
            ["RateLimiting:Login:Account:PermitLimit"] = "100"
        });
        using var client = factory.CreateClient();
        const string password = "SecretPassword1234!";
        const string remoteIp = "203.0.113.21";

        await PostLoginAsync(client, "first-secret@example.com", password, remoteIp);
        factory.Logs.Reset();
        var limitedResponse = await PostLoginAsync(
            client,
            "second-secret@example.com",
            password,
            remoteIp);

        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        AssertAnonymousRejectionLog(
            factory.Logs,
            "ip",
            "second-secret@example.com",
            password,
            remoteIp);
    }

    private static async Task<string> CreateUserAndLoginAsync(
        RateLimitingWebApplicationFactory factory,
        string email)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SplitzUser>>();
            var user = new SplitzUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };
            Assert.True((await userManager.CreateAsync(user, "Password1234")).Succeeded);
        }

        using var client = factory.CreateClient();
        var response = await PostLoginAsync(client, email, "Password1234");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string password = "WrongPassword123!",
        string? remoteIp = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        if (remoteIp is not null)
            request.Headers.Add("X-Test-Remote-IP", remoteIp);

        return client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAvatarAsync(
        HttpClient client,
        string token,
        int requestNumber)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/avatar");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3, (byte)requestNumber]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", $"hardening-{requestNumber}.png");
        request.Content = content;
        return await client.SendAsync(request);
    }

    private static void AssertAnonymousRejectionLog(
        CapturingRateLimitLogger logs,
        string partitionType,
        string email,
        string password,
        string remoteIp)
    {
        var log = Assert.Single(logs.Messages);
        Assert.Contains("Rate limit rejected POST", log, StringComparison.Ordinal);
        Assert.Contains("account/login", log, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("for login", log, StringComparison.Ordinal);
        Assert.Contains($"partition type {partitionType}", log, StringComparison.Ordinal);
        Assert.DoesNotContain(email, log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, log, StringComparison.Ordinal);
        Assert.DoesNotContain(remoteIp, log, StringComparison.Ordinal);
    }
}