using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

[PrimaryKey(nameof(UserId), nameof(FriendUserId))]
public class Friend
{
    public required string UserId { get; set; }

    public SplitzUser User { get; set; } = null!;

    public required string FriendUserId { get; set; }

    public SplitzUser FriendUser { get; set; } = null!;

    [MaxLength(256)] public string? Remark { get; set; }
}

public class FriendDto
{
    public required SplitzUserReducedDto FriendUser { get; set; }

    [MaxLength(256)] public string? Remark { get; set; }
}