using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

public class GroupJoinLink
{
    public Guid GroupJoinLinkId { get; set; } 

    public required Guid GroupId { get; set; } 

    public required Group Group { get; set; }
}