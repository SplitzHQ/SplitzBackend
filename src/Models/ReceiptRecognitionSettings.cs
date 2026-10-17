using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SplitzBackend.Models;

/// <summary>
///     A user's own OpenAI-compatible provider for receipt recognition. When <see cref="Enabled" /> is true
///     and the server allows custom providers, these values are used instead of the server provider.
///     One row per user; the API key is stored protected and never returned to the client.
/// </summary>
public class ReceiptRecognitionSettings
{
    [Key]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public SplitzUser User { get; set; } = null!;

    public required bool Enabled { get; set; }

    [MaxLength(2048)]
    public string? Endpoint { get; set; }

    [MaxLength(256)]
    public string? Model { get; set; }

    /// <summary>The API key after ASP.NET Core data protection. Null when no key has been saved.</summary>
    public string? ProtectedApiKey { get; set; }

    public required DateTime UpdateTime { get; set; }
}

/// <summary>Recognition availability and the user's custom provider, with the key reduced to a flag.</summary>
public class ReceiptRecognitionSettingsDto
{
    /// <summary>True when the server has its own provider configured.</summary>
    public required bool ServerProviderAvailable { get; set; }

    /// <summary>True when the server lets users store their own provider.</summary>
    public required bool CustomProviderAllowed { get; set; }

    public required bool CustomProviderEnabled { get; set; }

    public string? Endpoint { get; set; }

    public string? Model { get; set; }

    public required bool HasApiKey { get; set; }

    /// <summary>True when a recognition request from this user would have a provider to call.</summary>
    public required bool Available { get; set; }

    /// <summary>Most images one recognition request accepts.</summary>
    public required int MaxImages { get; set; }
}

/// <summary>Body for saving the custom provider.</summary>
public class ReceiptRecognitionSettingsInputDto
{
    public required bool Enabled { get; set; }

    /// <summary>Base URL of an OpenAI-compatible API. Required when <see cref="Enabled" /> is true.</summary>
    [MaxLength(2048)]
    public string? Endpoint { get; set; }

    [MaxLength(256)]
    public string? Model { get; set; }

    /// <summary>A new API key. Null or empty keeps the key that is already stored.</summary>
    [MaxLength(4096)]
    public string? ApiKey { get; set; }
}