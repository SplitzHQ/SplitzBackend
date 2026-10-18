using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SplitzBackend.Models;
using Xunit;

namespace SplitzBackend.Tests;

/// <summary>
///     End-to-end coverage of receipt recognition: provider settings, the recognition request with
///     several images, failure recording, linking to a saved expense and the per-user limit.
/// </summary>
public class ReceiptRecognitionFlowTests
{
    private const string ModelReply = """
        {"name": "Blue Bottle", "merchant": "Blue Bottle Coffee", "amount": "12.50", "currency": "usd",
         "transaction_time": "2026-03-14T18:30:00Z", "category": "dining_out", "location": "Mint St", "notes": "2 lattes"}
        """;

    [Fact]
    public async Task RecognitionIsUnavailableUntilAProviderIsConfigured()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var user = await CreateUserAndLoginAsync(factory, "rr-unavailable@example.com");
        using var client = factory.CreateClient();

        var settings = await ReadAsync<JsonElement>(
            await SendAsync(client, user, HttpMethod.Get, "/llmsettings"));
        Assert.False(settings.GetProperty("serverProviderAvailable").GetBoolean());
        Assert.True(settings.GetProperty("customProviderAllowed").GetBoolean());
        Assert.False(settings.GetProperty("customProviderEnabled").GetBoolean());
        Assert.False(settings.GetProperty("hasApiKey").GetBoolean());
        Assert.False(settings.GetProperty("available").GetBoolean());

        var response = await PostRecognitionAsync(client, user, 1);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not configured", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CustomProviderIsValidatedStoredAndTheKeyIsNeverReturned()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var user = await CreateUserAndLoginAsync(factory, "rr-settings@example.com");
        using var client = factory.CreateClient();

        var missingKey = await SendAsync(client, user, HttpMethod.Put, "/llmsettings",
            JsonSerializer.Serialize(new { enabled = true, endpoint = "https://llm.example.com/v1", model = "m" }));
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        Assert.Contains("API key", await missingKey.Content.ReadAsStringAsync());

        var badEndpoint = await SendAsync(client, user, HttpMethod.Put, "/llmsettings",
            JsonSerializer.Serialize(new { enabled = true, endpoint = "ftp://x", model = "m", apiKey = "k" }));
        Assert.Equal(HttpStatusCode.BadRequest, badEndpoint.StatusCode);

        var saved = await ReadAsync<JsonElement>(await SendAsync(client, user, HttpMethod.Put,
            "/llmsettings",
            JsonSerializer.Serialize(new
            {
                enabled = true,
                endpoint = "https://llm.example.com/v1/",
                model = "vision-1",
                apiKey = "sk-secret"
            })));
        Assert.True(saved.GetProperty("customProviderEnabled").GetBoolean());
        Assert.True(saved.GetProperty("hasApiKey").GetBoolean());
        Assert.True(saved.GetProperty("available").GetBoolean());
        Assert.Equal("https://llm.example.com/v1/", saved.GetProperty("endpoint").GetString());
        Assert.Equal("vision-1", saved.GetProperty("model").GetString());
        Assert.DoesNotContain("sk-secret", saved.GetRawText());

        // Saving again without a key keeps the stored one; the database never holds it in clear text.
        var kept = await ReadAsync<JsonElement>(await SendAsync(client, user, HttpMethod.Put,
            "/llmsettings",
            JsonSerializer.Serialize(new { enabled = true, endpoint = "https://llm.example.com/v1", model = "vision-2" })));
        Assert.True(kept.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("vision-2", kept.GetProperty("model").GetString());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
            var row = await db.LLMSettings.SingleAsync(s => s.UserId == user.UserId);
            Assert.NotNull(row.ProtectedApiKey);
            Assert.DoesNotContain("sk-secret", row.ProtectedApiKey);
        }

