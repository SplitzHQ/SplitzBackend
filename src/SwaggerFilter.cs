using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SplitzBackend;

internal class SwaggerSecurityOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Check for presence of Authorize attribute on controller or method
        var hasAuthorize = (context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? [])
            .Union(context.MethodInfo.GetCustomAttributes(true))
            .OfType<AuthorizeAttribute>()
            .Any();

        // Check for presence of AllowAnonymous
        var hasAllowAnonymous = (context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? [])
            .Union(context.MethodInfo.GetCustomAttributes(true))
            .OfType<AllowAnonymousAttribute>()
            .Any();

        // Check if this is a login or register endpoint from the identity API
        //var isLoginOrRegisterEndpoint = false;
        //if (context.ApiDescription?.RelativePath != null)
        //{
        //    var path = context.ApiDescription.RelativePath.ToLower();
        //    isLoginOrRegisterEndpoint =
        //        path.Contains("account/login") ||
        //        path.Contains("account/register");
        //}

        // Remove security requirements if:
        // 1. Not explicitly authorized OR
        // 2. Marked as allow anonymous OR
        // 3. Is a login/register endpoint
        if (hasAuthorize && !hasAllowAnonymous)
        {
            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new()
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        []
                    }
                }
            };
        }
    }
}
