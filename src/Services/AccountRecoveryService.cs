using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using SplitzBackend.Models;

namespace SplitzBackend.Services;

public sealed class AccountRecoveryService(
    UserManager<SplitzUser> userManager,
    IEmailSender<SplitzUser> emailSender)
{
    public async Task RequestPasswordResetAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return;

        var code = await userManager.GeneratePasswordResetTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        await emailSender.SendPasswordResetCodeAsync(user, email, HtmlEncoder.Default.Encode(code));
    }

    public async Task<IdentityResult> ResetPasswordAsync(string email, string resetCode, string newPassword)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return IdentityResult.Failed(userManager.ErrorDescriber.InvalidToken());

        string code;
        try
        {
            code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(resetCode));
        }
        catch (FormatException)
        {
            return IdentityResult.Failed(userManager.ErrorDescriber.InvalidToken());
        }

        var wasEmailConfirmed = user.EmailConfirmed;
        user.EmailConfirmed = true;

        var result = await userManager.ResetPasswordAsync(user, code, newPassword);
        if (!result.Succeeded)
            user.EmailConfirmed = wasEmailConfirmed;

        return result;
    }
}