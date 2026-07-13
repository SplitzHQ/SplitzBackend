using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
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
        var writer = new RateLimitRejectionWriter(NullLogger<RateLimitRejectionWriter>.Instance);

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
    }
}