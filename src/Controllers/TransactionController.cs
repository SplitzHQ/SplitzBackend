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

        await using var dbTransaction = await context.Database.BeginTransactionAsync();

        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        await RecalculateGroupBalancesAsync(group.GroupId);
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

        context.Entry(existingTransaction).CurrentValues.SetValues(mapper.Map<Transaction>(transactionInputDto));
        await context.SaveChangesAsync();

        await RecalculateGroupBalancesAsync(existingTransaction.Group.GroupId);
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

        var transaction = await context.Transactions.FindAsync(id);
        if (transaction == null) return NotFound();

        var group = await context.Groups.Include(group => group.Members)
            .FirstOrDefaultAsync(g => g.GroupId == transaction.GroupId && g.Members.Contains(user));
        if (group is null)
            return BadRequest("Group not found");

        await using var dbTransaction = await context.Database.BeginTransactionAsync();

        context.Transactions.Remove(transaction);
        await context.SaveChangesAsync();

        await RecalculateGroupBalancesAsync(group.GroupId);
        await context.SaveChangesAsync();

        group.LastActivityTime = DateTime.UtcNow;
        group.TransactionCount--;
        await context.SaveChangesAsync();

        await dbTransaction.CommitAsync();

        return NoContent();
    }

    private async Task RecalculateGroupBalancesAsync(Guid groupId)
    {
        var balancesSet = context.Set<GroupBalance>();

        var existingBalances = await balancesSet.Where(b => b.GroupId == groupId).ToListAsync();
        if (existingBalances.Count != 0)
            balancesSet.RemoveRange(existingBalances);

        var transactions = await context.Transactions
            .AsNoTracking()
            .Include(t => t.Balances)
            .Where(t => t.GroupId == groupId)
            .ToListAsync();

        if (transactions.Count == 0)
            return;

        var currency = transactions[0].Currency;
        if (transactions.Any(t => t.Currency != currency))
            throw new InvalidOperationException("Group contains transactions with multiple currencies; GroupBalance does not support this.");

        var net = new Dictionary<(string UserId, string FriendUserId), decimal>();

        foreach (var transaction in transactions)
        {
            var creditors = transaction.Balances
                .Where(b => b.Balance > 0)
                .Select(b => (b.UserId, Amount: b.Balance))
                .ToList();
            var debtors = transaction.Balances
                .Where(b => b.Balance < 0)
                .Select(b => (b.UserId, Amount: -b.Balance))
                .ToList();

            var creditorIndex = 0;
            var debtorIndex = 0;

            while (creditorIndex < creditors.Count && debtorIndex < debtors.Count)
            {
                var (creditorId, creditorAmount) = creditors[creditorIndex];
                var (debtorId, debtorAmount) = debtors[debtorIndex];

                var payAmount = creditorAmount < debtorAmount ? creditorAmount : debtorAmount;
                if (payAmount != 0 && debtorId != creditorId)
                {
                    AddNet(net, debtorId, creditorId, payAmount);
                    AddNet(net, creditorId, debtorId, -payAmount);
                }

                creditorAmount -= payAmount;
                debtorAmount -= payAmount;

                creditors[creditorIndex] = (creditorId, creditorAmount);
                debtors[debtorIndex] = (debtorId, debtorAmount);

                if (creditorAmount == 0)
                    creditorIndex++;
                if (debtorAmount == 0)
                    debtorIndex++;
            }
        }

        foreach (var ((userId, friendUserId), balance) in net)
        {
            if (balance == 0)
                continue;

            balancesSet.Add(new GroupBalance
            {
                GroupId = groupId,
                UserId = userId,
                FriendUserId = friendUserId,
                Balance = balance,
                Currency = currency
            });
        }
    }

    private static void AddNet(Dictionary<(string UserId, string FriendUserId), decimal> net, string userId,
        string friendUserId, decimal delta)
    {
        var key = (userId, friendUserId);
        if (net.TryGetValue(key, out var existing))
            net[key] = existing + delta;
        else
            net[key] = delta;
    }

    private bool TransactionExists(Guid id)
    {
        return context.Transactions.Any(e => e.TransactionId == id);
    }
}