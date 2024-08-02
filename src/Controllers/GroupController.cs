using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;

namespace SplitzBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class GroupController(ILogger<GroupController> logger, SplitzDbContext db, UserManager<SplitzUser> userManager) : ControllerBase
    {
        /// <summary>
        /// Get the current user's groups
        /// </summary>
        /// <returns></returns>
        [HttpGet(Name = "GetGroup")]
        [Produces("application/json")]
        public async Task<ActionResult<Group>> Get()
        {
            var user = await userManager.GetUserAsync(User);
            if (user is null)
                return Unauthorized();
            user = await db.Users.Include(t => t.Groups).FirstAsync(u => u.Id == user.Id);
            return Ok(user.Groups);
        }

        /// <summary>
        /// Create a new group
        /// </summary>
        /// <param name="group">group info</param>
        /// <returns></returns>
        [HttpPost(Name = "CreateGroup")]
        [Produces("application/json")]
        public async Task<ActionResult<Group>> Create(Group group)
        {
            var user = await userManager.GetUserAsync(User);
            if (user is null)
                return Unauthorized();
            group.Members = [user];
            group.Transactions = [];
            group.Balances = [];
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            return Ok(group);
        }

        /// <summary>
        /// create a join link for a group
        /// </summary>
        /// <param name="groupId">group id</param>
        /// <returns></returns>
        [HttpPost("{groupId}/join-link", Name = "CreateGroupJoinLink")]
        [Produces("application/json")]
        public async Task<ActionResult<GroupJoinLink>> CreateGroupJoinLink(string groupId)
        {
            var user = await userManager.GetUserAsync(User);
            if (user is null)
                return Unauthorized();
            return NotFound();
        }
    }
}
