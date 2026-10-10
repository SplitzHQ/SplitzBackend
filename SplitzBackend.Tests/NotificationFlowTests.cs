using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SplitzBackend.Models;
using Xunit;

namespace SplitzBackend.Tests;

/// <summary>
///     End-to-end coverage of friend requests, group invites and preference-aware activity notifications.
/// </summary>
public class NotificationFlowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task FriendRequestIsDeliveredAcceptedAndCreatesMutualFriendship()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var alice = await CreateUserAndLoginAsync(factory, "alice-fr@example.com");
        var bob = await CreateUserAndLoginAsync(factory, "bob-fr@example.com");
        using var client = factory.CreateClient();

        var send = await SendAsync(client, alice, HttpMethod.Post, "/account/friend/request",
            JsonSerializer.Serialize(new { friendId = bob.UserId, remark = "Bobby" }));
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var request = await ReadAsync<JsonElement>(send);
        var requestId = request.GetProperty("friendRequestId").GetGuid();
        Assert.Equal("Pending", request.GetProperty("status").GetString());

        // Nothing is a friend yet.
        var aliceInfo = await ReadAsync<JsonElement>(await SendAsync(client, alice, HttpMethod.Get, "/account"));
        Assert.Empty(aliceInfo.GetProperty("friends").EnumerateArray());

        // Bob sees a request notification and the summary counts it.
        var bobRequests = await ReadAsync<List<JsonElement>>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification?category=Request"));
        var notification = Assert.Single(bobRequests);
        Assert.Equal("FriendRequest", notification.GetProperty("type").GetString());
        Assert.Equal("Request", notification.GetProperty("category").GetString());
        Assert.Equal("High", notification.GetProperty("priority").GetString());
        Assert.Equal(requestId, notification.GetProperty("data").GetProperty("requestId").GetGuid());
        Assert.Equal(alice.UserId, notification.GetProperty("data").GetProperty("fromUserId").GetString());

        var summary = await ReadAsync<JsonElement>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification/summary"));
        Assert.Equal(1, summary.GetProperty("pendingRequests").GetInt32());
        Assert.Equal(1, summary.GetProperty("unreadRequests").GetInt32());

        var listed = await ReadAsync<JsonElement>(
            await SendAsync(client, bob, HttpMethod.Get, "/account/friend/request"));
        Assert.Single(listed.GetProperty("incoming").EnumerateArray());
        Assert.Empty(listed.GetProperty("outgoing").EnumerateArray());

        // Bob accepts.
        var accept = await SendAsync(client, bob, HttpMethod.Post,
            $"/account/friend/request/{requestId}/accept", "\"Al\"");
        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);

        aliceInfo = await ReadAsync<JsonElement>(await SendAsync(client, alice, HttpMethod.Get, "/account"));
        var aliceFriend = Assert.Single(aliceInfo.GetProperty("friends").EnumerateArray());
        Assert.Equal(bob.UserId, aliceFriend.GetProperty("friendUser").GetProperty("id").GetString());
        Assert.Equal("Bobby", aliceFriend.GetProperty("remark").GetString());

        var bobInfo = await ReadAsync<JsonElement>(await SendAsync(client, bob, HttpMethod.Get, "/account"));
        var bobFriend = Assert.Single(bobInfo.GetProperty("friends").EnumerateArray());
        Assert.Equal(alice.UserId, bobFriend.GetProperty("friendUser").GetProperty("id").GetString());
        Assert.Equal("Al", bobFriend.GetProperty("remark").GetString());

        // The request notification is gone for Bob; Alice is told about the acceptance.
        summary = await ReadAsync<JsonElement>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification/summary"));
        Assert.Equal(0, summary.GetProperty("pendingRequests").GetInt32());

        var aliceActivity = await ReadAsync<List<JsonElement>>(
            await SendAsync(client, alice, HttpMethod.Get, "/notification?category=Activity"));
        var accepted = Assert.Single(aliceActivity);
        Assert.Equal("FriendRequestAccepted", accepted.GetProperty("type").GetString());
        Assert.Equal(bob.UserId, accepted.GetProperty("data").GetProperty("friendUserId").GetString());

        // Accepting twice is not possible.
        var again = await SendAsync(client, bob, HttpMethod.Post,
            $"/account/friend/request/{requestId}/accept", "null");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task IgnoredFriendRequestClearsNotificationWithoutCreatingFriendship()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var alice = await CreateUserAndLoginAsync(factory, "alice-ignore@example.com");
        var bob = await CreateUserAndLoginAsync(factory, "bob-ignore@example.com");
        using var client = factory.CreateClient();

        var request = await ReadAsync<JsonElement>(
            await SendAsync(client, alice, HttpMethod.Post, "/account/friend/request",
                JsonSerializer.Serialize(new { friendId = bob.UserId })));
        var requestId = request.GetProperty("friendRequestId").GetGuid();

        var ignore = await SendAsync(client, bob, HttpMethod.Post, $"/account/friend/request/{requestId}/ignore");
        Assert.Equal(HttpStatusCode.NoContent, ignore.StatusCode);

        var bobRequests = await ReadAsync<List<JsonElement>>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification?category=Request"));
        Assert.Empty(bobRequests);

        var bobInfo = await ReadAsync<JsonElement>(await SendAsync(client, bob, HttpMethod.Get, "/account"));
        Assert.Empty(bobInfo.GetProperty("friends").EnumerateArray());

        var aliceActivity = await ReadAsync<List<JsonElement>>(
            await SendAsync(client, alice, HttpMethod.Get, "/notification"));
        Assert.Empty(aliceActivity);
    }

    [Fact]
    public async Task GroupCreationInvitesFriendsWhoJoinOnlyAfterAccepting()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var alice = await CreateUserAndLoginAsync(factory, "alice-gi@example.com");
        var bob = await CreateUserAndLoginAsync(factory, "bob-gi@example.com");
        var stranger = await CreateUserAndLoginAsync(factory, "stranger-gi@example.com");
        using var client = factory.CreateClient();
        await MakeFriendsAsync(client, alice, bob);

        var create = await SendAsync(client, alice, HttpMethod.Post, "/group",
            JsonSerializer.Serialize(new { name = "Trip", membersId = new[] { alice.UserId, bob.UserId, stranger.UserId } }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var group = await ReadAsync<JsonElement>(create);
        var groupId = group.GetProperty("groupId").GetGuid();
        var creator = Assert.Single(group.GetProperty("members").EnumerateArray());
        Assert.Equal(alice.UserId, creator.GetProperty("id").GetString());

        // Only the friend is invited; the stranger is skipped.
        var pending = await ReadAsync<List<JsonElement>>(
            await SendAsync(client, alice, HttpMethod.Get, $"/group/{groupId}/invites"));
        var invite = Assert.Single(pending);
        Assert.Equal(bob.UserId, invite.GetProperty("invitedUser").GetProperty("id").GetString());
        var inviteId = invite.GetProperty("groupInviteId").GetGuid();

        var bobNotifications = await ReadAsync<List<JsonElement>>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification?category=Request"));
        var notification = Assert.Single(bobNotifications);
        Assert.Equal("GroupInvite", notification.GetProperty("type").GetString());
        Assert.Equal(inviteId, notification.GetProperty("data").GetProperty("inviteId").GetGuid());
        Assert.Equal("Trip", notification.GetProperty("data").GetProperty("groupName").GetString());

        Assert.Empty(await ReadAsync<List<JsonElement>>(
            await SendAsync(client, stranger, HttpMethod.Get, "/notification")));

        // Bob cannot see the group before accepting.
        var forbidden = await SendAsync(client, bob, HttpMethod.Get, $"/group/{groupId}");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        var accept = await SendAsync(client, bob, HttpMethod.Post, $"/group/invite/{inviteId}/accept");
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var joined = await ReadAsync<JsonElement>(accept);
        Assert.Equal(2, joined.GetProperty("members").GetArrayLength());

        Assert.Empty(await ReadAsync<List<JsonElement>>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification?category=Request")));
        Assert.Empty(await ReadAsync<List<JsonElement>>(
            await SendAsync(client, alice, HttpMethod.Get, $"/group/{groupId}/invites")));

        // Inviting an existing member is a no-op.
        var reinvite = await SendAsync(client, alice, HttpMethod.Post, $"/group/{groupId}/invites",
            JsonSerializer.Serialize(new[] { bob.UserId }));
        Assert.Equal(HttpStatusCode.OK, reinvite.StatusCode);
        Assert.Empty(await ReadAsync<List<JsonElement>>(reinvite));
    }

    [Fact]
    public async Task TransactionNotificationsRespectRelatedVariantAndPreferences()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var alice = await CreateUserAndLoginAsync(factory, "alice-tx@example.com");
        var bob = await CreateUserAndLoginAsync(factory, "bob-tx@example.com");
        var carol = await CreateUserAndLoginAsync(factory, "carol-tx@example.com");
        using var client = factory.CreateClient();
        var groupId = await CreateGroupWithMembersAsync(factory, "Dinner", alice, bob, carol);

        // Bob mutes plain transaction activity globally.
        var mute = await SendAsync(client, bob, HttpMethod.Put, "/notification/preferences",
            JsonSerializer.Serialize(new { type = "TransactionCreated", groupId = (Guid?)null, enabled = false }));
        Assert.Equal(HttpStatusCode.OK, mute.StatusCode);

        // Requests cannot be muted.
        var muteRequest = await SendAsync(client, bob, HttpMethod.Put, "/notification/preferences",
            JsonSerializer.Serialize(new { type = "FriendRequest", groupId = (Guid?)null, enabled = false }));
        Assert.Equal(HttpStatusCode.BadRequest, muteRequest.StatusCode);

        // Alice pays 30 for herself and Carol.
        await PostTransactionAsync(client, alice, groupId, "Pizza", 30m,
            [(alice.UserId, 15m), (carol.UserId, -15m)]);

        var carolActivity = await ReadAsync<List<JsonElement>>(
            await SendAsync(client, carol, HttpMethod.Get, "/notification?category=Activity"));
        var related = Assert.Single(carolActivity);
        Assert.Equal("RelatedTransactionCreated", related.GetProperty("type").GetString());
        Assert.Equal("High", related.GetProperty("priority").GetString());
        Assert.Equal(groupId, related.GetProperty("groupId").GetGuid());
        Assert.Equal(-15m, related.GetProperty("data").GetProperty("userBalance").GetDecimal());
        Assert.Equal("Pizza", related.GetProperty("data").GetProperty("transactionName").GetString());
        Assert.Equal(alice.UserId, related.GetProperty("data").GetProperty("creatorUserId").GetString());
        Assert.Equal(alice.Email, related.GetProperty("data").GetProperty("creatorName").GetString());
        Assert.Equal("Dinner", related.GetProperty("data").GetProperty("groupName").GetString());

        Assert.Empty(await ReadAsync<List<JsonElement>>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification?category=Activity")));
        Assert.Empty(await ReadAsync<List<JsonElement>>(
            await SendAsync(client, alice, HttpMethod.Get, "/notification?category=Activity")));

        // A group override re-enables it for Bob in this group only.
        var unmute = await SendAsync(client, bob, HttpMethod.Put, "/notification/preferences",
            JsonSerializer.Serialize(new { type = "TransactionCreated", groupId, enabled = true }));
        Assert.Equal(HttpStatusCode.OK, unmute.StatusCode);

        await PostTransactionAsync(client, alice, groupId, "Taxi", 10m,
            [(alice.UserId, 5m), (carol.UserId, -5m)]);

        var bobActivity = await ReadAsync<List<JsonElement>>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification?category=Activity"));
        var plain = Assert.Single(bobActivity);
        Assert.Equal("TransactionCreated", plain.GetProperty("type").GetString());
        Assert.Equal("Low", plain.GetProperty("priority").GetString());
        Assert.True(plain.GetProperty("data").GetProperty("userBalance").ValueKind == JsonValueKind.Null);

        var preferences = await ReadAsync<List<JsonElement>>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification/preferences"));
        Assert.Equal(2, preferences.Count);

        // Read-all and dismiss-all only touch activity.
        var readAll = await SendAsync(client, bob, HttpMethod.Post, "/notification/read-all");
        Assert.Equal(HttpStatusCode.NoContent, readAll.StatusCode);
        var summary = await ReadAsync<JsonElement>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification/summary"));
        Assert.Equal(0, summary.GetProperty("unreadActivity").GetInt32());

        var dismissAll = await SendAsync(client, bob, HttpMethod.Post, "/notification/dismiss-all");
        Assert.Equal(HttpStatusCode.NoContent, dismissAll.StatusCode);
        Assert.Empty(await ReadAsync<List<JsonElement>>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification")));
    }

    [Fact]
    public async Task NotificationNamesAreLoadedOnReadAndPreferNicknames()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var alice = await CreateUserAndLoginAsync(factory, "alice-names@example.com");
        var bob = await CreateUserAndLoginAsync(factory, "bob-names@example.com");
        using var client = factory.CreateClient();

        // Without a friendship, the friend request shows the plain user name.
        var request = await ReadAsync<JsonElement>(
            await SendAsync(client, alice, HttpMethod.Post, "/account/friend/request",
                JsonSerializer.Serialize(new { friendId = bob.UserId })));
        var friendRequest = Assert.Single(await ReadAsync<List<JsonElement>>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification?category=Request")));
        Assert.Equal(alice.Email, friendRequest.GetProperty("data").GetProperty("fromUserName").GetString());

        // Bob calls Alice "Al" when he accepts.
        var requestId = request.GetProperty("friendRequestId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, bob, HttpMethod.Post,
            $"/account/friend/request/{requestId}/accept", "\"Al\"")).StatusCode);

        var create = await SendAsync(client, alice, HttpMethod.Post, "/group",
            JsonSerializer.Serialize(new { name = "Trip", membersId = new[] { alice.UserId, bob.UserId } }));
        var groupId = (await ReadAsync<JsonElement>(create)).GetProperty("groupId").GetGuid();

        var invite = Assert.Single(await ReadAsync<List<JsonElement>>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification?category=Request")));
        Assert.Equal("Al", invite.GetProperty("data").GetProperty("invitedByUserName").GetString());
        Assert.Equal("Trip", invite.GetProperty("data").GetProperty("groupName").GetString());

        // Alice has no nickname for Bob, so she sees his user name.
        var accepted = Assert.Single(await ReadAsync<List<JsonElement>>(
            await SendAsync(client, alice, HttpMethod.Get, "/notification?category=Activity")));
        Assert.Equal(bob.Email, accepted.GetProperty("data").GetProperty("friendUserName").GetString());

        // Names are not stored, so a renamed group and a changed nickname show at once.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
            var stored = db.Notifications.Where(n => n.UserId == bob.UserId).Select(n => n.Data).ToList();
            Assert.All(stored, data =>
            {
                Assert.DoesNotContain("Trip", data);
                Assert.DoesNotContain(alice.Email, data);
                Assert.DoesNotContain("Name\"", data);
            });

            var group = db.Groups.Single(g => g.GroupId == groupId);
            group.Name = "Holiday";
            var friend = db.Set<Friend>().Single(f => f.UserId == bob.UserId && f.FriendUserId == alice.UserId);
            friend.Remark = null;
            await db.SaveChangesAsync();
        }

        invite = Assert.Single(await ReadAsync<List<JsonElement>>(
            await SendAsync(client, bob, HttpMethod.Get, "/notification?category=Request")));
        Assert.Equal("Holiday", invite.GetProperty("data").GetProperty("groupName").GetString());
        Assert.Equal(alice.Email, invite.GetProperty("data").GetProperty("invitedByUserName").GetString());
    }

    [Fact]
    public async Task NotificationTypesEndpointListsCatalog()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var alice = await CreateUserAndLoginAsync(factory, "alice-types@example.com");
        using var client = factory.CreateClient();

        var types = await ReadAsync<List<JsonElement>>(
            await SendAsync(client, alice, HttpMethod.Get, "/notification/types"));
        Assert.Equal(NotificationCatalog.All.Count, types.Count);
        var friendRequest = types.Single(t => t.GetProperty("type").GetString() == "FriendRequest");
        Assert.False(friendRequest.GetProperty("configurable").GetBoolean());
        Assert.Equal("Request", friendRequest.GetProperty("category").GetString());
    }

    // --- helpers ---

    private static async Task PostTransactionAsync(HttpClient client, TestUser payer, Guid groupId, string name,
        decimal amount, (string UserId, decimal Balance)[] balances)
    {
        var response = await SendAsync(client, payer, HttpMethod.Post, "/transaction", JsonSerializer.Serialize(new
        {
            groupId,
            name,
            icon = "pizza",
            createTime = DateTime.UtcNow,
            transactionTime = DateTime.UtcNow,
            amount,
            currency = "USD",
            tags = Array.Empty<object>(),
            balances = balances.Select(b => new { userId = b.UserId, balance = b.Balance })
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task MakeFriendsAsync(HttpClient client, TestUser from, TestUser to)
    {
        var request = await ReadAsync<JsonElement>(
            await SendAsync(client, from, HttpMethod.Post, "/account/friend/request",
                JsonSerializer.Serialize(new { friendId = to.UserId })));
        var requestId = request.GetProperty("friendRequestId").GetGuid();
        var accept = await SendAsync(client, to, HttpMethod.Post, $"/account/friend/request/{requestId}/accept",
            "null");
        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
    }

    /// <summary>
    ///     Seed a group directly so activity tests do not depend on the invite flow.
    /// </summary>
    private static async Task<Guid> CreateGroupWithMembersAsync(RateLimitingWebApplicationFactory factory,
        string name, params TestUser[] members)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
        var ids = members.Select(m => m.UserId).ToList();
        var users = db.Users.Where(u => ids.Contains(u.Id)).ToList();
        var group = new Group
        {
            GroupId = Guid.NewGuid(),
            Name = name,
            Members = users,
            MembersIdHash = "",
            TransactionCount = 0,
            LastActivityTime = DateTime.UtcNow
        };
        group.UpdateMembersIdHash();
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group.GroupId;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.RequestUri}: {response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, TestUser user, HttpMethod method,
        string path, string? jsonBody = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<TestUser> CreateUserAndLoginAsync(RateLimitingWebApplicationFactory factory,
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
        return new TestUser(body.RootElement.GetProperty("accessToken").GetString()!, email, userId);
    }

    private sealed record TestUser(string Token, string Email, string UserId);
}