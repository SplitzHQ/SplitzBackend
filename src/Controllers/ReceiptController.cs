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
using SplitzBackend.Services.LLM;

namespace SplitzBackend.Controllers;

/// <summary>
///     Upload receipt photos and auto fill expense fields.
/// </summary>
[Authorize]
[ApiController]
[Route("[controller]")]
public class ReceiptController(
    SplitzDbContext context,
    UserManager<SplitzUser> userManager,
    IMapper mapper,
    IReceiptRecognitionService recognition,
    ILLMSettingsService llmSettings,
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
    public async Task<ActionResult<ReceiptDto>> RecognizeReceipt(
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

        var provider = await llmSettings.ResolveProviderAsync(user.Id, cancellationToken);
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
            return Ok(mapper.Map<ReceiptDto>(result));
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

    /// <summary>Get user receipt by ID.</summary>
    [HttpGet("{id}", Name = "GetReceipt")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ReceiptDto>> GetReceipt(Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var result = await context.Receipts
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReceiptId == id && r.UserId == user.Id, cancellationToken);
        if (result is null)
            return NotFound();

        return mapper.Map<ReceiptDto>(result);
    }

    /// <summary>Get receipt recognition availability and image limits for the current user.</summary>
    [HttpGet("capabilities", Name = "GetReceiptRecognitionCapabilities")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<ReceiptRecognitionCapabilitiesDto>> GetRecognitionCapabilities(
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var providerSettings = await llmSettings.GetAsync(user.Id, cancellationToken);
        return new ReceiptRecognitionCapabilitiesDto
        {
            Available = providerSettings.Available,
            CustomProviderAllowed = providerSettings.CustomProviderAllowed,
            MaxImages = options.Value.MaxImages
        };
    }
}

public sealed record ReceiptRecognitionCapabilitiesDto
{
    /// <summary>True when this user has a provider available for receipt recognition.</summary>
    public required bool Available { get; init; }

    /// <summary>True when the user can configure a provider if recognition is unavailable.</summary>
    public required bool CustomProviderAllowed { get; init; }

    /// <summary>Maximum number of images in one recognition request.</summary>
    public required int MaxImages { get; init; }
}