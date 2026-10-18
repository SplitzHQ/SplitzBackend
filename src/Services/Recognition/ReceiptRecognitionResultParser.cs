using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SplitzBackend.Services.Recognition;

/// <summary>The fields read from the model reply, already validated and trimmed to the column sizes.</summary>
public sealed record ReceiptRecognitionResult(
    string? Name,
    string? Merchant,
    decimal? Amount,
    string? Currency,
    DateTime? TransactionTime,
    string? Category,
    string? Location,
    string? Notes);

/// <summary>
///     Turns the model's reply into a <see cref="ReceiptRecognitionResult" />. Models do not always obey
///     the output instructions, so the parser accepts code fences, text around the object, camelCase or
///     snake_case keys, and amounts written as strings with currency symbols.
/// </summary>
public static partial class ReceiptRecognitionResultParser
{
    public const int MaxNameLength = 256;
    public const int MaxMerchantLength = 256;
    public const int MaxLocationLength = 128;
    public const int MaxNotesLength = 1000;
    public const int MaxCategoryLength = 50;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    ///     Parses <paramref name="raw" />. When <paramref name="allowedCategories" /> is given, a category
    ///     outside that list becomes null. Returns false with a reason when no JSON object can be read.
    /// </summary>
    public static bool TryParse(
        string? raw,
        IReadOnlyCollection<string>? allowedCategories,
        out ReceiptRecognitionResult result,
        out string? error)
    {
        result = new ReceiptRecognitionResult(null, null, null, null, null, null, null, null);
        error = null;

        var json = ExtractJsonObject(raw);
        if (json is null)
        {
            error = "The model reply did not contain a JSON object.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException)
        {
            error = "The model reply was not valid JSON.";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "The model reply was not a JSON object.";
                return false;
            }

            result = new ReceiptRecognitionResult(
                ReadString(root, MaxNameLength, "name", "title"),
                ReadString(root, MaxMerchantLength, "merchant", "vendor", "store"),
                ReadAmount(root),
                ReadCurrency(root),
                ReadDateTime(root),
                ReadCategory(root, allowedCategories),
                ReadString(root, MaxLocationLength, "location", "address"),
                ReadString(root, MaxNotesLength, "notes", "note", "description"));
            return true;
        }
    }

    /// <summary>
    ///     Finds the outermost braces in the reply. Code fences and any prose before or after the object
    ///     are dropped.
    /// </summary>
    internal static string? ExtractJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        return raw[start..(end + 1)];
    }

    /// <summary>Looks up the first present key; names are matched ignoring case and underscores.</summary>
    private static bool TryGetProperty(JsonElement root, string[] names, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            var normalized = property.Name.Replace("_", "", StringComparison.Ordinal);
            foreach (var name in names)
            {
                if (!normalized.Equals(name.Replace("_", "", StringComparison.Ordinal),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                value = property.Value;
                return value.ValueKind != JsonValueKind.Null;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement root, int maxLength, params string[] names)
    {
        if (!TryGetProperty(root, names, out var value))
            return null;

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    /// <summary>Accepts a JSON number or a string such as "$1,234.50". Zero and negative totals are dropped.</summary>
    private static decimal? ReadAmount(JsonElement root)
    {
        if (!TryGetProperty(root, ["amount", "total", "total_amount"], out var value))
            return null;

        decimal amount;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDecimal(out amount):
                break;
            case JsonValueKind.String:
                var digits = NormalizeDecimalMarks(AmountNoiseRegex().Replace(value.GetString() ?? string.Empty, ""));
                if (!decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
                    return null;
                break;
            default:
                return null;
        }

        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        return amount > 0 ? amount : null;
    }

    /// <summary>
    ///     Rewrites "1.234,50" and "12,50" to "1234.50" and "12.50". When both marks appear, the last one
    ///     is the decimal mark. A lone comma is a decimal mark only when exactly two digits follow it.
    /// </summary>
    internal static string NormalizeDecimalMarks(string digits)
    {
        var lastComma = digits.LastIndexOf(',');
        var lastDot = digits.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
            return lastComma > lastDot
                ? digits.Replace(".", "", StringComparison.Ordinal).Replace(',', '.')
                : digits.Replace(",", "", StringComparison.Ordinal);

        if (lastComma >= 0)
            return lastComma == digits.Length - 3
                ? digits.Replace(',', '.')
                : digits.Replace(",", "", StringComparison.Ordinal);

        return digits;
    }

    private static string? ReadCurrency(JsonElement root)
    {
        var currency = ReadString(root, 10, "currency", "currency_code")?.ToUpperInvariant();
        return currency is not null && CurrencyRegex().IsMatch(currency) ? currency : null;
    }

    /// <summary>A time without an offset is taken as UTC; the client shows it in local time either way.</summary>
    private static DateTime? ReadDateTime(JsonElement root)
    {
        var text = ReadString(root, 64, "transaction_time", "date", "datetime", "time", "issued_at");
        if (text is null)
            return null;

        return DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadCategory(JsonElement root, IReadOnlyCollection<string>? allowedCategories)
    {
        var category = ReadString(root, MaxCategoryLength, "category")?.Trim().ToLowerInvariant();
        if (category is null)
            return null;
        if (allowedCategories is not { Count: > 0 })
            return category;

        return allowedCategories.FirstOrDefault(allowed =>
            allowed.Equals(category, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"[^\d.,\-]")]
    private static partial Regex AmountNoiseRegex();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyRegex();
}