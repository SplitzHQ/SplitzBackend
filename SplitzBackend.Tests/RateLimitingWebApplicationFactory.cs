using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SplitzBackend.Models;

namespace SplitzBackend.Tests;

public sealed class RateLimitingWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> testConfiguration;
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"splitz-rate-limiting-{Guid.NewGuid():N}.db");

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