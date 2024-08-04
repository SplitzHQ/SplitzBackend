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

    public decimal? Amount { get; set; }

    public string? Currency { get; set; }

    public List<Tag> Tags { get; set; } = new();

    [MaxLength(128)] public string? GeoCoordinate { get; set; }

    [Url] [MaxLength(256)] public string? Photo { get; set; }
}

public class TransactionDraft : TransactionDraftBase
{
    public required SplitzUser User { get; set; } = null!;

    public required Group? Group { get; set; } = null!;

    public List<TransactionDraftBalance> Balances { get; set; } = new();
}

public class TransactionDraftDto : TransactionDraftBase
{
    public List<TransactionDraftBalanceDto> Balances { get; set; } = new();
}

public class TransactionDraftInputDto : TransactionBase
{
    public List<TransactionDraftBalanceInputDto> Balances { get; set; } = new();
}