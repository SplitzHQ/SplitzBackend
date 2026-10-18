using Microsoft.Extensions.Options;
using SplitzBackend.Services.Recognition;
using Xunit;
using SplitzBackend.Services.LLM;

namespace SplitzBackend.Tests;

public class ReceiptRecognitionOptionsTests
{
    [Fact]
    public void DefaultsAreDisabledAndValid()
    {
        var options = new ReceiptRecognitionOptions();

        Assert.Equal(6, options.MaxImages);
        Assert.Equal(1600, options.MaxImageSize);
        Assert.Empty(ReceiptRecognitionOptions.Validate(options));
    }

    /// <summary>Checks the default shared provider and its timeout.</summary>
    [Fact]
    public void SharedProviderDefaultsAreDisabledAndValid()
    {
        var options = new LLMOptions();

        Assert.False(options.Enabled);
        Assert.False(options.IsServerProviderConfigured);
        Assert.True(options.AllowCustomProvider);
        Assert.Equal(90, options.TimeoutSeconds);
        Assert.Empty(LLMOptions.Validate(options));
    }

    /// <summary>Checks timeout validation even when the server provider is disabled.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SharedProviderRejectsNonPositiveTimeout(int timeoutSeconds)
    {
        var result = new LLMOptionsValidator().Validate(Options.DefaultName,
            new LLMOptions { TimeoutSeconds = timeoutSeconds });

        Assert.True(result.Failed);
        Assert.Contains("LLM:TimeoutSeconds", Assert.Single(result.Failures!));
    }

    [Fact]
    public void EnabledProviderNeedsEndpointModelAndKey()
    {
        var options = new LLMOptions { Enabled = true, Endpoint = "not a url" };

        var failures = LLMOptions.Validate(options);

        Assert.Contains(failures, f => f.Contains("Endpoint"));
        Assert.Contains(failures, f => f.Contains("Model"));
        Assert.Contains(failures, f => f.Contains("ApiKey"));
        Assert.False(options.IsServerProviderConfigured);
    }

    [Fact]
    public void CompleteProviderIsConfigured()
    {
        var options = new LLMOptions
        {
            Enabled = true,
            Endpoint = "https://api.openai.com/v1",
            Model = "gpt-4.1-mini",
            ApiKey = "sk-test"
        };

        Assert.True(options.IsServerProviderConfigured);
        Assert.Empty(LLMOptions.Validate(options));
        Assert.True(new LLMOptionsValidator().Validate(Options.DefaultName, options).Succeeded);
    }

    [Fact]
    public void ValidatorRejectsNonPositiveLimits()
    {
        var options = new ReceiptRecognitionOptions { MaxImages = 0, MaxImageSize = 0 };

        var result = new ReceiptRecognitionOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures!.Count());
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1/", "https://api.openai.com/v1/chat/completions")]
    [InlineData("http://localhost:11434/v1/chat/completions", "http://localhost:11434/v1/chat/completions")]
    public void ProviderResolvesTheChatCompletionsUrl(string endpoint, string expected)
    {
        var provider = new LLMProvider(endpoint, "model", "key", false);

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
        Assert.Equal(expected, LLMProvider.IsValidEndpoint(endpoint));
    }
}