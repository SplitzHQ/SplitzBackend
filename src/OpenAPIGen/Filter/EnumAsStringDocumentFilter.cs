using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SplitzBackend.OpenAPIGen.Filter;

public class EnumAsStringDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        if (swaggerDoc.Components?.Schemas == null) return;

        foreach (var (name, schema) in swaggerDoc.Components.Schemas)
        {
            if (schema is not OpenApiSchema concrete) continue;
            if (concrete.Enum == null || concrete.Enum.Count == 0) continue;

            // Find the matching C# enum type
            var enumType = FindEnumType(name);
            if (enumType == null) continue;

            var names = Enum.GetNames(enumType);
            concrete.Type = JsonSchemaType.String;
            concrete.Format = null;
            concrete.Enum = names.Select(n => (JsonNode)JsonValue.Create(n)!).ToList();
        }
    }

    private static Type? FindEnumType(string schemaName)
    {
        return Assembly.GetExecutingAssembly()
            .GetTypes()
            .FirstOrDefault(t => t.IsEnum && t.Name == schemaName);
    }
}
