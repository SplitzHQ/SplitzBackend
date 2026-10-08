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

public class RateLimitingUploadPoolTests
{
    [Fact]
    public async Task AllUploadRoutesShareOneProcessWideConcurrencyPool()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var owner = await CreateUserAndLoginAsync(factory, "pool-owner@example.com");
        var secondUser = await CreateUserAndLoginAsync(factory, "pool-second@example.com");
        var thirdUser = await CreateUserAndLoginAsync(factory, "pool-third@example.com");
        var resources = await CreateOwnedResourcesAsync(factory, owner.UserId);
        var storage = factory.Services.GetRequiredService<BlockingImageStorageService>();
        storage.StartBlocking();
        using var client = factory.CreateClient();

        var activeUploads = new[]
        {
            PostFileAsync(client, owner.Token, "/account/avatar", 1),
            PostFileAsync(client, owner.Token, $"/group/{resources.GroupId}/avatar", 2),
            PostFileAsync(client, owner.Token, $"/transaction/{resources.TransactionId}/receipt", 3),
            PostFileAsync(client, owner.Token, $"/transactiondraft/{resources.DraftId}/receipt", 4),
            PostFileAsync(client, secondUser.Token, "/account/avatar", 5)
        };
        var startedUploads = activeUploads.ToList();

        try
        {
            await storage.WaitForUploadsAsync(5, TimeSpan.FromSeconds(5));

            var stopwatch = Stopwatch.StartNew();
            var sixthUpload = PostFileAsync(client, thirdUser.Token, "/account/avatar", 6);
            startedUploads.Add(sixthUpload);
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
            await Task.WhenAll(startedUploads);
        }

        Assert.All(activeUploads, task => Assert.Equal(HttpStatusCode.OK, task.Result.StatusCode));
    }

    [Fact]
    public async Task AllUploadRoutesShareOneHourlyPoolPerAuthenticatedUser()
    {
        using var factory = new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Login:Account:PermitLimit"] = "10",
            ["RateLimiting:Upload:HourlyPerUser:PermitLimit"] = "4"
        });
        var owner = await CreateUserAndLoginAsync(factory, "quota-owner@example.com");
        var ownerSecondToken = await LoginAsync(factory, owner.Email);
        Assert.NotEqual(owner.Token, ownerSecondToken);
        var otherUser = await CreateUserAndLoginAsync(factory, "quota-other@example.com");
        var resources = await CreateOwnedResourcesAsync(factory, owner.UserId);
        using var client = factory.CreateClient();

        var responses = new[]
        {
            await PostFileAsync(client, owner.Token, "/account/avatar", 1),
            await PostFileAsync(client, ownerSecondToken, $"/group/{resources.GroupId}/avatar", 2),
            await PostFileAsync(client, owner.Token, $"/transaction/{resources.TransactionId}/receipt", 3),
            await PostFileAsync(client, ownerSecondToken, $"/transactiondraft/{resources.DraftId}/receipt", 4)
        };
        var limitedResponse = await PostFileAsync(client, ownerSecondToken, "/account/avatar", 5);
        var otherUserResponse = await PostFileAsync(client, otherUser.Token, "/account/avatar", 1);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.NotNull(limitedResponse.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.OK, otherUserResponse.StatusCode);
    }

    [Fact]
    public async Task OwnershipFailuresArePreservedBeforeImageProcessing()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var owner = await CreateUserAndLoginAsync(factory, "ownership-owner@example.com");
        var outsider = await CreateUserAndLoginAsync(factory, "ownership-outsider@example.com");
        var resources = await CreateOwnedResourcesAsync(factory, owner.UserId);
        var storage = factory.Services.GetRequiredService<BlockingImageStorageService>();
        using var client = factory.CreateClient();

        var groupResponse = await PostFileAsync(
            client,
            outsider.Token,
            $"/group/{resources.GroupId}/avatar",
            1);
        var transactionResponse = await PostFileAsync(
            client,
            outsider.Token,
            $"/transaction/{resources.TransactionId}/receipt",
            2);
        var draftResponse = await PostFileAsync(
            client,
            outsider.Token,
            $"/transactiondraft/{resources.DraftId}/receipt",
            3);

        Assert.Equal(HttpStatusCode.Unauthorized, groupResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, transactionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, draftResponse.StatusCode);
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

        return new TestUser(await LoginAsync(factory, email), userId, email);
    }

    private static async Task<string> LoginAsync(
        RateLimitingWebApplicationFactory factory,
        string email)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/account/login", new
        {
            email,
            password = "Password1234"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static async Task<OwnedResources> CreateOwnedResourcesAsync(
        RateLimitingWebApplicationFactory factory,
        string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
        var user = await context.Users.SingleAsync(candidate => candidate.Id == userId);
        var group = new Group
        {
            GroupId = Guid.NewGuid(),
            Name = "Upload pool group",
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
            Name = "Upload pool transaction",
            Icon = "default",
            CreateTime = DateTime.UtcNow,
            TransactionTime = DateTime.UtcNow,
            Amount = 10m,
            Currency = "USD",
            Tags = []
        };
        var draft = new TransactionDraft
        {
            TransactionDraftId = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            Group = group,
            GroupId = group.GroupId,
            Name = "Upload pool draft",
            Icon = "default",
            Amount = 10m,
            Currency = "USD",
            Tags = []
        };

        context.AddRange(transaction, draft);
        await context.SaveChangesAsync();
        return new OwnedResources(group.GroupId, transaction.TransactionId, draft.TransactionDraftId);
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
        content.Add(file, "file", $"upload-{requestNumber}.png");
        request.Content = content;
        return await client.SendAsync(request);
    }

    private sealed record OwnedResources(Guid GroupId, Guid TransactionId, Guid DraftId);

    private sealed record TestUser(string Token, string UserId, string Email);
}