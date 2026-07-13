using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SplitzBackend.Models;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitingTransactionReceiptTests
{
    [Fact]
    public async Task AvatarAndReceiptShareTheProcessWideConcurrencyPool()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var user = await CreateUserAndLoginAsync(factory, "receipt-concurrency@example.com");
        var transactionId = await CreateOwnedTransactionAsync(factory, user.UserId);
        var storage = factory.Services.GetRequiredService<BlockingImageStorageService>();
        storage.StartBlocking();
        using var client = factory.CreateClient();

        var activeUploads = Enumerable.Range(0, 4)
            .Select(index => PostAvatarAsync(client, user.Token, index))
            .Append(PostReceiptAsync(client, user.Token, transactionId, 5))
            .ToArray();
        await storage.WaitForUploadsAsync(5, TimeSpan.FromSeconds(5));

        var stopwatch = Stopwatch.StartNew();
        var sixthUpload = PostAvatarAsync(client, user.Token, 6);

        try
        {
            var completed = await Task.WhenAny(sixthUpload, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(sixthUpload, completed);
            var limitedResponse = await sixthUpload;
            stopwatch.Stop();

            Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(5), limitedResponse.Headers.RetryAfter?.Delta);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
            Assert.Equal(5, storage.UploadCount);
        }
        finally
        {
            storage.Release();
            await Task.WhenAll(activeUploads);
        }

        Assert.All(activeUploads, task => Assert.Equal(HttpStatusCode.OK, task.Result.StatusCode));
    }

    [Fact]
    public async Task AvatarAndReceiptShareTheAuthenticatedUsersHourlyQuota()
    {
        using var factory = new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Upload:HourlyPerUser:PermitLimit"] = "2"
        });
        var user = await CreateUserAndLoginAsync(factory, "receipt-hourly@example.com");
        var transactionId = await CreateOwnedTransactionAsync(factory, user.UserId);
        using var client = factory.CreateClient();

        var avatarResponse = await PostAvatarAsync(client, user.Token, 1);
        var receiptResponse = await PostReceiptAsync(client, user.Token, transactionId, 2);
        var limitedResponse = await PostReceiptAsync(client, user.Token, transactionId, 3);

        Assert.Equal(HttpStatusCode.OK, avatarResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, receiptResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.NotNull(limitedResponse.Headers.RetryAfter?.Delta);
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
        return new TestUser(body.RootElement.GetProperty("accessToken").GetString()!, userId);
    }

    private static async Task<Guid> CreateOwnedTransactionAsync(
        RateLimitingWebApplicationFactory factory,
        string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
        var user = await context.Users.SingleAsync(candidate => candidate.Id == userId);
        var group = new Group
        {
            GroupId = Guid.NewGuid(),
            Name = "Receipt test group",
            Members = [user],
            MembersIdHash = string.Empty,
            TransactionCount = 1,
            LastActivityTime = DateTime.UtcNow
        };
        group.UpdateMembersIdHash();

        var transaction = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            GroupId = group.GroupId,
            Group = group,
            Name = "Receipt test transaction",
            Icon = "default",
            CreateTime = DateTime.UtcNow,
            TransactionTime = DateTime.UtcNow,
            Amount = 10m,
            Currency = "USD",
            Tags = []
        };
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();
        return transaction.TransactionId;
    }

    private static Task<HttpResponseMessage> PostAvatarAsync(
        HttpClient client,
        string token,
        int requestNumber)
    {
        return PostFileAsync(client, token, "/account/avatar", requestNumber);
    }

    private static Task<HttpResponseMessage> PostReceiptAsync(
        HttpClient client,
        string token,
        Guid transactionId,
        int requestNumber)
    {
        return PostFileAsync(client, token, $"/transaction/{transactionId}/receipt", requestNumber);
    }

    private static async Task<HttpResponseMessage> PostFileAsync(
        HttpClient client,
        string token,
        string path,
        int requestNumber)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3, (byte)(requestNumber % byte.MaxValue)]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", $"receipt-{requestNumber}.png");
        request.Content = content;
        return await client.SendAsync(request);
    }

    private sealed record TestUser(string Token, string UserId);
}