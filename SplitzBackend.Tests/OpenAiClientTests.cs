using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SplitzBackend.Services.Recognition;
using Xunit;

namespace SplitzBackend.Tests;

public class OpenAiClientTests
{
    private static readonly ReceiptRecognitionProvider Provider =
        new("https://llm.example.com/v1", "vision-model", "secret-key", true);

    [Fact]
    public async Task SendsOneChatRequestWithEveryImageInline()
    {
        var handler = new RecordingHandler(JsonReply("""{"choices":[{"message":{"role":"assistant","content":"{\"amount\": 1}"}}]}"""));
        var client = CreateClient(handler);
        var images = new[]
        {
            new RecognitionImage("image/webp", [1, 2, 3]),
            new RecognitionImage("image/webp", [4, 5, 6])
        };

        var reply = await client.SendRequestAsync(Provider, "system", "user", images, CancellationToken.None);

        Assert.Equal("{\"amount\": 1}", reply);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://llm.example.com/v1/chat/completions", request.Uri);
        Assert.Equal("Bearer secret-key", request.Authorization);

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("vision-model", body.RootElement.GetProperty("model").GetString());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("system", messages[0].GetProperty("content").GetString());

        var parts = messages[1].GetProperty("content");
        Assert.Equal(3, parts.GetArrayLength());
        Assert.Equal("text", parts[0].GetProperty("type").GetString());
        Assert.Equal("user", parts[0].GetProperty("text").GetString());
        Assert.Equal("image_url", parts[1].GetProperty("type").GetString());
        Assert.Equal("data:image/webp;base64,AQID", parts[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal("data:image/webp;base64,BAUG", parts[2].GetProperty("image_url").GetProperty("url").GetString());
        // Only the fields every compatible provider accepts are sent.
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
        Assert.False(body.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task JoinsTextPartsWhenContentIsAList()
    {
        var handler = new RecordingHandler(JsonReply(
            """{"choices":[{"message":{"content":[{"type":"text","text":"{\"a\":"},{"type":"text","text":"1}"}]}}]}"""));
        var client = CreateClient(handler);

        var reply = await client.SendRequestAsync(Provider, "s", "u", [], CancellationToken.None);

        Assert.Equal("{\"a\":1}", reply);
    }

    [Fact]
    public async Task ReportsProviderErrorsWithoutLeakingTheKey()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"message\":\"Incorrect API key provided\"}}", Encoding.UTF8,
                "application/json")
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<VisionModelRequestException>(() =>
            client.SendRequestAsync(Provider, "s", "u", [], CancellationToken.None));

        Assert.Contains("401", ex.Message);
        Assert.Contains("Incorrect API key", ex.Message);
        Assert.DoesNotContain("secret-key", ex.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":null}}]}")]
    public async Task RejectsRepliesWithoutContent(string body)
    {
        var client = CreateClient(new RecordingHandler(JsonReply(body)));

        await Assert.ThrowsAsync<VisionModelRequestException>(() =>
            client.SendRequestAsync(Provider, "s", "u", [], CancellationToken.None));
    }

    [Fact]
    public async Task TimesOutSlowProviders()
    {
        var client = CreateClient(new RecordingHandler(JsonReply("{}"), TimeSpan.FromSeconds(5)),
            timeoutSeconds: 1);

        var ex = await Assert.ThrowsAsync<VisionModelRequestException>(() =>
            client.SendRequestAsync(Provider, "s", "u", [], CancellationToken.None));

        Assert.Contains("did not answer", ex.Message);
    }

    private static OpenAiClient CreateClient(HttpMessageHandler handler,
        int timeoutSeconds = 30)
    {
        return new OpenAiClient(
            new SingleClientFactory(handler),
            Options.Create(new ReceiptRecognitionOptions { TimeoutSeconds = timeoutSeconds }),
            NullLogger<OpenAiClient>.Instance);
    }

    private static HttpResponseMessage JsonReply(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
        }
    }

    private sealed record RecordedRequest(string Uri, string? Authorization, string Body);

    private sealed class RecordingHandler(HttpResponseMessage response, TimeSpan? delay = null) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            if (delay is not null)
                await Task.Delay(delay.Value, cancellationToken);
            return response;
        }
    }
}