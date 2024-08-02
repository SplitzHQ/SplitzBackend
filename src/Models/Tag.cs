using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

public class Tag
{
    public Guid TagId { get; set; }

    public required string UserId { get; set; }

    public SplitzUser User { get; set; } = null!;

    [MaxLength(256)]
    public required string Name { get; set; }

    [MaxLength(256)]
    public string? Icon { get; set; }
}

public class TagDto
{
    public Guid TagId { get; set; }

    [MaxLength(256)]
    public required string Name { get; set; }

    [MaxLength(256)]
    public string? Icon { get; set; }
}