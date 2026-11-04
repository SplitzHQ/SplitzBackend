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

/// <summary>Checks the archive rules for invoices and groups, the default list filters and the read-only lock.</summary>
public sealed class ArchiveApiTests
{
    [Fact]
    public async Task OnlySettledInvoicesCanBeArchivedAndTheirExpensesFollow()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var alice = await CreateUserAsync(factory, "alice-archive-invoice@example.com");
        var bob = await CreateUserAsync(factory, "bob-archive-invoice@example.com");
        var outsider = await CreateUserAsync(factory, "outsider-archive-invoice@example.com");
        var groupId = await CreateGroupAsync(factory, alice, bob);
        using var client = factory.CreateClient();

        var transactionId = await AddTransactionAsync(client, alice, bob, groupId);
        var invoiceId = await CreateInvoiceAsync(client, alice, groupId, transactionId);

        // An open invoice cannot be archived.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(client, alice, HttpMethod.Post, $"/invoice/{invoiceId}/archive")).StatusCode);

        var settlementId = await SettleAsync(client, bob, alice, invoiceId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, outsider, HttpMethod.Post, $"/invoice/{invoiceId}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, bob, HttpMethod.Post, $"/invoice/{invoiceId}/archive")).StatusCode);

        // The invoice and its expense are archived, still readable by id, and hidden from lists by default.
        var invoice = await GetJsonAsync(client, alice, $"/invoice/{invoiceId}");
        Assert.True(invoice.GetProperty("isArchived").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, invoice.GetProperty("archiveTime").ValueKind);
        Assert.True(Assert.Single(invoice.GetProperty("transactions").EnumerateArray())
            .GetProperty("isArchived").GetBoolean());
        Assert.True((await GetJsonAsync(client, alice, $"/transaction/{transactionId}"))
            .GetProperty("isArchived").GetBoolean());

        foreach (var path in new[] { $"/group/{groupId}/invoices", $"/group/{groupId}/transactions", "/invoice" })
        {
            Assert.Empty((await GetJsonAsync(client, alice, path)).EnumerateArray());
            var all = await GetJsonAsync(client, alice, $"{path}?includeArchived=true");
            Assert.True(Assert.Single(all.EnumerateArray()).GetProperty("isArchived").GetBoolean());
        }

        // Archived items are read-only.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, alice, HttpMethod.Delete,
            $"/invoice/{invoiceId}/settlement/{settlementId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, alice, HttpMethod.Put,
            $"/invoice/{invoiceId}",
            new { groupId, currency = "USD", transactionIds = new[] { transactionId } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(client, alice, HttpMethod.Delete, $"/invoice/{invoiceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(client, alice, HttpMethod.Delete, $"/transaction/{transactionId}")).StatusCode);

        // Unarchiving brings the invoice and its expense back and unlocks them.
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, alice, HttpMethod.Post, $"/invoice/{invoiceId}/unarchive")).StatusCode);
        var transactions = await GetJsonAsync(client, alice, $"/group/{groupId}/transactions");
        Assert.False(Assert.Single(transactions.EnumerateArray()).GetProperty("isArchived").GetBoolean());
        var restored = await GetJsonAsync(client, alice, $"/invoice/{invoiceId}");
        Assert.False(restored.GetProperty("isArchived").GetBoolean());
        Assert.Equal(JsonValueKind.Null, restored.GetProperty("archiveTime").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, alice, HttpMethod.Delete,
            $"/invoice/{invoiceId}/settlement/{settlementId}")).StatusCode);
    }

    [Fact]
    public async Task OnlyGroupsWithoutUnpaidExpensesCanBeArchived()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var alice = await CreateUserAsync(factory, "alice-archive-group@example.com");
        var bob = await CreateUserAsync(factory, "bob-archive-group@example.com");
        var groupId = await CreateGroupAsync(factory, alice, bob);
        using var client = factory.CreateClient();
        var archivePath = $"/group/{groupId}/archive";

        // An expense that is not in an invoice is unpaid.
        var transactionId = await AddTransactionAsync(client, alice, bob, groupId);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, alice, HttpMethod.Post, archivePath)).StatusCode);

        // An expense in an open invoice is still unpaid.
        var invoiceId = await CreateInvoiceAsync(client, alice, groupId, transactionId);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, alice, HttpMethod.Post, archivePath)).StatusCode);

        var settlementId = await SettleAsync(client, bob, alice, invoiceId);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, alice, HttpMethod.Post, archivePath)).StatusCode);

        // The group is hidden from the list by default but can still be opened.
        Assert.Empty((await GetJsonAsync(client, bob, "/group")).EnumerateArray());
        var all = await GetJsonAsync(client, bob, "/group?includeArchived=true");
        Assert.True(Assert.Single(all.EnumerateArray()).GetProperty("isArchived").GetBoolean());
        var group = await GetJsonAsync(client, bob, $"/group/{groupId}");
        Assert.True(group.GetProperty("isArchived").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, group.GetProperty("archiveTime").ValueKind);

        // An archived group accepts no new expenses and its invoices are read-only.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, alice, HttpMethod.Post, "/transaction",
            TransactionBody(groupId, alice, bob))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, alice, HttpMethod.Delete,
            $"/invoice/{invoiceId}/settlement/{settlementId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(client, alice, HttpMethod.Delete, $"/transaction/{transactionId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(client, bob, HttpMethod.Post, $"/group/{groupId}/unarchive")).StatusCode);
        Assert.Single((await GetJsonAsync(client, alice, "/group")).EnumerateArray());
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, alice, HttpMethod.Post, "/transaction",
            TransactionBody(groupId, alice, bob))).StatusCode);
    }

    /// <summary>Adds a group with both users as members.</summary>
    private static async Task<Guid> CreateGroupAsync(RateLimitingWebApplicationFactory factory, TestUser first,
        TestUser second)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
        var members = await db.Users.Where(u => u.Id == first.UserId || u.Id == second.UserId).ToListAsync();
        var group = new Group
        {
            GroupId = Guid.NewGuid(),
            Name = "Archive",
            Members = members,
            MembersIdHash = "",
            TransactionCount = 0,
            LastActivityTime = DateTime.UtcNow
        };
        group.UpdateMembersIdHash();
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group.GroupId;
    }

    /// <summary>A 10 USD expense that the payer paid and split evenly with the other user.</summary>
    private static object TransactionBody(Guid groupId, TestUser payer, TestUser other)
    {
        return new
        {
            groupId,
            name = "Dinner",
            icon = "dining_out",
            createTime = DateTime.UtcNow,
            transactionTime = DateTime.UtcNow,
            amount = "10.00",
            currency = "USD",
            tags = Array.Empty<object>(),
            balances = new[]
            {
                new { userId = payer.UserId, balance = "5.00" },
                new { userId = other.UserId, balance = "-5.00" }
            }
        };
    }

    private static async Task<Guid> AddTransactionAsync(HttpClient client, TestUser payer, TestUser other,
        Guid groupId)
    {
        var response = await SendAsync(client, payer, HttpMethod.Post, "/transaction",
            TransactionBody(groupId, payer, other));
        return (await ReadJsonAsync(response)).GetProperty("transactionId").GetGuid();
    }

    private static async Task<Guid> CreateInvoiceAsync(HttpClient client, TestUser user, Guid groupId,
        Guid transactionId)
    {
        var response = await SendAsync(client, user, HttpMethod.Post, "/invoice",
            new { groupId, currency = "USD", transactionIds = new[] { transactionId } });
        return (await ReadJsonAsync(response)).GetProperty("invoiceId").GetGuid();
    }

    /// <summary>Records the full 5 USD payment, which settles the invoice.</summary>
    private static async Task<Guid> SettleAsync(HttpClient client, TestUser debtor, TestUser creditor,
        Guid invoiceId)
    {
        var response = await SendAsync(client, debtor, HttpMethod.Post, $"/invoice/{invoiceId}/settlement",
            new { fromUserId = debtor.UserId, toUserId = creditor.UserId, amount = "5.00" });
        return (await ReadJsonAsync(response)).GetProperty("invoiceSettlementId").GetGuid();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, TestUser user, HttpMethod method,
        string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, TestUser user, string path)
    {
        return await ReadJsonAsync(await SendAsync(client, user, HttpMethod.Get, path));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }

    /// <summary>Creates a confirmed test user and returns the login token.</summary>
    private static async Task<TestUser> CreateUserAsync(RateLimitingWebApplicationFactory factory, string email)
    {
        string id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<SplitzUser>>();
            var user = new SplitzUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await manager.CreateAsync(user, "Password1234")).Succeeded);
            id = user.Id;
        }

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/account/login", new { email, password = "Password1234" });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new TestUser(id, json.RootElement.GetProperty("accessToken").GetString()!);
    }

    private sealed record TestUser(string UserId, string Token);
}