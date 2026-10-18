using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SplitzBackend.Models;
using SplitzBackend.Services.LLM;

namespace SplitzBackend.Controllers;

/// <summary>Manages the current user's provider for AI features.</summary>
[Authorize]
[ApiController]
[Route("[controller]")]
public class LLMSettingsController(
    UserManager<SplitzUser> userManager,
    ILLMSettingsService settings) : ControllerBase
{
    /// <summary>AI provider availability and the current user's custom provider. The API key is never returned.</summary>
    [HttpGet(Name = "GetLLMSettings")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<LLMSettingsDto>> GetSettings(
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        return await settings.GetAsync(user.Id, cancellationToken);
    }

    /// <summary>
    ///     Save the current user's custom provider. Leaving <c>apiKey</c> empty keeps the stored key.
    ///     Returns 400 with a reason when the provider is turned on but incomplete.
    /// </summary>
    [HttpPut(Name = "UpdateLLMSettings")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<LLMSettingsDto>> UpdateSettings(
        LLMSettingsInputDto input,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var update = await settings.UpdateAsync(user.Id, input, cancellationToken);
        if (update.Error is not null)
            return BadRequest(update.Error);

        return update.Settings!;
    }

    /// <summary>Remove the current user's custom provider and its stored key.</summary>
    [HttpDelete(Name = "ClearLLMSettings")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ClearSettings(CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        await settings.ClearAsync(user.Id, cancellationToken);
        return NoContent();
    }
}