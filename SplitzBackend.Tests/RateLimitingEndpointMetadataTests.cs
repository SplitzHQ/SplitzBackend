using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SplitzBackend.Services.RateLimiting;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitingEndpointMetadataTests
{
    [Fact]
    public void OnlyTheGeneratedLoginPostEndpointHasTaskOneRateLimitMetadata()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var protectedEndpoints = endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null)
            .ToList();

        var loginEndpoint = Assert.Single(protectedEndpoints);
        Assert.Equal("/account/login", loginEndpoint.RoutePattern.RawText);
        Assert.Contains(
            HttpMethods.Post,
            loginEndpoint.Metadata.GetRequiredMetadata<IHttpMethodMetadata>().HttpMethods);
        Assert.Equal(
            RateLimitPolicyNames.LoginIp,
            loginEndpoint.Metadata.GetRequiredMetadata<EnableRateLimitingAttribute>().PolicyName);
        Assert.Equal(
            new RateLimitEndpointMetadata("login", "ip"),
            loginEndpoint.Metadata.GetRequiredMetadata<RateLimitEndpointMetadata>());
        Assert.Equal(
            new AccountRateLimitEndpointMetadata("login"),
            loginEndpoint.Metadata.GetRequiredMetadata<AccountRateLimitEndpointMetadata>());
    }
}