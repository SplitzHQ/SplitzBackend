using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;

namespace SplitzBackend.Controllers;

[Authorize]
[ApiController]
[Route("[controller]")]
public class TransactionController(
    SplitzDbContext context,
    UserManager<SplitzUser> userManager,
    IMapper mapper) : ControllerBase
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

        return CreatedAtAction(nameof(GetTransaction), new { id = transaction.TransactionId }, mapper.Map<TransactionDto>(transaction));
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
        ApplyGroupBalanceChanges(existingTransaction.Group, existingTransaction.Balances, existingTransaction.Currency, true);
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
    /// <returns></returns>
    [HttpDelete("{id}", Name = "DeleteTransaction")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteTransaction(Guid id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var transaction = await context.Transactions
            .Include(t => t.Balances)
            .FirstOrDefaultAsync(t => t.TransactionId == id);
        if (transaction == null) return NotFound();

        var group = await context.Groups
            .Include(group => group.Members)
            .Include(group => group.Balances)
            .FirstOrDefaultAsync(g => g.GroupId == transaction.GroupId && g.Members.Contains(user));
        if (group is null)
            return BadRequest("Group not found");

        await using var dbTransaction = await context.Database.BeginTransactionAsync();

        context.Transactions.Remove(transaction);
        await context.SaveChangesAsync();

        ApplyGroupBalanceChanges(group, transaction.Balances, transaction.Currency, true);
        await context.SaveChangesAsync();

        group.LastActivityTime = DateTime.UtcNow;
        group.TransactionCount--;
        await context.SaveChangesAsync();

        await dbTransaction.CommitAsync();

        return NoContent();
    }

    private void ApplyGroupBalanceChanges(Group group, IEnumerable<TransactionBalance> balances, string currency, bool negateDelta = false)
    {
        if (group.Balances == null)
        {
            throw new InvalidOperationException("Group balances not loaded");
        }
        var groupBalancesDict = group.Balances.ToDictionary(b => (b.UserId, b.Currency));
        foreach (var balance in balances)
        {
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
}