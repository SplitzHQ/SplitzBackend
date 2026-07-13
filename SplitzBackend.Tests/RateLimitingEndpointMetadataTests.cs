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
    public void ProtectedAccountEndpointsHaveTheirExpectedRateLimitMetadata()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var protectedEndpoints = endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null)
            .OrderBy(endpoint => endpoint.RoutePattern.RawText)
            .ToList();

        Assert.Collection(
            protectedEndpoints,
            endpoint => AssertEndpoint(
                endpoint,
                "/account/confirmEmail",
                "email-confirmation-ip",
                "email-confirmation",
                HttpMethods.Get),
            endpoint => AssertEndpoint(
                endpoint,
                "/account/forgotPassword",
                "email-delivery-ip",
                "email-delivery"),
            endpoint => AssertEndpoint(
                endpoint,
                "/account/login",
                RateLimitPolicyNames.LoginIp,
                "login"),
            endpoint => AssertEndpoint(
                endpoint,
                "/account/recovery/request",
                "email-delivery-ip",
                "email-delivery"),
            endpoint => AssertEndpoint(
                endpoint,
                "/account/register",
                "registration-ip",
                "registration"),
            endpoint => AssertEndpoint(
                endpoint,
                "/account/resendConfirmationEmail",
                "email-delivery-ip",
                "email-delivery"));
    }

    private static void AssertEndpoint(
        RouteEndpoint endpoint,
        string route,
        string policyName,
        string category,
        string method = "POST")
    {
        Assert.Equal(route, endpoint.RoutePattern.RawText);
        Assert.Contains(
            method,
            endpoint.Metadata.GetRequiredMetadata<IHttpMethodMetadata>().HttpMethods);
        Assert.Equal(
            policyName,
            endpoint.Metadata.GetRequiredMetadata<EnableRateLimitingAttribute>().PolicyName);
        Assert.Equal(
            new RateLimitEndpointMetadata(category, "ip"),
            endpoint.Metadata.GetRequiredMetadata<RateLimitEndpointMetadata>());
        Assert.Equal(
            new AccountRateLimitEndpointMetadata(category),
            endpoint.Metadata.GetRequiredMetadata<AccountRateLimitEndpointMetadata>());
    }
}