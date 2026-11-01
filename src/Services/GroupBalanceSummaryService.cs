using System.Globalization;
using SplitzBackend.Models;

namespace SplitzBackend.Services;

/// <summary>Builds per-currency balance summaries for a group member.</summary>
public interface IGroupBalanceSummaryService
{
    /// <summary>Returns suggested payments involving the specified user.</summary>
    List<GroupBalanceSummaryDto> Summarize(IEnumerable<GroupBalance> balances, string userId);
}

/// <summary>Uses the shared invoice debt matcher to prepare group balance summaries.</summary>
public sealed class GroupBalanceSummaryService : IGroupBalanceSummaryService
{
    private static readonly Dictionary<string, int> CurrencyDigits = CultureInfo
        .GetCultures(CultureTypes.SpecificCultures)
        .Select(culture => (Currency: new RegionInfo(culture.Name).ISOCurrencySymbol,
            Digits: culture.NumberFormat.CurrencyDecimalDigits))
        .GroupBy(currency => currency.Currency)
        .ToDictionary(group => group.Key, group => group.First().Digits, StringComparer.Ordinal);

    /// <inheritdoc />
    public List<GroupBalanceSummaryDto> Summarize(IEnumerable<GroupBalance> balances, string userId)
    {
        return balances.GroupBy(balance => balance.Currency).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var digits = CurrencyDigits.GetValueOrDefault(group.Key, 2);
                var members = group.GroupBy(balance => balance.UserId)
                    .ToDictionary(member => member.Key, member => member.First().User.UserName ?? member.Key);
                // Round before matching so decimal residue cannot create a zero-value payment in the UI.
                var netBalances = group.GroupBy(balance => balance.UserId)
                    .ToDictionary(member => member.Key,
                        member => decimal.Round(member.Sum(balance => balance.Balance), digits,
                            MidpointRounding.AwayFromZero));
                var transfers = DebtSimplifier.Simplify(netBalances)
                    .Where(debt => debt.FromUserId == userId || debt.ToUserId == userId)
                    .Select(debt =>
                    {
                        var receiving = debt.ToUserId == userId;
                        var memberId = receiving ? debt.FromUserId : debt.ToUserId;
                        return new GroupMemberTransferDto
                        {
                            UserId = memberId,
                            Name = members[memberId],
                            Amount = receiving ? -debt.Amount : debt.Amount,
                            Currency = group.Key
                        };
                    }).ToList();
                return new GroupBalanceSummaryDto
                {
                    Currency = group.Key,
                    TotalBalance = netBalances.GetValueOrDefault(userId),
                    MemberBalances = transfers
                };
            }).ToList();
    }
}