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