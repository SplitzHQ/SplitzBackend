using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;
using SplitzBackend.Services;

namespace SplitzBackend.Controllers;

[Authorize]
[ApiController]
[Route("[controller]")]
public class NotificationController(
    SplitzDbContext context,
    UserManager<SplitzUser> userManager,
    INotificationService notificationService,
    IMapper mapper) : ControllerBase
{
    /// <summary>
    ///     List notifications for the current user, newest first. Returns undismissed notifications by default.
    /// </summary>
    /// <param name="category">Only return notifications in this category.</param>
    /// <param name="includeDismissed">If true, includes dismissed notifications.</param>
    /// <param name="limit">Maximum number of notifications to return (1-200).</param>
    [HttpGet(Name = "GetNotifications")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<List<NotificationDto>>> GetNotifications(
        [FromQuery] NotificationCategory? category = null,
        [FromQuery] bool includeDismissed = false,
        [FromQuery] int limit = 100)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var query = context.Notifications.Where(n => n.UserId == user.Id);

        if (category is not null)
            query = query.Where(n => n.Category == category);

        if (!includeDismissed)
            query = query.Where(n => !n.IsDismissed);

        var notifications = await query
            .OrderByDescending(n => n.CreateTime)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync();

        var dtos = mapper.Map<List<NotificationDto>>(notifications);
        await notificationService.LoadUserAndGroupReferencesAsync(user.Id,
            dtos.Select(d => d.Data).OfType<NotificationData>().ToList());
        return dtos;
    }

    /// <summary>
    ///     Unread and pending counts, for badges on the bell and the avatar.
    /// </summary>
    [HttpGet("summary", Name = "GetNotificationSummary")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<NotificationSummaryDto>> GetSummary()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var counts = await context.Notifications
            .Where(n => n.UserId == user.Id && !n.IsDismissed)
            .GroupBy(n => n.Category)
            .Select(g => new
            {
                Category = g.Key,
                Unread = g.Count(n => !n.IsRead),
                Total = g.Count()
            })
            .ToListAsync();

        var requests = counts.FirstOrDefault(c => c.Category == NotificationCategory.Request);
        var activity = counts.FirstOrDefault(c => c.Category == NotificationCategory.Activity);

        return new NotificationSummaryDto
        {
            UnreadRequests = requests?.Unread ?? 0,
            PendingRequests = requests?.Total ?? 0,
            UnreadActivity = activity?.Unread ?? 0
        };
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
    ///     Mark every undismissed notification as read, optionally only within one category.
    /// </summary>
    [HttpPost("read-all", Name = "MarkAllNotificationsRead")]
    [ProducesResponseType(401)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> MarkAllRead([FromQuery] NotificationCategory? category = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var query = context.Notifications.Where(n => n.UserId == user.Id && !n.IsDismissed && !n.IsRead);
        if (category is not null)
            query = query.Where(n => n.Category == category);

        await query.ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

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

        notification.IsRead = true;
        notification.IsDismissed = true;
        await context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    ///     Dismiss all activity notifications for the current user. Requests stay until they are accepted or ignored.
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
            .Where(n => n.UserId == user.Id && !n.IsDismissed && n.Category == NotificationCategory.Activity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsDismissed, true)
                .SetProperty(n => n.IsRead, true));

        return NoContent();
    }

    // --- Preferences ---

    /// <summary>
    ///     All notification types with their category, priority and whether they can be turned off.
    /// </summary>
    [HttpGet("types", Name = "GetNotificationTypes")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public ActionResult<List<NotificationTypeDto>> GetTypes()
    {
        return mapper.Map<List<NotificationTypeDto>>(NotificationCatalog.All);
    }

    /// <summary>
    ///     The current user's notification preferences. Only explicit settings are returned;
    ///     a type without a row is enabled.
    /// </summary>
    /// <param name="groupId">When set, returns only the overrides for that group.</param>
    [HttpGet("preferences", Name = "GetNotificationPreferences")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<List<NotificationPreferenceDto>>> GetPreferences([FromQuery] Guid? groupId = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var query = context.NotificationPreferences.Where(p => p.UserId == user.Id);
        if (groupId is not null)
            query = query.Where(p => p.GroupId == groupId);

        var preferences = await query.ToListAsync();
        return mapper.Map<List<NotificationPreferenceDto>>(preferences);
    }

    /// <summary>
    ///     Create or update one preference. Pass a group ID to override the global setting for that group.
    /// </summary>
    [HttpPut("preferences", Name = "SetNotificationPreference")]
    [Produces("application/json")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<NotificationPreferenceDto>> SetPreference(NotificationPreferenceInputDto input)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        if (!NotificationCatalog.TryGet(input.Type, out var definition))
            return BadRequest("Unknown notification type");
        if (!definition.Configurable)
            return BadRequest("This notification type cannot be turned off");

        if (input.GroupId is not null)
        {
            var isMember = await context.Groups
                .AnyAsync(g => g.GroupId == input.GroupId && g.Members.Contains(user));
            if (!isMember)
                return NotFound();
        }

        var preference = await context.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == user.Id && p.Type == input.Type && p.GroupId == input.GroupId);

        if (preference is null)
        {
            preference = new NotificationPreference
            {
                NotificationPreferenceId = Guid.NewGuid(),
                UserId = user.Id,
                Type = input.Type,
                GroupId = input.GroupId,
                Enabled = input.Enabled
            };
            context.NotificationPreferences.Add(preference);
        }
        else
        {
            preference.Enabled = input.Enabled;
        }

        await context.SaveChangesAsync();
        return mapper.Map<NotificationPreferenceDto>(preference);
    }

    /// <summary>
    ///     Remove a preference so the type falls back to its default (or to the global setting for a group override).
    /// </summary>
    /// <param name="type">Notification type.</param>
    /// <param name="groupId">Group override to remove, or null for the global setting.</param>
    [HttpDelete("preferences", Name = "ResetNotificationPreference")]
    [ProducesResponseType(401)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ResetPreference([FromQuery] string type, [FromQuery] Guid? groupId = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        await context.NotificationPreferences
            .Where(p => p.UserId == user.Id && p.Type == type && p.GroupId == groupId)
            .ExecuteDeleteAsync();

        return NoContent();
    }

    /// <summary>
    ///     Remove every override for one group so it follows the global settings again.
    /// </summary>
    [HttpDelete("preferences/group/{groupId}", Name = "ResetGroupNotificationPreferences")]
    [ProducesResponseType(401)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ResetGroupPreferences(Guid groupId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        await context.NotificationPreferences
            .Where(p => p.UserId == user.Id && p.GroupId == groupId)
            .ExecuteDeleteAsync();

        return NoContent();
    }
}