using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

/// <summary>
///     An invitation for a user to join a group. The user becomes a member only after accepting.
/// </summary>
[Index(nameof(InvitedUserId), nameof(Status))]
[Index(nameof(GroupId), nameof(Status))]
public class GroupInvite
{
    public Guid GroupInviteId { get; set; }

    public required Guid GroupId { get; set; }

    [ForeignKey(nameof(GroupId))]
    public Group Group { get; set; } = null!;

    public required string InvitedUserId { get; set; }

    [ForeignKey(nameof(InvitedUserId))]
    public SplitzUser InvitedUser { get; set; } = null!;

    public required string InvitedByUserId { get; set; }

    [ForeignKey(nameof(InvitedByUserId))]
    public SplitzUser InvitedByUser { get; set; } = null!;

    public required RequestStatus Status { get; set; } = RequestStatus.Pending;

    public required DateTime CreateTime { get; set; }

    public DateTime? RespondTime { get; set; }
}

public class GroupInviteDto
{
    public required Guid GroupInviteId { get; set; }

    public required GroupReducedDto Group { get; set; }

    public required SplitzUserReducedDto InvitedUser { get; set; }

    public required SplitzUserReducedDto InvitedByUser { get; set; }

    public required RequestStatus Status { get; set; }

    public required DateTime CreateTime { get; set; }
}