using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SplitzBackend.Models;

namespace SplitzBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class AccountController(ILogger<AccountController> logger, SplitzDbContext db, UserManager<SplitzUser> userManager) : ControllerBase
    {
        [HttpGet(Name = "GetUserInfo")]
        public async Task<ActionResult<SplitzUserDto>> Get()
        {
            var user = await userManager.GetUserAsync(User);
            if (user is null)
                return NotFound();
            return Ok(new SplitzUserDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Photo = user.Photo,
                Friends = user.Friends.Select(f => new SplitzUserDto
                {
                    Id = f.Id,
                    UserName = f.UserName,
                    Email = f.Email,
                    Photo = f.Photo
                }).ToList()
            });
        }

        [HttpPost(Name = "UpdateUserInfo")]
        public async Task<ActionResult> Update(SplitzUserUpdateViewModel dto)
        {
            var user = await userManager.GetUserAsync(User);
            if (user is null)
                return NotFound();
            if(dto.UserName is not null)
                user.UserName = dto.UserName;
            if(dto.Photo is not null)
                user.Photo = dto.Photo;
            await db.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("friend/{id}", Name = "AddFriend")]
        public async Task<ActionResult> AddFriend(string id)
        {
            var user = await userManager.GetUserAsync(User);
            if (user is null)
                return NotFound();
            var friend = await userManager.FindByIdAsync(id);
            if (friend is null)
                return NotFound();
            user.Friends.Add(friend);
            await db.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("friend/{id}", Name = "RemoveFriend")]
        public async Task<ActionResult> RemoveFriend(string id)
        {
            var user = await userManager.GetUserAsync(User);
            if (user is null)
                return NotFound();
            var friend = await userManager.FindByIdAsync(id);
            if (friend is null)
                return NotFound();
            user.Friends.Remove(friend);
            await db.SaveChangesAsync();
            return Ok();
        }
    }

    public class SplitzUserUpdateViewModel
    {
        public string? UserName { get; set; }
        public string? Photo { get; set; }
    }
}
