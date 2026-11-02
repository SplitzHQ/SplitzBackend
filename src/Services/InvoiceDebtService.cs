using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;

namespace SplitzBackend.Services;

public interface IInvoiceDebtService
{
    /// <summary>
    ///     Simplify debts from transaction balances using greedy matching.
    ///     Positive balance = creditor (paid more), negative = debtor (owes money).
    /// </summary>
    List<InvoiceDebt> SimplifyDebts(Guid invoiceId, List<Transaction> transactions);

    /// <summary>Returns each debt with payments in the same direction and the amount still owed.</summary>
    List<InvoiceDebtProgress> GetDebtProgress(IEnumerable<InvoiceDebt> debts,
        IEnumerable<InvoiceSettlement> settlements);

    /// <summary>
    ///     Check if all debts are fully covered by settlements.
    /// </summary>
    bool CheckIfSettled(IEnumerable<InvoiceDebt> debts, IEnumerable<InvoiceSettlement> settlements);

    /// <summary>
    ///     Recalculate debts for an invoice. Called when transactions are modified or deleted.
    /// </summary>
    Task RecalculateInvoiceDebtsAsync(Guid invoiceId);
}

/// <summary>A debt with the total paid and the amount still owed.</summary>
public sealed record InvoiceDebtProgress(InvoiceDebt Debt, decimal Paid, decimal Remaining);

public sealed class InvoiceDebtService(SplitzDbContext context) : IInvoiceDebtService
{
    public List<InvoiceDebt> SimplifyDebts(Guid invoiceId, List<Transaction> transactions)
    {
        // Aggregate net balance per user
        var netBalances = new Dictionary<string, decimal>();
        foreach (var transaction in transactions)
            foreach (var balance in transaction.Balances)
            {
                if (!netBalances.TryGetValue(balance.UserId, out var current))
                    current = 0;
                netBalances[balance.UserId] = current + balance.Balance;
            }

        return DebtSimplifier.Simplify(netBalances)
            .Select(debt => new InvoiceDebt
            {
                InvoiceId = invoiceId,
                FromUserId = debt.FromUserId,
                ToUserId = debt.ToUserId,
                Amount = debt.Amount
            })
            .ToList();
    }

    /// <inheritdoc />
    public List<InvoiceDebtProgress> GetDebtProgress(IEnumerable<InvoiceDebt> debts,
        IEnumerable<InvoiceSettlement> settlements)
    {
        // Reverse payments do not cover a debt. Match the ordered user pair.

        var settlementTotals = new Dictionary<(string From, string To), decimal>();
        foreach (var s in settlements)
        {
            var key = (s.FromUserId, s.ToUserId);
            settlementTotals.TryGetValue(key, out var current);
            settlementTotals[key] = current + s.Amount;
        }

        return debts.Select(debt =>
        {
            settlementTotals.TryGetValue((debt.FromUserId, debt.ToUserId), out var paid);
            return new InvoiceDebtProgress(debt, paid, Math.Max(0m, debt.Amount - paid));
        }).ToList();
    }

    /// <inheritdoc />
    public bool CheckIfSettled(IEnumerable<InvoiceDebt> debts, IEnumerable<InvoiceSettlement> settlements)
    {
        return GetDebtProgress(debts, settlements).All(progress => progress.Remaining == 0m);
    }

    public async Task RecalculateInvoiceDebtsAsync(Guid invoiceId)
    {
        var invoice = await context.Invoices
            .Include(i => i.Transactions).ThenInclude(t => t.Balances)
            .Include(i => i.Debts)
            .Include(i => i.Settlements)
            .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);

        if (invoice is null) return;

        context.Set<InvoiceDebt>().RemoveRange(invoice.Debts);

        var debts = SimplifyDebts(invoice.InvoiceId, invoice.Transactions);
        context.Set<InvoiceDebt>().AddRange(debts);

        invoice.Status = CheckIfSettled(debts, invoice.Settlements)
            ? InvoiceStatus.Settled
            : InvoiceStatus.Open;

        await context.SaveChangesAsync();
    }
}