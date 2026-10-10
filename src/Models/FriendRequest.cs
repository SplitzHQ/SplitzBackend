using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

/// <summary>
///     Lifecycle of a friend request or group invite.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequestStatus
{
    Pending = 0,
    Accepted = 1,
    Ignored = 2
}

/// <summary>
///     A pending friendship. Accepting creates <see cref="Friend" /> rows in both directions.
/// </summary>
[Index(nameof(ToUserId), nameof(Status))]
[Index(nameof(FromUserId), nameof(Status))]
public class FriendRequest
{
    public Guid FriendRequestId { get; set; }

    public required string FromUserId { get; set; }

    [ForeignKey(nameof(FromUserId))]
    public SplitzUser FromUser { get; set; } = null!;

    public required string ToUserId { get; set; }

    [ForeignKey(nameof(ToUserId))]
    public SplitzUser ToUser { get; set; } = null!;

    /// <summary>
    ///     Nickname the sender wants to use for the recipient once they are friends.
    /// </summary>
    [MaxLength(256)]
    public string? Remark { get; set; }

    public required RequestStatus Status { get; set; } = RequestStatus.Pending;

    public required DateTime CreateTime { get; set; }

    public DateTime? RespondTime { get; set; }
}

/// <summary>
///     Input for sending a friend request.
/// </summary>
public sealed record SendFriendRequestRequest
{
    /// <summary>
    ///     ID of the user who receives the request.
    /// </summary>
    [Required]
    public required string FriendId { get; init; }

    /// <summary>
    ///     Nickname to use for the friend after they accept.
    /// </summary>
    public string? Remark { get; init; }
}

public class FriendRequestDto
{
    public required Guid FriendRequestId { get; set; }

    public required SplitzUserReducedDto FromUser { get; set; }

    public required SplitzUserReducedDto ToUser { get; set; }

    [MaxLength(256)] public string? Remark { get; set; }

    public required RequestStatus Status { get; set; }

    public required DateTime CreateTime { get; set; }
}

/// <summary>
///     Pending friend requests from the current user's point of view.
/// </summary>
public class FriendRequestListDto
{
    public required List<FriendRequestDto> Incoming { get; set; }

    public required List<FriendRequestDto> Outgoing { get; set; }
}