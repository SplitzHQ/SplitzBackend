using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

[PrimaryKey(nameof(TransactionDraftId), nameof(UserId))]
public class TransactionDraftBalance
{
    public Guid TransactionDraftId { get; set; }

    public TransactionDraft TransactionDraft { get; set; } = null!;

    public required string UserId { get; set; }

    public SplitzUser User { get; set; } = null!;

    public decimal? Balance { get; set; }
}

public class TransactionDraftBalanceDto
{
    public Guid TransactionDraftId { get; set; }

    public required SplitzUserReducedDto User { get; set; }

    public decimal? Balance { get; set; }
}