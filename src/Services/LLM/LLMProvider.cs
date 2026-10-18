using SplitzBackend.Services.LLM;

namespace SplitzBackend.Services.LLM;

/// <summary>The server or user provider selected for an AI request.</summary>
/// <param name="Endpoint">Base URL, for example <c>https://api.openai.com/v1</c>, or a full chat completions URL.</param>
/// <param name="Model">Model name sent in the request body.</param>
/// <param name="ApiKey">Bearer token. Never stored on the recognition record or written to logs.</param>
/// <param name="IsCustom">True when the values came from the user's settings instead of the server.</param>
public sealed record LLMProvider(string Endpoint, string Model, string ApiKey, bool IsCustom)
{
    private const string ChatCompletionsPath = "/chat/completions";

    /// <summary>Accepts only absolute http(s) URLs so a stored endpoint can never point the server at a local file or socket.</summary>
    public static bool IsValidEndpoint(string? endpoint)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    ///     Resolves the chat completions URL. A base URL gets <c>/chat/completions</c> appended;
    ///     an endpoint that already ends with that path is used as is.
    /// </summary>
    public Uri ChatCompletionsUri
    {
        get
        {
            var trimmed = Endpoint.TrimEnd('/');
            if (!trimmed.EndsWith(ChatCompletionsPath, StringComparison.OrdinalIgnoreCase))
                trimmed += ChatCompletionsPath;
            return new Uri(trimmed, UriKind.Absolute);
        }
    }
}