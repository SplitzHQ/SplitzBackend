using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

/// <summary>
///     Where a notification is surfaced in the client.
///     Requests need a user decision (accept / ignore) and are shown on the avatar.
///     Activity is informational and is shown behind the bell button.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationCategory
{
    Request = 0,
    Activity = 1
}

/// <summary>
///     How urgent a notification is. Clients can sort or style by this value.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

/// <summary>
///     String identifiers for every notification type. Stored in <see cref="Notification.Type" />.
/// </summary>
public static class NotificationTypes
{
    // Requests
    public const string FriendRequest = "FriendRequest";
    public const string GroupInvite = "GroupInvite";

    // Activity
    public const string FriendRequestAccepted = "FriendRequestAccepted";
    public const string TransactionCreated = "TransactionCreated";
    public const string RelatedTransactionCreated = "RelatedTransactionCreated";
    public const string InvoiceCreated = "InvoiceCreated";
    public const string RelatedInvoiceCreated = "RelatedInvoiceCreated";
    public const string SettlementRecorded = "SettlementRecorded";
    public const string RelatedSettlementRecorded = "RelatedSettlementRecorded";
    public const string InvoiceSettled = "InvoiceSettled";
    public const string RelatedInvoiceSettled = "RelatedInvoiceSettled";
}

/// <summary>
///     Static metadata about one notification type.
/// </summary>
/// <param name="Type">Type identifier, see <see cref="NotificationTypes" />.</param>
/// <param name="Category">Where the client shows the notification.</param>
/// <param name="Priority">Default priority for new notifications of this type.</param>
/// <param name="Configurable">Whether the user can turn this type off in preferences.</param>
/// <param name="DataType">CLR type of the JSON payload in <see cref="Notification.Data" />.</param>
public sealed record NotificationTypeDefinition(
    string Type,
    NotificationCategory Category,
    NotificationPriority Priority,
    bool Configurable,
    Type DataType);

/// <summary>
///     Registry of all notification types. Requests are never configurable because they are the only way
///     for a user to act on a pending friend request or group invite.
/// </summary>
public static class NotificationCatalog
{
    private static readonly NotificationTypeDefinition[] Definitions =
    [
        new(NotificationTypes.FriendRequest, NotificationCategory.Request, NotificationPriority.High, false,
            typeof(FriendRequestNotification)),
        new(NotificationTypes.GroupInvite, NotificationCategory.Request, NotificationPriority.High, false,
            typeof(GroupInviteNotification)),

        new(NotificationTypes.FriendRequestAccepted, NotificationCategory.Activity, NotificationPriority.Normal, true,
            typeof(FriendRequestAcceptedNotification)),
        new(NotificationTypes.TransactionCreated, NotificationCategory.Activity, NotificationPriority.Low, true,
            typeof(TransactionCreatedNotification)),
        new(NotificationTypes.RelatedTransactionCreated, NotificationCategory.Activity, NotificationPriority.High,
            true, typeof(TransactionCreatedNotification)),
        new(NotificationTypes.InvoiceCreated, NotificationCategory.Activity, NotificationPriority.Low, true,
            typeof(InvoiceCreatedNotification)),
        new(NotificationTypes.RelatedInvoiceCreated, NotificationCategory.Activity, NotificationPriority.High, true,
            typeof(InvoiceCreatedNotification)),
        new(NotificationTypes.SettlementRecorded, NotificationCategory.Activity, NotificationPriority.Low, true,
            typeof(SettlementRecordedNotification)),
        new(NotificationTypes.RelatedSettlementRecorded, NotificationCategory.Activity, NotificationPriority.High,
            true, typeof(SettlementRecordedNotification)),
        new(NotificationTypes.InvoiceSettled, NotificationCategory.Activity, NotificationPriority.Low, true,
            typeof(InvoiceSettledNotification)),
        new(NotificationTypes.RelatedInvoiceSettled, NotificationCategory.Activity, NotificationPriority.High, true,
            typeof(InvoiceSettledNotification))
    ];

