using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.Recognition;

/// <summary>Receipt image limits, bound from the <c>ReceiptRecognition</c> configuration section.</summary>
public sealed class ReceiptRecognitionOptions
{
    public const string SectionName = "ReceiptRecognition";

    /// <summary>Most images accepted in one recognition request.</summary>
    public int MaxImages { get; init; } = 6;

    /// <summary>Longest side of each stored image, in pixels. The same image is sent to the model.</summary>
    public int MaxImageSize { get; init; } = 1600;

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