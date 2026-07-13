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
                || !IsLoginRoute(routeEndpointBuilder)
                || !SupportsPost(endpointBuilder))
                return;

            endpointBuilder.Metadata.Add(new EnableRateLimitingAttribute(RateLimitPolicyNames.LoginIp));
            endpointBuilder.Metadata.Add(new RateLimitEndpointMetadata("login", "ip"));
            endpointBuilder.Metadata.Add(new AccountRateLimitEndpointMetadata("login"));
            endpointBuilder.FilterFactories.Add(LoginAccountRateLimitEndpointFilter.Create);
        });

        return builder;
    }

    private static bool IsLoginRoute(RouteEndpointBuilder endpointBuilder)
    {
        return string.Equals(
            endpointBuilder.RoutePattern.RawText?.TrimStart('/'),
            "account/login",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportsPost(EndpointBuilder endpointBuilder)
    {
        return endpointBuilder.Metadata
            .OfType<IHttpMethodMetadata>()
            .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post, StringComparer.OrdinalIgnoreCase));
    }
}