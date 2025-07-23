using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

public class Tag
{
    public Guid TagId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Tag name cannot be empty")]
    [MaxLength(256)]
    public required string Name { get; set; }

    [MaxLength(256)] public string? Icon { get; set; }
}