    public static IReadOnlyList<NotificationTypeDefinition> All { get; } = Definitions;

    private static readonly Dictionary<string, NotificationTypeDefinition> ByType =
        Definitions.ToDictionary(d => d.Type, StringComparer.Ordinal);

    public static bool TryGet(string type, out NotificationTypeDefinition definition)
    {
        return ByType.TryGetValue(type, out definition!);
    }

    public static NotificationTypeDefinition Get(string type)
    {
        return TryGet(type, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown notification type");
    }
}

// --- Notification payloads ---

/// <summary>
///     Marks a payload property that is loaded from the database each time the notification is read.
///     The property is not written to <see cref="Notification.Data" />, so a renamed user or group shows its
///     current name and photo.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ResolvedOnReadAttribute : Attribute;

/// <summary>
///     Users and groups that notification payloads refer to, with their display data for one reader.
///     Payloads register IDs with <see cref="AddUser" /> and <see cref="AddGroup" />. The notification service
///     loads the data in one batch, and then payloads read it with the lookup methods.
/// </summary>
public sealed class NotificationReferences
{
    private readonly Dictionary<string, UserReference?> users = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, GroupReference?> groups = new();

    public IReadOnlyCollection<string> UserIds => users.Keys;

    public IReadOnlyCollection<Guid> GroupIds => groups.Keys;

    public void AddUser(string? userId)
    {
        if (!string.IsNullOrEmpty(userId))
            users.TryAdd(userId, null);
    }

    public void AddGroup(Guid? groupId)
    {
        if (groupId is { } id)
            groups.TryAdd(id, null);
    }

    /// <param name="userId">User to describe.</param>
    /// <param name="displayName">The reader's nickname for the user, or the user name when there is no nickname.</param>
    /// <param name="photo">Signed photo URL.</param>
    public void SetUser(string userId, string displayName, string? photo)
    {
        users[userId] = new UserReference(displayName, photo);
    }

    public void SetGroup(Guid groupId, string name, string? photo)
    {
        groups[groupId] = new GroupReference(name, photo);
    }

    public string? UserName(string? userId)
    {
        return FindUser(userId)?.DisplayName;
    }

    public string? UserPhoto(string? userId)
    {
        return FindUser(userId)?.Photo;
    }

    public string? GroupName(Guid? groupId)
    {
        return FindGroup(groupId)?.Name;
    }

    public string? GroupPhoto(Guid? groupId)
    {
        return FindGroup(groupId)?.Photo;
    }

    private UserReference? FindUser(string? userId)
    {
        return userId is not null && users.TryGetValue(userId, out var user) ? user : null;
    }

    private GroupReference? FindGroup(Guid? groupId)
    {
        return groupId is { } id && groups.TryGetValue(id, out var group) ? group : null;
    }

    private sealed record UserReference(string DisplayName, string? Photo);

    private sealed record GroupReference(string Name, string? Photo);
}

/// <summary>
///     Base class for notification payloads. A payload stores only IDs for users and groups.
///     Names and photos are filled in when the notification is read, see <see cref="ResolvedOnReadAttribute" />.
/// </summary>
public abstract class NotificationData
{
    /// <summary>
    ///     Register the users and groups that this payload refers to.
    /// </summary>
    public abstract void AddReferences(NotificationReferences references);

    /// <summary>
    ///     Set the <see cref="ResolvedOnReadAttribute" /> properties from the loaded references.
    /// </summary>
    public abstract void ApplyReferences(NotificationReferences references);
}

public class FriendRequestNotification : NotificationData
{
    public required Guid RequestId { get; set; }
    public required string FromUserId { get; set; }
    [ResolvedOnRead] public string? FromUserName { get; set; }
    [ResolvedOnRead] public string? FromUserPhoto { get; set; }
    public string? Remark { get; set; }

    public override void AddReferences(NotificationReferences references)
    {
        references.AddUser(FromUserId);
    }

