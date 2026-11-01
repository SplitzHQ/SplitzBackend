namespace SplitzBackend.Services;

/// <summary>A payment from a debtor to a creditor.</summary>
public sealed record SuggestedDebt(string FromUserId, string ToUserId, decimal Amount);

/// <summary>Matches the largest debtors to the largest creditors using decimal balances.</summary>
public static class DebtSimplifier
{
    /// <summary>Simplifies net balances. Positive amounts identify creditors; negative amounts identify debtors.</summary>
    public static List<SuggestedDebt> Simplify(IEnumerable<KeyValuePair<string, decimal>> netBalances)
    {
        var balances = netBalances.ToList();
        var creditors = balances
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (UserId: kv.Key, Amount: kv.Value))
            .ToList();
        var debtors = balances
            .Where(kv => kv.Value < 0)
            .OrderBy(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (UserId: kv.Key, Amount: -kv.Value))
            .ToList();

        var debts = new List<SuggestedDebt>();
        var ci = 0;
        var di = 0;
        while (ci < creditors.Count && di < debtors.Count)
        {
            var creditor = creditors[ci];
            var debtor = debtors[di];
            var transfer = Math.Min(creditor.Amount, debtor.Amount);
            debts.Add(new SuggestedDebt(debtor.UserId, creditor.UserId, transfer));

            creditors[ci] = (creditor.UserId, creditor.Amount - transfer);
            debtors[di] = (debtor.UserId, debtor.Amount - transfer);
            if (creditors[ci].Amount == 0) ci++;
            if (debtors[di].Amount == 0) di++;
        }

        return debts;
    }
}