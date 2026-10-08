using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SplitzBackend.Services.RateLimiting;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitRejectionWriterTests
{
    [Fact]
    public async Task WritesNeutralProblemDetailsWithRetryAfter()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/account/recovery/reset";
        context.Request.QueryString = new QueryString(
            "?email=secret%40example.com&resetCode=secret-token&newPassword=SecretPassword1234");
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/account/recovery/reset"),
            order: 0,
            EndpointMetadataCollection.Empty,
            displayName: null));
        var logger = new CapturingLogger<RateLimitRejectionWriter>();
        var writer = new RateLimitRejectionWriter(Options.Create(new RateLimitOptions()), logger);

        await writer.WriteAsync(
            context,
            retryAfter: TimeSpan.FromMilliseconds(1200),
            category: "login",
            partitionType: "account");

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("2", context.Response.Headers.RetryAfter);

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Too Many Requests", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("rate_limit_exceeded", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("login", body.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", body.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var log = Assert.Single(logger.Messages);
        Assert.Contains("POST /account/recovery/reset", log, StringComparison.Ordinal);
        Assert.DoesNotContain("secret@example.com", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-token", log, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretPassword1234", log, StringComparison.Ordinal);
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }
}