using Microsoft.AspNetCore.Identity.Data;
using SplitzBackend.Services;
using SplitzBackend.Services.RateLimiting;

namespace SplitzBackend;

public static class AccountRecoveryEndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapAccountRecoveryEndpoints(this RouteGroupBuilder accountGroup)
    {
        accountGroup.MapPost("/recovery/request", async (
                ForgotPasswordRequest request,
                AccountRecoveryService accountRecoveryService) =>
            {
                await accountRecoveryService.RequestPasswordResetAsync(request.Email);
                return Results.Ok();
            })
            .WithName("RequestAccountRecovery")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK)
            .AddSplitzEmailDeliveryRateLimits();

        accountGroup.MapPost("/recovery/reset", async (
                ResetPasswordRequest request,
                AccountRecoveryService accountRecoveryService) =>
            {
                var result = await accountRecoveryService.ResetPasswordAsync(
                    request.Email,
                    request.ResetCode,
                    request.NewPassword);

                if (result.Succeeded)
                    return Results.Ok();

                var errors = result.Errors
                    .GroupBy(error => error.Code)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Description).ToArray());
                return Results.ValidationProblem(errors);
            })
            .WithName("ResetRecoveredAccountPassword")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .AddSplitzPasswordResetRateLimits();

        return accountGroup;
    }
}