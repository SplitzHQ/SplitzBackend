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
    ILogger<GroupController> logger,
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
    [ProducesResponseType(200)]
    public async Task<ActionResult<GroupDto>> Create(GroupInputDto groupInputDto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var group = mapper.Map<Group>(groupInputDto);
        group.Members = [user];
        group.Transactions = [];
        group.Balances = [];
        db.Groups.Add(group);
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
        await db.SaveChangesAsync();
        return mapper.Map<GroupDto>(groupJoinLink.Group);
    }
}