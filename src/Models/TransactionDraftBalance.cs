using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

public class TransactionDraftBalanceBase
{
    public Guid TransactionDraftId { get; set; }

    public required string UserId { get; set; }

    public decimal Balance { get; set; }
}

[PrimaryKey(nameof(TransactionDraftId), nameof(UserId))]
public class TransactionDraftBalance : TransactionDraftBalanceBase
{
    public TransactionDraft TransactionDraft { get; set; } = null!;

    public SplitzUser User { get; set; } = null!;
}

public class TransactionDraftBalanceDto : TransactionDraftBalanceBase
{
    public required SplitzUserReducedDto User { get; set; }
}

public class TransactionDraftBalanceInputDto : TransactionDraftBalanceBase
{
}