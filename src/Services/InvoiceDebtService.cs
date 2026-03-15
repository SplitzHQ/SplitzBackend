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

    /// <summary>
    ///     Check if all debts are fully covered by settlements.
    /// </summary>
    bool CheckIfSettled(IEnumerable<InvoiceDebt> debts, IEnumerable<InvoiceSettlement> settlements);

    /// <summary>
    ///     Recalculate debts for an invoice. Called when transactions are modified or deleted.
    /// </summary>
    Task RecalculateInvoiceDebtsAsync(Guid invoiceId);
}

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

        // Split into creditors (positive) and debtors (negative)
        var creditors = netBalances
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (UserId: kv.Key, Amount: kv.Value))
            .ToList();

        var debtors = netBalances
            .Where(kv => kv.Value < 0)
            .OrderBy(kv => kv.Value) // most negative first
            .Select(kv => (UserId: kv.Key, Amount: -kv.Value)) // make positive
            .ToList();

        var debts = new List<InvoiceDebt>();
        var ci = 0;
        var di = 0;

        while (ci < creditors.Count && di < debtors.Count)
        {
            var creditor = creditors[ci];
            var debtor = debtors[di];
            var transfer = Math.Min(creditor.Amount, debtor.Amount);

            if (transfer > 0)
                debts.Add(new InvoiceDebt
                {
                    InvoiceId = invoiceId,
                    FromUserId = debtor.UserId,
                    ToUserId = creditor.UserId,
                    Amount = transfer
                });

            creditors[ci] = (creditor.UserId, creditor.Amount - transfer);
            debtors[di] = (debtor.UserId, debtor.Amount - transfer);

            if (creditors[ci].Amount == 0) ci++;
            if (debtors[di].Amount == 0) di++;
        }

        return debts;
    }

    public bool CheckIfSettled(IEnumerable<InvoiceDebt> debts, IEnumerable<InvoiceSettlement> settlements)
    {
        // Aggregate net settlement per (from, to) pair
        var settlementTotals = new Dictionary<(string From, string To), decimal>();
        foreach (var s in settlements)
        {
            var key = (s.FromUserId, s.ToUserId);
            settlementTotals.TryGetValue(key, out var current);
            settlementTotals[key] = current + s.Amount;
        }

        foreach (var debt in debts)
        {
            var key = (debt.FromUserId, debt.ToUserId);
            settlementTotals.TryGetValue(key, out var settled);
            if (settled < debt.Amount)
                return false;
        }

        return true;
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