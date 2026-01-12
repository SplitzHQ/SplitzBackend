using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;
using SplitzBackend.Services;

namespace SplitzBackend.Controllers;

[Authorize]
[ApiController]
[Route("[controller]")]
public class TransactionDraftController(
    SplitzDbContext context,
    UserManager<SplitzUser> userManager,
    IMapper mapper,
    IImageStorageService imageStorage) : ControllerBase
{
    /// <summary>
    ///     Get transaction by id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet("{id}", Name = "GetTransactionDraft")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<TransactionDraftDto>> GetTransactionDraft(Guid id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var transaction =
            await mapper.ProjectTo<TransactionDraftDto>(context.TransactionDrafts.Where(t =>
                t.User.Id == user.Id && t.TransactionDraftId == id)).FirstOrDefaultAsync();

        if (transaction == null) return NotFound();

        return transaction;
    }

    /// <summary>
    ///     Add a transaction
    /// </summary>
    /// <param name="transactionDraftInputDto"></param>
    /// <returns></returns>
    [HttpPost(Name = "AddTransactionDraft")]
    [Produces("application/json")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(201)]
    public async Task<ActionResult<TransactionDraftDto>> PostTransactionDraft(
        TransactionDraftInputDto transactionDraftInputDto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var transactionDraft = mapper.Map<TransactionDraft>(transactionDraftInputDto);

        if (transactionDraft.UserId != user.Id)
            return BadRequest("TransactionDraft user does not match");

        if (transactionDraft.GroupId != null)
        {
            var group = await context.Groups.Include(group => group.Members)
                .FirstOrDefaultAsync(g => g.GroupId == transactionDraft.GroupId && g.Members.Contains(user));
            if (group is null)
                return BadRequest("Group not found");
            var transactionMembers = transactionDraft.Balances.Select(b => b.UserId);
            if (transactionMembers.Any(id => !group.Members.Select(m => m.Id).Contains(id)))
                return BadRequest("Balance user not in group");
        }

        context.TransactionDrafts.Add(transactionDraft);
        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTransactionDraft), new { id = transactionDraft.TransactionDraftId },
            transactionDraft);
    }

    /// <summary>
    ///     Update a transaction
    /// </summary>
    /// <param name="transactionDraftId"></param>
    /// <param name="transactionDraftInputDto"></param>
    /// <returns></returns>
    [HttpPut("{transactionDraftId}", Name = "UpdateTransactionDraft")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PutTransactionDraft(Guid transactionDraftId,
        TransactionDraftInputDto transactionDraftInputDto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var transactionDraft = mapper.Map<TransactionDraft>(transactionDraftInputDto);

        if (transactionDraft.TransactionDraftId != transactionDraftId)
            return BadRequest("TransactionDraft id does not match");
        if (transactionDraft.UserId != user.Id)
            return BadRequest("TransactionDraft user does not match");

        if (transactionDraft.GroupId != null)
        {
            var group = await context.Groups.Include(group => group.Members)
                .FirstOrDefaultAsync(g => g.GroupId == transactionDraft.GroupId && g.Members.Contains(user));
            if (group is null)
                return BadRequest("Group not found");
            var transactionMembers = transactionDraft.Balances.Select(b => b.UserId);
            if (transactionMembers.Any(id => !group.Members.Select(m => m.Id).Contains(id)))
                return BadRequest("Balance user not in group");
        }

        context.Entry(transactionDraft).State = EntityState.Modified;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!TransactionDraftExists(transactionDraftId))
                return NotFound();
            throw;
        }

        return NoContent();
    }

    /// <summary>
    ///     Upload a receipt image for a transaction draft.
    /// </summary>
    [HttpPost("{id}/receipt", Name = "UploadTransactionDraftReceipt")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    [ProducesResponseType(200)]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<ActionResult<UploadImageResult>> UploadReceipt(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (file.Length <= 0)
            return BadRequest("Empty file");

        var draft = await context.TransactionDrafts.FirstOrDefaultAsync(t => t.TransactionDraftId == id,
            cancellationToken);
        if (draft is null)
            return NotFound();
        if (draft.UserId != user.Id)
            return BadRequest("TransactionDraft user does not match");

        var existingPhoto = draft.Photo;

        await using var input = file.OpenReadStream();
        var result = await imageStorage.UploadProcessedImageAsync(
            input,
            file.ContentType,
            $"drafts/{id}/receipt",
            new ImageResizeRequest(2048),
            cancellationToken);

        draft.Photo = result.Url;
        await context.SaveChangesAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    ///     Delete a transaction
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpDelete("{id}", Name = "DeleteTransactionDraft")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteTransactionDraft(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var transactionDraft = await context.TransactionDrafts.FindAsync([id], cancellationToken);
        if (transactionDraft == null) return NotFound();

        if (transactionDraft.UserId != user.Id)
            return BadRequest("TransactionDraft user does not match");

        if (transactionDraft.GroupId != null)
        {
            var groupExists =
                await context.Groups.AnyAsync(g => g.GroupId == transactionDraft.GroupId && g.Members.Contains(user));
            if (!groupExists)
                return BadRequest("Group not found");
        }

        var receipt = transactionDraft.Photo;

        context.TransactionDrafts.Remove(transactionDraft);
        await context.SaveChangesAsync(cancellationToken);

        await imageStorage.DeleteIfOwnedAsync(receipt, cancellationToken);

        return NoContent();
    }

    private bool TransactionDraftExists(Guid id)
    {
        return context.TransactionDrafts.Any(e => e.TransactionDraftId == id);
    }
}