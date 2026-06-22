using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SplitzBackend.Services;

namespace SplitzBackend.Controllers;

[ApiController]
[Route("account")]
public sealed class AccountEmailController(IOptions<EmailOptions> emailOptions) : ControllerBase
{
    /// <summary>
    ///     Reports whether transactional email-backed account workflows are available.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("email-capabilities", Name = "GetEmailCapabilities")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    public ActionResult<EmailCapabilitiesDto> GetEmailCapabilities()
    {
        var isAvailable = emailOptions.Value.IsAvailable;
        return Ok(new EmailCapabilitiesDto(isAvailable, isAvailable));
    }
}

public sealed record EmailCapabilitiesDto(bool EmailEnabled, bool PasswordResetEnabled);