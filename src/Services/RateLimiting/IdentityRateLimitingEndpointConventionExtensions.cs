using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace SplitzBackend.Services.RateLimiting;

/// <summary>
/// Attaches the per-IP policy and the per-account filter to the anonymous account endpoints. Identity's
/// endpoints are matched by route because <c>MapIdentityApi</c> returns them as one group.
/// </summary>
public static class IdentityRateLimitingEndpointConventionExtensions
{
    public static IEndpointConventionBuilder AddSplitzIdentityRateLimits(
        this IEndpointConventionBuilder builder)
    {
        builder.Add(endpointBuilder =>
        {
            if (endpointBuilder is not RouteEndpointBuilder routeEndpointBuilder)
                return;

            if (IsRoute(routeEndpointBuilder, "account/confirmEmail")
                && SupportsMethod(endpointBuilder, HttpMethods.Get))
            {
                AddRateLimits(
                    endpointBuilder,
                    RateLimitPolicyNames.EmailConfirmationIp,
                    AccountRateLimitEndpointFilter.ForUserId(RateLimitCategories.EmailConfirmation));
            }
            else if (IsRoute(routeEndpointBuilder, "account/login")
                && SupportsMethod(endpointBuilder, HttpMethods.Post))
            {
                AddRateLimits(
                    endpointBuilder,
                    RateLimitPolicyNames.LoginIp,
                    AccountRateLimitEndpointFilter.ForEmail(
                        RateLimitCategories.Login,
                        arguments => arguments.OfType<LoginRequest>().SingleOrDefault()?.Email));
            }
            else if (IsRoute(routeEndpointBuilder, "account/register")
                && SupportsMethod(endpointBuilder, HttpMethods.Post))
            {
                AddRateLimits(
                    endpointBuilder,
                    RateLimitPolicyNames.RegistrationIp,
                    AccountRateLimitEndpointFilter.ForEmail(
                        RateLimitCategories.Registration,
                        arguments => arguments.OfType<RegisterRequest>().SingleOrDefault()?.Email));
            }
            else if ((IsRoute(routeEndpointBuilder, "account/forgotPassword")
                    || IsRoute(routeEndpointBuilder, "account/resendConfirmationEmail"))
                && SupportsMethod(endpointBuilder, HttpMethods.Post))
            {
                AddEmailDeliveryRateLimits(endpointBuilder);
            }
            else if (IsRoute(routeEndpointBuilder, "account/resetPassword")
                && SupportsMethod(endpointBuilder, HttpMethods.Post))
            {
                AddPasswordResetRateLimits(endpointBuilder);
            }
        });

        return builder;
    }

    public static IEndpointConventionBuilder AddSplitzEmailDeliveryRateLimits(
        this IEndpointConventionBuilder builder)
    {
        builder.Add(AddEmailDeliveryRateLimits);
        return builder;
    }

    public static IEndpointConventionBuilder AddSplitzPasswordResetRateLimits(
        this IEndpointConventionBuilder builder)
    {
        builder.Add(AddPasswordResetRateLimits);
        return builder;
    }

    private static void AddRateLimits(
        EndpointBuilder endpointBuilder,
        string ipPolicyName,
        Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> accountFilterFactory)
    {
        endpointBuilder.Metadata.Add(new EnableRateLimitingAttribute(ipPolicyName));
        endpointBuilder.FilterFactories.Add(accountFilterFactory);
    }

    private static void AddEmailDeliveryRateLimits(EndpointBuilder endpointBuilder)
    {
        AddRateLimits(
            endpointBuilder,
            RateLimitPolicyNames.EmailDeliveryIp,
            AccountRateLimitEndpointFilter.ForEmail(
                RateLimitCategories.EmailDelivery,
                arguments => arguments.OfType<ForgotPasswordRequest>().SingleOrDefault()?.Email
                    ?? arguments.OfType<ResendConfirmationEmailRequest>().SingleOrDefault()?.Email));
    }

    private static void AddPasswordResetRateLimits(EndpointBuilder endpointBuilder)
    {
        AddRateLimits(
            endpointBuilder,
            RateLimitPolicyNames.PasswordResetIp,
            AccountRateLimitEndpointFilter.ForEmail(
                RateLimitCategories.PasswordReset,
                arguments => arguments.OfType<ResetPasswordRequest>().SingleOrDefault()?.Email));
    }

    private static bool IsRoute(RouteEndpointBuilder endpointBuilder, string route)
    {
        return string.Equals(
            endpointBuilder.RoutePattern.RawText?.TrimStart('/'),
            route,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportsMethod(EndpointBuilder endpointBuilder, string method)
    {
        return endpointBuilder.Metadata
            .OfType<IHttpMethodMetadata>()
            .Any(metadata => metadata.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase));
    }
}