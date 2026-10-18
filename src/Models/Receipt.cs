using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

/// <summary>Recognition status of a receipt.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReceiptRecognitionStatus
{
    /// <summary>Images are stored and the model call is in progress or was interrupted.</summary>
    Pending = 0,

    /// <summary>The model answered and the reply was parsed.</summary>
    Succeeded = 1,

    /// <summary>The model call failed or its reply could not be parsed. See <see cref="Receipt.Error" />.</summary>
    Failed = 2
}

/// <summary>
/// Represents a single receipt.
/// </summary>
[Index(nameof(UserId), nameof(CreateTime))]
public class Receipt
{
    public Guid ReceiptId { get; set; }

    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public SplitzUser User { get; set; } = null!;

    public required DateTime CreateTime { get; set; }

    /// <summary>Object storage keys of the stored images, in upload order. Mapped to a JSON column.</summary>
    public List<string> ImageKeys { get; set; } = new();

    /// <summary>Endpoint that was called. The API key is never stored here.</summary>
    [MaxLength(2048)]
    public required string Endpoint { get; set; }

    [MaxLength(256)]
    public required string Model { get; set; }

    /// <summary>True when the user's own provider settings were used instead of the server provider.</summary>
    public required bool UsedCustomProvider { get; set; }

    public ReceiptRecognitionStatus Status { get; set; } = ReceiptRecognitionStatus.Pending;

    /// <summary>The assistant reply exactly as received.</summary>
    public string? RawResponse { get; set; }

    [MaxLength(1000)]
    public string? Error { get; set; }

    [MaxLength(256)]
    public string? Name { get; set; }

    [MaxLength(256)]
    public string? Merchant { get; set; }

    public decimal? Amount { get; set; }

    [MaxLength(3)]
    public string? Currency { get; set; }

    public DateTime? TransactionTime { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; }

    [MaxLength(128)]
    public string? Location { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Set when the user saves an expense from this recognition. Cleared if that expense is deleted.</summary>
    public Guid? TransactionId { get; set; }

    [ForeignKey(nameof(TransactionId))]
    public Transaction? Transaction { get; set; }
}

/// <summary>Receipt details returned after recognition or when fetching a stored receipt.</summary>
public class ReceiptDto
{
    /// <summary>Unique identifier of the receipt.</summary>
    public required Guid ReceiptId { get; set; }

    public required DateTime CreateTime { get; set; }

    /// <summary>Signed URLs of the stored images, in upload order.</summary>
    public List<string> Images { get; set; } = new();

    public required string Model { get; set; }

    public required bool UsedCustomProvider { get; set; }

    public required ReceiptRecognitionStatus Status { get; set; }

    public string? RawResponse { get; set; }

    public string? Error { get; set; }

    public string? Name { get; set; }

    public string? Merchant { get; set; }

    public decimal? Amount { get; set; }

    public string? Currency { get; set; }

    public DateTime? TransactionTime { get; set; }

    public string? Category { get; set; }

    public string? Location { get; set; }

    public string? Notes { get; set; }

    public Guid? TransactionId { get; set; }
}

/// <summary>Multipart body of a recognition request.</summary>
public class ReceiptRecognitionRequest
{
    /// <summary>One or more photos of the same receipt.</summary>
    [Required]
    public required List<IFormFile> Files { get; set; }

    /// <summary>
    ///     Category keys the client understands. When given, the model is asked to pick one of them and
    ///     any other answer is dropped.
    /// </summary>
    public List<string>? Categories { get; set; }
}