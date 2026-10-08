using Microsoft.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace SplitzBackend.Tests;

public class RateLimitOpenApiTests
{
    private static readonly (string Path, HttpMethod Method)[] ProtectedOperations =
    [
        ("/account/avatar", HttpMethod.Post),
        ("/account/confirmEmail", HttpMethod.Get),
        ("/account/forgotPassword", HttpMethod.Post),
        ("/account/login", HttpMethod.Post),
        ("/account/recovery/request", HttpMethod.Post),
        ("/account/recovery/reset", HttpMethod.Post),
        ("/account/register", HttpMethod.Post),
        ("/account/resendConfirmationEmail", HttpMethod.Post),
        ("/account/resetPassword", HttpMethod.Post),
        ("/group/{groupId}/avatar", HttpMethod.Post),
        ("/transaction/{id}/receipt", HttpMethod.Post),
        ("/transactiondraft/{id}/receipt", HttpMethod.Post)
    ];

    [Fact]
    public void ProtectedOperationsDocumentRateLimitResponsesAndRetryHeader()
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var client = factory.CreateClient();
        var document = factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");
        Assert.NotNull(document.Paths);

        var documentedOperations = document.Paths!
            .SelectMany(path => (path.Value.Operations ?? [])
                .Where(operation => operation.Value.Responses?.ContainsKey("429") ?? false)
                .Select(operation => (path.Key, operation.Key.Method)))
            .OrderBy(operation => operation.Key, StringComparer.Ordinal)
            .ThenBy(operation => operation.Method, StringComparer.Ordinal)
            .ToArray();
        var expectedOperations = ProtectedOperations
            .Select(operation => (operation.Path, operation.Method.Method))
            .OrderBy(operation => operation.Path, StringComparer.Ordinal)
            .ThenBy(operation => operation.Method, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedOperations, documentedOperations);

        foreach (var (path, method) in ProtectedOperations)
        {
            var operation = GetOperation(document, path, method);
            Assert.NotNull(operation.Responses);
            var response = operation.Responses!["429"];

            Assert.Equal("Too Many Requests", response.Description);
            Assert.NotNull(response.Content);
            var mediaType = response.Content!["application/problem+json"];
            var schemaReference = Assert.IsType<OpenApiSchemaReference>(mediaType.Schema);
            Assert.NotNull(schemaReference.Reference);
            Assert.Equal("ProblemDetails", schemaReference.Reference!.Id);

            Assert.NotNull(response.Headers);
            var retryAfter = response.Headers!["Retry-After"];
            Assert.Equal(JsonSchemaType.String, retryAfter.Schema?.Type);
            Assert.Equal("delta-seconds or HTTP-date", retryAfter.Description);
        }
    }

    [Theory]
    [InlineData("/group", "GET")]
    [InlineData("/transaction", "POST")]
    [InlineData("/transactiondraft", "POST")]
    public void OutOfScopeOperationsDoNotDocumentRateLimitResponses(string path, string method)
    {
        using var factory = new RateLimitingWebApplicationFactory();
        using var client = factory.CreateClient();
        var document = factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        var operation = GetOperation(document, path, new HttpMethod(method));

        Assert.False(operation.Responses?.ContainsKey("429") ?? false);
    }

    private static OpenApiOperation GetOperation(
        OpenApiDocument document,
        string path,
        HttpMethod method)
    {
        Assert.NotNull(document.Paths);
        var pathItem = document.Paths![path];
        Assert.NotNull(pathItem.Operations);
        return pathItem.Operations![method];
    }
}