using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SplitzBackend.Models;
using SplitzBackend.Services.RateLimiting;
using SplitzBackend.Services.Recognition;

namespace SplitzBackend.Controllers;

/// <summary>
///     Receipt recognition: upload receipt photos, let a vision model read them, and manage the
///     user's own model provider.
/// </summary>
[Authorize]
[ApiController]
[Route("[controller]")]
public class ReceiptRecognitionController(
    SplitzDbContext context,
    UserManager<SplitzUser> userManager,
    IMapper mapper,
    IReceiptRecognitionService recognition,
    IReceiptRecognitionSettingsService settings,
    IOptions<ReceiptRecognitionOptions> options) : ControllerBase
{
    /// <summary>Per-file cap. Matches the receipt upload endpoints.</summary>
    private const long MaxFileBytes = 15 * 1024 * 1024;

    /// <summary>
    ///     Recognize an expense from one or more photos of the same receipt.
    ///     The images and the model reply are kept; the parsed fields are returned for the client to prefill.
    ///     A model failure is reported in the body with status <c>Failed</c>, not as an HTTP error.
    /// </summary>
    [HttpPost(Name = "RecognizeReceipt")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [RequestSizeLimit(60 * 1024 * 1024)]
    [EnableRateLimiting(RateLimitPolicyNames.ReceiptRecognitionPerUser)]
    public async Task<ActionResult<ReceiptRecognitionDto>> Recognize(
        [FromForm] ReceiptRecognitionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var files = request.Files.Where(f => f.Length > 0).ToList();
        if (files.Count == 0)
            return BadRequest("At least one image is required.");
        if (files.Count > options.Value.MaxImages)
            return BadRequest($"At most {options.Value.MaxImages} images are accepted.");
        if (files.Any(f => f.Length > MaxFileBytes))
            return BadRequest("Each image must be 15 MB or smaller.");

        var provider = await settings.ResolveProviderAsync(user.Id, cancellationToken);
        if (provider is null)
            return BadRequest("Receipt recognition is not configured.");

        var categories = request.Categories?
            .Select(c => c.Trim())
            .Where(c => c.Length is > 0 and <= ReceiptRecognitionResultParser.MaxCategoryLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var streams = files.Select(f => f.OpenReadStream()).ToList();
        try
        {
            var uploads = streams.Zip(files, (stream, file) => new ReceiptImageUpload(stream, file.ContentType))
                .ToList();
            var result = await recognition.RecognizeAsync(user.Id, provider, uploads, categories, cancellationToken);
            return Ok(mapper.Map<ReceiptRecognitionDto>(result));
        }
        catch (InvalidImageException ex)
        {
            return BadRequest(ex.Message);
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    /// <summary>Get one of the current user's recognitions.</summary>
    [HttpGet("{id}", Name = "GetReceiptRecognition")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ReceiptRecognitionDto>> GetRecognition(Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var result = await context.ReceiptRecognitions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReceiptRecognitionId == id && r.UserId == user.Id, cancellationToken);
        if (result is null)
            return NotFound();

        return mapper.Map<ReceiptRecognitionDto>(result);
    }

    /// <summary>Recognition availability and the current user's custom provider. The API key is never returned.</summary>
    [HttpGet("settings", Name = "GetReceiptRecognitionSettings")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<ReceiptRecognitionSettingsDto>> GetSettings(
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
    [HttpPut("settings", Name = "UpdateReceiptRecognitionSettings")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<ReceiptRecognitionSettingsDto>> UpdateSettings(
        ReceiptRecognitionSettingsInputDto input,
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
    [HttpDelete("settings", Name = "ClearReceiptRecognitionSettings")]
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