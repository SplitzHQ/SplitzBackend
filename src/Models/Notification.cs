using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SplitzBackend.Models;

// --- Notification Data Types ---

[JsonDerivedType(typeof(InvoiceCreatedNotification), "InvoiceCreated")]
[JsonDerivedType(typeof(SettlementRecordedNotification), "SettlementRecorded")]
[JsonDerivedType(typeof(InvoiceSettledNotification), "InvoiceSettled")]
public abstract class NotificationData;

public class InvoiceCreatedNotification : NotificationData
{
    public required string CreatorName { get; set; }
    public string? InvoiceName { get; set; }
    public required Guid InvoiceId { get; set; }
    public required Guid GroupId { get; set; }
}

public class SettlementRecordedNotification : NotificationData
{
    public required string RecorderName { get; set; }
    public required decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string FromUserId { get; set; }
    public required string ToUserId { get; set; }
    public required Guid InvoiceId { get; set; }
    public string? InvoiceName { get; set; }
}

public class InvoiceSettledNotification : NotificationData
{
    public required Guid InvoiceId { get; set; }
    public string? InvoiceName { get; set; }
    public required Guid GroupId { get; set; }
}

// --- Notification Entity ---

public class Notification
{
    public Guid NotificationId { get; set; }

    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public SplitzUser User { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public required string Type { get; set; }

    [MaxLength(256)] public string? ReferenceId { get; set; }

    /// <summary>
    ///     JSON-serialized data for the notification. Frontend uses Type + Data to build localized messages.
    /// </summary>
    [Required]
    [MaxLength(1024)]
    public required string Data { get; set; }

    /// <summary>
    ///     Deserialize Data into a typed NotificationData object.
    /// </summary>
    public NotificationData? GetTypedData()
    {
        return Type switch
        {
            "InvoiceCreated" => JsonSerializer.Deserialize<InvoiceCreatedNotification>(Data, JsonOptions),
            "SettlementRecorded" => JsonSerializer.Deserialize<SettlementRecordedNotification>(Data, JsonOptions),
            "InvoiceSettled" => JsonSerializer.Deserialize<InvoiceSettledNotification>(Data, JsonOptions),
            _ => null
        };
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public required bool IsRead { get; set; } = false;

    public required bool IsDismissed { get; set; } = false;

    public required DateTime CreateTime { get; set; }
}

public class NotificationDto
{
    public required Guid NotificationId { get; set; }

    [Required]
    [MaxLength(50)]
    public required string Type { get; set; }

    [MaxLength(256)] public string? ReferenceId { get; set; }

    /// <summary>
    ///     Deserialized notification data object. The concrete type depends on Type.
    /// </summary>
    [Required]
    public required object Data { get; set; }

    public required bool IsRead { get; set; }

    public required bool IsDismissed { get; set; }

    public required DateTime CreateTime { get; set; }
}