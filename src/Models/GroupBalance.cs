using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

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

    [Required]
    [MinLength(3, ErrorMessage = "Currency code must be 3 characters")]
    [MaxLength(3)]
    [RegularExpression(@"^[A-Z]{3}$", ErrorMessage = "Currency must be a valid 3-letter ISO code (e.g., USD, EUR)")]
    public required string Currency { get; set; }
}

public class GroupBalanceDto
{
    public required Guid GroupId { get; set; }

    public required SplitzUserReducedDto User { get; set; }

    public required SplitzUserReducedDto FriendUser { get; set; }

    public required decimal Balance { get; set; }

    [Required]
    [MinLength(3, ErrorMessage = "Currency code must be 3 characters")]
    [MaxLength(3)]
    [RegularExpression(@"^[A-Z]{3}$", ErrorMessage = "Currency must be a valid 3-letter ISO code (e.g., USD, EUR)")]
    public required string Currency { get; set; }
}