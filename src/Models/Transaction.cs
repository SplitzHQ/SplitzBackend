using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

public class TransactionBase
{
    public Guid TransactionId { get; set; }

    public required Guid GroupId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Name cannot be empty")]
    [MaxLength(256, ErrorMessage = "Name cannot exceed 256 characters")]
    public required string Name { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Icon cannot be empty")]
    [MaxLength(50, ErrorMessage = "Icon cannot exceed 50 characters")]
    public required string Icon { get; set; }

    public required DateTime CreateTime { get; set; }

    public required DateTime TransactionTime { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public required decimal Amount { get; set; }

    [Required]
    [MinLength(3, ErrorMessage = "Currency code must be exactly 3 characters")]
    [MaxLength(3, ErrorMessage = "Currency code cannot exceed 3 characters")]
    [RegularExpression(@"^[A-Z]{3}$", ErrorMessage = "Currency must be a valid 3-letter ISO code (e.g., USD, EUR)")]
    public required string Currency { get; set; }

    public required List<Tag> Tags { get; set; } = new();

    [MaxLength(128)] public string? GeoCoordinate { get; set; }
}

public class Transaction : TransactionBase
{
    public required Group Group { get; set; } = null!;

    [MaxLength(256)] public string? Photo { get; set; }

    public List<TransactionBalance> Balances { get; set; } = new();
}

public class TransactionDto : TransactionBase
{
    [MaxLength(256)] public string? Photo { get; set; }
    public List<TransactionBalanceDto> Balances { get; set; } = new();
}

public class TransactionInputDto : TransactionBase
{
    public List<TransactionBalanceInputDto> Balances { get; set; } = new();
}