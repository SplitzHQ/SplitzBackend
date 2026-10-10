using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

/// <summary>
///     A user's choice to receive or mute one notification type.
///     A row with <see cref="GroupId" /> = null is the global setting; a row with a group overrides it for that group.
///     When no row exists the type is enabled.
/// </summary>
[Index(nameof(UserId), nameof(Type), nameof(GroupId), IsUnique = true)]
public class NotificationPreference
{
    public Guid NotificationPreferenceId { get; set; }

    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public SplitzUser User { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public required string Type { get; set; }

    public Guid? GroupId { get; set; }

    [ForeignKey(nameof(GroupId))]
    public Group? Group { get; set; }

    public required bool Enabled { get; set; }
}

public class NotificationPreferenceDto
{
    [Required]
    [MaxLength(50)]
    public required string Type { get; set; }

    public Guid? GroupId { get; set; }

    public required bool Enabled { get; set; }
}

public class NotificationPreferenceInputDto
{
    [Required]
    [MaxLength(50)]
    public required string Type { get; set; }

    /// <summary>
    ///     Null sets the global preference; a group ID sets an override for that group.
    /// </summary>
    public Guid? GroupId { get; set; }

    public required bool Enabled { get; set; }
}