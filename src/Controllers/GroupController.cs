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
        var groups = db.Groups.Where(g => g.Members.Contains(user));
        return await mapper.ProjectTo<GroupDto>(groups).ToListAsync();
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
        var group = await mapper
            .ProjectTo<GroupDto>(db.Groups.Where(g => g.Members.Contains(user) && g.GroupId == groupId))
            .FirstOrDefaultAsync();
        if (group is null)
            return NotFound();
        return group;
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
    [ProducesResponseType(201)]
    [ProducesResponseType(409)]
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
    public async Task<ActionResult<GroupJoinLinkDto>> CreateGroupJoinLink(Guid groupId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var group = await db.Groups.Where(g => g.GroupId == groupId && g.Members.Contains(user)).FirstOrDefaultAsync();
        if (group is null)
            return NotFound();
        var link = new GroupJoinLink { GroupId = groupId, GroupJoinLinkId = Guid.NewGuid() };
        db.GroupJoinLinks.Add(link);
        await db.SaveChangesAsync();
        return Ok(mapper.Map<GroupJoinLinkDto>(link));
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
}