        var cleared = await SendAsync(client, user, HttpMethod.Delete, "/llmsettings");
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        var after = await ReadAsync<JsonElement>(
            await SendAsync(client, user, HttpMethod.Get, "/llmsettings"));
        Assert.False(after.GetProperty("hasApiKey").GetBoolean());
        Assert.False(after.GetProperty("available").GetBoolean());
    }

    /// <summary>Checks that shared server policy blocks custom providers and keeps receipt limits separate.</summary>
    [Fact]
    public async Task SharedPolicyDisablesCustomProviders()
    {
        using var factory = new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["LLM:AllowCustomProvider"] = "false"
        });
        var user = await CreateUserAndLoginAsync(factory, "llm-policy@example.com");
        using var client = factory.CreateClient();
        var settings = await ReadAsync<JsonElement>(
            await SendAsync(client, user, HttpMethod.Get, "/llmsettings"));
        Assert.False(settings.GetProperty("customProviderAllowed").GetBoolean());
        var update = await SendAsync(client, user, HttpMethod.Put, "/llmsettings",
            JsonSerializer.Serialize(new { enabled = false }));
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        var capabilities = await ReadAsync<JsonElement>(
            await SendAsync(client, user, HttpMethod.Get, "/receiptrecognition/capabilities"));
        Assert.False(capabilities.GetProperty("available").GetBoolean());
        Assert.False(capabilities.GetProperty("customProviderAllowed").GetBoolean());
        Assert.Equal(6, capabilities.GetProperty("maxImages").GetInt32());
    }

    [Fact]
    public async Task RecognizesAllImagesStoresThemAndLinksTheSavedExpense()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var user = await CreateUserAndLoginAsync(factory, "rr-flow@example.com");
        var stranger = await CreateUserAndLoginAsync(factory, "rr-stranger@example.com");
        var groupId = await CreateGroupAsync(factory, user.UserId);
        var fakeModel = factory.Services.GetRequiredService<FakeReceiptRecognitionClient>();
        using var client = factory.CreateClient();
        await ConfigureCustomProviderAsync(client, user);

        fakeModel.EnqueueReply(ModelReply);
        var response = await PostRecognitionAsync(client, user, 2, ["dining_out", "groceries"]);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recognition = await ReadAsync<JsonElement>(response);

        Assert.Equal("Succeeded", recognition.GetProperty("status").GetString());
        Assert.Equal("Blue Bottle", recognition.GetProperty("name").GetString());
        Assert.Equal("Blue Bottle Coffee", recognition.GetProperty("merchant").GetString());
        Assert.Equal(12.50m, recognition.GetProperty("amount").GetDecimal());
        Assert.Equal("USD", recognition.GetProperty("currency").GetString());
        Assert.Equal("dining_out", recognition.GetProperty("category").GetString());
        Assert.Equal("Mint St", recognition.GetProperty("location").GetString());
        Assert.Equal("2 lattes", recognition.GetProperty("notes").GetString());
        Assert.Equal("vision-1", recognition.GetProperty("model").GetString());
        Assert.True(recognition.GetProperty("usedCustomProvider").GetBoolean());
        Assert.Contains("Blue Bottle", recognition.GetProperty("rawResponse").GetString());
        Assert.Equal(JsonValueKind.Null, recognition.GetProperty("transactionId").ValueKind);

        var recognitionId = recognition.GetProperty("receiptRecognitionId").GetGuid();
        var images = recognition.GetProperty("images").EnumerateArray().Select(i => i.GetString()!).ToList();
        Assert.Equal(2, images.Count);
        Assert.All(images, image => Assert.StartsWith($"receipts/{user.UserId}/{recognitionId}/", image));
        Assert.EndsWith("/1.webp", images[0]);
        Assert.EndsWith("/2.webp", images[1]);

        var call = Assert.Single(fakeModel.Calls);
        Assert.True(call.Provider.IsCustom);
        Assert.Equal("sk-secret", call.Provider.ApiKey);
        Assert.Equal(2, call.Images.Count);
        Assert.Contains("dining_out, groceries", call.SystemPrompt);
        Assert.Contains("2 images", call.UserPrompt);

        // Someone else cannot read or link it.
        var foreign = await SendAsync(client, stranger, HttpMethod.Get, $"/receiptrecognition/{recognitionId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        // Saving the expense links the recognition and uses its first image as the photo.
        var create = await SendAsync(client, user, HttpMethod.Post, "/transaction",
            JsonSerializer.Serialize(TransactionBody(groupId, user.UserId, recognitionId)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var transaction = await ReadAsync<JsonElement>(create);
        Assert.Equal(images[0], transaction.GetProperty("photo").GetString());
        var transactionId = transaction.GetProperty("transactionId").GetGuid();

        var linked = await ReadAsync<JsonElement>(
            await SendAsync(client, user, HttpMethod.Get, $"/receiptrecognition/{recognitionId}"));
        Assert.Equal(transactionId, linked.GetProperty("transactionId").GetGuid());

        // The same recognition cannot be attached to a second expense.
        var reuse = await SendAsync(client, user, HttpMethod.Post, "/transaction",
            JsonSerializer.Serialize(TransactionBody(groupId, user.UserId, recognitionId)));
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);

        // Deleting the expense keeps the recognition and its images.
        var delete = await SendAsync(client, user, HttpMethod.Delete, $"/transaction/{transactionId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var unlinked = await ReadAsync<JsonElement>(
            await SendAsync(client, user, HttpMethod.Get, $"/receiptrecognition/{recognitionId}"));
        Assert.Equal(JsonValueKind.Null, unlinked.GetProperty("transactionId").ValueKind);
        Assert.Equal(2, unlinked.GetProperty("images").GetArrayLength());
    }

    [Fact]
    public async Task ProviderFailuresAreRecordedWithTheImages()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var user = await CreateUserAndLoginAsync(factory, "rr-failure@example.com");
        var fakeModel = factory.Services.GetRequiredService<FakeReceiptRecognitionClient>();
        using var client = factory.CreateClient();
        await ConfigureCustomProviderAsync(client, user);

        fakeModel.EnqueueFailure("The model endpoint returned 429: slow down");
        var failed = await ReadAsync<JsonElement>(await PostRecognitionAsync(client, user, 1));
        Assert.Equal("Failed", failed.GetProperty("status").GetString());
        Assert.Contains("429", failed.GetProperty("error").GetString());
        Assert.Single(failed.GetProperty("images").EnumerateArray());

        fakeModel.EnqueueReply("Sorry, I cannot read this receipt.");
        var unparsable = await ReadAsync<JsonElement>(await PostRecognitionAsync(client, user, 1));
        Assert.Equal("Failed", unparsable.GetProperty("status").GetString());
        Assert.Contains("JSON", unparsable.GetProperty("error").GetString());
        Assert.Equal("Sorry, I cannot read this receipt.", unparsable.GetProperty("rawResponse").GetString());
    }

    [Fact]
    public async Task ServerProviderIsUsedWhenTheUserHasNoCustomProvider()
    {
        using var factory = new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["LLM:Enabled"] = "true",
            ["LLM:Endpoint"] = "https://server-llm.example.com/v1",
            ["LLM:Model"] = "server-vision",
            ["LLM:ApiKey"] = "server-key",
            ["ReceiptRecognition:MaxImages"] = "2"
        });
        var user = await CreateUserAndLoginAsync(factory, "rr-server@example.com");
        var fakeModel = factory.Services.GetRequiredService<FakeReceiptRecognitionClient>();
        using var client = factory.CreateClient();

        var settings = await ReadAsync<JsonElement>(
            await SendAsync(client, user, HttpMethod.Get, "/llmsettings"));
        Assert.True(settings.GetProperty("serverProviderAvailable").GetBoolean());
        Assert.True(settings.GetProperty("available").GetBoolean());
        Assert.False(settings.TryGetProperty("maxImages", out _));
        var capabilities = await ReadAsync<JsonElement>(
            await SendAsync(client, user, HttpMethod.Get, "/receiptrecognition/capabilities"));
        Assert.True(capabilities.GetProperty("available").GetBoolean());
        Assert.Equal(2, capabilities.GetProperty("maxImages").GetInt32());

        var tooMany = await PostRecognitionAsync(client, user, 3);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        fakeModel.EnqueueReply(ModelReply);
        var recognition = await ReadAsync<JsonElement>(await PostRecognitionAsync(client, user, 1));
        Assert.Equal("Succeeded", recognition.GetProperty("status").GetString());
        Assert.False(recognition.GetProperty("usedCustomProvider").GetBoolean());
        Assert.Equal("server-vision", recognition.GetProperty("model").GetString());
        var call = Assert.Single(fakeModel.Calls);
        Assert.False(call.Provider.IsCustom);
        Assert.Equal("server-key", call.Provider.ApiKey);
    }

    [Fact]
    public async Task RecognitionHasItsOwnPerUserLimit()
    {
        using var factory = new RateLimitingWebApplicationFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:ReceiptRecognitionPerUser:PermitLimit"] = "1"
        });
        var user = await CreateUserAndLoginAsync(factory, "rr-limit@example.com");
        var fakeModel = factory.Services.GetRequiredService<FakeReceiptRecognitionClient>();
        using var client = factory.CreateClient();
        await ConfigureCustomProviderAsync(client, user);

        fakeModel.EnqueueReply(ModelReply);
        var first = await PostRecognitionAsync(client, user, 1);
        var second = await PostRecognitionAsync(client, user, 1);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.NotNull(second.Headers.RetryAfter?.Delta);
    }

    private static object TransactionBody(Guid groupId, string userId, Guid recognitionId)
    {
        return new
        {
            groupId,
            name = "Blue Bottle",
            icon = "dining_out",
            createTime = DateTime.UtcNow,
            transactionTime = DateTime.UtcNow,
            amount = "12.50",
            currency = "USD",
            tags = Array.Empty<object>(),
            balances = new[] { new { userId, balance = "0.00" } },
            receiptRecognitionId = recognitionId
        };
    }

    private static async Task ConfigureCustomProviderAsync(HttpClient client, TestUser user)
    {
        var response = await SendAsync(client, user, HttpMethod.Put, "/llmsettings",
            JsonSerializer.Serialize(new
            {
                enabled = true,
                endpoint = "https://llm.example.com/v1",
                model = "vision-1",
                apiKey = "sk-secret"
            }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostRecognitionAsync(
        HttpClient client,
        TestUser user,
        int fileCount,
        string[]? categories = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/receiptrecognition");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        using var content = new MultipartFormDataContent();
        for (var index = 0; index < fileCount; index++)
        {
            var file = new ByteArrayContent([1, 2, 3, (byte)index]);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "files", $"receipt-{index}.png");
        }

        foreach (var category in categories ?? [])
            content.Add(new StringContent(category), "categories");
        request.Content = content;
        return await client.SendAsync(request);
    }

    private static async Task<TestUser> CreateUserAndLoginAsync(
        RateLimitingWebApplicationFactory factory,
        string email)
    {
        string userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SplitzUser>>();
            var user = new SplitzUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await userManager.CreateAsync(user, "Password1234")).Succeeded);
            userId = user.Id;
        }

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/account/login", new { email, password = "Password1234" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new TestUser(body.RootElement.GetProperty("accessToken").GetString()!, userId);
    }

    private static async Task<Guid> CreateGroupAsync(RateLimitingWebApplicationFactory factory, string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
        var user = await context.Users.SingleAsync(candidate => candidate.Id == userId);
        var group = new Group
        {
            GroupId = Guid.NewGuid(),
            Name = "Recognition test group",
            Members = [user],
            MembersIdHash = string.Empty,
            TransactionCount = 0,
            LastActivityTime = DateTime.UtcNow
        };
        group.UpdateMembersIdHash();
        context.Groups.Add(group);
        await context.SaveChangesAsync();
        return group.GroupId;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        TestUser user,
        HttpMethod method,
        string path,
        string? jsonBody = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private sealed record TestUser(string Token, string UserId);
}