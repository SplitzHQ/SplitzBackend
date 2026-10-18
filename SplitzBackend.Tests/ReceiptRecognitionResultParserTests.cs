using SplitzBackend.Services.Recognition;
using Xunit;

namespace SplitzBackend.Tests;

public class ReceiptRecognitionResultParserTests
{
    private static readonly string[] Categories = ["dining_out", "groceries", "fuel", "uncategorized"];

    [Fact]
    public void ParsesACleanReply()
    {
        const string reply = """
            {
              "name": "Blue Bottle Coffee",
              "merchant": "Blue Bottle Coffee Co.",
              "amount": 12.5,
              "currency": "usd",
              "transaction_time": "2026-03-14T18:30:00Z",
              "category": "dining_out",
              "location": "66 Mint St, San Francisco",
              "notes": "2 lattes, 1 croissant"
            }
            """;

        Assert.True(ReceiptRecognitionResultParser.TryParse(reply, Categories, out var result, out var error));
        Assert.Null(error);
        Assert.Equal("Blue Bottle Coffee", result.Name);
        Assert.Equal("Blue Bottle Coffee Co.", result.Merchant);
        Assert.Equal(12.50m, result.Amount);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(new DateTime(2026, 3, 14, 18, 30, 0, DateTimeKind.Utc), result.TransactionTime);
        Assert.Equal(DateTimeKind.Utc, result.TransactionTime!.Value.Kind);
        Assert.Equal("dining_out", result.Category);
        Assert.Equal("66 Mint St, San Francisco", result.Location);
        Assert.Equal("2 lattes, 1 croissant", result.Notes);
    }

    [Fact]
    public void AcceptsCodeFencesProseAndCamelCaseKeys()
    {
        const string reply = """
            Here is the result:
            ```json
            {"name": "Shell", "amount": "$1,234.56", "currency": "USD", "transactionTime": "2026-01-05", "category": "Fuel"}
            ```
            Let me know if you need anything else.
            """;

        Assert.True(ReceiptRecognitionResultParser.TryParse(reply, Categories, out var result, out _));
        Assert.Equal("Shell", result.Name);
        Assert.Equal(1234.56m, result.Amount);
        Assert.Equal(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), result.TransactionTime);
        Assert.Equal("fuel", result.Category);
    }

    [Theory]
    [InlineData("\"12,50\"", 12.50)]
    [InlineData("\"€ 1.234,50\"", 1234.50)]
    [InlineData("\"USD 7\"", 7)]
    [InlineData("19.999", 20.00)]
    public void ReadsAmountsInSeveralWrittenForms(string amountJson, decimal expected)
    {
        var reply = $"{{\"amount\": {amountJson}}}";

        Assert.True(ReceiptRecognitionResultParser.TryParse(reply, null, out var result, out _));
        Assert.Equal(expected, result.Amount);
    }

    [Fact]
    public void DropsValuesThatFailValidation()
    {
        const string reply = """
            {"amount": -3, "currency": "dollars", "category": "rocket_fuel", "transaction_time": "yesterday", "name": ""}
            """;

        Assert.True(ReceiptRecognitionResultParser.TryParse(reply, Categories, out var result, out _));
        Assert.Null(result.Amount);
        Assert.Null(result.Currency);
        Assert.Null(result.Category);
        Assert.Null(result.TransactionTime);
        Assert.Null(result.Name);
    }

    [Fact]
    public void KeepsAnyCategoryWhenNoListIsGiven()
    {
        Assert.True(ReceiptRecognitionResultParser.TryParse("{\"category\": \"Rocket_Fuel\"}", null, out var result,
            out _));
        Assert.Equal("rocket_fuel", result.Category);
    }

    [Fact]
    public void TruncatesLongTextToTheColumnSize()
    {
        var reply = $"{{\"location\": \"{new string('x', 500)}\"}}";

        Assert.True(ReceiptRecognitionResultParser.TryParse(reply, null, out var result, out _));
        Assert.Equal(ReceiptRecognitionResultParser.MaxLocationLength, result.Location!.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I cannot read this image.")]
    [InlineData("{\"name\": \"missing brace\"")]
    [InlineData("[1, 2, 3]")]
    public void ReportsRepliesWithoutAJsonObject(string? reply)
    {
        Assert.False(ReceiptRecognitionResultParser.TryParse(reply, null, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}