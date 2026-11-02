using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using SplitzBackend.Models;
using SplitzBackend.Services;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace SplitzBackend.Tests;

/// <summary>Checks invoice payment progress and the response mapping.</summary>
public sealed class InvoiceDebtProgressTests
{
    /// <summary>Checks unpaid, partial, exact and excess payments without decimal rounding errors.</summary>
    [Theory]
    [InlineData("12.50", "0", "0", "12.50")]
    [InlineData("12.50", "2.10", "3.20", "7.20")]
    [InlineData("0.3", "0.1", "0.2", "0")]
    [InlineData("5", "8", "0", "0")]
    public void ProgressAndStatusUseTheSamePaymentTotals(string amount, string first, string second, string remaining)
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInvoiceDebtService>();
        var debt = CreateDebt(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));
        var payments = new[]
        {
            CreatePayment("ann", "bob", decimal.Parse(first, System.Globalization.CultureInfo.InvariantCulture)),
            CreatePayment("ann", "bob", decimal.Parse(second, System.Globalization.CultureInfo.InvariantCulture))
        };

        var progress = Assert.Single(service.GetDebtProgress([debt], payments));
        Assert.Same(debt, progress.Debt);
        Assert.Equal(payments.Sum(payment => payment.Amount), progress.Paid);
        Assert.Equal(decimal.Parse(remaining, System.Globalization.CultureInfo.InvariantCulture), progress.Remaining);
        Assert.Equal(progress.Remaining == 0m, service.CheckIfSettled([debt], payments));
    }

    /// <summary>Only payments between the same two people in the same direction cover a debt.</summary>
    [Fact]
    public void UnrelatedAndReversePaymentsDoNotCoverDebt()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInvoiceDebtService>();
        var debt = CreateDebt(10m);
        var payments = new[] { CreatePayment("bob", "ann", 10m), CreatePayment("ann", "cat", 10m) };

        var progress = Assert.Single(service.GetDebtProgress([debt], payments));
        Assert.Equal(0m, progress.Paid);
        Assert.Equal(10m, progress.Remaining);
        Assert.False(service.CheckIfSettled([debt], payments));
        Assert.Empty(service.GetDebtProgress([], payments));
        Assert.True(service.CheckIfSettled([], payments));
    }

    /// <summary>Each response debt contains its own progress and string-valued money fields in OpenAPI.</summary>
    [Fact]
    public void InvoiceMappingIncludesProgressForEveryDebt()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
        var first = CreateDebt(10m);
        var second = CreateDebt(4m);
        second.FromUserId = "cat";
        second.FromUser = new SplitzUser { Id = "cat", UserName = "Cat" };
        var invoice = new Invoice
        {
            InvoiceId = first.InvoiceId,
            GroupId = Guid.NewGuid(),
            Currency = "USD",
            CreatedByUserId = "ann",
            CreatedBy = first.FromUser,
            CreateTime = DateTime.UtcNow,
            Status = InvoiceStatus.Open,
            Debts = [first, second],
            Settlements = [CreatePayment("ann", "bob", 3m), CreatePayment("cat", "bob", 5m)]
        };

        var response = mapper.Map<InvoiceDto>(invoice);
        Assert.Collection(response.Debts,
            debt =>
            {
                Assert.Equal("ann", debt.FromUser.Id);
                Assert.Equal("bob", debt.ToUser.Id);
                Assert.Equal(10m, debt.Amount);
                Assert.Equal(3m, debt.Paid);
                Assert.Equal(7m, debt.Remaining);
            },
            debt =>
            {
                Assert.Equal("cat", debt.FromUser.Id);
                Assert.Equal(5m, debt.Paid);
                Assert.Equal(0m, debt.Remaining);
            });

        invoice.Settlements.Clear();
        Assert.All(mapper.Map<InvoiceDto>(invoice).Debts, debt =>
        {
            Assert.Equal(0m, debt.Paid);
            Assert.Equal(debt.Amount, debt.Remaining);
        });

        var schema = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1")
            .Components!.Schemas!["InvoiceDebtDto"];
        foreach (var field in new[] { "paid", "remaining" })
        {
            Assert.Equal(JsonSchemaType.String, schema.Properties![field].Type);
            Assert.Contains(field, schema.Required!);
        }
    }

    /// <summary>Creates a debt with user details for response mapping.</summary>
    private static InvoiceDebt CreateDebt(decimal amount) => new()
    {
        InvoiceId = Guid.Empty,
        FromUserId = "ann",
        FromUser = new SplitzUser { Id = "ann", UserName = "Ann" },
        ToUserId = "bob",
        ToUser = new SplitzUser { Id = "bob", UserName = "Bob" },
        Amount = amount
    };

    /// <summary>Creates a payment for the specified ordered pair.</summary>
    private static InvoiceSettlement CreatePayment(string from, string to, decimal amount) => new()
    {
        InvoiceId = Guid.Empty,
        FromUserId = from,
        FromUser = new SplitzUser { Id = from, UserName = from },
        ToUserId = to,
        ToUser = new SplitzUser { Id = to, UserName = to },
        Amount = amount,
        RecordedByUserId = from,
        RecordedBy = new SplitzUser { Id = from, UserName = from },
        RecordedTime = DateTime.UtcNow
    };
}