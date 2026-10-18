using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SplitzBackend.Models;

namespace SplitzBackend.Services.LLM;

/// <summary>Result of saving settings: either the new settings or one message describing what was wrong.</summary>
public sealed record LLMSettingsUpdateResult(LLMSettingsDto? Settings, string? Error);

/// <summary>Reads and writes a user's custom provider and decides which provider a request should use.</summary>
public interface ILLMSettingsService
{
    /// <summary>Gets the user's provider settings without the API key.</summary>
    Task<LLMSettingsDto> GetAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Validates and saves provider settings, keeping the stored key when no new key is given.</summary>
    Task<LLMSettingsUpdateResult> UpdateAsync(string userId, LLMSettingsInputDto input,
        CancellationToken cancellationToken);

    /// <summary>Removes the custom provider, including the stored key.</summary>
    Task ClearAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    ///     The user's custom provider when it is enabled, allowed and complete; otherwise the server
    ///     provider; otherwise null.
    /// </summary>
    Task<LLMProvider?> ResolveProviderAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Stores user providers and selects a provider for AI requests.</summary>
public sealed class LLMSettingsService(
    SplitzDbContext context,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<LLMOptions> options,
    ILogger<LLMSettingsService> logger) : ILLMSettingsService
{
    /// <summary>Changing this string makes every stored key unreadable, so treat it as part of the schema.</summary>
    private const string ProtectorPurpose = "SplitzBackend.ReceiptRecognition.ApiKey.v1";

    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    /// <inheritdoc />
    public async Task<LLMSettingsDto> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var settings = await context.LLMSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        return ToLLMSettingsDto(settings);
    }

    /// <inheritdoc />
    public async Task<LLMSettingsUpdateResult> UpdateAsync(string userId,
        LLMSettingsInputDto input, CancellationToken cancellationToken)
    {
        if (!options.Value.AllowCustomProvider)
            return new LLMSettingsUpdateResult(null, "Custom providers are turned off on this server.");

        var settings = await context.LLMSettings
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        settings ??= context.LLMSettings.Add(new LLMSettings
        {
            UserId = userId,
            Enabled = false,
            UpdateTime = DateTime.UtcNow
        }).Entity;

        var endpoint = input.Endpoint?.Trim();
        var model = input.Model?.Trim();
        var newKey = input.ApiKey?.Trim();
        var hasKey = !string.IsNullOrEmpty(newKey) || settings.ProtectedApiKey is not null;

        if (input.Enabled)
        {
            if (!LLMProvider.IsValidEndpoint(endpoint))
                return new LLMSettingsUpdateResult(null, "Endpoint must be an absolute http or https URL.");
            if (string.IsNullOrEmpty(model))
                return new LLMSettingsUpdateResult(null, "Model is required.");
            if (!hasKey)
                return new LLMSettingsUpdateResult(null, "API key is required.");
        }

        settings.Enabled = input.Enabled;
        settings.Endpoint = string.IsNullOrEmpty(endpoint) ? null : endpoint;
        settings.Model = string.IsNullOrEmpty(model) ? null : model;
        if (!string.IsNullOrEmpty(newKey))
            settings.ProtectedApiKey = protector.Protect(newKey);
        settings.UpdateTime = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return new LLMSettingsUpdateResult(ToLLMSettingsDto(settings), null);
    }

    /// <inheritdoc />
    public async Task ClearAsync(string userId, CancellationToken cancellationToken)
    {
        await context.LLMSettings
            .Where(s => s.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LLMProvider?> ResolveProviderAsync(string userId,
        CancellationToken cancellationToken)
    {
        var serverOptions = options.Value;
        if (serverOptions.AllowCustomProvider)
        {
            var settings = await context.LLMSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
            var apiKey = TryUnprotect(settings);
            if (settings is { Enabled: true } &&
                LLMProvider.IsValidEndpoint(settings.Endpoint) &&
                !string.IsNullOrWhiteSpace(settings.Model) &&
                apiKey is not null)
                return new LLMProvider(settings.Endpoint!, settings.Model, apiKey, true);
        }

        return serverOptions.IsServerProviderConfigured
            ? new LLMProvider(serverOptions.Endpoint, serverOptions.Model, serverOptions.ApiKey, false)
            : null;
    }

    /// <summary>
    /// Builds the public settings response and checks whether a provider is available.
    /// Normally it should be done in mapper, but we need to check the provider availability, so we have to do it here.
    /// </summary>
    private LLMSettingsDto ToLLMSettingsDto(LLMSettings? settings)
    {
        var serverOptions = options.Value;
        var hasKey = TryUnprotect(settings) is not null;
        var customEnabled = serverOptions.AllowCustomProvider && settings is { Enabled: true };
        var customComplete = customEnabled &&
                             LLMProvider.IsValidEndpoint(settings!.Endpoint) &&
                             !string.IsNullOrWhiteSpace(settings.Model) &&
                             hasKey;

        return new LLMSettingsDto
        {
            ServerProviderAvailable = serverOptions.IsServerProviderConfigured,
            CustomProviderAllowed = serverOptions.AllowCustomProvider,
            CustomProviderEnabled = settings?.Enabled ?? false,
            Endpoint = settings?.Endpoint,
            Model = settings?.Model,
            HasApiKey = hasKey,
            Available = customComplete || serverOptions.IsServerProviderConfigured
        };
    }

    /// <summary>
    ///     A key becomes unreadable when the data protection key ring is lost, for example after a
    ///     container was recreated without a persisted key folder. That is reported as "no key" so the
    ///     user can enter it again instead of seeing every request fail.
    /// </summary>
    private string? TryUnprotect(LLMSettings? settings)
    {
        if (settings?.ProtectedApiKey is null)
            return null;

        try
        {
            return protector.Unprotect(settings.ProtectedApiKey);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "Stored LLM API key for user {UserId} cannot be read.",
                settings.UserId);
            return null;
        }
    }
}