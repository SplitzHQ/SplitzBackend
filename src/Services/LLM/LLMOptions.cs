using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.LLM;

/// <summary>Server provider settings shared by AI features, bound from the <c>LLM</c> section.</summary>
public sealed class LLMOptions
{
    public const string SectionName = "LLM";

    /// <summary>Enables the server provider when its endpoint, model and API key are set.</summary>
    public bool Enabled { get; init; }

    /// <summary>Base URL of an OpenAI-compatible API.</summary>
    public string Endpoint { get; init; } = "https://api.openai.com/v1";

    /// <summary>Name of the model used by AI features. Receipt recognition requires image input support.</summary>
    public string Model { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Allows users to store their own provider.</summary>
    public bool AllowCustomProvider { get; init; } = true;

    /// <summary>Maximum duration of one model call, in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 90;

    /// <summary>True when the server provider is enabled and complete.</summary>
    public bool IsServerProviderConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(Model) &&
        !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Returns the configuration errors that prevent model calls.</summary>
    public static List<string> Validate(LLMOptions options)
    {
        var failures = new List<string>();
        if (options.TimeoutSeconds <= 0)
            failures.Add("LLM:TimeoutSeconds must be greater than zero.");
        if (!options.Enabled)
            return failures;

        if (!LLMProvider.IsValidEndpoint(options.Endpoint))
            failures.Add("LLM:Endpoint must be an absolute http or https URL when Enabled is true.");
        if (string.IsNullOrWhiteSpace(options.Model))
            failures.Add("LLM:Model is required when Enabled is true.");
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add("LLM:ApiKey is required when Enabled is true.");
        return failures;
    }
}

/// <summary>Validates shared provider settings at startup.</summary>
public sealed class LLMOptionsValidator : IValidateOptions<LLMOptions>
{
    /// <summary>Returns all provider configuration errors.</summary>
    public ValidateOptionsResult Validate(string? name, LLMOptions options)
    {
        var failures = LLMOptions.Validate(options);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}