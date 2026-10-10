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
public class AccountController(
    SplitzDbContext db,
    UserManager<SplitzUser> userManager,
    IMapper mapper,
    IImageStorageService imageStorage,
    INotificationService notifications) : ControllerBase
{
    /// <summary>
    ///     Get the current user's information
    /// </summary>
    /// <returns></returns>
    [HttpGet(Name = "GetUserInfo")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<SplitzUserDto>> Get()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        user = await db.Users
            .Include(u => u.Friends)
            .ThenInclude(f => f.FriendUser)
            .Include(u => u.Groups)
            .Include(u => u.Balances)
            .FirstAsync(u => u.Id == user.Id);
        return mapper.Map<SplitzUserDto>(user);
    }

    /// <summary>
    ///     Update the current user's username and photo
    /// </summary>
    /// <param name="userDto">user info</param>
    /// <returns></returns>
    [HttpPatch(Name = "UpdateUserInfo")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    public async Task<ActionResult> Update(SplitzUserUpdateViewModel userDto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (userDto.UserName is not null)
            user.UserName = userDto.UserName;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    ///     Send a friend request. The other user becomes a friend only after they accept.
    ///     If they already sent a request to the current user, both requests are accepted at once.
    /// </summary>
    /// <param name="input">Recipient's friendId and optional nickname.</param>
    [HttpPost("friend/request", Name = "SendFriendRequest")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<FriendRequestDto>> SendFriendRequest([FromBody] SendFriendRequestRequest input)
    {
        var friendId = input.FriendId;
        var remark = input.Remark;
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (friendId == user.Id)
            return BadRequest("You cannot add yourself as a friend");
        var friend = await userManager.FindByIdAsync(friendId);
        if (friend is null)
            return NotFound();

        var alreadyFriends = await db.Set<Friend>()
            .AnyAsync(f => f.UserId == user.Id && f.FriendUserId == friend.Id);
        if (alreadyFriends)
            return BadRequest("Already friends");

        var existing = await db.FriendRequests
            .Include(r => r.FromUser)
            .Include(r => r.ToUser)
            .Where(r => r.Status == RequestStatus.Pending &&
                        ((r.FromUserId == user.Id && r.ToUserId == friend.Id) ||
                         (r.FromUserId == friend.Id && r.ToUserId == user.Id)))
            .FirstOrDefaultAsync();

        // The other side already asked: treat this as accepting their request.
        if (existing is not null && existing.FromUserId == friend.Id)
        {
            await AcceptFriendRequestCore(existing, user, remark);
            await db.SaveChangesAsync();
            return mapper.Map<FriendRequestDto>(existing);
        }

        if (existing is not null)
            return mapper.Map<FriendRequestDto>(existing);

        var request = new FriendRequest
        {
            FriendRequestId = Guid.NewGuid(),
            FromUserId = user.Id,
            FromUser = user,
            ToUserId = friend.Id,
            ToUser = friend,
            Remark = remark,
            Status = RequestStatus.Pending,
            CreateTime = DateTime.UtcNow
        };
        db.FriendRequests.Add(request);

        await notifications.NotifyAsync([friend.Id], NotificationTypes.FriendRequest,
            new FriendRequestNotification
            {
                RequestId = request.FriendRequestId,
                FromUserId = user.Id,
            },
            referenceId: request.FriendRequestId.ToString());

        await db.SaveChangesAsync();
        return mapper.Map<FriendRequestDto>(request);
    }

    /// <summary>
    ///     Pending friend requests sent to and by the current user.
    /// </summary>
    [HttpGet("friend/request", Name = "GetFriendRequests")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<FriendRequestListDto>> GetFriendRequests()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var requests = await db.FriendRequests
            .Include(r => r.FromUser)
            .Include(r => r.ToUser)
            .Where(r => r.Status == RequestStatus.Pending && (r.FromUserId == user.Id || r.ToUserId == user.Id))
            .OrderByDescending(r => r.CreateTime)
            .ToListAsync();

        return new FriendRequestListDto
        {
            Incoming = mapper.Map<List<FriendRequestDto>>(requests.Where(r => r.ToUserId == user.Id)),
            Outgoing = mapper.Map<List<FriendRequestDto>>(requests.Where(r => r.FromUserId == user.Id))
        };
    }

    /// <summary>
    ///     Accept a friend request. Creates the friendship in both directions and tells the sender.
    /// </summary>
    /// <param name="requestId">friend request id</param>
    /// <param name="remark">nickname the current user wants for the new friend</param>
    [HttpPost("friend/request/{requestId}/accept", Name = "AcceptFriendRequest")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult> AcceptFriendRequest(Guid requestId, [FromBody] string? remark)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var request = await db.FriendRequests
            .Include(r => r.FromUser)
            .FirstOrDefaultAsync(r => r.FriendRequestId == requestId && r.ToUserId == user.Id &&
                                      r.Status == RequestStatus.Pending);
        if (request is null)
            return NotFound();

        await AcceptFriendRequestCore(request, user, remark);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    ///     Ignore a friend request. The sender is not told.
    /// </summary>
    [HttpPost("friend/request/{requestId}/ignore", Name = "IgnoreFriendRequest")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult> IgnoreFriendRequest(Guid requestId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var request = await db.FriendRequests
            .FirstOrDefaultAsync(r => r.FriendRequestId == requestId && r.ToUserId == user.Id &&
                                      r.Status == RequestStatus.Pending);
        if (request is null)
            return NotFound();

        request.Status = RequestStatus.Ignored;
        request.RespondTime = DateTime.UtcNow;
        await notifications.MarkNotificationsAsReadAsync(user.Id, NotificationTypes.FriendRequest, requestId.ToString());
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    ///     Cancel a friend request the current user sent.
    /// </summary>
    [HttpDelete("friend/request/{requestId}", Name = "CancelFriendRequest")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult> CancelFriendRequest(Guid requestId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var request = await db.FriendRequests
            .FirstOrDefaultAsync(r => r.FriendRequestId == requestId && r.FromUserId == user.Id &&
                                      r.Status == RequestStatus.Pending);
        if (request is null)
            return NotFound();

        db.FriendRequests.Remove(request);
        await notifications.MarkNotificationsAsReadAsync(request.ToUserId, NotificationTypes.FriendRequest,
            requestId.ToString());
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    ///     Shared accept logic: mark the request, create both Friend rows, clear the request notification,
    ///     and notify the sender. Does not save.
    /// </summary>
    private async Task AcceptFriendRequestCore(FriendRequest request, SplitzUser accepter, string? accepterRemark)
    {
        request.Status = RequestStatus.Accepted;
        request.RespondTime = DateTime.UtcNow;

        var pairs = new[]
        {
            (UserId: request.FromUserId, FriendId: request.ToUserId, Remark: request.Remark),
            (UserId: request.ToUserId, FriendId: request.FromUserId, Remark: accepterRemark)
        };
        var existingRows = await db.Set<Friend>()
            .Where(f => (f.UserId == request.FromUserId && f.FriendUserId == request.ToUserId) ||
                        (f.UserId == request.ToUserId && f.FriendUserId == request.FromUserId))
            .ToListAsync();

        foreach (var (userId, friendId, remark) in pairs)
        {
            if (existingRows.Any(f => f.UserId == userId && f.FriendUserId == friendId))
                continue;
            db.Set<Friend>().Add(new Friend { UserId = userId, FriendUserId = friendId, Remark = remark });
        }

        await notifications.MarkNotificationsAsReadAsync(accepter.Id, NotificationTypes.FriendRequest,
            request.FriendRequestId.ToString());

        await notifications.NotifyAsync([request.FromUserId], NotificationTypes.FriendRequestAccepted,
            new FriendRequestAcceptedNotification
            {
                FriendUserId = accepter.Id
            },
            referenceId: request.FriendRequestId.ToString());
    }

    /// <summary>
    ///     Update the remark of a friend
    /// </summary>
    /// <param name="id">friend's id</param>
    /// <param name="remark">friend's new remark</param>
    /// <returns></returns>
    [HttpPatch("friend/{id}", Name = "UpdateFriendRemark")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult> UpdateFriend(string id, string? remark)
    {
        if (remark == null)
            return BadRequest();
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var friendShip = await db.Set<Friend>()
            .FirstOrDefaultAsync(f => f.UserId == user.Id && f.FriendUserId == id);
        if (friendShip is null)
            return NotFound();

        friendShip.Remark = remark;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    ///     Remove a friend. Friendship is mutual, so both directions are removed.
    /// </summary>
    /// <param name="id">friend's id</param>
    /// <returns></returns>
    [HttpDelete("friend/{id}", Name = "RemoveFriend")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult> RemoveFriend(string id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var rows = await db.Set<Friend>()
            .Where(f => (f.UserId == user.Id && f.FriendUserId == id) ||
                        (f.UserId == id && f.FriendUserId == user.Id))
            .ToListAsync();
        if (rows.All(f => f.UserId != user.Id))
            return NotFound();

        db.Set<Friend>().RemoveRange(rows);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    ///     Upload the current user's avatar image.
    /// </summary>
    [HttpPost("avatar", Name = "UploadUserAvatar")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    [ProducesResponseType(200)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [UploadRateLimitEndpointMetadata]
    [EnableRateLimiting(RateLimitPolicyNames.UploadPerUser)]
    public async Task<ActionResult<UploadImageResult>> UploadAvatar(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (file.Length <= 0)
            return BadRequest("Empty file");

        var existingPhoto = user.Photo;

        await using var input = file.OpenReadStream();
        var result = await imageStorage.UploadProcessedImageAsync(
            input,
            file.ContentType,
            $"users/{user.Id}/avatar",
            new ImageResizeRequest(512, false),
            cancellationToken);

        user.Photo = result.ObjectKey;
        await db.SaveChangesAsync(cancellationToken);
        await imageStorage.DeleteIfOwnedAsync(existingPhoto, cancellationToken);
        return Ok(result);
    }
}

public class SplitzUserUpdateViewModel
{
    public string? UserName { get; set; }
}