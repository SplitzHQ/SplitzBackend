using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace SplitzBackend.Services.RateLimiting;

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
                    "email-confirmation",
                    EmailConfirmationAccountRateLimitEndpointFilter.Create);
            }
            else if (IsRoute(routeEndpointBuilder, "account/login")
                && SupportsMethod(endpointBuilder, HttpMethods.Post))
            {
                AddRateLimits(
                    endpointBuilder,
                    RateLimitPolicyNames.LoginIp,
                    "login",
                    LoginAccountRateLimitEndpointFilter.Create);
            }
            else if (IsRoute(routeEndpointBuilder, "account/register")
                && SupportsMethod(endpointBuilder, HttpMethods.Post))
            {
                AddRateLimits(
                    endpointBuilder,
                    RateLimitPolicyNames.RegistrationIp,
                    "registration",
                    RegistrationAccountRateLimitEndpointFilter.Create);
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
        string policyName,
        string category,
        Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> filterFactory)
    {
        endpointBuilder.Metadata.Add(new EnableRateLimitingAttribute(policyName));
        endpointBuilder.Metadata.Add(new RateLimitEndpointMetadata(category, "ip"));
        endpointBuilder.Metadata.Add(new AccountRateLimitEndpointMetadata(category));
        endpointBuilder.FilterFactories.Add(filterFactory);
    }

    private static void AddEmailDeliveryRateLimits(EndpointBuilder endpointBuilder)
    {
        AddRateLimits(
            endpointBuilder,
            RateLimitPolicyNames.EmailDeliveryIp,
            "email-delivery",
            EmailDeliveryAccountRateLimitEndpointFilter.Create);
    }

    private static void AddPasswordResetRateLimits(EndpointBuilder endpointBuilder)
    {
        AddRateLimits(
            endpointBuilder,
            RateLimitPolicyNames.PasswordResetIp,
            "password-reset",
            PasswordResetAccountRateLimitEndpointFilter.Create);
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