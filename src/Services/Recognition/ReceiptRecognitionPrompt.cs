using System.Text;

namespace SplitzBackend.Services.Recognition;

/// <summary>Builds the instructions sent with the receipt images.</summary>
public static class ReceiptRecognitionPrompt
{
    /// <summary>
    ///     The system turn. It names every field the parser understands and asks for a bare JSON object,
    ///     because not every compatible provider supports a JSON response format parameter.
    /// </summary>
    public static string BuildSystemPrompt(IReadOnlyCollection<string>? allowedCategories)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("You read photos of receipts, invoices and bills and extract one expense from them.");
        prompt.AppendLine("All images belong to the same expense, for example several pages or photos of one receipt.");
        prompt.AppendLine("Reply with exactly one JSON object and no other text, no code fence and no explanation.");
        prompt.AppendLine("Use this shape; use null for anything the images do not show, and never guess numbers:");
        prompt.AppendLine("{");
        prompt.AppendLine("  \"name\": \"short title for the expense, usually the merchant or the purpose\",");
        prompt.AppendLine("  \"merchant\": \"merchant or issuer name\",");
        prompt.AppendLine("  \"amount\": 12.34,");
        prompt.AppendLine("  \"currency\": \"three-letter ISO 4217 code in upper case, for example USD\",");
        prompt.AppendLine("  \"transaction_time\": \"when the receipt was issued, ISO 8601, for example 2026-03-14T18:30:00\",");
        prompt.AppendLine("  \"category\": \"one of the allowed category keys\",");
        prompt.AppendLine("  \"location\": \"address or city printed on the receipt\",");
        prompt.AppendLine("  \"notes\": \"one short line listing the main items\"");
        prompt.AppendLine("}");
        prompt.AppendLine("\"amount\" is the final total that was paid, after tax, tip and discounts, as a plain number.");
        prompt.AppendLine("Infer the currency from symbols, language and location when no code is printed.");

        if (allowedCategories is { Count: > 0 })
        {
            prompt.AppendLine("\"category\" must be one of these keys, or null when none fits:");
            prompt.AppendLine(string.Join(", ", allowedCategories));
        }
        else
        {
            prompt.AppendLine("\"category\" is a short lower_snake_case word for the kind of expense.");
        }

        return prompt.ToString();
    }

    /// <summary>The user turn that accompanies the images.</summary>
    public static string BuildUserPrompt(int imageCount)
    {
        return imageCount == 1
            ? "Extract the expense from this image."
            : $"Extract the expense from these {imageCount} images.";
    }
}