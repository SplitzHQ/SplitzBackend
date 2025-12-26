using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;

namespace SplitzBackend.Controllers;

[Authorize]
[ApiController]
[Route("[controller]")]
public class GroupController(
    SplitzDbContext db,
    UserManager<SplitzUser> userManager,
    IMapper mapper)
    : ControllerBase
{
    /// <summary>
    ///     Get the current user's groups
    /// </summary>
    /// <returns></returns>
    [HttpGet(Name = "GetGroups")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<List<GroupDto>>> GetGroups()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        
        var groups = await db.Groups
            .Include(g => g.Members)
            .Include(g => g.Transactions)
                .ThenInclude(t => t.Balances)
                    .ThenInclude(b => b.User)
            .Where(g => g.Members.Contains(user))
            .ToListAsync();
        
        var groupDtos = groups.Select(g => CalculateGroupBalance(g, mapper)).ToList();
        return groupDtos;
    }

    /// <summary>
    ///     Get the group info
    /// </summary>
    /// <returns></returns>
    [HttpGet("{groupId}", Name = "GetGroup")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<GroupDto>> GetGroup(Guid groupId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        
        var group = await db.Groups
            .Include(g => g.Members)
            .Include(g => g.Transactions)
                .ThenInclude(t => t.Balances)
                    .ThenInclude(b => b.User)
            .Where(g => g.Members.Contains(user) && g.GroupId == groupId)
            .FirstOrDefaultAsync();
        
        if (group is null)
            return NotFound();
        
        return CalculateGroupBalance(group, mapper);
    }

    /// <summary>
    ///     Get the group transactions
    /// </summary>
    /// <param name="groupId"></param>
    /// <returns></returns>
    [HttpGet("{groupId}/transactions", Name = "GetGroupTransactions")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<List<TransactionDto>>> GetGroupTransactions(Guid groupId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var group = db.Groups.Where(g => g.Members.Contains(user) && g.GroupId == groupId);
        if (await group.FirstOrDefaultAsync() is null)
            return NotFound();
        var transactions = group.SelectMany(g => g.Transactions);
        return await mapper.ProjectTo<TransactionDto>(transactions).ToListAsync();
    }

    /// <summary>
    ///     Create a new group
    /// </summary>
    /// <param name="groupInputDto">group info</param>
    /// <returns></returns>
    [HttpPost(Name = "CreateGroup")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(200)]
    [ProducesResponseType(201)]
    public async Task<ActionResult<GroupDto>> Create(GroupInputDto groupInputDto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var group = mapper.Map<Group>(groupInputDto);

        // check if members are user's friends
        var members = await db.Users.Include(splitzUser => splitzUser.Friends)
            .Where(splitzUser =>
                groupInputDto.MembersId.Contains(splitzUser.Id) &&
                splitzUser.Friends.Select(friend => friend.FriendUserId).Contains(user.Id))
            .ToListAsync();
        members = members.Concat([user]).Distinct().ToList();
        group.Members = members;
        group.UpdateMembersIdHash();

        // check if group with the same member hash exists
        var existingGroup = await db.Groups
            .Where(g => g.MembersIdHash == group.MembersIdHash)
            .FirstOrDefaultAsync();
        if (existingGroup is not null)
            // return existing group
            return Ok(mapper.Map<GroupDto>(existingGroup));

        group.Transactions = [];
        group.Balances = [];
        group.LastActivityTime = DateTime.Now;
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetGroup), new { groupId = group.GroupId }, mapper.Map<GroupDto>(group));
    }

    /// <summary>
    ///     Update group info
    /// </summary>
    /// <param name="groupId">Group Id</param>
    /// <param name="groupInputDto">group info</param>
    [HttpPut("{groupId}", Name = "UpdateGroup")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(201)]
    public async Task<ActionResult<GroupDto>> UpdateGroup(Guid groupId, GroupInputDto groupInputDto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var group = await db.Groups.Include(g => g.Members).FirstOrDefaultAsync(g => g.GroupId == groupId);
        if (group is null)
            return NotFound();
        if (!group.Members.Contains(user))
            return Unauthorized();
        mapper.Map(groupInputDto, group);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetGroup), new { groupId = group.GroupId }, mapper.Map<GroupDto>(group));
    }


    /// <summary>
    ///    Add members to a group
    /// </summary>
    /// <param name="groupId">group id</param>
    /// <param name="userIds">list of user ids</param>
    /// <returns></returns>
    [HttpPost("{groupId}/members", Name = "AddGroupMember")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(201)]
    public async Task<ActionResult<GroupDto>> AddGroupMember(Guid groupId, List<string> userIds)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var group = await db.Groups.Include(g => g.Members).FirstOrDefaultAsync(g => g.GroupId == groupId);
        if (group is null)
            return NotFound();

        // check if members are user's friends
        var members = await db.Users.Include(splitzUser => splitzUser.Friends)
            .Where(splitzUser =>
                userIds.Contains(splitzUser.Id) &&
                splitzUser.Friends.Select(friend => friend.FriendUserId).Contains(user.Id))
            .ToListAsync();
        group.Members = [.. group.Members, .. members];
        group.Members = group.Members.Distinct().ToList();
        group.UpdateMembersIdHash();
        group.LastActivityTime = DateTime.Now;
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetGroup), new { groupId = group.GroupId }, mapper.Map<GroupDto>(group));
    }

    /// <summary>
    ///     create a join link for a group
    /// </summary>
    /// <param name="groupId">group id</param>
    /// <returns></returns>
    [HttpPost("{groupId}/join-link", Name = "CreateGroupJoinLink")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<GroupReducedDto>> CreateGroupJoinLink(Guid groupId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var group = await db.Groups.Where(g => g.GroupId == groupId && g.Members.Contains(user)).FirstOrDefaultAsync();
        if (group is null)
            return NotFound();
        var link = new GroupJoinLink { GroupId = groupId };
        db.GroupJoinLinks.Add(link);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetGroupInfoByLink), new { joinLinkId = link.GroupJoinLinkId },
            mapper.Map<GroupReducedDto>(link));
    }

    /// <summary>
    ///     get group info from join link
    /// </summary>
    /// <param name="joinLinkId">join link id</param>
    /// <returns></returns>
    [HttpGet("join/{joinLinkId}", Name = "GetGroupInfoByLink")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<GroupReducedDto>> GetGroupInfoByLink(Guid joinLinkId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var groupJoinLink = await db.GroupJoinLinks.Include(e => e.Group)
            .FirstOrDefaultAsync(e => e.GroupJoinLinkId == joinLinkId);
        if (groupJoinLink is null)
            return NotFound();
        return mapper.Map<GroupReducedDto>(groupJoinLink.Group);
    }

    /// <summary>
    ///     join a group by a join link
    /// </summary>
    /// <param name="joinLinkId">join link id</param>
    /// <returns></returns>
    [HttpPost("join/{joinLinkId}", Name = "JoinGroupByLink")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<GroupDto>> JoinGroupByLink(Guid joinLinkId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var groupJoinLink = await db.GroupJoinLinks.Include(e => e.Group)
            .FirstOrDefaultAsync(e => e.GroupJoinLinkId == joinLinkId);
        if (groupJoinLink is null)
            return NotFound();
        groupJoinLink.Group.Members.Add(user);
        groupJoinLink.Group.UpdateMembersIdHash();
        groupJoinLink.Group.LastActivityTime = DateTime.Now;
        await db.SaveChangesAsync();
        return mapper.Map<GroupDto>(groupJoinLink.Group);
    }

    private static GroupDto CalculateGroupBalance(Group group, IMapper mapper)
    {
        var groupDto = mapper.Map<GroupDto>(group);
        
        // Calculate GroupBalance from TransactionBalance
        // Get all transaction balances grouped by currency
        var transactionBalances = group.Transactions
            .SelectMany(t => t.Balances.Select(b => new
            {
                b.UserId,
                b.Balance,
                Currency = t.Currency,
                User = b.User
            }))
            .ToList();

        // Group by currency
        var balancesByCurrency = transactionBalances
            .GroupBy(tb => tb.Currency)
            .ToList();

        var groupBalances = new List<GroupBalanceDto>();

        foreach (var currencyGroup in balancesByCurrency)
        {
            var currency = currencyGroup.Key;
            var balances = currencyGroup.ToList();

            // Get all unique user IDs
            var userIds = balances.Select(b => b.UserId).Distinct().ToList();

            // Calculate balance for each pair of users
            for (int i = 0; i < userIds.Count; i++)
            {
                for (int j = i + 1; j < userIds.Count; j++)
                {
                    var userId = userIds[i];
                    var friendUserId = userIds[j];

                    // Calculate net balance between the two users
                    // TransactionBalance.Balance meaning:
                    // - If Balance > 0: user owes money (user is debtor)
                    // - If Balance < 0: user is owed money (user is creditor)
                    //
                    // For GroupBalance from user A's perspective to friend B:
                    // - If Balance > 0: A owes B
                    // - If Balance < 0: B owes A
                    //
                    // Calculate per-transaction balance between the two users
                    var netBalance = 0m;
                    var transactions = group.Transactions.Where(t => t.Currency == currency).ToList();
                    
                    foreach (var transaction in transactions)
                    {
                        var userBalance = transaction.Balances.FirstOrDefault(b => b.UserId == userId);
                        var friendBalance = transaction.Balances.FirstOrDefault(b => b.UserId == friendUserId);
                        
                        if (userBalance != null && friendBalance != null)
                        {
                            // For this transaction, calculate the net balance from user's perspective
                            // Formula: netBalance += friendBalance.Balance - userBalance.Balance
                            // This works correctly for two-person transactions and gives a reasonable
                            // approximation for multi-person transactions
                            netBalance += friendBalance.Balance - userBalance.Balance;
                        }
                    }

                    if (netBalance != 0)
                    {
                        var user = mapper.Map<SplitzUserReducedDto>(
                            balances.First(b => b.UserId == userId).User);
                        var friendUser = mapper.Map<SplitzUserReducedDto>(
                            balances.First(b => b.UserId == friendUserId).User);

                        groupBalances.Add(new GroupBalanceDto
                        {
                            GroupId = group.GroupId,
                            User = user,
                            FriendUser = friendUser,
                            Balance = netBalance,
                            Currency = currency
                        });
                    }
                }
            }
        }

        groupDto.Balances = groupBalances;
        return groupDto;
    }
}