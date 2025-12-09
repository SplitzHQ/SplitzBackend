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
public class AccountController(
    SplitzDbContext db,
    UserManager<SplitzUser> userManager,
    IMapper mapper) : ControllerBase
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
        if (userDto.Photo is not null)
            user.Photo = userDto.Photo;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    ///     Add a friend to the current user
    /// </summary>
    /// <param name="id">friend's id</param>
    /// <param name="remark">friend's remark</param>
    /// <returns></returns>
    [HttpPost("friend/{id}", Name = "AddFriend")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult> AddFriend(string id, [FromBody] string? remark)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        var friend = await userManager.FindByIdAsync(id);
        if (friend is null)
            return NotFound();
        if (user.Friends.Any(f => f.FriendUserId == friend.Id && f.UserId == user.Id))
            return NoContent();
        user.Friends.Add(new Friend
        {
            UserId = user.Id,
            FriendUserId = friend.Id,
            Remark = remark
        });
        await db.SaveChangesAsync();
        return NoContent();
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
        var friend = await userManager.FindByIdAsync(id);
        if (friend is null)
            return NotFound();
        var friendShip = user.Friends.FirstOrDefault(f => f.FriendUserId == friend.Id && f.UserId == user.Id);
        if (friendShip is null)
            return NotFound();

        friendShip.Remark = remark;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    ///     Remove a friend from the current user
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
        var friend = await userManager.FindByIdAsync(id);
        if (friend is null)
            return NotFound();
        var friendShip = user.Friends.FirstOrDefault(f => f.FriendUserId == friend.Id && f.UserId == user.Id);
        if (friendShip is null)
            return NotFound();

        user.Friends.Remove(friendShip);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public class SplitzUserUpdateViewModel
{
    public string? UserName { get; set; }
    public string? Photo { get; set; }
}