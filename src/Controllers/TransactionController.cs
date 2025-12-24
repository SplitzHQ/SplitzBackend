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

        var group = await context.Groups.Include(group => group.Members)
            .FirstOrDefaultAsync(g => g.GroupId == transaction.GroupId && g.Members.Contains(user));
        if (group is null)
            return BadRequest("Group not found");
        group.LastActivityTime = DateTime.Now;
        group.TransactionCount++;

        var transactionMembers = transaction.Balances.Select(b => b.UserId);
        if (transactionMembers.Any(id => !group.Members.Select(m => m.Id).Contains(id)))
            return BadRequest("Balance user not in group");

        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        // Reload the transaction with related data and map to DTO
        var transactionDto = await mapper.ProjectTo<TransactionDto>(
            context.Transactions
                .Include(t => t.Balances)
                    .ThenInclude(b => b.User)
                .Where(t => t.TransactionId == transaction.TransactionId)
        ).FirstOrDefaultAsync();

        return CreatedAtAction(nameof(GetTransaction), new { id = transaction.TransactionId }, transactionDto);
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

        var transaction = mapper.Map<Transaction>(transactionInputDto);

        if (transaction.TransactionId != transactionId)
            return BadRequest("Transaction id does not match");
        var group = await context.Groups.Include(group => group.Members)
            .FirstOrDefaultAsync(g => g.GroupId == transaction.GroupId && g.Members.Contains(user));
        if (group is null)
            return BadRequest("Group not found");
        group.LastActivityTime = DateTime.Now;
        var transactionMembers = transaction.Balances.Select(b => b.UserId);
        if (transactionMembers.Any(id => !group.Members.Select(m => m.Id).Contains(id)))
            return BadRequest("Balance user not in group");

        context.Entry(transaction).State = EntityState.Modified;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!TransactionExists(transactionId))
                return NotFound();
            throw;
        }

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

        var transaction = await context.Transactions.FindAsync(id);
        if (transaction == null) return NotFound();

        var group = await context.Groups.Include(group => group.Members)
            .FirstOrDefaultAsync(g => g.GroupId == transaction.GroupId && g.Members.Contains(user));
        if (group is null)
            return BadRequest("Group not found");
        group.LastActivityTime = DateTime.UtcNow;
        group.TransactionCount--;

        context.Transactions.Remove(transaction);
        await context.SaveChangesAsync();

        return NoContent();
    }

    private bool TransactionExists(Guid id)
    {
        return context.Transactions.Any(e => e.TransactionId == id);
    }
}