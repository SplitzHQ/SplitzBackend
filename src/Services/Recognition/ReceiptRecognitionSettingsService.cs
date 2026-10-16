using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SplitzBackend.Models;

namespace SplitzBackend.Services.Recognition;

/// <summary>Result of saving settings: either the new settings or one message describing what was wrong.</summary>
public sealed record ReceiptRecognitionSettingsUpdate(ReceiptRecognitionSettingsDto? Settings, string? Error);

/// <summary>Reads and writes a user's custom provider and decides which provider a request should use.</summary>
public interface IReceiptRecognitionSettingsService
{
    Task<ReceiptRecognitionSettingsDto> GetAsync(string userId, CancellationToken cancellationToken);

    Task<ReceiptRecognitionSettingsUpdate> UpdateAsync(string userId, ReceiptRecognitionSettingsInputDto input,
        CancellationToken cancellationToken);

    /// <summary>Removes the custom provider, including the stored key.</summary>
    Task ClearAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    ///     The user's custom provider when it is enabled, allowed and complete; otherwise the server
    ///     provider; otherwise null.
    /// </summary>
    Task<ReceiptRecognitionProvider?> ResolveProviderAsync(string userId, CancellationToken cancellationToken);
}

public sealed class ReceiptRecognitionSettingsService(
    SplitzDbContext context,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<ReceiptRecognitionOptions> options,
    ILogger<ReceiptRecognitionSettingsService> logger) : IReceiptRecognitionSettingsService
{
    /// <summary>Changing this string makes every stored key unreadable, so treat it as part of the schema.</summary>
    private const string ProtectorPurpose = "SplitzBackend.ReceiptRecognition.ApiKey.v1";

    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public async Task<ReceiptRecognitionSettingsDto> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var settings = await context.ReceiptRecognitionSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        return ToDto(settings);
    }

    public async Task<ReceiptRecognitionSettingsUpdate> UpdateAsync(string userId,
        ReceiptRecognitionSettingsInputDto input, CancellationToken cancellationToken)
    {
        if (!options.Value.AllowCustomProvider)
            return new ReceiptRecognitionSettingsUpdate(null, "Custom providers are turned off on this server.");

        var settings = await context.ReceiptRecognitionSettings
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        settings ??= context.ReceiptRecognitionSettings.Add(new ReceiptRecognitionSettings
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
            if (!ReceiptRecognitionProvider.IsValidEndpoint(endpoint))
                return new ReceiptRecognitionSettingsUpdate(null, "Endpoint must be an absolute http or https URL.");
            if (string.IsNullOrEmpty(model))
                return new ReceiptRecognitionSettingsUpdate(null, "Model is required.");
            if (!hasKey)
                return new ReceiptRecognitionSettingsUpdate(null, "API key is required.");
        }

        settings.Enabled = input.Enabled;
        settings.Endpoint = string.IsNullOrEmpty(endpoint) ? null : endpoint;
        settings.Model = string.IsNullOrEmpty(model) ? null : model;
        if (!string.IsNullOrEmpty(newKey))
            settings.ProtectedApiKey = protector.Protect(newKey);
        settings.UpdateTime = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return new ReceiptRecognitionSettingsUpdate(ToDto(settings), null);
    }

    public async Task ClearAsync(string userId, CancellationToken cancellationToken)
    {
        await context.ReceiptRecognitionSettings
            .Where(s => s.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<ReceiptRecognitionProvider?> ResolveProviderAsync(string userId,
        CancellationToken cancellationToken)
    {
        var serverOptions = options.Value;
        if (serverOptions.AllowCustomProvider)
        {
            var settings = await context.ReceiptRecognitionSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
            var apiKey = TryUnprotect(settings);
            if (settings is { Enabled: true } &&
                ReceiptRecognitionProvider.IsValidEndpoint(settings.Endpoint) &&
                !string.IsNullOrWhiteSpace(settings.Model) &&
                apiKey is not null)
                return new ReceiptRecognitionProvider(settings.Endpoint!, settings.Model, apiKey, true);
        }

        return serverOptions.IsServerProviderConfigured
            ? new ReceiptRecognitionProvider(serverOptions.Endpoint, serverOptions.Model, serverOptions.ApiKey, false)
            : null;
    }

    private ReceiptRecognitionSettingsDto ToDto(ReceiptRecognitionSettings? settings)
    {
        var serverOptions = options.Value;
        var hasKey = TryUnprotect(settings) is not null;
        var customEnabled = serverOptions.AllowCustomProvider && settings is { Enabled: true };
        var customComplete = customEnabled &&
                             ReceiptRecognitionProvider.IsValidEndpoint(settings!.Endpoint) &&
                             !string.IsNullOrWhiteSpace(settings.Model) &&
                             hasKey;

        return new ReceiptRecognitionSettingsDto
        {
            ServerProviderAvailable = serverOptions.IsServerProviderConfigured,
            CustomProviderAllowed = serverOptions.AllowCustomProvider,
            CustomProviderEnabled = settings?.Enabled ?? false,
            Endpoint = settings?.Endpoint,
            Model = settings?.Model,
            HasApiKey = hasKey,
            Available = customComplete || serverOptions.IsServerProviderConfigured,
            MaxImages = serverOptions.MaxImages
        };
    }

    /// <summary>
    ///     A key becomes unreadable when the data protection key ring is lost, for example after a
    ///     container was recreated without a persisted key folder. That is reported as "no key" so the
    ///     user can enter it again instead of seeing every request fail.
    /// </summary>
    private string? TryUnprotect(ReceiptRecognitionSettings? settings)
    {
        if (settings?.ProtectedApiKey is null)
            return null;

        try
        {
            return protector.Unprotect(settings.ProtectedApiKey);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "Stored receipt recognition API key for user {UserId} cannot be read.",
                settings.UserId);
            return null;
        }
    }
}