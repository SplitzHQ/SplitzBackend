using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InvoiceStatus
{
    Open = 0,
    Settled = 1
}

public class Invoice
{
    public Guid InvoiceId { get; set; }

    public required Guid GroupId { get; set; }

    [ForeignKey(nameof(GroupId))]
    public Group Group { get; set; } = null!;

    [MaxLength(256)] public string? Name { get; set; }

    [Required]
    [MinLength(3, ErrorMessage = "Currency code must be exactly 3 characters")]
    [MaxLength(3, ErrorMessage = "Currency code cannot exceed 3 characters")]
    [RegularExpression(@"^[A-Z]{3}$", ErrorMessage = "Currency must be a valid 3-letter ISO code (e.g., USD, EUR)")]
    public required string Currency { get; set; }

    public required string CreatedByUserId { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public SplitzUser CreatedBy { get; set; } = null!;

    public required DateTime CreateTime { get; set; }

    public required InvoiceStatus Status { get; set; } = InvoiceStatus.Open;

    /// <summary>
    ///     True when a member archived the invoice. Only a settled invoice can be archived.
    ///     An archived invoice and its expenses are read-only and hidden from lists by default.
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>The time the invoice was archived. Null when the invoice is not archived.</summary>
    public DateTime? ArchiveTime { get; set; }

    public List<Transaction> Transactions { get; set; } = new();

    public List<InvoiceDebt> Debts { get; set; } = new();

    public List<InvoiceSettlement> Settlements { get; set; } = new();
}

[PrimaryKey(nameof(InvoiceId), nameof(FromUserId), nameof(ToUserId))]
public class InvoiceDebt
{
    public required Guid InvoiceId { get; set; }

    [ForeignKey(nameof(InvoiceId))]
    public Invoice Invoice { get; set; } = null!;

    public required string FromUserId { get; set; }

    [ForeignKey(nameof(FromUserId))]
    public SplitzUser FromUser { get; set; } = null!;

    public required string ToUserId { get; set; }

    [ForeignKey(nameof(ToUserId))]
    public SplitzUser ToUser { get; set; } = null!;

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public required decimal Amount { get; set; }
}

public class InvoiceSettlement
{
    public Guid InvoiceSettlementId { get; set; }

    public required Guid InvoiceId { get; set; }

    [ForeignKey(nameof(InvoiceId))]
    public Invoice Invoice { get; set; } = null!;

    public required string FromUserId { get; set; }

    [ForeignKey(nameof(FromUserId))]
    public SplitzUser FromUser { get; set; } = null!;

    public required string ToUserId { get; set; }

    [ForeignKey(nameof(ToUserId))]
    public SplitzUser ToUser { get; set; } = null!;

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public required decimal Amount { get; set; }

    public required string RecordedByUserId { get; set; }

    [ForeignKey(nameof(RecordedByUserId))]
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

    public required bool IsArchived { get; set; }

    public DateTime? ArchiveTime { get; set; }

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

    public required bool IsArchived { get; set; }

    public DateTime? ArchiveTime { get; set; }
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

/// <summary>An invoice debt with payments in the same direction and the amount still owed.</summary>
public class InvoiceDebtDto
{
    public required Guid InvoiceId { get; set; }

    public required SplitzUserReducedDto FromUser { get; set; }

    public required SplitzUserReducedDto ToUser { get; set; }

    public required decimal Amount { get; set; }

    /// <summary>Total payments from this debtor to this creditor, including any excess payment.</summary>
    public required decimal Paid { get; set; }

    /// <summary>Amount still owed. This value is never less than zero.</summary>
    public required decimal Remaining { get; set; }
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