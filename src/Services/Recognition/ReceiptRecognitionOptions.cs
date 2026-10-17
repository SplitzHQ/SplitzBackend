using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.Recognition;

/// <summary>
///     Server-wide receipt recognition settings, bound from the <c>ReceiptRecognition</c> configuration section.
///     The server provider is used for every user who has not turned on a custom provider of their own.
/// </summary>
public sealed class ReceiptRecognitionOptions
{
    public const string SectionName = "ReceiptRecognition";

    /// <summary>Turns the server provider on. <see cref="Endpoint" />, <see cref="Model" /> and <see cref="ApiKey" /> must also be set.</summary>
    public bool Enabled { get; init; }

    /// <summary>Base URL of an OpenAI-compatible API, for example <c>https://api.openai.com/v1</c>.</summary>
    public string Endpoint { get; init; } = "https://api.openai.com/v1";

    /// <summary>Name of a model that accepts image input.</summary>
    public string Model { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Whether users may store their own endpoint, model and key.</summary>
    public bool AllowCustomProvider { get; init; } = true;

    /// <summary>Most images accepted in one recognition request.</summary>
    public int MaxImages { get; init; } = 6;

    /// <summary>Longest side of each stored image, in pixels. The same image is sent to the model.</summary>
    public int MaxImageSize { get; init; } = 1600;

    /// <summary>How long one model call may run before it is abandoned.</summary>
    public int TimeoutSeconds { get; init; } = 90;

    /// <summary>True when the server provider can be used.</summary>
    public bool IsServerProviderConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(Model) &&
        !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    ///     Checks the values that would otherwise fail at the first request.
    ///     Returns one message per problem; an empty list means the options are valid.
    /// </summary>
    public static List<string> Validate(ReceiptRecognitionOptions options)
    {
        var failures = new List<string>();

        if (options.MaxImages <= 0)
            failures.Add("ReceiptRecognition:MaxImages must be greater than zero.");
        if (options.MaxImageSize <= 0)
            failures.Add("ReceiptRecognition:MaxImageSize must be greater than zero.");
        if (options.TimeoutSeconds <= 0)
            failures.Add("ReceiptRecognition:TimeoutSeconds must be greater than zero.");

        if (!options.Enabled)
            return failures;

        if (!ReceiptRecognitionProvider.IsValidEndpoint(options.Endpoint))
            failures.Add("ReceiptRecognition:Endpoint must be an absolute http or https URL when Enabled is true.");
        if (string.IsNullOrWhiteSpace(options.Model))
            failures.Add("ReceiptRecognition:Model is required when Enabled is true.");
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add("ReceiptRecognition:ApiKey is required when Enabled is true.");

        return failures;
    }
}

/// <summary>Runs <see cref="ReceiptRecognitionOptions.Validate" /> at startup.</summary>
public sealed class ReceiptRecognitionOptionsValidator : IValidateOptions<ReceiptRecognitionOptions>
{
    public ValidateOptionsResult Validate(string? name, ReceiptRecognitionOptions options)
    {
        var failures = ReceiptRecognitionOptions.Validate(options);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}