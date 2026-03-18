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
public class NotificationController(
    SplitzDbContext context,
    UserManager<SplitzUser> userManager,
    IMapper mapper) : ControllerBase
{
    /// <summary>
    ///     List notifications for the current user. Returns undismissed notifications by default.
    /// </summary>
    /// <param name="includeDismissed">If true, includes dismissed notifications</param>
    [HttpGet(Name = "GetNotifications")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<List<NotificationDto>>> GetNotifications(
        [FromQuery] bool includeDismissed = false)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var query = context.Notifications.Where(n => n.UserId == user.Id);

        if (!includeDismissed)
            query = query.Where(n => !n.IsDismissed);

        var notifications = await query.OrderByDescending(n => n.CreateTime).ToListAsync();

        return mapper.Map<List<NotificationDto>>(notifications);
    }

    /// <summary>
    ///     Mark a notification as read
    /// </summary>
    [HttpPatch("{id}/read", Name = "MarkNotificationRead")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var notification = await context.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == id && n.UserId == user.Id);

        if (notification is null)
            return NotFound();

        notification.IsRead = true;
        await context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    ///     Dismiss a notification
    /// </summary>
    [HttpPatch("{id}/dismiss", Name = "DismissNotification")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Dismiss(Guid id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var notification = await context.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == id && n.UserId == user.Id);

        if (notification is null)
            return NotFound();

        notification.IsDismissed = true;
        await context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    ///     Dismiss all notifications for the current user
    /// </summary>
    [HttpPost("dismiss-all", Name = "DismissAllNotifications")]
    [ProducesResponseType(401)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DismissAll()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        await context.Notifications
            .Where(n => n.UserId == user.Id && !n.IsDismissed)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDismissed, true));

        return NoContent();
    }
}