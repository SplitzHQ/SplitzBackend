using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

[PrimaryKey(nameof(UserId), nameof(FriendUserId))]
public class Friend
{
    public required string UserId { get; set; }

    public SplitzUser User { get; set; } = null!;

    public required string FriendUserId { get; set; }

    public SplitzUser FriendUser { get; set; } = null!;

    /// <summary>
    /// A nickname about the friendship.
    /// </summary>
    [MaxLength(256)] public string? Remark { get; set; }
}

public class FriendDto
{
    public required SplitzUserReducedDto FriendUser { get; set; }

    /// <summary>
    /// A nickname about the friendship.
    /// </summary>
    [MaxLength(256)] public string? Remark { get; set; }
}