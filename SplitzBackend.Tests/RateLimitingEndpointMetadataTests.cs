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

        Assert.Equal(12, protectedEndpoints.Count);
        AssertUploadEndpoint(protectedEndpoints["/account/avatar"]);
        AssertUploadEndpoint(protectedEndpoints["/group/{groupid}/avatar"]);
        AssertUploadEndpoint(protectedEndpoints["/transaction/{id}/receipt"]);
        AssertUploadEndpoint(protectedEndpoints["/transactiondraft/{id}/receipt"]);
        AssertEndpoint(
                protectedEndpoints["/account/confirmemail"],
                "/account/confirmEmail",
                RateLimitPolicyNames.EmailConfirmationIp,
                HttpMethods.Get);
        AssertEndpoint(
                protectedEndpoints["/account/forgotpassword"],
                "/account/forgotPassword",
                RateLimitPolicyNames.EmailDeliveryIp);
        AssertEndpoint(
                protectedEndpoints["/account/login"],
                "/account/login",
                RateLimitPolicyNames.LoginIp);
        AssertEndpoint(
                protectedEndpoints["/account/recovery/request"],
                "/account/recovery/request",
                RateLimitPolicyNames.EmailDeliveryIp);
        AssertEndpoint(
                protectedEndpoints["/account/recovery/reset"],
                "/account/recovery/reset",
                RateLimitPolicyNames.PasswordResetIp);
        AssertEndpoint(
                protectedEndpoints["/account/register"],
                "/account/register",
                RateLimitPolicyNames.RegistrationIp);
        AssertEndpoint(
                protectedEndpoints["/account/resendconfirmationemail"],
                "/account/resendConfirmationEmail",
                RateLimitPolicyNames.EmailDeliveryIp);
        AssertEndpoint(
                protectedEndpoints["/account/resetpassword"],
                "/account/resetPassword",
                RateLimitPolicyNames.PasswordResetIp);

        var uploadEndpoints = endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<UploadRateLimitEndpointMetadataAttribute>() is not null)
            .Select(endpoint => NormalizeRoute(endpoint.RoutePattern.RawText))
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "/account/avatar",
                "/group/{groupid}/avatar",
                "/transaction/{id}/receipt",
                "/transactiondraft/{id}/receipt"
            ],
            uploadEndpoints);
    }

    private static void AssertEndpoint(
        RouteEndpoint endpoint,
        string route,
        string policyName,
        string method = "POST")
    {
        Assert.Equal(route, endpoint.RoutePattern.RawText);
        Assert.Contains(
            method,
            endpoint.Metadata.GetRequiredMetadata<IHttpMethodMetadata>().HttpMethods);
        Assert.Equal(
            policyName,
            endpoint.Metadata.GetRequiredMetadata<EnableRateLimitingAttribute>().PolicyName);
        Assert.Null(endpoint.Metadata.GetMetadata<UploadRateLimitEndpointMetadataAttribute>());
    }

    private static void AssertUploadEndpoint(RouteEndpoint endpoint)
    {
        Assert.Contains(
            HttpMethods.Post,
            endpoint.Metadata.GetRequiredMetadata<IHttpMethodMetadata>().HttpMethods);
        Assert.Equal(
            RateLimitPolicyNames.UploadPerUser,
            endpoint.Metadata.GetRequiredMetadata<EnableRateLimitingAttribute>().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<UploadRateLimitEndpointMetadataAttribute>());
    }

    private static string NormalizeRoute(string? route)
    {
        return $"/{route?.TrimStart('/').ToLowerInvariant()}";
    }
}