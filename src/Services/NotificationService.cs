using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;

namespace SplitzBackend.Services;

/// <summary>
///     Creates notifications while respecting user preferences.
///     Methods add rows to the <see cref="SplitzDbContext" /> but do not save; the caller owns the unit of work.
/// </summary>
public interface INotificationService
{
    /// <summary>
    ///     Queue one notification per recipient. Recipients who muted the type (globally or for the group) are skipped.
    /// </summary>
    /// <param name="recipientIds">Users to notify. Duplicates are ignored.</param>
    /// <param name="type">A value from <see cref="NotificationTypes" />.</param>
    /// <param name="data">Payload matching the type's registered data class.</param>
    /// <param name="groupId">Group the event happened in, used for group-scoped preferences.</param>
    /// <param name="referenceId">Identifier of the related entity (request, invoice, transaction ...).</param>
    /// <param name="cancellationToken">Cancels the preference lookup.</param>
    Task NotifyAsync(
        IEnumerable<string> recipientIds,
        string type,
        NotificationData data,
        Guid? groupId = null,
        string? referenceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Mark request notifications for one entity as read and dismissed.
    /// </summary>
    Task MarkNotificationsAsReadAsync(string userId, string type, string referenceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Decide whether a user can receive a notification type, honoring group overrides over the global setting.
    /// </summary>
    Task<bool> CheckUserNotificationPreferenceAsync(string userId, string type, Guid? groupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fill in the current user names, group names and photos of payloads that were read from the database.
    ///     A user name will be replaced by the nickname for that user when one is set.
    /// </summary>
    /// <param name="readerId">User who reads the notifications. Their friend nicknames are used.</param>
    /// <param name="payloads">Payloads to update in place.</param>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    Task LoadUserAndGroupReferencesAsync(string readerId, IReadOnlyCollection<NotificationData> payloads,
        CancellationToken cancellationToken = default);
}

public sealed class NotificationService(SplitzDbContext context, IObjectStorage objectStorage)
    : INotificationService
{
    public async Task NotifyAsync(
        IEnumerable<string> recipientIds,
        string type,
        NotificationData data,
        Guid? groupId = null,
        string? referenceId = null,
        CancellationToken cancellationToken = default)
    {
        var definition = NotificationCatalog.Get(type);
        var recipients = recipientIds.Distinct().ToList();
        if (recipients.Count == 0)
            return;

        var eligibleNotificationRecipients = await FilterUserIdsByNotificationPreferencesAsync(recipients, definition, groupId, cancellationToken);
        if (eligibleNotificationRecipients.Count == 0)
            return;

        var json = JsonSerializer.Serialize(data, definition.DataType, Notification.JsonOptions);
        var now = DateTime.UtcNow;

        foreach (var userId in eligibleNotificationRecipients)
            context.Notifications.Add(new Notification
            {
                NotificationId = Guid.NewGuid(),
                UserId = userId,
                Type = definition.Type,
                Category = definition.Category,
                Priority = definition.Priority,
                GroupId = groupId,
                ReferenceId = referenceId,
                Data = json,
                IsRead = false,
                IsDismissed = false,
                CreateTime = now
            });
    }

    public async Task MarkNotificationsAsReadAsync(string userId, string type, string referenceId,
        CancellationToken cancellationToken = default)
    {
        var notifications = await context.Notifications
            .Where(n => n.UserId == userId && n.Type == type && n.ReferenceId == referenceId && !n.IsDismissed)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.IsDismissed = true;
        }
    }

    public async Task<bool> CheckUserNotificationPreferenceAsync(string userId, string type, Guid? groupId,
        CancellationToken cancellationToken = default)
    {
        var definition = NotificationCatalog.Get(type);
        var enabled = await FilterUserIdsByNotificationPreferencesAsync([userId], definition, groupId, cancellationToken);
        return enabled.Count == 1;
    }

    public async Task LoadUserAndGroupReferencesAsync(string readerId, IReadOnlyCollection<NotificationData> payloads,
        CancellationToken cancellationToken = default)
    {
        var references = new NotificationReferences();
        foreach (var payload in payloads)
            payload.AddReferences(references);

        var userIds = references.UserIds.ToList();
        var groupIds = references.GroupIds.ToList();

        if (userIds.Count > 0)
        {
            var users = await context.Users
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.UserName, u.Photo })
                .ToListAsync(cancellationToken);

            // Only the reader's own Friend rows hold the nicknames that the reader chose.
            var nicknames = await context.Set<Friend>()
                .Where(f => f.UserId == readerId && userIds.Contains(f.FriendUserId) && f.Remark != null &&
                            f.Remark != "")
                .ToDictionaryAsync(f => f.FriendUserId, f => f.Remark!, cancellationToken);

            foreach (var user in users)
                references.SetUser(user.Id,
                    nicknames.GetValueOrDefault(user.Id) ?? user.UserName ?? string.Empty,
                    ObjectStorageUrlBuilder.GenerateUrlFromKey(objectStorage, user.Photo));
        }

        if (groupIds.Count > 0)
        {
            var groups = await context.Groups
                .Where(g => groupIds.Contains(g.GroupId))
                .Select(g => new { g.GroupId, g.Name, g.Photo })
                .ToListAsync(cancellationToken);

            foreach (var group in groups)
                references.SetGroup(group.GroupId, group.Name,
                    ObjectStorageUrlBuilder.GenerateUrlFromKey(objectStorage, group.Photo));
        }

        foreach (var payload in payloads)
            payload.ApplyReferences(references);
    }

    /// <summary>
    ///     Filters the given user IDs based on their notification preferences for the specified type and group.
    /// </summary>
    private async Task<List<string>> FilterUserIdsByNotificationPreferencesAsync(
        List<string> userIds,
        NotificationTypeDefinition definition,
        Guid? groupId,
        CancellationToken cancellationToken)
    {
        if (!definition.Configurable)
            return userIds;

        var preferences = await context.NotificationPreferences
            .Where(p => p.Type == definition.Type && userIds.Contains(p.UserId) &&
                        (p.GroupId == null || p.GroupId == groupId))
            .Select(p => new { p.UserId, p.GroupId, p.Enabled })
            .ToListAsync(cancellationToken);

        if (preferences.Count == 0)
            return userIds;

        var byUser = preferences.ToLookup(p => p.UserId);
        return userIds.Where(userId =>
        {
            var userPreferences = byUser[userId].ToList();
            var groupOverride = groupId is null ? null : userPreferences.FirstOrDefault(p => p.GroupId == groupId);
            if (groupOverride is not null)
                return groupOverride.Enabled;
            var global = userPreferences.FirstOrDefault(p => p.GroupId == null);
            return global?.Enabled ?? true;
        }).ToList();
    }
}