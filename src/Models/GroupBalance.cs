using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

[PrimaryKey(nameof(GroupId), nameof(UserId), nameof(FriendUserId))]
public class GroupBalance
{
    public required Guid GroupId { get; set; }

    public Group Group { get; set; } = null!;

    public required string UserId { get; set; }

    public SplitzUser User { get; set; } = null!;

    public required string FriendUserId { get; set; }

    public SplitzUser FriendUser { get; set; } = null!;

    public required decimal Balance { get; set; }

    [MaxLength(16)]
    public required string Currency { get; set; }
}

public class GroupBalanceDto
{
    public required Guid GroupId { get; set; }

    public required SplitzUserReducedDto User { get; set; }

    public required SplitzUserReducedDto FriendUser { get; set; }

    public required decimal Balance { get; set; }

    [MaxLength(16)]
    public required string Currency { get; set; }
}