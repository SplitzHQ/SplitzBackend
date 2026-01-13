using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

public class TransactionDraftBase
{
    public Guid TransactionDraftId { get; set; }

    public required string UserId { get; set; }

    public Guid? GroupId { get; set; }

    public string? Name { get; set; }

    public string? Icon { get; set; }

    public DateTime? CreateTime { get; set; }

    public DateTime? TransactionTime { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal? Amount { get; set; }

    [MinLength(3, ErrorMessage = "Currency code must be 3 characters")]
    [MaxLength(3, ErrorMessage = "Currency code cannot exceed 3 characters")]
    [RegularExpression(@"^[A-Z]{3}$", ErrorMessage = "Currency must be a valid 3-letter ISO code (e.g., USD, EUR)")]
    public string? Currency { get; set; }

    public List<Tag> Tags { get; set; } = new();

    [MaxLength(128)] public string? GeoCoordinate { get; set; }
}

public class TransactionDraft : TransactionDraftBase
{
    public required SplitzUser User { get; set; } = null!;

    public required Group? Group { get; set; } = null!;

    [MaxLength(256)] public string? Photo { get; set; }

    public List<TransactionDraftBalance> Balances { get; set; } = new();
}

public class TransactionDraftDto : TransactionDraftBase
{
    [MaxLength(256)] public string? Photo { get; set; }

    public List<TransactionDraftBalanceDto> Balances { get; set; } = new();
}

public class TransactionDraftInputDto : TransactionDraftBase
{
    public List<TransactionDraftBalanceInputDto> Balances { get; set; } = new();
}