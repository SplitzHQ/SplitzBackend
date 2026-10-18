using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SplitzBackend.Models;

/// <summary>
///     A user's OpenAI-compatible provider for AI features. The protected API key is never returned to clients.
///     The existing table name preserves saved providers across the settings rename.
/// </summary>
public class LLMSettings
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

    /// <summary>The API key protected by ASP.NET Core Data Protection, or null if no key is saved.</summary>
    public string? ProtectedApiKey { get; set; }

    public required DateTime UpdateTime { get; set; }
}

/// <summary>Provider availability and the user's settings. The API key is represented by a flag.</summary>
public sealed record LLMSettingsDto
{
    /// <summary>True when the server has a provider configured.</summary>
    public required bool ServerProviderAvailable { get; init; }

    /// <summary>True when the server lets users store their own provider.</summary>
    public required bool CustomProviderAllowed { get; init; }

    public required bool CustomProviderEnabled { get; init; }
    public string? Endpoint { get; init; }
    public string? Model { get; init; }
    public required bool HasApiKey { get; init; }

    /// <summary>True when this user has a provider available for AI requests.</summary>
    public required bool Available { get; init; }
}

/// <summary>Settings for saving a user's provider.</summary>
public sealed record LLMSettingsInputDto
{
    public required bool Enabled { get; init; }

    /// <summary>Base URL of an OpenAI-compatible API. Required when the provider is enabled.</summary>
    [MaxLength(2048)]
    public string? Endpoint { get; init; }

    [MaxLength(256)]
    public string? Model { get; init; }

    /// <summary>A new API key. Null or empty keeps the stored key.</summary>
    [MaxLength(4096)]
    public string? ApiKey { get; init; }
}