    public override void ApplyReferences(NotificationReferences references)
    {
        FromUserName = references.UserName(FromUserId);
        FromUserPhoto = references.UserPhoto(FromUserId);
    }
}

public class FriendRequestAcceptedNotification : NotificationData
{
    public required string FriendUserId { get; set; }
    [ResolvedOnRead] public string? FriendUserName { get; set; }
    [ResolvedOnRead] public string? FriendUserPhoto { get; set; }

    public override void AddReferences(NotificationReferences references)
    {
        references.AddUser(FriendUserId);
    }

    public override void ApplyReferences(NotificationReferences references)
    {
        FriendUserName = references.UserName(FriendUserId);
        FriendUserPhoto = references.UserPhoto(FriendUserId);
    }
}

public class GroupInviteNotification : NotificationData
{
    public required Guid InviteId { get; set; }
    public required Guid GroupId { get; set; }
    [ResolvedOnRead] public string? GroupName { get; set; }
    [ResolvedOnRead] public string? GroupPhoto { get; set; }
    public required string InvitedByUserId { get; set; }
    [ResolvedOnRead] public string? InvitedByUserName { get; set; }

    public override void AddReferences(NotificationReferences references)
    {
        references.AddGroup(GroupId);
        references.AddUser(InvitedByUserId);
    }

    public override void ApplyReferences(NotificationReferences references)
    {
        GroupName = references.GroupName(GroupId);
        GroupPhoto = references.GroupPhoto(GroupId);
        InvitedByUserName = references.UserName(InvitedByUserId);
    }
}

/// <summary>
///     Payload for TransactionCreated and RelatedTransactionCreated.
///     <see cref="UserBalance" /> is the recipient's share: negative means they owe, positive means they are owed.
/// </summary>
public class TransactionCreatedNotification : NotificationData
{
    public required Guid TransactionId { get; set; }
    public required Guid GroupId { get; set; }
    [ResolvedOnRead] public string? GroupName { get; set; }
    public required string CreatorUserId { get; set; }
    [ResolvedOnRead] public string? CreatorName { get; set; }
    public required string TransactionName { get; set; }
    public required decimal Amount { get; set; }
    public required string Currency { get; set; }
    public decimal? UserBalance { get; set; }

    public override void AddReferences(NotificationReferences references)
    {
        references.AddGroup(GroupId);
        references.AddUser(CreatorUserId);
    }

    public override void ApplyReferences(NotificationReferences references)
    {
        GroupName = references.GroupName(GroupId);
        CreatorName = references.UserName(CreatorUserId);
    }
}

/// <summary>
///     Payload for InvoiceCreated and RelatedInvoiceCreated.
/// </summary>
public class InvoiceCreatedNotification : NotificationData
{
    public required string CreatorUserId { get; set; }
    [ResolvedOnRead] public string? CreatorName { get; set; }
    public string? InvoiceName { get; set; }
    public required Guid InvoiceId { get; set; }
    public required Guid GroupId { get; set; }
    [ResolvedOnRead] public string? GroupName { get; set; }

    public override void AddReferences(NotificationReferences references)
    {
        references.AddGroup(GroupId);
        references.AddUser(CreatorUserId);
    }

    public override void ApplyReferences(NotificationReferences references)
    {
        GroupName = references.GroupName(GroupId);
        CreatorName = references.UserName(CreatorUserId);
    }
}

/// <summary>
///     Payload for SettlementRecorded and RelatedSettlementRecorded.
/// </summary>
public class SettlementRecordedNotification : NotificationData
{
    public required string RecorderUserId { get; set; }
    [ResolvedOnRead] public string? RecorderName { get; set; }
    public required decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string FromUserId { get; set; }
    [ResolvedOnRead] public string? FromUserName { get; set; }
    public required string ToUserId { get; set; }
    [ResolvedOnRead] public string? ToUserName { get; set; }
    public required Guid InvoiceId { get; set; }
    public string? InvoiceName { get; set; }
    public Guid? GroupId { get; set; }
    [ResolvedOnRead] public string? GroupName { get; set; }

