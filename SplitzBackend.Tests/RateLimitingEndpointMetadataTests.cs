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
            .ToDictionary(
                endpoint => NormalizeRoute(endpoint.RoutePattern.RawText),
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal(9, protectedEndpoints.Count);
        AssertUploadEndpoint(protectedEndpoints["/account/avatar"]);
        AssertEndpoint(
                protectedEndpoints["/account/confirmemail"],
                "/account/confirmEmail",
                "email-confirmation-ip",
                "email-confirmation",
                HttpMethods.Get);
        AssertEndpoint(
                protectedEndpoints["/account/forgotpassword"],
                "/account/forgotPassword",
                "email-delivery-ip",
                "email-delivery");
        AssertEndpoint(
                protectedEndpoints["/account/login"],
                "/account/login",
                RateLimitPolicyNames.LoginIp,
                "login");
        AssertEndpoint(
                protectedEndpoints["/account/recovery/request"],
                "/account/recovery/request",
                "email-delivery-ip",
                "email-delivery");
        AssertEndpoint(
                protectedEndpoints["/account/recovery/reset"],
                "/account/recovery/reset",
                "password-reset-ip",
                "password-reset");
        AssertEndpoint(
                protectedEndpoints["/account/register"],
                "/account/register",
                "registration-ip",
                "registration");
        AssertEndpoint(
                protectedEndpoints["/account/resendconfirmationemail"],
                "/account/resendConfirmationEmail",
                "email-delivery-ip",
                "email-delivery");
        AssertEndpoint(
                protectedEndpoints["/account/resetpassword"],
                "/account/resetPassword",
                "password-reset-ip",
                "password-reset");
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

    private static void AssertUploadEndpoint(RouteEndpoint endpoint)
    {
        Assert.Contains(
            HttpMethods.Post,
            endpoint.Metadata.GetRequiredMetadata<IHttpMethodMetadata>().HttpMethods);
        Assert.Equal("upload-per-user", endpoint.Metadata.GetRequiredMetadata<EnableRateLimitingAttribute>().PolicyName);
        Assert.Contains(
            endpoint.Metadata,
            metadata => metadata.GetType().Name == "UploadRateLimitEndpointMetadataAttribute");
        Assert.DoesNotContain(endpoint.Metadata, metadata => metadata is AccountRateLimitEndpointMetadata);
    }

    private static string NormalizeRoute(string? route)
    {
        return $"/{route?.TrimStart('/').ToLowerInvariant()}";
    }
}