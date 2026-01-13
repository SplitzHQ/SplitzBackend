using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SplitzBackend.OpenAPIGen.Filter;

public class DecimalAsStringSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(decimal) && context.Type != typeof(decimal?)) return;
        if (schema is not OpenApiSchema concrete) return;
        concrete.Type = JsonSchemaType.String;
        concrete.Format = "decimal";
    }
}