    public override void AddReferences(NotificationReferences references)
    {
        references.AddGroup(GroupId);
        references.AddUser(RecorderUserId);
        references.AddUser(FromUserId);
        references.AddUser(ToUserId);
    }

    public override void ApplyReferences(NotificationReferences references)
    {
        GroupName = references.GroupName(GroupId);
        RecorderName = references.UserName(RecorderUserId);
        FromUserName = references.UserName(FromUserId);
        ToUserName = references.UserName(ToUserId);
    }
}

/// <summary>
///     Payload for InvoiceSettled and RelatedInvoiceSettled.
/// </summary>
public class InvoiceSettledNotification : NotificationData
{
    public required Guid InvoiceId { get; set; }
    public string? InvoiceName { get; set; }
    public required Guid GroupId { get; set; }
    [ResolvedOnRead] public string? GroupName { get; set; }

    public override void AddReferences(NotificationReferences references)
    {
        references.AddGroup(GroupId);
    }

    public override void ApplyReferences(NotificationReferences references)
    {
        GroupName = references.GroupName(GroupId);
    }
}

// --- Notification entity ---

[Index(nameof(UserId), nameof(IsDismissed), nameof(CreateTime))]
public class Notification
{
    public Guid NotificationId { get; set; }

    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public SplitzUser User { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public required string Type { get; set; }

    public required NotificationCategory Category { get; set; }

    public required NotificationPriority Priority { get; set; }

    /// <summary>
    ///     Group the notification belongs to, when any. Used for group-scoped preferences and filtering.
    /// </summary>
    public Guid? GroupId { get; set; }

    [MaxLength(256)] public string? ReferenceId { get; set; }

    /// <summary>
    ///     JSON-serialized payload. The client uses Type + Data to build localized messages.
    /// </summary>
    [Required]
    [MaxLength(2048)]
    public required string Data { get; set; }

    /// <summary>
    ///     Options for reading and writing <see cref="Data" />. Unlike the API options, they skip
    ///     <see cref="ResolvedOnReadAttribute" /> properties, so names and photos are never stored.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipResolvedOnReadProperties } }
    };

    private static void SkipResolvedOnReadProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
            if (typeInfo.Properties[i].AttributeProvider?.IsDefined(typeof(ResolvedOnReadAttribute), true) == true)
                typeInfo.Properties.RemoveAt(i);
    }

    /// <summary>
    ///     Deserialize <see cref="Data" /> into the payload type registered for <see cref="Type" />.
    /// </summary>
    public NotificationData? GetTypedData()
    {
        if (!NotificationCatalog.TryGet(Type, out var definition))
            return null;
        return JsonSerializer.Deserialize(Data, definition.DataType, JsonOptions) as NotificationData;
    }

    public required bool IsRead { get; set; } = false;

    public required bool IsDismissed { get; set; } = false;

    public required DateTime CreateTime { get; set; }
}

// --- DTOs ---

public class NotificationDto
{
    public required Guid NotificationId { get; set; }

    [Required]
    [MaxLength(50)]
    public required string Type { get; set; }

    public required NotificationCategory Category { get; set; }

    public required NotificationPriority Priority { get; set; }

    public Guid? GroupId { get; set; }

    [MaxLength(256)] public string? ReferenceId { get; set; }

    /// <summary>
    ///     Deserialized payload. The concrete shape depends on Type.
    /// </summary>
    [Required]
    public required object Data { get; set; }

    public required bool IsRead { get; set; }

    public required bool IsDismissed { get; set; }

    public required DateTime CreateTime { get; set; }
}

/// <summary>
///     Unread counts per category, for badges.
/// </summary>
public class NotificationSummaryDto
{
    public required int UnreadRequests { get; set; }
    public required int UnreadActivity { get; set; }
    public required int PendingRequests { get; set; }
}

/// <summary>
///     Describes one notification type so the client can render a settings list.
/// </summary>
public class NotificationTypeDto
{
    public required string Type { get; set; }
    public required NotificationCategory Category { get; set; }
    public required NotificationPriority Priority { get; set; }
    public required bool Configurable { get; set; }
}