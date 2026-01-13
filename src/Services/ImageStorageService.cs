namespace SplitzBackend.Services;

public sealed record UploadImageResult(
    string Url,
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
        var keyWithExt = objectKey.EndsWith(processed.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? objectKey
            : objectKey + processed.FileExtension;
        var url = await objectStorage.UploadAsync(keyWithExt, processed.ContentType, processed.Stream,
            cancellationToken);
        return new UploadImageResult(url, keyWithExt, processed.ContentType);
    }

    public Task DeleteIfOwnedAsync(string? storedUrlOrKey, CancellationToken cancellationToken)
    {
        return objectStorage.DeleteIfOwnedAsync(storedUrlOrKey, cancellationToken);
    }
}