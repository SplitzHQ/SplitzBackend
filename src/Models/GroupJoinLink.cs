namespace SplitzBackend.Models;

public class GroupJoinLink
{
    public Guid GroupJoinLinkId { get; set; }

    public required Guid GroupId { get; set; }

    public Group Group { get; set; } = null!;
}

public class GroupJoinLinkDto
{
    public required Guid GroupJoinLinkId { get; set; }

    public required Guid GroupId { get; set; }
}