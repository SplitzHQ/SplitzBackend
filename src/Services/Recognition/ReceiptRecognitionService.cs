using Microsoft.Extensions.Options;
using SplitzBackend.Models;
using SplitzBackend.Services.LLM;

namespace SplitzBackend.Services.Recognition;

/// <summary>One uploaded file, before processing.</summary>
public sealed record ReceiptImageUpload(Stream Content, string? ContentType);

/// <summary>Stores the images, asks the model about them and records the outcome.</summary>
public interface IReceiptRecognitionService
{
    /// <summary>
    ///     Processes and stores every upload, calls <paramref name="provider" /> once with all of them,
    ///     and saves a <see cref="Receipt" /> row. A provider failure is recorded on the row
    ///     and does not throw; only an unreadable image throws <see cref="InvalidImageException" />.
    /// </summary>
    Task<Receipt> RecognizeAsync(
        string userId,
        LLMProvider provider,
        IReadOnlyList<ReceiptImageUpload> uploads,
        IReadOnlyCollection<string>? allowedCategories,
        CancellationToken cancellationToken);
}

/// <summary>Helpers for the object keys under which receipt images are stored.</summary>
public static class ReceiptImageKeys
{
    public const string Prefix = "receipts/";

    public static string Build(string userId, Guid receiptId, int index, string extension)
    {
        return $"{Prefix}{userId}/{receiptId}/{index}{extension}";
    }

    /// <summary>
    ///     Checks if the object key corresponds to an image is owned by a receipt row.
    /// </summary>
    public static bool IsReceiptImage(string? objectKey)
    {
        return objectKey is not null && objectKey.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ReceiptRecognitionService(
    SplitzDbContext context,
    IImageProcessingService imageProcessing,
    IObjectStorage objectStorage,
    IVisionModelClient client,
    IOptions<ReceiptRecognitionOptions> options,
    ILogger<ReceiptRecognitionService> logger) : IReceiptRecognitionService
{
    private const int MaxErrorLength = 1000;

    public async Task<Receipt> RecognizeAsync(
        string userId,
        LLMProvider provider,
        IReadOnlyList<ReceiptImageUpload> uploads,
        IReadOnlyCollection<string>? allowedCategories,
        CancellationToken cancellationToken)
    {
        var receiptId = Guid.NewGuid();
        var images = new List<VisionModelImage>(uploads.Count);
        var imageKeys = new List<string>(uploads.Count);

        // Every image is resized once and that single encoding is both stored and sent to the model.
        for (var index = 0; index < uploads.Count; index++)
        {
            var processed = await ProcessAsync(uploads[index], index, cancellationToken);
            var key = ReceiptImageKeys.Build(userId, receiptId, index + 1, processed.FileExtension);
            using var content = new MemoryStream(processed.Bytes, false);
            await objectStorage.UploadAsync(key, processed.ContentType, content, cancellationToken);
            imageKeys.Add(key);
            images.Add(new VisionModelImage(processed.ContentType, processed.Bytes));
        }

        // Saved before the model call so the images stay traceable even if the request is cut off.
        var receipt = new Receipt
        {
            ReceiptId = receiptId,
            UserId = userId,
            CreateTime = DateTime.UtcNow,
            ImageKeys = imageKeys,
            Endpoint = provider.Endpoint,
            Model = provider.Model,
            UsedCustomProvider = provider.IsCustom,
            Status = ReceiptRecognitionStatus.Pending
        };
        context.Receipts.Add(receipt);
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            var reply = await client.SendRequestAsync(
                provider,
                ReceiptRecognitionPrompt.BuildSystemPrompt(allowedCategories),
                ReceiptRecognitionPrompt.BuildUserPrompt(images.Count),
                images,
                cancellationToken);
            receipt.RawResponse = reply;

            if (ReceiptRecognitionResultParser.TryParse(reply, allowedCategories, out var result, out var parseError))
                Apply(receipt, result);
            else
                Fail(receipt, parseError!);
        }
        catch (VisionModelRequestException ex)
        {
            Fail(receipt, ex.Message);
        }

        await context.SaveChangesAsync(cancellationToken);
        return receipt;
    }

    private async Task<(byte[] Bytes, string ContentType, string FileExtension)> ProcessAsync(
        ReceiptImageUpload upload, int index, CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        try
        {
            var processed = await imageProcessing.ProcessAsync(upload.Content, output,
                new ImageResizeRequest(options.Value.MaxImageSize), cancellationToken);
            return (output.ToArray(), processed.ContentType, processed.FileExtension);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation(ex, "Receipt image {Index} could not be decoded.", index + 1);
            throw new InvalidImageException($"File {index + 1} is not a readable image.", ex);
        }
    }

    private static void Apply(Receipt receipt, ReceiptRecognitionResult result)
    {
        receipt.Status = ReceiptRecognitionStatus.Succeeded;
        receipt.Error = null;
        receipt.Name = result.Name;
        receipt.Merchant = result.Merchant;
        receipt.Amount = result.Amount;
        receipt.Currency = result.Currency;
        receipt.TransactionTime = result.TransactionTime;
        receipt.Category = result.Category;
        receipt.Location = result.Location;
        receipt.Notes = result.Notes;
    }

    private static void Fail(Receipt receipt, string error)
    {
        receipt.Status = ReceiptRecognitionStatus.Failed;
        receipt.Error = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
    }
}