using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace SplitzBackend.Models;

/// <summary>Outcome of one recognition request.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReceiptRecognitionStatus
{
    /// <summary>Images are stored and the model call is in progress or was interrupted.</summary>
    Pending = 0,

    /// <summary>The model answered and the reply was parsed.</summary>
    Succeeded = 1,

    /// <summary>The model call failed or its reply could not be parsed. See <see cref="ReceiptRecognition.Error" />.</summary>
    Failed = 2
}

/// <summary>
///     One receipt recognition request: the images the user uploaded, which provider answered,
///     the raw reply and the fields read from it. Rows are kept even when the request failed so
///     the images and the reply can be inspected later.
/// </summary>
[Index(nameof(UserId), nameof(CreateTime))]
public class ReceiptRecognition
{
    public Guid ReceiptRecognitionId { get; set; }

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

/// <summary>What the client receives after a recognition request, or when it fetches an old one.</summary>
public class ReceiptRecognitionDto
{
    public required Guid ReceiptRecognitionId { get; set; }

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