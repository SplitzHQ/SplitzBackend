using Microsoft.Extensions.Options;
using SplitzBackend.Services.Recognition;
using Xunit;

namespace SplitzBackend.Tests;

public class ReceiptRecognitionOptionsTests
{
    [Fact]
    public void DefaultsAreDisabledAndValid()
    {
        var options = new ReceiptRecognitionOptions();

        Assert.False(options.Enabled);
        Assert.False(options.IsServerProviderConfigured);
        Assert.True(options.AllowCustomProvider);
        Assert.Equal(6, options.MaxImages);
        Assert.Equal(1600, options.MaxImageSize);
        Assert.Equal(90, options.TimeoutSeconds);
        Assert.Empty(ReceiptRecognitionOptions.Validate(options));
    }

    [Fact]
    public void EnabledProviderNeedsEndpointModelAndKey()
    {
        var options = new ReceiptRecognitionOptions { Enabled = true, Endpoint = "not a url" };

        var failures = ReceiptRecognitionOptions.Validate(options);

        Assert.Contains(failures, f => f.Contains("Endpoint"));
        Assert.Contains(failures, f => f.Contains("Model"));
        Assert.Contains(failures, f => f.Contains("ApiKey"));
        Assert.False(options.IsServerProviderConfigured);
    }

    [Fact]
    public void CompleteProviderIsConfigured()
    {
        var options = new ReceiptRecognitionOptions
        {
            Enabled = true,
            Endpoint = "https://api.openai.com/v1",
            Model = "gpt-4.1-mini",
            ApiKey = "sk-test"
        };

        Assert.True(options.IsServerProviderConfigured);
        Assert.Empty(ReceiptRecognitionOptions.Validate(options));
        Assert.True(new ReceiptRecognitionOptionsValidator().Validate(Options.DefaultName, options).Succeeded);
    }

    [Fact]
    public void ValidatorRejectsNonPositiveLimits()
    {
        var options = new ReceiptRecognitionOptions { MaxImages = 0, MaxImageSize = 0, TimeoutSeconds = 0 };

        var result = new ReceiptRecognitionOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1/", "https://api.openai.com/v1/chat/completions")]
    [InlineData("http://localhost:11434/v1/chat/completions", "http://localhost:11434/v1/chat/completions")]
    public void ProviderResolvesTheChatCompletionsUrl(string endpoint, string expected)
    {
        var provider = new ReceiptRecognitionProvider(endpoint, "model", "key", false);

        Assert.Equal(expected, provider.ChatCompletionsUri.ToString());
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", true)]
    [InlineData("http://localhost:11434/v1", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("api.openai.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyHttpEndpointsAreValid(string? endpoint, bool expected)
    {
        Assert.Equal(expected, ReceiptRecognitionProvider.IsValidEndpoint(endpoint));
    }
}