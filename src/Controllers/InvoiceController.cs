using System.Text.Json;
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
public class InvoiceController(
    SplitzDbContext context,
    UserManager<SplitzUser> userManager,
    IMapper mapper,
    IInvoiceDebtService invoiceDebtService) : ControllerBase
{
    /// <summary>
    ///     List invoices for the current user's groups
    /// </summary>
    [HttpGet(Name = "GetInvoices")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<List<InvoiceReducedDto>>> GetInvoices()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var invoices = await context.Invoices
            .Where(i => i.Group.Members.Contains(user))
            .OrderByDescending(i => i.CreateTime)
            .Include(i => i.CreatedBy)
            .ToListAsync();

        return mapper.Map<List<InvoiceReducedDto>>(invoices);
    }

    /// <summary>
    ///     Get invoice by id with debts, settlements, and transactions
    /// </summary>
    [HttpGet("{id}", Name = "GetInvoice")]
    [Produces("application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(200)]
    public async Task<ActionResult<InvoiceDto>> GetInvoice(Guid id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var invoice = await context.Invoices
            .Where(i => i.Group.Members.Contains(user) && i.InvoiceId == id)
            .Include(i => i.CreatedBy)
            .Include(i => i.Transactions).ThenInclude(t => t.Balances).ThenInclude(b => b.User)
            .Include(i => i.Debts).ThenInclude(d => d.FromUser)
            .Include(i => i.Debts).ThenInclude(d => d.ToUser)
            .Include(i => i.Settlements).ThenInclude(s => s.FromUser)
            .Include(i => i.Settlements).ThenInclude(s => s.ToUser)
            .Include(i => i.Settlements).ThenInclude(s => s.RecordedBy)
            .FirstOrDefaultAsync();

        if (invoice is null)
            return NotFound();

        return mapper.Map<InvoiceDto>(invoice);
    }

    /// <summary>
    ///     Create an invoice from selected transactions in a group
    /// </summary>
    [HttpPost(Name = "CreateInvoice")]
    [Produces("application/json")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(201)]
    public async Task<ActionResult<InvoiceDto>> CreateInvoice(InvoiceInputDto input)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var group = await context.Groups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.GroupId == input.GroupId && g.Members.Contains(user));
        if (group is null)
            return BadRequest("Group not found");

        var transactions = await context.Transactions
            .Include(t => t.Balances)
            .Where(t => input.TransactionIds.Contains(t.TransactionId) && t.GroupId == input.GroupId)
            .ToListAsync();

        if (transactions.Count != input.TransactionIds.Count)
            return BadRequest("One or more transactions not found in the specified group");

        if (transactions.Any(t => t.Currency != input.Currency))
            return BadRequest("All transactions must have the same currency as the invoice");

        if (transactions.Any(t => t.InvoiceId != null))
            return BadRequest("One or more transactions already belong to another invoice");

        var invoice = new Invoice
        {
            InvoiceId = Guid.NewGuid(),
            GroupId = input.GroupId,
            Name = input.Name,
            Currency = input.Currency,
            CreatedByUserId = user.Id,
            CreateTime = DateTime.UtcNow,
            Status = InvoiceStatus.Open
        };

        await using var dbTransaction = await context.Database.BeginTransactionAsync();

        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        foreach (var transaction in transactions)
            transaction.InvoiceId = invoice.InvoiceId;
        await context.SaveChangesAsync();

        var debts = invoiceDebtService.SimplifyDebts(invoice.InvoiceId, transactions);
        context.Set<InvoiceDebt>().AddRange(debts);
        await context.SaveChangesAsync();

        await dbTransaction.CommitAsync();

        // Create notifications for involved group members (except creator)
        var involvedUserIds = debts
            .SelectMany(d => new[] { d.FromUserId, d.ToUserId })
            .Distinct()
            .Where(id => id != user.Id);

        foreach (var userId in involvedUserIds)
            context.Notifications.Add(new Notification
            {
                NotificationId = Guid.NewGuid(),
                UserId = userId,
                Type = "InvoiceCreated",
                ReferenceId = invoice.InvoiceId.ToString(),
                Data = JsonSerializer.Serialize(new InvoiceCreatedNotification
                {
                    CreatorName = user.UserName!,
                    InvoiceName = invoice.Name,
                    InvoiceId = invoice.InvoiceId,
                    GroupId = invoice.GroupId
                }, Notification.JsonOptions),
                IsRead = false,
                IsDismissed = false,
                CreateTime = DateTime.UtcNow
            });

        await context.SaveChangesAsync();

        // Reload for proper DTO mapping with navigation properties
        var result = await context.Invoices
            .Where(i => i.InvoiceId == invoice.InvoiceId)
            .Include(i => i.CreatedBy)
            .Include(i => i.Transactions).ThenInclude(t => t.Balances).ThenInclude(b => b.User)
            .Include(i => i.Debts).ThenInclude(d => d.FromUser)
            .Include(i => i.Debts).ThenInclude(d => d.ToUser)
            .Include(i => i.Settlements).ThenInclude(s => s.FromUser)
            .Include(i => i.Settlements).ThenInclude(s => s.ToUser)
            .Include(i => i.Settlements).ThenInclude(s => s.RecordedBy)
            .FirstAsync();

        return CreatedAtAction(nameof(GetInvoice), new { id = invoice.InvoiceId }, mapper.Map<InvoiceDto>(result));
    }

    /// <summary>
    ///     Update invoice transactions (add/remove). Recalculates debts.
    /// </summary>
    [HttpPut("{id}", Name = "UpdateInvoice")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> UpdateInvoice(Guid id, InvoiceInputDto input)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var invoice = await context.Invoices
            .Include(i => i.Transactions).ThenInclude(t => t.Balances)
            .Include(i => i.Debts)
            .Include(i => i.Group).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(i => i.InvoiceId == id);

        if (invoice is null)
            return NotFound();

        if (!invoice.Group.Members.Contains(user))
            return Unauthorized();

        if (invoice.GroupId != input.GroupId)
            return BadRequest("Changing the group of an invoice is not allowed");

        var newTransactions = await context.Transactions
            .Include(t => t.Balances)
            .Where(t => input.TransactionIds.Contains(t.TransactionId) && t.GroupId == input.GroupId)
            .ToListAsync();

        if (newTransactions.Count != input.TransactionIds.Count)
            return BadRequest("One or more transactions not found in the specified group");

        if (newTransactions.Any(t => t.Currency != input.Currency))
            return BadRequest("All transactions must have the same currency as the invoice");

        if (newTransactions.Any(t => t.InvoiceId != null && t.InvoiceId != id))
            return BadRequest("One or more transactions already belong to another invoice");

        await using var dbTransaction = await context.Database.BeginTransactionAsync();

        // Unlink old transactions
        foreach (var transaction in invoice.Transactions)
            transaction.InvoiceId = null;
        await context.SaveChangesAsync();

        // Link new transactions
        foreach (var transaction in newTransactions)
            transaction.InvoiceId = invoice.InvoiceId;

        invoice.Name = input.Name;
        invoice.Currency = input.Currency;

        // Recalculate debts
        context.Set<InvoiceDebt>().RemoveRange(invoice.Debts);
        await context.SaveChangesAsync();

        var debts = invoiceDebtService.SimplifyDebts(invoice.InvoiceId, newTransactions);
        context.Set<InvoiceDebt>().AddRange(debts);

        // Check if still settled
        invoice.Status = invoiceDebtService.CheckIfSettled(debts, invoice.Settlements ?? new())
            ? InvoiceStatus.Settled
            : InvoiceStatus.Open;

        await context.SaveChangesAsync();
        await dbTransaction.CommitAsync();

        return NoContent();
    }

    /// <summary>
    ///     Delete an invoice. Only the creator can delete.
    /// </summary>
    [HttpDelete("{id}", Name = "DeleteInvoice")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteInvoice(Guid id)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var invoice = await context.Invoices
            .Include(i => i.Transactions)
            .Include(i => i.Group).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(i => i.InvoiceId == id);

        if (invoice is null)
            return NotFound();

        if (!invoice.Group.Members.Contains(user))
            return Unauthorized();

        if (invoice.CreatedByUserId != user.Id)
            return BadRequest("Only the invoice creator can delete the invoice");

        await using var dbTransaction = await context.Database.BeginTransactionAsync();

        // Unlink transactions
        foreach (var transaction in invoice.Transactions)
            transaction.InvoiceId = null;
        await context.SaveChangesAsync();

        // Debts and settlements cascade-delete via FK
        context.Invoices.Remove(invoice);
        await context.SaveChangesAsync();

        await dbTransaction.CommitAsync();

        return NoContent();
    }

    /// <summary>
    ///     Record a settlement for an invoice
    /// </summary>
    [HttpPost("{id}/settlement", Name = "AddSettlement")]
    [Produces("application/json")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(201)]
    public async Task<ActionResult<InvoiceSettlementDto>> AddSettlement(Guid id,
        InvoiceSettlementInputDto input)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var invoice = await context.Invoices
            .Include(i => i.Debts)
            .Include(i => i.Settlements)
            .Include(i => i.Group).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(i => i.InvoiceId == id);

        if (invoice is null)
            return NotFound();

        if (!invoice.Group.Members.Contains(user))
            return Unauthorized();

        // Validate from/to users are involved in the invoice debts
        var involvedUserIds = invoice.Debts
            .SelectMany(d => new[] { d.FromUserId, d.ToUserId })
            .Distinct()
            .ToHashSet();

        if (!involvedUserIds.Contains(input.FromUserId) || !involvedUserIds.Contains(input.ToUserId))
            return BadRequest("From/To users must be involved in the invoice debts");

        if (input.FromUserId == input.ToUserId)
            return BadRequest("From and To users must be different");

        var settlement = new InvoiceSettlement
        {
            InvoiceSettlementId = Guid.NewGuid(),
            InvoiceId = id,
            FromUserId = input.FromUserId,
            ToUserId = input.ToUserId,
            Amount = input.Amount,
            RecordedByUserId = user.Id,
            RecordedTime = DateTime.UtcNow
        };

        context.Set<InvoiceSettlement>().Add(settlement);

        // Check if all debts are settled
        invoice.Status = invoiceDebtService.CheckIfSettled(invoice.Debts, invoice.Settlements.ToList())
            ? InvoiceStatus.Settled
            : InvoiceStatus.Open;

        await context.SaveChangesAsync();

        // Notify the receiver of the settlement
        var settlementData = JsonSerializer.Serialize(new SettlementRecordedNotification
        {
            RecorderName = user.UserName!,
            Amount = input.Amount,
            Currency = invoice.Currency,
            FromUserId = input.FromUserId,
            ToUserId = input.ToUserId,
            InvoiceId = invoice.InvoiceId,
            InvoiceName = invoice.Name
        }, Notification.JsonOptions);

        if (input.ToUserId != user.Id)
            context.Notifications.Add(new Notification
            {
                NotificationId = Guid.NewGuid(),
                UserId = input.ToUserId,
                Type = "SettlementRecorded",
                ReferenceId = invoice.InvoiceId.ToString(),
                Data = settlementData,
                IsRead = false,
                IsDismissed = false,
                CreateTime = DateTime.UtcNow
            });

        if (input.FromUserId != user.Id)
            context.Notifications.Add(new Notification
            {
                NotificationId = Guid.NewGuid(),
                UserId = input.FromUserId,
                Type = "SettlementRecorded",
                ReferenceId = invoice.InvoiceId.ToString(),
                Data = settlementData,
                IsRead = false,
                IsDismissed = false,
                CreateTime = DateTime.UtcNow
            });

        // Notify all involved when invoice is fully settled
        if (invoice.Status == InvoiceStatus.Settled)
        {
            var settledData = JsonSerializer.Serialize(new InvoiceSettledNotification
            {
                InvoiceId = invoice.InvoiceId,
                InvoiceName = invoice.Name,
                GroupId = invoice.GroupId
            }, Notification.JsonOptions);

            foreach (var userId in involvedUserIds.Where(uid => uid != user.Id))
                context.Notifications.Add(new Notification
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = userId,
                    Type = "InvoiceSettled",
                    ReferenceId = invoice.InvoiceId.ToString(),
                    Data = settledData,
                    IsRead = false,
                    IsDismissed = false,
                    CreateTime = DateTime.UtcNow
                });
        }

        await context.SaveChangesAsync();

        var result = await context.Set<InvoiceSettlement>()
            .Where(s => s.InvoiceSettlementId == settlement.InvoiceSettlementId)
            .Include(s => s.FromUser)
            .Include(s => s.ToUser)
            .Include(s => s.RecordedBy)
            .FirstAsync();

        return CreatedAtAction(nameof(GetInvoice), new { id = invoice.InvoiceId }, mapper.Map<InvoiceSettlementDto>(result));
    }

    /// <summary>
    ///     Remove a settlement from an invoice
    /// </summary>
    [HttpDelete("{id}/settlement/{settlementId}", Name = "DeleteSettlement")]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteSettlement(Guid id, Guid settlementId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var invoice = await context.Invoices
            .Include(i => i.Debts)
            .Include(i => i.Settlements)
            .Include(i => i.Group).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(i => i.InvoiceId == id);

        if (invoice is null)
            return NotFound();

        if (!invoice.Group.Members.Contains(user))
            return Unauthorized();

        var settlement = invoice.Settlements.FirstOrDefault(s => s.InvoiceSettlementId == settlementId);
        if (settlement is null)
            return NotFound();

        context.Set<InvoiceSettlement>().Remove(settlement);

        // Recheck settled status
        var remainingSettlements = invoice.Settlements.Where(s => s.InvoiceSettlementId != settlementId).ToList();
        invoice.Status = invoiceDebtService.CheckIfSettled(invoice.Debts, remainingSettlements)
            ? InvoiceStatus.Settled
            : InvoiceStatus.Open;

        await context.SaveChangesAsync();

        return NoContent();
    }

}