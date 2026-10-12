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

/// <summary>
///     Guards the model-wide UTC DateTime convention: values read from SQLite carry Kind = Utc,
///     so the API always emits a trailing "Z", and Local inputs are normalized before they are stored.
/// </summary>
public class UtcDateTimeConventionTests
{
    [Fact]
    public async Task EveryDateTimeReadFromTheDatabaseIsUtc()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var user = await CreateUserAndLoginAsync(factory, "utc-kind@example.com");

        Guid groupId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
            var owner = await db.Users.SingleAsync(u => u.Id == user.UserId);
            var group = new Group
            {
                GroupId = Guid.NewGuid(),
                Name = "Kinds",
                Members = [owner],
                MembersIdHash = "",
                TransactionCount = 0,
                LastActivityTime = DateTime.UtcNow
            };
            group.UpdateMembersIdHash();
            db.Groups.Add(group);
            db.Notifications.Add(new Notification
            {
                NotificationId = Guid.NewGuid(),
                UserId = owner.Id,
                Type = NotificationTypes.InvoiceSettled,
                Category = NotificationCategory.Activity,
                Priority = NotificationPriority.Low,
                Data = "{}",
                IsRead = false,
                IsDismissed = false,
                CreateTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            groupId = group.GroupId;
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
            var group = await db.Groups.AsNoTracking().SingleAsync(g => g.GroupId == groupId);
            var notification = await db.Notifications.AsNoTracking().SingleAsync(n => n.UserId == user.UserId);

            Assert.Equal(DateTimeKind.Utc, group.LastActivityTime.Kind);
            Assert.Equal(DateTimeKind.Utc, notification.CreateTime.Kind);
        }

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/group/{groupId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var lastActivity = body.RootElement.GetProperty("lastActivityTime").GetString()!;
        Assert.EndsWith("Z", lastActivity);
    }

    [Fact]
    public async Task LocalDateTimeIsStoredAsTheEquivalentUtcInstant()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var user = await CreateUserAndLoginAsync(factory, "utc-local@example.com");

        // A fixed instant expressed in local time, as System.Text.Json produces for an input with an offset.
        var instant = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        var local = instant.ToLocalTime();
        Assert.Equal(DateTimeKind.Local, local.Kind);

        Guid groupId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
            var owner = await db.Users.SingleAsync(u => u.Id == user.UserId);
            var group = new Group
            {
                GroupId = Guid.NewGuid(),
                Name = "Local",
                Members = [owner],
                MembersIdHash = "",
                TransactionCount = 0,
                LastActivityTime = local
            };
            group.UpdateMembersIdHash();
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            groupId = group.GroupId;
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
            var stored = await db.Groups.AsNoTracking().SingleAsync(g => g.GroupId == groupId);

            Assert.Equal(DateTimeKind.Utc, stored.LastActivityTime.Kind);
            Assert.Equal(instant, stored.LastActivityTime);
        }
    }

    private static async Task<(string Token, string UserId)> CreateUserAndLoginAsync(
        RateLimitingWebApplicationFactory factory, string email)
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
        return (body.RootElement.GetProperty("accessToken").GetString()!, userId);
    }
}