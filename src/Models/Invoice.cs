using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

public enum InvoiceStatus
{
    Open = 0,
    Settled = 1
}

public class Invoice
{
    public Guid InvoiceId { get; set; }

    public required Guid GroupId { get; set; }

    public Group Group { get; set; } = null!;

    [MaxLength(256)] public string? Name { get; set; }

    [Required]
    [MinLength(3, ErrorMessage = "Currency code must be exactly 3 characters")]
    [MaxLength(3, ErrorMessage = "Currency code cannot exceed 3 characters")]
    [RegularExpression(@"^[A-Z]{3}$", ErrorMessage = "Currency must be a valid 3-letter ISO code (e.g., USD, EUR)")]
    public required string Currency { get; set; }

    public required string CreatedByUserId { get; set; }

    public SplitzUser CreatedBy { get; set; } = null!;

    public required DateTime CreateTime { get; set; }

    public required InvoiceStatus Status { get; set; } = InvoiceStatus.Open;

    public List<Transaction> Transactions { get; set; } = new();

    public List<InvoiceDebt> Debts { get; set; } = new();

    public List<InvoiceSettlement> Settlements { get; set; } = new();
}

[PrimaryKey(nameof(InvoiceId), nameof(FromUserId), nameof(ToUserId))]
public class InvoiceDebt
{
    public required Guid InvoiceId { get; set; }

    public Invoice Invoice { get; set; } = null!;

    public required string FromUserId { get; set; }

    public SplitzUser FromUser { get; set; } = null!;

    public required string ToUserId { get; set; }

    public SplitzUser ToUser { get; set; } = null!;

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public required decimal Amount { get; set; }
}

public class InvoiceSettlement
{
    public Guid InvoiceSettlementId { get; set; }

    public required Guid InvoiceId { get; set; }

    public Invoice Invoice { get; set; } = null!;

    public required string FromUserId { get; set; }

    public SplitzUser FromUser { get; set; } = null!;

    public required string ToUserId { get; set; }

    public SplitzUser ToUser { get; set; } = null!;

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public required decimal Amount { get; set; }

    public required string RecordedByUserId { get; set; }

    public SplitzUser RecordedBy { get; set; } = null!;

    public required DateTime RecordedTime { get; set; }
}

// --- DTOs ---

public class InvoiceDto
{
    public required Guid InvoiceId { get; set; }

    public required Guid GroupId { get; set; }

    [MaxLength(256)] public string? Name { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(3)]
    [RegularExpression(@"^[A-Z]{3}$")]
    public required string Currency { get; set; }

    public required SplitzUserReducedDto CreatedBy { get; set; }

    public required DateTime CreateTime { get; set; }

    public required InvoiceStatus Status { get; set; }

    public List<TransactionDto> Transactions { get; set; } = new();

    public List<InvoiceDebtDto> Debts { get; set; } = new();

    public List<InvoiceSettlementDto> Settlements { get; set; } = new();
}

public class InvoiceReducedDto
{
    public required Guid InvoiceId { get; set; }

    public required Guid GroupId { get; set; }

    [MaxLength(256)] public string? Name { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(3)]
    [RegularExpression(@"^[A-Z]{3}$")]
    public required string Currency { get; set; }

    public required SplitzUserReducedDto CreatedBy { get; set; }

    public required DateTime CreateTime { get; set; }

    public required InvoiceStatus Status { get; set; }
}

public class InvoiceInputDto
{
    public required Guid GroupId { get; set; }

    [MaxLength(256)] public string? Name { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(3)]
    [RegularExpression(@"^[A-Z]{3}$")]
    public required string Currency { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "At least one transaction is required")]
    public required List<Guid> TransactionIds { get; set; }
}

public class InvoiceDebtDto
{
    public required Guid InvoiceId { get; set; }

    public required SplitzUserReducedDto FromUser { get; set; }

    public required SplitzUserReducedDto ToUser { get; set; }

    public required decimal Amount { get; set; }
}

public class InvoiceSettlementDto
{
    public required Guid InvoiceSettlementId { get; set; }

    public required Guid InvoiceId { get; set; }

    public required SplitzUserReducedDto FromUser { get; set; }

    public required SplitzUserReducedDto ToUser { get; set; }

    public required decimal Amount { get; set; }

    public required SplitzUserReducedDto RecordedBy { get; set; }

    public required DateTime RecordedTime { get; set; }
}

public class InvoiceSettlementInputDto
{
    [Required] public required string FromUserId { get; set; }

    [Required] public required string ToUserId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public required decimal Amount { get; set; }
}