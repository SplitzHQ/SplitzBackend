using Microsoft.Extensions.Options;

namespace SplitzBackend.Services;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; init; } = "Resend";
    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string FromEmail { get; init; } = string.Empty;
    public string FromName { get; init; } = "SplitZ";
    public string FrontendBaseUrl { get; init; } = "http://localhost:5173";
    public string ConfirmEmailPath { get; init; } = "/confirm-email";
    public string ResetPasswordPath { get; init; } = "/reset-password";

    public bool IsAvailable =>
        Enabled &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(FromEmail) &&
        !string.IsNullOrWhiteSpace(FrontendBaseUrl);

    public static void ValidateForProduction(EmailOptions options)
    {
        var failures = new List<string>();

        if (options.Enabled && string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add("Email:ApiKey is required when Email:Enabled is true.");
        if (options.Enabled && string.IsNullOrWhiteSpace(options.FromEmail))
            failures.Add("Email:FromEmail is required when Email:Enabled is true.");
        if (options.Enabled && string.IsNullOrWhiteSpace(options.FrontendBaseUrl))
            failures.Add("Email:FrontendBaseUrl is required when Email:Enabled is true.");

        if (failures.Count > 0)
            throw new OptionsValidationException(SectionName, typeof(EmailOptions), failures);
    }
}