using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;
using SplitzBackend.Services;
using SplitzBackend.Services.RateLimiting;

namespace SplitzBackend.Controllers;

[Authorize]
[ApiController]
[Route("[controller]")]
public class GroupController(
    SplitzDbContext db,
    UserManager<SplitzUser> userManager,
    IMapper mapper,
    IImageStorageService imageStorage,
    INotificationService notifications)
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
            .Where(g => g.Members.Contains(user))
            .Include(g => g.Members)
            .Include(g => g.Balances)
            .OrderByDescending(g => g.LastActivityTime)
            .ToListAsync();
        return mapper.Map<List<GroupDto>>(groups);
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
            .Where(g => g.Members.Contains(user) && g.GroupId == groupId)
            .Include(g => g.Members)
            .Include(g => g.Balances)
            .FirstOrDefaultAsync();
        if (group is null)
            return NotFound();
        return mapper.Map<GroupDto>(group);
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
        var group = await db.Groups
            .Where(g => g.Members.Contains(user) && g.GroupId == groupId)
            .FirstOrDefaultAsync();
        if (group is null)
            return NotFound();
        var transactions = await db.Transactions
            .Where(t => t.GroupId == groupId)
            .Include(t => t.Balances).ThenInclude(b => b.User)
            .ToListAsync();
        return mapper.Map<List<TransactionDto>>(transactions);
    }

    /// <summary>
    ///     Get the group invoices
    /// </summary>
    /// <param name="groupId"></param>
    /// <returns></returns>
    [HttpGet("{groupId}/invoices", Name = "GetGroupInvoices")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<List<InvoiceReducedDto>>> GetGroupInvoices(Guid groupId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var group = await db.Groups
            .Where(g => g.Members.Contains(user) && g.GroupId == groupId)
            .FirstOrDefaultAsync();
        if (group is null)
            return NotFound();
        var invoices = await db.Invoices
            .Where(i => i.GroupId == groupId)
            .OrderByDescending(i => i.CreateTime)
            .Include(i => i.CreatedBy)
            .ToListAsync();
        return mapper.Map<List<InvoiceReducedDto>>(invoices);
    }

    /// <summary>
    ///     Create a new group. The creator joins immediately; every other user in MembersId receives an invite.
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
        group.Members = [user];
        group.UpdateMembersIdHash();

        group.Transactions = [];
        group.Balances = [];
        group.LastActivityTime = DateTime.UtcNow;
        db.Groups.Add(group);

        await CreateInvitesAsync(group, user, groupInputDto.MembersId);

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
    ///     Upload a group avatar image.
    /// </summary>
    [HttpPost("{groupId}/avatar", Name = "UploadGroupAvatar")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    [ProducesResponseType(200)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [UploadRateLimitEndpointMetadata]
    [EnableRateLimiting(RateLimitPolicyNames.UploadPerUser)]
    public async Task<ActionResult<UploadImageResult>> UploadGroupAvatar(
        Guid groupId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (file.Length <= 0)
            return BadRequest("Empty file");

        var group = await db.Groups.Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.GroupId == groupId, cancellationToken);
        if (group is null)
            return NotFound();
        if (!group.Members.Contains(user))
            return Unauthorized();

        var existingPhoto = group.Photo;

        await using var input = file.OpenReadStream();
        var result = await imageStorage.UploadProcessedImageAsync(
            input,
            file.ContentType,
            $"groups/{groupId}/avatar",
            new ImageResizeRequest(512, false),
            cancellationToken);

        group.Photo = result.ObjectKey;
        group.LastActivityTime = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await imageStorage.DeleteIfOwnedAsync(existingPhoto, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    ///     Delete a group.
    /// </summary>
    [HttpDelete("{groupId}", Name = "DeleteGroup")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteGroup(Guid groupId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var group = await db.Groups.Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.GroupId == groupId, cancellationToken);
        if (group is null)
            return NotFound();
        if (!group.Members.Contains(user))
            return Unauthorized();

        var groupPhoto = group.Photo;
        db.Groups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);

        await imageStorage.DeleteIfOwnedAsync(groupPhoto, cancellationToken);

        return NoContent();
    }


    /// <summary>
    ///     Invite friends to a group. Each invited user gets a request notification and joins only after accepting.
    ///     Users who are not friends of the inviter, are already members, or already have a pending invite are skipped.
    /// </summary>
    /// <param name="groupId">group id</param>
    /// <param name="userIds">list of user ids</param>
    /// <returns>The invites that were created.</returns>
    [HttpPost("{groupId}/invites", Name = "InviteGroupMembers")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<List<GroupInviteDto>>> InviteGroupMembers(Guid groupId, List<string> userIds)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var group = await db.Groups.Include(g => g.Members).FirstOrDefaultAsync(g => g.GroupId == groupId);
        if (group is null)
            return NotFound();
        if (!group.Members.Contains(user))
            return Unauthorized();

        var invites = await CreateInvitesAsync(group, user, userIds);
        await db.SaveChangesAsync();
        return Ok(mapper.Map<List<GroupInviteDto>>(invites));
    }

    /// <summary>
    ///     Pending invites for a group, so members can see who has not answered yet.
    /// </summary>
    [HttpGet("{groupId}/invites", Name = "GetGroupInvites")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<List<GroupInviteDto>>> GetGroupInvites(Guid groupId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var isMember = await db.Groups.AnyAsync(g => g.GroupId == groupId && g.Members.Contains(user));
        if (!isMember)
            return NotFound();

        var invites = await db.GroupInvites
            .Include(i => i.Group)
            .Include(i => i.InvitedUser)
            .Include(i => i.InvitedByUser)
            .Where(i => i.GroupId == groupId && i.Status == RequestStatus.Pending)
            .OrderByDescending(i => i.CreateTime)
            .ToListAsync();
        return mapper.Map<List<GroupInviteDto>>(invites);
    }

    /// <summary>
    ///     Pending invites addressed to the current user.
    /// </summary>
    [HttpGet("invites", Name = "GetMyGroupInvites")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<List<GroupInviteDto>>> GetMyGroupInvites()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var invites = await db.GroupInvites
            .Include(i => i.Group)
            .Include(i => i.InvitedUser)
            .Include(i => i.InvitedByUser)
            .Where(i => i.InvitedUserId == user.Id && i.Status == RequestStatus.Pending)
            .OrderByDescending(i => i.CreateTime)
            .ToListAsync();
        return mapper.Map<List<GroupInviteDto>>(invites);
    }

    /// <summary>
    ///     Accept a group invite and join the group.
    /// </summary>
    [HttpPost("invite/{inviteId}/accept", Name = "AcceptGroupInvite")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<GroupDto>> AcceptGroupInvite(Guid inviteId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var invite = await db.GroupInvites
            .Include(i => i.Group).ThenInclude(g => g.Members)
            .Include(i => i.Group).ThenInclude(g => g.Balances)
            .FirstOrDefaultAsync(i => i.GroupInviteId == inviteId && i.InvitedUserId == user.Id &&
                                      i.Status == RequestStatus.Pending);
        if (invite is null)
            return NotFound();

        invite.Status = RequestStatus.Accepted;
        invite.RespondTime = DateTime.UtcNow;

        if (!invite.Group.Members.Contains(user))
        {
            invite.Group.Members.Add(user);
            invite.Group.UpdateMembersIdHash();
            invite.Group.LastActivityTime = DateTime.UtcNow;
        }

        await notifications.MarkNotificationsAsReadAsync(user.Id, NotificationTypes.GroupInvite, inviteId.ToString());
        await db.SaveChangesAsync();
        return mapper.Map<GroupDto>(invite.Group);
    }

    /// <summary>
    ///     Ignore a group invite. The inviter is not told.
    /// </summary>
    [HttpPost("invite/{inviteId}/ignore", Name = "IgnoreGroupInvite")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> IgnoreGroupInvite(Guid inviteId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var invite = await db.GroupInvites
            .FirstOrDefaultAsync(i => i.GroupInviteId == inviteId && i.InvitedUserId == user.Id &&
                                      i.Status == RequestStatus.Pending);
        if (invite is null)
            return NotFound();

        invite.Status = RequestStatus.Ignored;
        invite.RespondTime = DateTime.UtcNow;
        await notifications.MarkNotificationsAsReadAsync(user.Id, NotificationTypes.GroupInvite, inviteId.ToString());
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    ///     Create pending invites for the given users and queue their notifications. Does not save.
    ///     Only friends of the inviter can be invited; existing members and duplicate invites are skipped.
    /// </summary>
    private async Task<List<GroupInvite>> CreateInvitesAsync(Group group, SplitzUser inviter,
        IEnumerable<string> userIds)
    {
        var memberIds = group.Members.Select(m => m.Id).ToHashSet();
        var candidateIds = userIds.Distinct().Where(id => !memberIds.Contains(id)).ToList();
        if (candidateIds.Count == 0)
            return [];

        // Friendship can be recorded in either direction (legacy one-way rows included).
        var friendIds = await db.Set<Friend>()
            .Where(f => (f.UserId == inviter.Id && candidateIds.Contains(f.FriendUserId)) ||
                        (f.FriendUserId == inviter.Id && candidateIds.Contains(f.UserId)))
            .Select(f => f.UserId == inviter.Id ? f.FriendUserId : f.UserId)
            .Distinct()
            .ToListAsync();

        var alreadyInvited = await db.GroupInvites
            .Where(i => i.GroupId == group.GroupId && i.Status == RequestStatus.Pending &&
                        friendIds.Contains(i.InvitedUserId))
            .Select(i => i.InvitedUserId)
            .ToListAsync();

        var toInvite = await db.Users
            .Where(u => friendIds.Contains(u.Id) && !alreadyInvited.Contains(u.Id))
            .ToListAsync();

        var invites = new List<GroupInvite>();
        foreach (var invitee in toInvite)
        {
            var invite = new GroupInvite
            {
                GroupInviteId = Guid.NewGuid(),
                GroupId = group.GroupId,
                Group = group,
                InvitedUserId = invitee.Id,
                InvitedUser = invitee,
                InvitedByUserId = inviter.Id,
                InvitedByUser = inviter,
                Status = RequestStatus.Pending,
                CreateTime = DateTime.UtcNow
            };
            db.GroupInvites.Add(invite);
            invites.Add(invite);

            await notifications.NotifyAsync([invitee.Id], NotificationTypes.GroupInvite,
                new GroupInviteNotification
                {
                    InviteId = invite.GroupInviteId,
                    GroupId = group.GroupId,
                    InvitedByUserId = inviter.Id
                },
                group.GroupId,
                invite.GroupInviteId.ToString());
        }

        return invites;
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
        groupJoinLink.Group.LastActivityTime = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return mapper.Map<GroupDto>(groupJoinLink.Group);
    }
}