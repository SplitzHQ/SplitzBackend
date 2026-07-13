using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SplitzBackend.Models;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitingAvatarUploadTests
{
    [Fact]
    public async Task SixthActiveAvatarUploadIsRejectedImmediatelyWithoutBlockingNonUploadRequests()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var users = new List<TestUser>();
        for (var userNumber = 1; userNumber <= 6; userNumber++)
            users.Add(await CreateUserAndLoginAsync(factory, $"concurrent-{userNumber}@example.com"));

        var storage = factory.Services.GetRequiredService<BlockingImageStorageService>();
        var logs = factory.Logs;
        logs.Reset();
        storage.StartBlocking();
        using var client = factory.CreateClient();

        var activeUploads = Enumerable.Range(0, 5)
            .Select(index => PostAvatarAsync(client, users[index].Token, index))
            .ToArray();
        await storage.WaitForUploadsAsync(5, TimeSpan.FromSeconds(5));

        var stopwatch = Stopwatch.StartNew();
        var sixthUpload = PostAvatarAsync(client, users[5].Token, 6);

        try
        {
            var completed = await Task.WhenAny(sixthUpload, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(sixthUpload, completed);
            var limitedResponse = await sixthUpload;
            stopwatch.Stop();

            Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(5), limitedResponse.Headers.RetryAfter?.Delta);
            await AssertRateLimitProblemAsync(limitedResponse);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
            Assert.Equal(5, storage.UploadCount);
            AssertUploadRejectionLog(logs, "global", users[5]);

            var anonymousResponse = await PostAvatarAsync(client, token: null, requestNumber: 7);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
            Assert.Equal(5, storage.UploadCount);

            using var profileRequest = CreateAuthenticatedRequest(HttpMethod.Patch, "/account", users[5].Token);
            profileRequest.Content = JsonContent.Create(new { userName = "Still Responsive" });
            var profileResponse = await client.SendAsync(profileRequest);
            Assert.Equal(HttpStatusCode.NoContent, profileResponse.StatusCode);
        }
        finally
        {
            storage.Release();
            await Task.WhenAll(activeUploads);
        }

        Assert.All(activeUploads, task => Assert.Equal(HttpStatusCode.OK, task.Result.StatusCode));
    }

    [Fact]
    public async Task AvatarHourlyQuotaRejectsThe201stRequestAndIsIsolatedByUser()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        const string firstEmail = "hourly-first@example.com";
        var firstUser = await CreateUserAndLoginAsync(factory, firstEmail);
        var secondUser = await CreateUserAndLoginAsync(factory, "hourly-second@example.com");
        var storage = factory.Services.GetRequiredService<BlockingImageStorageService>();
        var logs = factory.Logs;
        logs.Reset();
        using var client = factory.CreateClient();

        for (var requestNumber = 1; requestNumber <= 200; requestNumber++)
        {
            var response = await PostAvatarAsync(client, firstUser.Token, requestNumber);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var limitedResponse = await PostAvatarAsync(client, firstUser.Token, 201);
        var otherUserResponse = await PostAvatarAsync(client, secondUser.Token, 1);

        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.NotNull(limitedResponse.Headers.RetryAfter?.Delta);
        await AssertRateLimitProblemAsync(limitedResponse);
        Assert.Equal(HttpStatusCode.OK, otherUserResponse.StatusCode);
        Assert.Equal(201, storage.UploadCount);
        AssertUploadRejectionLog(logs, "user", firstUser);
    }

    [Fact]
    public async Task UnauthenticatedAvatarRequestPreservesAuthorizationAndDoesNotConsumeUploadCapacity()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var storage = factory.Services.GetRequiredService<BlockingImageStorageService>();
        using var client = factory.CreateClient();

        var response = await PostAvatarAsync(client, token: null, requestNumber: 1);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, storage.UploadCount);
    }

    private static async Task<TestUser> CreateUserAndLoginAsync(
        RateLimitingWebApplicationFactory factory,
        string email)
    {
        string userId;
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
            userId = user.Id;
        }

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/account/login", new
        {
            email,
            password = "Password1234"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new TestUser(
            body.RootElement.GetProperty("accessToken").GetString()!,
            email,
            userId);
    }

    private static async Task<HttpResponseMessage> PostAvatarAsync(
        HttpClient client,
        string? token,
        int requestNumber)
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/account/avatar", token);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3, (byte)(requestNumber % byte.MaxValue)]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", $"avatar-{requestNumber}.png");
        request.Content = content;
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string path,
        string? token)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static void AssertUploadRejectionLog(
        CapturingRateLimitLogger logs,
        string partitionType,
        TestUser user)
    {
        var log = Assert.Single(logs.Messages);
        Assert.Contains("Rate limit rejected POST", log, StringComparison.Ordinal);
        Assert.Contains("account/avatar", log, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"partition type {partitionType}", log, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Email, log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.Token, log, StringComparison.Ordinal);
        Assert.DoesNotContain(user.UserId, log, StringComparison.Ordinal);
    }

    private static async Task AssertRateLimitProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Too Many Requests", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "Too many requests were received. Please try again later.",
            body.RootElement.GetProperty("detail").GetString());
        Assert.Equal("rate_limit_exceeded", body.RootElement.GetProperty("code").GetString());
    }

    private sealed record TestUser(string Token, string Email, string UserId);
}