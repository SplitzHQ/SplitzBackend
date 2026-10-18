using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SplitzBackend.Models;
using SplitzBackend.Services;
using SplitzBackend.Services.Recognition;
using SplitzBackend.Services.LLM;

namespace SplitzBackend.Tests;

public sealed class RateLimitingWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> testConfiguration;
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"splitz-rate-limiting-{Guid.NewGuid():N}.db");

    internal CapturingRateLimitLogger Logs { get; } = new();

    public RateLimitingWebApplicationFactory()
        : this(new Dictionary<string, string?>())
    {
    }

    public RateLimitingWebApplicationFactory(IReadOnlyDictionary<string, string?> configurationOverrides)
    {
        testConfiguration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Sqlite"] = $"Data Source={databasePath};Pooling=False",
            ["Email:Enabled"] = "false",
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:Login:Ip:PermitLimit"] = "100",
            ["RateLimiting:Login:Account:PermitLimit"] = "1"
        };

        foreach (var pair in configurationOverrides)
            testConfiguration[pair.Key] = pair.Value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(testConfiguration);
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
            services.RemoveAll<IEmailSender<SplitzUser>>();
            services.AddSingleton<RecordingIdentityEmailSender>();
            services.AddSingleton<IEmailSender<SplitzUser>>(provider =>
                provider.GetRequiredService<RecordingIdentityEmailSender>());
            services.RemoveAll<IImageStorageService>();
            services.AddSingleton<BlockingImageStorageService>();
            services.AddSingleton<IImageStorageService>(provider =>
                provider.GetRequiredService<BlockingImageStorageService>());
            services.RemoveAll<IObjectStorage>();
            services.AddSingleton<IObjectStorage, PassThroughObjectStorage>();
            services.RemoveAll<IImageProcessingService>();
            services.AddSingleton<IImageProcessingService, PassThroughImageProcessingService>();
            services.RemoveAll<IVisionModelClient>();
            services.AddSingleton<FakeReceiptRecognitionClient>();
            services.AddSingleton<IVisionModelClient>(provider =>
                provider.GetRequiredService<FakeReceiptRecognitionClient>());
            services.RemoveAll<ILogger<Services.RateLimiting.RateLimitRejectionWriter>>();
            services.AddSingleton<ILogger<Services.RateLimiting.RateLimitRejectionWriter>>(Logs);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        DeleteDatabaseFile(databasePath);
        DeleteDatabaseFile($"{databasePath}-shm");
        DeleteDatabaseFile($"{databasePath}-wal");
    }

    private static void DeleteDatabaseFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

internal sealed class CapturingRateLimitLogger : ILogger<Services.RateLimiting.RateLimitRejectionWriter>
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> messages = new();

    public IReadOnlyCollection<string> Messages => messages.ToArray();

    public void Reset()
    {
        messages.Clear();
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        messages.Enqueue(formatter(state, exception));
    }
}

internal sealed class BlockingImageStorageService : IImageStorageService
{
    private readonly SemaphoreSlim enteredUploads = new(0);
    private TaskCompletionSource releaseUploads = CompletedRelease();
    private int uploadCount;

    public int UploadCount => Volatile.Read(ref uploadCount);

    public void StartBlocking()
    {
        releaseUploads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release()
    {
        releaseUploads.TrySetResult();
    }

    public async Task WaitForUploadsAsync(int count, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        for (var index = 0; index < count; index++)
            await enteredUploads.WaitAsync(cancellation.Token);
    }

    public async Task<UploadImageResult> UploadProcessedImageAsync(
        Stream input,
        string? inputContentType,
        string objectKey,
        ImageResizeRequest resize,
        CancellationToken cancellationToken)
    {
        var count = Interlocked.Increment(ref uploadCount);
        enteredUploads.Release();
        await releaseUploads.Task.WaitAsync(cancellationToken);
        return new UploadImageResult($"{objectKey}-{count}.webp", "image/webp");
    }

    public Task DeleteIfOwnedAsync(string? storedUrlOrKey, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static TaskCompletionSource CompletedRelease()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        release.SetResult();
        return release;
    }
}

/// <summary>
///     Object storage stand-in for tests: photo values are returned unchanged and nothing is uploaded.
/// </summary>
internal sealed class PassThroughObjectStorage : IObjectStorage
{
    public Task UploadAsync(string objectKey, string contentType, Stream content,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task DeleteIfOwnedAsync(string? storedUrlOrKey, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public string BuildRelativeUrl(string objectKey, TimeSpan roundingInterval, string cacheControl)
    {
        return "/" + objectKey;
    }

    public bool TryParseObjectKey(string? storedUrlOrKey, out string objectKey)
    {
        objectKey = string.Empty;
        return false;
    }
}

/// <summary>Image processing stand-in: the upload bytes pass through unchanged as a fake webp.</summary>
internal sealed class PassThroughImageProcessingService : IImageProcessingService
{
    public async Task<ProcessedImage> ProcessAsync(Stream input, Stream output, ImageResizeRequest resize,
        CancellationToken cancellationToken)
    {
        await input.CopyToAsync(output, cancellationToken);
        output.Position = 0;
        return new ProcessedImage(output, "image/webp", ".webp");
    }
}

/// <summary>Scripted model: returns the queued reply or throws the queued failure, recording every call.</summary>
internal sealed class FakeReceiptRecognitionClient : IVisionModelClient
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Func<string>> replies = new();

    public List<RecordedRecognitionCall> Calls { get; } = new();

    public void EnqueueReply(string reply)
    {
        replies.Enqueue(() => reply);
    }

    public void EnqueueFailure(string message)
    {
        replies.Enqueue(() => throw new VisionModelRequestException(message));
    }

    public Task<string> SendRequestAsync(
        LLMProvider provider,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<RecognitionImage> images,
        CancellationToken cancellationToken)
    {
        lock (Calls)
        {
            Calls.Add(new RecordedRecognitionCall(provider, systemPrompt, userPrompt, images.ToList()));
        }

        if (!replies.TryDequeue(out var next))
            throw new InvalidOperationException("No reply was queued for the fake recognition client.");
        return Task.FromResult(next());
    }
}

internal sealed record RecordedRecognitionCall(
    LLMProvider Provider,
    string SystemPrompt,
    string UserPrompt,
    List<RecognitionImage> Images);

internal sealed class RecordingIdentityEmailSender : IEmailSender<SplitzUser>
{
    private int deliveryCount;

    public int DeliveryCount => Volatile.Read(ref deliveryCount);

    public Task SendConfirmationLinkAsync(SplitzUser user, string email, string confirmationLink)
    {
        Interlocked.Increment(ref deliveryCount);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetLinkAsync(SplitzUser user, string email, string resetLink)
    {
        Interlocked.Increment(ref deliveryCount);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(SplitzUser user, string email, string resetCode)
    {
        Interlocked.Increment(ref deliveryCount);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        Interlocked.Exchange(ref deliveryCount, 0);
    }
}

internal sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Remote-IP", out var value)
                    && IPAddress.TryParse(value.ToString(), out var remoteAddress))
                    context.Connection.RemoteIpAddress = remoteAddress;

                await nextMiddleware();
            });
            next(app);
        };
    }
}