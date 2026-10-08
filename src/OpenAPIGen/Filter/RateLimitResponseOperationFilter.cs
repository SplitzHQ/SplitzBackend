using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SplitzBackend.OpenAPIGen.Filter;

internal sealed class RateLimitResponseOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var isRateLimited = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<EnableRateLimitingAttribute>()
            .Any();
        if (!isRateLimited)
            return;

        var problemDetailsSchema = context.SchemaGenerator.GenerateSchema(
            typeof(ProblemDetails),
            context.SchemaRepository);

        operation.Responses ??= new OpenApiResponses();
        operation.Responses[StatusCodes.Status429TooManyRequests.ToString()] = new OpenApiResponse
        {
            Description = "Too Many Requests",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new()
                {
                    Schema = problemDetailsSchema
                }
            },
            Headers = new Dictionary<string, IOpenApiHeader>
            {
                ["Retry-After"] = new OpenApiHeader
                {
                    Description = "delta-seconds or HTTP-date",
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String
                    }
                }
            }
        };
    }
}