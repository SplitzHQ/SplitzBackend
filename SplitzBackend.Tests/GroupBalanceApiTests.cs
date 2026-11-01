using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;
using SplitzBackend.Models;
using Xunit;

namespace SplitzBackend.Tests;

/// <summary>Checks personalized group summaries, authorization and the generated API schema.</summary>
public sealed class GroupBalanceApiTests
{
    [Fact]
    public async Task GroupDetailAndListReturnTheCurrentUsersTransfers()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        var alice = await CreateUserAsync(factory, "alice-balance@example.com");
        var bob = await CreateUserAsync(factory, "bob-balance@example.com");
        var outsider = await CreateUserAsync(factory, "outsider-balance@example.com");
        Guid groupId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
            var members = await db.Users.Where(u => u.Id == alice.UserId || u.Id == bob.UserId).ToListAsync();
            var group = new Group
            {
                GroupId = Guid.NewGuid(),
                Name = "Balances",
                Members = members,
                MembersIdHash = "",
                TransactionCount = 0,
                LastActivityTime = DateTime.UtcNow
            };
            group.UpdateMembersIdHash();
            group.Balances = members.Select(member => new GroupBalance
            {
                GroupId = group.GroupId,
                UserId = member.Id,
                Currency = "USD",
                Balance = member.Id == alice.UserId ? 6.45m : -6.45m
            }).ToList();
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            groupId = group.GroupId;
        }

        using var client = factory.CreateClient();
        foreach (var (user, other, amount) in new[] { (alice, bob, -6.45m), (bob, alice, 6.45m) })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
            foreach (var path in new[] { $"/group/{groupId}", "/group" })
            {
                var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var group = path == "/group" ? Assert.Single(json.RootElement.EnumerateArray()) : json.RootElement;
                var summary = Assert.Single(group.GetProperty("balanceSummaries").EnumerateArray());
                Assert.Equal(-amount, summary.GetProperty("totalBalance").GetDecimal());
                Assert.Equal("USD", summary.GetProperty("currency").GetString());
                var transfer = Assert.Single(summary.GetProperty("memberBalances").EnumerateArray());
                Assert.Equal(other.UserId, transfer.GetProperty("userId").GetString());
                Assert.Equal(amount, transfer.GetProperty("amount").GetDecimal());
                Assert.False(string.IsNullOrEmpty(transfer.GetProperty("name").GetString()));
            }
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsider.Token);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/group/{groupId}")).StatusCode);
        using var groups = JsonDocument.Parse(await (await client.GetAsync("/group")).Content.ReadAsStringAsync());
        Assert.Empty(groups.RootElement.EnumerateArray());
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/group/{groupId}")).StatusCode);

        var schema = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var schemas = schema.Components!.Schemas!;
        Assert.True(schemas["GroupDto"].Properties!.ContainsKey("balanceSummaries"));
        Assert.Equal(JsonSchemaType.String, schemas["GroupBalanceSummaryDto"].Properties!["totalBalance"].Type);
        Assert.Equal(JsonSchemaType.String, schemas["GroupMemberTransferDto"].Properties!["amount"].Type);
    }

    [Fact]
    public void MapperResolvesBalanceSummariesFromEachOperationsUserId()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
        var group = new Group
        {
            Name = "Balances",
            MembersIdHash = "",
            TransactionCount = 0,
            LastActivityTime = DateTime.UtcNow,
            Balances =
            [
                new GroupBalance
                {
                    GroupId = Guid.Empty, UserId = "alice", Balance = 6.45m, Currency = "USD",
                    User = new SplitzUser { Id = "alice", UserName = "Alice" }
                },
                new GroupBalance
                {
                    GroupId = Guid.Empty, UserId = "bob", Balance = -6.45m, Currency = "USD",
                    User = new SplitzUser { Id = "bob", UserName = "Bob" }
                }
            ]
        };

        var alice = mapper.Map<GroupDto>(group, options => options.State = "alice");
        var bob = mapper.Map<GroupDto>(group, options => options.State = "bob");
        var aliceSummary = Assert.Single(alice.BalanceSummaries);
        var bobSummary = Assert.Single(bob.BalanceSummaries);
        Assert.Equal(6.45m, aliceSummary.TotalBalance);
        Assert.Equal(-6.45m, Assert.Single(aliceSummary.MemberBalances).Amount);
        Assert.Equal("bob", Assert.Single(aliceSummary.MemberBalances).UserId);
        Assert.Equal(-6.45m, bobSummary.TotalBalance);
        Assert.Equal(6.45m, Assert.Single(bobSummary.MemberBalances).Amount);
        Assert.Equal("alice", Assert.Single(bobSummary.MemberBalances).UserId);

        var error = Assert.Throws<AutoMapperMappingException>(() => mapper.Map<GroupDto>(group));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    /// <summary>Creates a confirmed test user and returns the login token.</summary>
    private static async Task<(string UserId, string Token)> CreateUserAsync(
        RateLimitingWebApplicationFactory factory, string email)
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
        return (id, json.RootElement.GetProperty("accessToken").GetString()!);
    }
}