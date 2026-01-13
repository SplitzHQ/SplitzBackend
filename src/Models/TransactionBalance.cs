using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

public class TransactionBalanceBase
{
    public Guid TransactionId { get; set; }

    public required string UserId { get; set; }

    public decimal Balance { get; set; }
}

[PrimaryKey(nameof(TransactionId), nameof(UserId))]
public class TransactionBalance : TransactionBalanceBase
{
    public Transaction Transaction { get; set; } = null!;

    public SplitzUser User { get; set; } = null!;
}

public class TransactionBalanceDto : TransactionBalanceBase
{
    public required SplitzUserReducedDto User { get; set; }
}

public class TransactionBalanceInputDto : TransactionBalanceBase
{
}