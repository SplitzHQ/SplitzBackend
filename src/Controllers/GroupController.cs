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
        return Ok(mapper.ProjectTo<GroupDto>(groups));
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
        return Ok(group);
    }

    /// <summary>
    ///     Create a new group
    /// </summary>
    /// <param name="group">group info</param>
    /// <returns></returns>
    [HttpPut(Name = "CreateGroup")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<GroupDto>> Create(Group group)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        group.Members = [user];
        group.Transactions = [];
        group.Balances = [];
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return Ok(mapper.Map<GroupDto>(group));
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
    public async Task<ActionResult<GroupBalanceDto>> CreateGroupJoinLink(Guid groupId)
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
        return Ok(mapper.Map<GroupBalanceDto>(link));
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
        return Ok(mapper.Map<GroupReducedDto>(groupJoinLink.Group));
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
        return Ok(mapper.Map<GroupDto>(groupJoinLink.Group));
    }
}