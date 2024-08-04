using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

public class TransactionBase
{
    public Guid TransactionId { get; set; }

    public required Guid GroupId { get; set; }

    public required string Name { get; set; }

    public required string Icon { get; set; }

    public required DateTime CreateTime { get; set; }

    public required DateTime TransactionTime { get; set; }

    public required decimal Amount { get; set; }

    public required string Currency { get; set; }

    public required List<Tag> Tags { get; set; } = new();

    [MaxLength(128)] public string? GeoCoordinate { get; set; }

    [Url] [MaxLength(256)] public string? Photo { get; set; }
}

public class Transaction : TransactionBase
{
    public required Group Group { get; set; } = null!;

    public List<TransactionBalance> Balances { get; set; } = new();
}

public class TransactionDto : TransactionBase
{
    public List<TransactionBalanceDto> Balances { get; set; } = new();
}

public class TransactionInputDto : TransactionBase
{
    public List<TransactionBalanceInputDto> Balances { get; set; } = new();
}