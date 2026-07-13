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
            if (endpointBuilder is not RouteEndpointBuilder routeEndpointBuilder
                || !SupportsPost(endpointBuilder))
                return;

            if (IsRoute(routeEndpointBuilder, "account/login"))
            {
                AddRateLimits(
                    endpointBuilder,
                    RateLimitPolicyNames.LoginIp,
                    "login",
                    LoginAccountRateLimitEndpointFilter.Create);
            }
            else if (IsRoute(routeEndpointBuilder, "account/register"))
            {
                AddRateLimits(
                    endpointBuilder,
                    RateLimitPolicyNames.RegistrationIp,
                    "registration",
                    RegistrationAccountRateLimitEndpointFilter.Create);
            }
        });

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

    private static bool IsRoute(RouteEndpointBuilder endpointBuilder, string route)
    {
        return string.Equals(
            endpointBuilder.RoutePattern.RawText?.TrimStart('/'),
            route,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportsPost(EndpointBuilder endpointBuilder)
    {
        return endpointBuilder.Metadata
            .OfType<IHttpMethodMetadata>()
            .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post, StringComparer.OrdinalIgnoreCase));
    }
}