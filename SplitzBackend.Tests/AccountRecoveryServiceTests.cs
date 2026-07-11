using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SplitzBackend.Models;
using SplitzBackend.Services;
using Xunit;

namespace SplitzBackend.Tests;

public sealed class AccountRecoveryServiceTests
{
    [Fact]
    public async Task RequestPasswordResetSendsTokenForUnconfirmedUser()
    {
        await using var services = CreateServices();
        var userManager = services.GetRequiredService<UserManager<SplitzUser>>();
        var emailSender = services.GetRequiredService<RecordingEmailSender>();
        var recoveryService = services.GetRequiredService<AccountRecoveryService>();
        var user = new SplitzUser { UserName = "alice@example.com", Email = "alice@example.com" };
        Assert.True((await userManager.CreateAsync(user, "Password1234")).Succeeded);

        await recoveryService.RequestPasswordResetAsync("alice@example.com");

        Assert.NotNull(emailSender.ResetCode);
        Assert.False(await userManager.IsEmailConfirmedAsync(user));
    }

    [Fact]
    public async Task ResetPasswordConfirmsUnconfirmedUserAfterValidToken()
    {
        await using var services = CreateServices();
        var userManager = services.GetRequiredService<UserManager<SplitzUser>>();
        var emailSender = services.GetRequiredService<RecordingEmailSender>();
        var recoveryService = services.GetRequiredService<AccountRecoveryService>();
        var user = new SplitzUser { UserName = "alice@example.com", Email = "alice@example.com" };
        Assert.True((await userManager.CreateAsync(user, "Password1234")).Succeeded);
        await recoveryService.RequestPasswordResetAsync("alice@example.com");

        var result = await recoveryService.ResetPasswordAsync(
            "alice@example.com",
            emailSender.ResetCode!,
            "NewPassword1234");

        Assert.True(result.Succeeded);
        Assert.True(await userManager.IsEmailConfirmedAsync(user));
        Assert.True(await userManager.CheckPasswordAsync(user, "NewPassword1234"));
    }

    [Fact]
    public async Task InvalidResetTokenDoesNotConfirmUser()
    {
        await using var services = CreateServices();
        var userManager = services.GetRequiredService<UserManager<SplitzUser>>();
        var recoveryService = services.GetRequiredService<AccountRecoveryService>();
        var user = new SplitzUser { UserName = "alice@example.com", Email = "alice@example.com" };
        Assert.True((await userManager.CreateAsync(user, "Password1234")).Succeeded);
        var invalidCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("invalid-token"));

        var result = await recoveryService.ResetPasswordAsync(
            "alice@example.com",
            invalidCode,
            "NewPassword1234");

        Assert.False(result.Succeeded);
        Assert.False(await userManager.IsEmailConfirmedAsync(user));
        Assert.True(await userManager.CheckPasswordAsync(user, "Password1234"));
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<SplitzDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<SplitzUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddEntityFrameworkStores<SplitzDbContext>()
            .AddDefaultTokenProviders();
        services.AddSingleton<RecordingEmailSender>();
        services.AddSingleton<IEmailSender<SplitzUser>>(provider => provider.GetRequiredService<RecordingEmailSender>());
        services.AddScoped<AccountRecoveryService>();
        return services.BuildServiceProvider();
    }

    private sealed class RecordingEmailSender : IEmailSender<SplitzUser>
    {
        public string? ResetCode { get; private set; }

        public Task SendConfirmationLinkAsync(SplitzUser user, string email, string confirmationLink) =>
            Task.CompletedTask;

        public Task SendPasswordResetLinkAsync(SplitzUser user, string email, string resetLink) =>
            Task.CompletedTask;

        public Task SendPasswordResetCodeAsync(SplitzUser user, string email, string resetCode)
        {
            ResetCode = resetCode;
            return Task.CompletedTask;
        }
    }
}