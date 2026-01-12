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
public class TransactionController(
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
    [HttpGet("{id}", Name = "GetTransaction")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<TransactionDto>> GetTransaction(Guid id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var transaction =
            await mapper.ProjectTo<TransactionDto>(context.Transactions.Where(t =>
                t.Group.Members.Contains(user) && t.TransactionId == id)).FirstOrDefaultAsync();

        if (transaction == null) return NotFound();

        return transaction;
    }

    /// <summary>
    ///     Add a transaction
    /// </summary>
    /// <param name="transactionInputDto"></param>
    /// <returns></returns>
    [HttpPost(Name = "AddTransaction")]
    [Produces("application/json")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(201)]
    public async Task<ActionResult<TransactionDto>> PostTransaction(TransactionInputDto transactionInputDto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var transaction = mapper.Map<Transaction>(transactionInputDto);

        var group = await context.Groups
            .Include(group => group.Members)
            .Include(group => group.Balances)
            .FirstOrDefaultAsync(g => g.GroupId == transaction.GroupId && g.Members.Contains(user));
        if (group is null)
            return BadRequest("Group not found");

        var transactionMembers = transaction.Balances.Select(b => b.UserId);
        if (transactionMembers.Any(id => !group.Members.Select(m => m.Id).Contains(id)))
            return BadRequest("Balance user not in group");

        await using var dbTransaction = await context.Database.BeginTransactionAsync();

        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        ApplyGroupBalanceChanges(group, transaction.Balances, transaction.Currency);
        await context.SaveChangesAsync();

        group.LastActivityTime = DateTime.Now;
        group.TransactionCount++;
        await context.SaveChangesAsync();

        await dbTransaction.CommitAsync();

        return CreatedAtAction(nameof(GetTransaction), new { id = transaction.TransactionId },
            mapper.Map<TransactionDto>(transaction));
    }

    /// <summary>
    ///     Upload a receipt image for a transaction.
    /// </summary>
    [HttpPost("{id}/receipt", Name = "UploadTransactionReceipt")]
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

        var transaction = await context.Transactions
            .Include(t => t.Group)
            .ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(t => t.TransactionId == id, cancellationToken);

        if (transaction is null)
            return NotFound();
        if (!transaction.Group.Members.Contains(user))
            return Unauthorized();

        var existingPhoto = transaction.Photo;

        await using var input = file.OpenReadStream();
        var result = await imageStorage.UploadProcessedImageAsync(
            input,
            file.ContentType,
            $"transactions/{id}/receipt",
            new ImageResizeRequest(2048),
            cancellationToken);

        transaction.Photo = result.Url;
        await context.SaveChangesAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    ///     Update a transaction
    /// </summary>
    /// <param name="transactionId"></param>
    /// <param name="transactionInputDto"></param>
    /// <returns></returns>
    [HttpPut("{transactionId}", Name = "UpdateTransaction")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PutTransaction(Guid transactionId, TransactionInputDto transactionInputDto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        if (transactionInputDto.TransactionId != transactionId)
            return BadRequest("Transaction id does not match");

        var existingTransaction = await context.Transactions
            .Include(t => t.Balances)
            .Include(t => t.Group)
            .ThenInclude(g => g.Members)
            .Include(t => t.Group)
            .ThenInclude(g => g.Balances)
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

        if (existingTransaction is null)
            return NotFound();

        if (!existingTransaction.Group.Members.Contains(user))
            return Unauthorized();

        if (existingTransaction.GroupId != transactionInputDto.GroupId)
            return BadRequest("Changing the group of a transaction is not allowed.");

        var targetMemberIds = existingTransaction.Group.Members.Select(m => m.Id).ToHashSet();
        var transactionMemberIds = transactionInputDto.Balances.Select(b => b.UserId);
        if (transactionMemberIds.Any(id => !targetMemberIds.Contains(id)))
            return BadRequest("Balance user not in group");

        await using var dbTransaction = await context.Database.BeginTransactionAsync();

        // Revert previous balance changes
        ApplyGroupBalanceChanges(existingTransaction.Group, existingTransaction.Balances, existingTransaction.Currency,
            true);
        await context.SaveChangesAsync();

        // Update transaction
        context.Entry(existingTransaction).CurrentValues.SetValues(mapper.Map<Transaction>(transactionInputDto));
        await context.SaveChangesAsync();

        // Apply new balance changes (existingTransaction now has updated balances)
        ApplyGroupBalanceChanges(existingTransaction.Group, existingTransaction.Balances, existingTransaction.Currency);
        await context.SaveChangesAsync();

        existingTransaction.Group.LastActivityTime = DateTime.UtcNow;
        await context.SaveChangesAsync();

        await dbTransaction.CommitAsync();

        return NoContent();
    }

    /// <summary>
    ///     Delete a transaction
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpDelete("{id}", Name = "DeleteTransaction")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteTransaction(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var transaction = await context.Transactions
            .Include(t => t.Balances)
            .FirstOrDefaultAsync(t => t.TransactionId == id, cancellationToken);
        if (transaction == null) return NotFound();

        var receipt = transaction.Photo;

        var group = await context.Groups
            .Include(group => group.Members)
            .Include(group => group.Balances)
            .FirstOrDefaultAsync(g => g.GroupId == transaction.GroupId && g.Members.Contains(user), cancellationToken);
        if (group is null)
            return BadRequest("Group not found");

        await using var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken);

        context.Transactions.Remove(transaction);
        await context.SaveChangesAsync(cancellationToken);

        ApplyGroupBalanceChanges(group, transaction.Balances, transaction.Currency, true);
        await context.SaveChangesAsync(cancellationToken);

        group.LastActivityTime = DateTime.UtcNow;
        group.TransactionCount--;
        await context.SaveChangesAsync(cancellationToken);

        await dbTransaction.CommitAsync(cancellationToken);

        await imageStorage.DeleteIfOwnedAsync(receipt, cancellationToken);

        return NoContent();
    }

    private void ApplyGroupBalanceChanges(Group group, IEnumerable<TransactionBalance> balances, string currency,
        bool negateDelta = false)
    {
        if (group.Balances == null) throw new InvalidOperationException("Group balances not loaded");
        var groupBalancesDict = group.Balances.ToDictionary(b => (b.UserId, b.Currency));
        foreach (var balance in balances)
            if (groupBalancesDict.TryGetValue((balance.UserId, currency), out var groupBalance))
            {
                groupBalance.Balance += negateDelta ? -balance.Balance : balance.Balance;
            }
            else
            {
                groupBalance = new GroupBalance
                {
                    GroupId = group.GroupId,
                    UserId = balance.UserId,
                    Balance = negateDelta ? -balance.Balance : balance.Balance,
                    Currency = currency
                };
                group.Balances.Add(groupBalance);
            }
    }
}