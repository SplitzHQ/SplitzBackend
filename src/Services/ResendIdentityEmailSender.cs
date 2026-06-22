using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Resend;
using SplitzBackend.Models;

namespace SplitzBackend.Services;

public sealed class ResendIdentityEmailSender(
    IServiceScopeFactory? serviceScopeFactory,
    IOptions<EmailOptions> emailOptions,
    ILogger<ResendIdentityEmailSender> logger) : IEmailSender<SplitzUser>
{
    public async Task SendConfirmationLinkAsync(SplitzUser user, string email, string confirmationLink)
    {
        var options = emailOptions.Value;
        if (!options.IsAvailable || serviceScopeFactory is null)
        {
            logger.LogInformation("Email confirmation delivery skipped because transactional email is unavailable.");
            return;
        }

        await SendAsync(
            email,
            "Confirm your SplitZ email",
            "Confirm your email address to finish setting up your SplitZ account.",
            BuildConfirmationUrl(options, confirmationLink));
    }

    public async Task SendPasswordResetLinkAsync(SplitzUser user, string email, string resetLink)
    {
        var options = emailOptions.Value;
        if (!options.IsAvailable || serviceScopeFactory is null)
        {
            logger.LogInformation("Password reset email delivery skipped because transactional email is unavailable.");
            return;
        }

        await SendAsync(
            email,
            "Reset your SplitZ password",
            "Use this link to reset your SplitZ password.",
            BuildPasswordResetUrl(options, email, resetLink, null));
    }

    public async Task SendPasswordResetCodeAsync(SplitzUser user, string email, string resetCode)
    {
        var options = emailOptions.Value;
        if (!options.IsAvailable || serviceScopeFactory is null)
        {
            logger.LogInformation("Password reset email delivery skipped because transactional email is unavailable.");
            return;
        }

        await SendAsync(
            email,
            "Reset your SplitZ password",
            "Use this link to reset your SplitZ password.",
            BuildPasswordResetUrl(options, email, null, resetCode));
    }

    public static string BuildConfirmationUrl(EmailOptions options, string confirmationLink)
    {
        var query = QueryHelpers.ParseQuery(new Uri(confirmationLink).Query);
        var parameters = new Dictionary<string, string?>
        {
            ["userId"] = query.TryGetValue("userId", out var userId) ? userId.ToString() : null,
            ["code"] = query.TryGetValue("code", out var code) ? code.ToString() : null,
            ["changedEmail"] = query.TryGetValue("changedEmail", out var changedEmail) ? changedEmail.ToString() : null
        };

        return QueryHelpers.AddQueryString(BuildFrontendBaseUrl(options.FrontendBaseUrl, options.ConfirmEmailPath), parameters);
    }

    public static string BuildPasswordResetUrl(EmailOptions options, string email, string? resetLink, string? resetCode)
    {
        var code = resetCode;
        if (!string.IsNullOrWhiteSpace(resetLink))
        {
            var query = QueryHelpers.ParseQuery(new Uri(resetLink).Query);
            if (query.TryGetValue("resetCode", out var resetCodeFromLink))
                code = resetCodeFromLink.ToString();
            if (query.TryGetValue("code", out var codeFromLink))
                code = codeFromLink.ToString();
        }

        return QueryHelpers.AddQueryString(
            BuildFrontendBaseUrl(options.FrontendBaseUrl, options.ResetPasswordPath),
            new Dictionary<string, string?>
            {
                ["email"] = email,
                ["resetCode"] = code
            });
    }

    private async Task SendAsync(string recipient, string subject, string text, string actionUrl)
    {
        var options = emailOptions.Value;
        var from = string.IsNullOrWhiteSpace(options.FromName)
            ? options.FromEmail
            : $"{options.FromName} <{options.FromEmail}>";

        var message = new EmailMessage
        {
            From = from,
            Subject = subject,
            TextBody = $"{text}\n\n{actionUrl}",
            HtmlBody = $"<p>{text}</p><p><a href=\"{actionUrl}\">Continue in SplitZ</a></p>"
        };
        message.To.Add(recipient);

        using var scope = serviceScopeFactory!.CreateScope();
        var resend = scope.ServiceProvider.GetRequiredService<IResend>();
        await resend.EmailSendAsync(message);
        logger.LogInformation("Transactional email sent for {EmailPurpose}.", subject);
    }

    private static string BuildFrontendBaseUrl(string frontendBaseUrl, string path)
    {
        var baseUrl = frontendBaseUrl.TrimEnd('/');
        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        return $"{baseUrl}{normalizedPath}";
    }
}