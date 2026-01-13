using NetVips;

namespace SplitzBackend.Services;

public sealed record ImageResizeRequest(
    int? MaxSize,
    bool KeepRatio = true
);

public sealed record ProcessedImage(
    Stream Stream,
    string ContentType,
    string FileExtension
);

public interface IImageProcessingService
{
    Task<ProcessedImage> ProcessAsync(Stream input, Stream output, ImageResizeRequest resize,
        CancellationToken cancellationToken);
}

public sealed class NetVipsImageProcessingService : IImageProcessingService
{
    private const int DefaultWebpQuality = 80;

    public async Task<ProcessedImage> ProcessAsync(Stream input, Stream output, ImageResizeRequest resize,
        CancellationToken cancellationToken)
    {
        // NetVips requires a seekable stream for some codecs; copy to memory to be safe.
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using var image = Image.NewFromStream(buffer);
        using var resized = await Resize(image, resize);

        // libvips determines format from extension; set basic quality.
        // Note: options are passed as a suffix, e.g. ".webp[Q=80]".
        resized.WriteToStream(output, $".webp[Q={DefaultWebpQuality}]");
        output.Position = 0;
        return new ProcessedImage(output, "image/webp", ".webp");
    }

    private static async Task<Image> Resize(Image image, ImageResizeRequest request)
    {
        if (request.MaxSize is null || request.MaxSize <= 0)
            return image;
        var maxSize = request.MaxSize.Value;
        var resizedImage = await Task.Run(() =>
            image.ThumbnailImage(maxSize, maxSize, Enums.Size.Down,
                crop: request.KeepRatio ? null : Enums.Interesting.Attention));
        return resizedImage;
    }
}