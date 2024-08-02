using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

public class Transaction
{
    public Guid TransactionId { get; set; }

    public Guid GroupId { get; set; }

    public Group Group { get; set; } = null!;

    [MaxLength(256)]
    public required string Name { get; set; }

    [MaxLength(256)]
    public required string Icon { get; set; }

    public required DateTime CreateTime { get; set; } = DateTime.Now;

    public required DateTime TransactionTime { get; set; }

    public required decimal Amount { get; set; }

    [MaxLength(16)]
    public required string Currency { get; set; }

    public required List<Tag> Tags { get; set; } = new();

    [MaxLength(128)]
    public string? GeoCoordinate { get; set; }

    [Url]
    [MaxLength(256)]
    public string? Photo { get; set; }

    public List<TransactionBalance> Balances { get; set; } = new();
}

public class TransactionDto
{
    public required Guid TransactionId { get; set; }

    public required Guid GroupId { get; set; }

    public required string Name { get; set; }

    public required string Icon { get; set; }

    public required DateTime CreateTime { get; set; }

    public required DateTime TransactionTime { get; set; }

    public required decimal Amount { get; set; }

    public required string Currency { get; set; }

    public required List<TagDto> Tags { get; set; }

    public string? GeoCoordinate { get; set; }

    public string? Photo { get; set; }

    public List<TransactionBalanceDto> Balances { get; set; } = new();
}