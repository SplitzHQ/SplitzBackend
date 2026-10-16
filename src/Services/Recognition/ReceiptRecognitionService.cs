using Microsoft.Extensions.Options;
using SplitzBackend.Models;

namespace SplitzBackend.Services.Recognition;

/// <summary>One uploaded file, before processing.</summary>
public sealed record ReceiptImageUpload(Stream Content, string? ContentType);

/// <summary>Stores the images, asks the model about them and records the outcome.</summary>
public interface IReceiptRecognitionService
{
    /// <summary>
    ///     Processes and stores every upload, calls <paramref name="provider" /> once with all of them,
    ///     and saves a <see cref="ReceiptRecognition" /> row. A provider failure is recorded on the row
    ///     and does not throw; only an unreadable image throws <see cref="InvalidImageException" />.
    /// </summary>
    Task<ReceiptRecognition> RecognizeAsync(
        string userId,
        ReceiptRecognitionProvider provider,
        IReadOnlyList<ReceiptImageUpload> uploads,
        IReadOnlyCollection<string>? allowedCategories,
        CancellationToken cancellationToken);
}

/// <summary>Helpers for the object keys under which recognition images are stored.</summary>
public static class ReceiptRecognitionImageKeys
{
    public const string Prefix = "receipts/";

    public static string Build(string userId, Guid recognitionId, int index, string extension)
    {
        return $"{Prefix}{userId}/{recognitionId}/{index}{extension}";
    }

    /// <summary>
    ///     True for images owned by a recognition row. A transaction may show one of these as its photo,
    ///     but must not delete it, because the recognition record still points at it.
    /// </summary>
    public static bool IsRecognitionImage(string? objectKey)
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

    public async Task<ReceiptRecognition> RecognizeAsync(
        string userId,
        ReceiptRecognitionProvider provider,
        IReadOnlyList<ReceiptImageUpload> uploads,
        IReadOnlyCollection<string>? allowedCategories,
        CancellationToken cancellationToken)
    {
        var recognitionId = Guid.NewGuid();
        var images = new List<RecognitionImage>(uploads.Count);
        var imageKeys = new List<string>(uploads.Count);

        // Every image is resized once and that single encoding is both stored and sent to the model.
        for (var index = 0; index < uploads.Count; index++)
        {
            var processed = await ProcessAsync(uploads[index], index, cancellationToken);
            var key = ReceiptRecognitionImageKeys.Build(userId, recognitionId, index + 1, processed.FileExtension);
            using var content = new MemoryStream(processed.Bytes, false);
            await objectStorage.UploadAsync(key, processed.ContentType, content, cancellationToken);
            imageKeys.Add(key);
            images.Add(new RecognitionImage(processed.ContentType, processed.Bytes));
        }

        // Saved before the model call so the images stay traceable even if the request is cut off.
        var recognition = new ReceiptRecognition
        {
            ReceiptRecognitionId = recognitionId,
            UserId = userId,
            CreateTime = DateTime.UtcNow,
            ImageKeys = imageKeys,
            Endpoint = provider.Endpoint,
            Model = provider.Model,
            UsedCustomProvider = provider.IsCustom,
            Status = ReceiptRecognitionStatus.Pending
        };
        context.ReceiptRecognitions.Add(recognition);
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            var reply = await client.SendRequestAsync(
                provider,
                ReceiptRecognitionPrompt.BuildSystemPrompt(allowedCategories),
                ReceiptRecognitionPrompt.BuildUserPrompt(images.Count),
                images,
                cancellationToken);
            recognition.RawResponse = reply;

            if (ReceiptRecognitionResultParser.TryParse(reply, allowedCategories, out var result, out var parseError))
                Apply(recognition, result);
            else
                Fail(recognition, parseError!);
        }
        catch (VisionModelRequestException ex)
        {
            Fail(recognition, ex.Message);
        }

        await context.SaveChangesAsync(cancellationToken);
        return recognition;
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

    private static void Apply(ReceiptRecognition recognition, ReceiptRecognitionResult result)
    {
        recognition.Status = ReceiptRecognitionStatus.Succeeded;
        recognition.Error = null;
        recognition.Name = result.Name;
        recognition.Merchant = result.Merchant;
        recognition.Amount = result.Amount;
        recognition.Currency = result.Currency;
        recognition.TransactionTime = result.TransactionTime;
        recognition.Category = result.Category;
        recognition.Location = result.Location;
        recognition.Notes = result.Notes;
    }

    private static void Fail(ReceiptRecognition recognition, string error)
    {
        recognition.Status = ReceiptRecognitionStatus.Failed;
        recognition.Error = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
    }
}