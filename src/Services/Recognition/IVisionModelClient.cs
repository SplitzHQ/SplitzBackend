using SplitzBackend.Services.LLM;

namespace SplitzBackend.Services.Recognition;

/// <summary>One image sent to the model, already resized and re-encoded.</summary>
public sealed record VisionModelImage(string ContentType, byte[] Bytes);

/// <summary>Talks to a vision model. Implementations return the assistant's reply text as is.</summary>
public interface IVisionModelClient
{
    /// <summary>
    ///     Sends the prompts and every image in one chat request and returns the reply text.
    ///     Throws <see cref="VisionModelRequestException" /> when the provider rejects the request
    ///     or returns a reply the client cannot read.
    /// </summary>
    Task<string> SendRequestAsync(
        LLMProvider provider,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<VisionModelImage> images,
        CancellationToken cancellationToken);
}

/// <summary>A provider-side failure. The message is safe to show to the user and never holds the API key.</summary>
public sealed class VisionModelRequestException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Raised when an file cannot be decoded as an image.</summary>
public sealed class InvalidImageException(string message, Exception? inner = null) : Exception(message, inner);