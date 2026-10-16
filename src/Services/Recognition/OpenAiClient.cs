using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services.Recognition;

/// <summary>
///     Calls the <c>/chat/completions</c> route of an OpenAI-compatible API with the images inlined as
///     base64 data URLs. Only the fields every compatible provider understands are sent, so the same
///     request works against OpenAI, local gateways and other vendors' compatibility layers.
/// </summary>
public sealed class OpenAiClient(
    IHttpClientFactory httpClientFactory,
    IOptions<ReceiptRecognitionOptions> options,
    ILogger<OpenAiClient> logger) : IVisionModelClient
{
    public const string HttpClientName = "ReceiptRecognition";

    private const int MaxErrorBodyLength = 300;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<string> SendRequestAsync(
        ReceiptRecognitionProvider provider,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<RecognitionImage> images,
        CancellationToken cancellationToken)
    {
        var request = new ChatCompletionRequest
        {
            Model = provider.Model,
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = BuildUserContent(userPrompt, images) }
            ]
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, provider.ChatCompletionsUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8,
            "application/json");

        // The configured timeout bounds the whole call; the caller's token still cancels early.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));

        var client = httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VisionModelRequestException(
                $"The model did not answer within {options.Value.TimeoutSeconds} seconds.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Receipt recognition request to {Host} failed.", provider.ChatCompletionsUri.Host);
            throw new VisionModelRequestException("The model endpoint could not be reached.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Receipt recognition request to {Host} returned {StatusCode}.",
                    provider.ChatCompletionsUri.Host, (int)response.StatusCode);
                throw new VisionModelRequestException(
                    $"The model endpoint returned {(int)response.StatusCode}: {Truncate(body)}");
            }

            return ReadReplyText(body);
        }
    }

    /// <summary>The user turn: the text instruction first, then every image in upload order.</summary>
    private static List<ContentPart> BuildUserContent(string userPrompt, IReadOnlyList<RecognitionImage> images)
    {
        var parts = new List<ContentPart>(images.Count + 1) { new() { Type = "text", Text = userPrompt } };
        parts.AddRange(images.Select(image => new ContentPart
        {
            Type = "image_url",
            ImageUrl = new ImageUrl
            {
                Url = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Bytes)}"
            }
        }));
        return parts;
    }

    /// <summary>
    ///     Reads <c>choices[0].message.content</c>. Most providers return a string; some return a list of
    ///     content parts, in which case the text parts are joined.
    /// </summary>
    internal static string ReadReplyText(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new VisionModelRequestException("The model endpoint returned a reply that is not JSON.", ex);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0 ||
                !choices[0].TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content))
                throw new VisionModelRequestException("The model endpoint returned no message content.");

            switch (content.ValueKind)
            {
                case JsonValueKind.String:
                    return content.GetString() ?? string.Empty;
                case JsonValueKind.Array:
                    var text = new StringBuilder();
                    foreach (var part in content.EnumerateArray())
                        if (part.TryGetProperty("text", out var partText) && partText.ValueKind == JsonValueKind.String)
                            text.Append(partText.GetString());
                    return text.ToString();
                default:
                    throw new VisionModelRequestException("The model endpoint returned no message content.");
            }
        }
    }

    private static string Truncate(string value)
    {
        value = value.Trim();
        return value.Length <= MaxErrorBodyLength ? value : value[..MaxErrorBodyLength] + "…";
    }

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")] public required string Model { get; init; }
        [JsonPropertyName("messages")] public required List<ChatMessage> Messages { get; init; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public required string Role { get; init; }

        /// <summary>A plain string for the system turn, a list of parts for the user turn.</summary>
        [JsonPropertyName("content")]
        public required object Content { get; init; }
    }

    private sealed class ContentPart
    {
        [JsonPropertyName("type")] public required string Type { get; init; }
        [JsonPropertyName("text")] public string? Text { get; init; }
        [JsonPropertyName("image_url")] public ImageUrl? ImageUrl { get; init; }
    }

    private sealed class ImageUrl
    {
        [JsonPropertyName("url")] public required string Url { get; init; }
    }
}