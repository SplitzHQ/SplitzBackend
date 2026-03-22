namespace SplitzBackend.Services;

public sealed record UploadImageResult(
    string ObjectKey,
    string ContentType
);

public interface IImageStorageService
{
    Task<UploadImageResult> UploadProcessedImageAsync(
        Stream input,
        string? inputContentType,
        string objectKey,
        ImageResizeRequest resize,
        CancellationToken cancellationToken);

    Task DeleteIfOwnedAsync(string? storedUrlOrKey, CancellationToken cancellationToken);
}

public sealed class ImageStorageService(IImageProcessingService imageProcessing, IObjectStorage objectStorage)
    : IImageStorageService
{
    public async Task<UploadImageResult> UploadProcessedImageAsync(
        Stream input,
        string? inputContentType,
        string objectKey,
        ImageResizeRequest resize,
        CancellationToken cancellationToken)
    {
        await using var processedImageStream = new MemoryStream();
        var processed = await imageProcessing.ProcessAsync(input, processedImageStream, resize, cancellationToken);
        // Append a Unix timestamp before the file extension so each upload produces a unique key,
        // allowing the previous pre-signed URL to remain cached while the new URL busts the cache.
        var versionedKey = $"{objectKey}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var keyWithExt = versionedKey + processed.FileExtension;
        await objectStorage.UploadAsync(keyWithExt, processed.ContentType, processed.Stream,
            cancellationToken);
        return new UploadImageResult(keyWithExt, processed.ContentType);
    }

    public Task DeleteIfOwnedAsync(string? storedUrlOrKey, CancellationToken cancellationToken)
    {
        return objectStorage.DeleteIfOwnedAsync(storedUrlOrKey, cancellationToken);
    }
}