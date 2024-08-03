using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

[PrimaryKey(nameof(TransactionId), nameof(UserId))]
public class TransactionBalance
{
    public Guid TransactionId { get; set; }

    public Transaction Transaction { get; set; } = null!;

    public required string UserId { get; set; }

    public SplitzUser User { get; set; } = null!;

    public decimal Balance { get; set; }
}

public class TransactionBalanceDto
{
    public Guid TransactionId { get; set; }

    public required SplitzUserReducedDto User { get; set; }

    public decimal Balance { get; set; }
}