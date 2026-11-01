using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;
using SplitzBackend.Services;
using Xunit;

namespace SplitzBackend.Tests;

/// <summary>Checks shared debt matching and per-user group balance summaries.</summary>
public sealed class GroupBalanceSummaryTests
{
    private readonly GroupBalanceSummaryService service = new();

    [Fact]
    public void ScreenshotBalancesProduceOnlyTheUsersSuggestedPayment()
    {
        var summary = Assert.Single(service.Summarize(ScreenshotBalances(), "me"));
        Assert.Equal(6.45m, summary.TotalBalance);
        var transfer = Assert.Single(summary.MemberBalances);
        Assert.Equal("debtor-c", transfer.UserId);
        Assert.Equal(-6.45m, transfer.Amount);
        Assert.Equal("USD", transfer.Currency);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-0.00000000000001")]
    [InlineData("0.00000000000001")]
    public void ZeroAndDecimalResidueProduceNoPayments(string amount)
    {
        var summary = Assert.Single(service.Summarize(
            [Balance("me", decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)),
                Balance("a", 10), Balance("b", -10)], "me"));
        Assert.Equal(0, summary.TotalBalance);
        Assert.Empty(summary.MemberBalances);
    }

    [Theory]
    [InlineData("JPY", "100", "100")]
    [InlineData("KWD", "1.234", "1.234")]
    [InlineData("USD", "1.235", "1.24")]
    public void PaymentsUseCurrencyPrecision(string currency, string raw, string expected)
    {
        var amount = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        var rounded = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        var summary = Assert.Single(service.Summarize(
            [Balance("me", amount, currency), Balance("a", -amount, currency)], "me"));
        Assert.Equal(rounded, summary.TotalBalance);
        Assert.Equal(-rounded, Assert.Single(summary.MemberBalances).Amount);
    }

    [Fact]
    public void DifferentCurrenciesRemainSeparate()
    {
        var summaries = service.Summarize([
            Balance("me", 10), Balance("a", -10),
            Balance("me", -7, "EUR"), Balance("a", 7, "EUR")], "me");
        Assert.Equal(2, summaries.Count);
        Assert.Equal(10m, summaries.Single(s => s.Currency == "USD").TotalBalance);
        Assert.Equal(-7m, summaries.Single(s => s.Currency == "EUR").TotalBalance);
    }

    [Fact]
    public void TransfersBalanceForEveryMemberAndAreIndependentOfInputOrder()
    {
        var balances = ScreenshotBalances();
        foreach (var balance in balances)
        {
            var summary = Assert.Single(service.Summarize(balances, balance.UserId));
            Assert.Equal(-summary.TotalBalance, summary.MemberBalances.Sum(t => t.Amount));
            var reordered = Assert.Single(service.Summarize(balances.AsEnumerable().Reverse(), balance.UserId));
            Assert.Equal(summary.MemberBalances, reordered.MemberBalances);
            foreach (var transfer in summary.MemberBalances)
            {
                var other = Assert.Single(service.Summarize(balances, transfer.UserId));
                Assert.Contains(other.MemberBalances, t => t.UserId == balance.UserId && t.Amount == -transfer.Amount);
            }
        }
    }

    [Fact]
    public void MatcherUsesStableUserIdsForTiesAndExactDecimalArithmetic()
    {
        var balances = new Dictionary<string, decimal> { ["me"] = 0.3m, ["a"] = -0.1m, ["b"] = -0.2m };
        Assert.Equal(new[] { new SuggestedDebt("b", "me", 0.2m), new SuggestedDebt("a", "me", 0.1m) },
            DebtSimplifier.Simplify(balances));
        var ties = new Dictionary<string, decimal> { ["b"] = -10m, ["me"] = 10m, ["a"] = -10m, ["creditor"] = 10m };
        Assert.Equal(DebtSimplifier.Simplify(ties), DebtSimplifier.Simplify(ties.Reverse()));
    }

    [Fact]
    public void InvoiceAndGroupUseTheSameMatcherWithoutRoundingInvoiceAmounts()
    {
        using var db = new SplitzDbContext(new DbContextOptionsBuilder<SplitzDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var invoiceService = new InvoiceDebtService(db);
        var invoiceId = Guid.NewGuid();
        var balances = ScreenshotBalances();
        var transaction = new Transaction
        {
            GroupId = Guid.NewGuid(),
            Group = null!,
            Name = "Dinner",
            Icon = "default",
            CreateTime = DateTime.UtcNow,
            TransactionTime = DateTime.UtcNow,
            Amount = 375.09m,
            Currency = "USD",
            Tags = [],
            Balances = balances.Select(b => new TransactionBalance { UserId = b.UserId, Balance = b.Balance }).ToList()
        };
        var debts = invoiceService.SimplifyDebts(invoiceId, [transaction]);
        Assert.All(debts, debt => Assert.Equal(invoiceId, debt.InvoiceId));
        foreach (var balance in balances)
        {
            var summary = Assert.Single(service.Summarize(balances, balance.UserId));
            var expected = debts.Where(d => d.FromUserId == balance.UserId || d.ToUserId == balance.UserId)
                .Select(d => d.ToUserId == balance.UserId ? -d.Amount : d.Amount);
            Assert.Equal(expected, summary.MemberBalances.Select(t => t.Amount));
        }
        transaction.Balances = [new() { UserId = "a", Balance = -1.234m }, new() { UserId = "b", Balance = 1.234m }];
        Assert.Equal(1.234m, Assert.Single(invoiceService.SimplifyDebts(invoiceId, [transaction])).Amount);
    }

    [Fact]
    public void EmptyGroupAndMemberWithoutBalanceHaveNoSuggestedPayments()
    {
        Assert.Empty(service.Summarize([], "me"));
        var summary = Assert.Single(service.Summarize(ScreenshotBalances(), "missing"));
        Assert.Equal(0, summary.TotalBalance);
        Assert.Empty(summary.MemberBalances);
    }

    /// <summary>Returns the balances from the reported display issue.</summary>
    private static List<GroupBalance> ScreenshotBalances() =>
    [
        Balance("me", 6.45m), Balance("debtor-a", -160.76m), Balance("creditor", 321.50m),
        Balance("debtor-b", -113.61m), Balance("settled", 0), Balance("debtor-c", -53.58m)
    ];

    /// <summary>Builds a group balance with its user loaded.</summary>
    private static GroupBalance Balance(string userId, decimal amount, string currency = "USD") => new()
    {
        GroupId = Guid.Empty,
        UserId = userId,
        User = new SplitzUser { Id = userId, UserName = userId },
        Balance = amount,
        Currency = currency
    };
}