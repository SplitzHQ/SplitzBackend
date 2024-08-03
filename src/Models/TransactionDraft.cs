using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

public class TransactionDraft
{
    public Guid TransactionDraftId { get; set; }

    public Guid UserId { get; set; }

    public SplitzUser User { get; set; } = null!;

    public Guid? GroupId { get; set; }

    public Group? Group { get; set; }

    [MaxLength(256)] public required string? Name { get; set; }

    [MaxLength(256)] public string? Icon { get; set; }

    public DateTime? CreateTime { get; set; } = DateTime.Now;

    public DateTime? TransactionTime { get; set; }

    public decimal? Amount { get; set; }

    [MaxLength(16)] public string? Currency { get; set; }

    public List<Tag> Tags { get; set; } = new();

    [MaxLength(128)] public string? GeoCoordinate { get; set; }

    [Url] [MaxLength(256)] public string? Photo { get; set; }

    public List<TransactionDraftBalance> Balances { get; set; } = new();
}

public class TransactionDraftDto
{
    public required Guid TransactionDraftId { get; set; }

    public required Guid UserId { get; set; }

    [MaxLength(256)] public required string? Name { get; set; }

    [MaxLength(256)] public required string? Icon { get; set; }

    public required DateTime? CreateTime { get; set; }

    public required DateTime? TransactionTime { get; set; }

    public required decimal? Amount { get; set; }

    [MaxLength(16)] public required string? Currency { get; set; }

    public required List<TagDto> Tags { get; set; }

    [MaxLength(128)] public string? GeoCoordinate { get; set; }

    [Url] [MaxLength(256)] public string? Photo { get; set; }

    public List<TransactionDraftBalanceDto> Balances { get; set; } = new();
}