//using Microsoft.AspNetCore.Authorization;
//using Microsoft.OpenApi;
//using Swashbuckle.AspNetCore.SwaggerGen;

//namespace SplitzBackend.OpenAPIGen.Filter;

//internal class SwaggerSecurityOperationFilter : IOperationFilter
//{
//    public void Apply(OpenApiOperation operation, OperationFilterContext context)
//    {
//        // Check for presence of Authorize attribute on controller or method
//        var hasAuthorize = (context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? [])
//            .Union(context.MethodInfo.GetCustomAttributes(true))
//            .OfType<AuthorizeAttribute>()
//            .Any();

//        // Check for presence of AllowAnonymous
//        var hasAllowAnonymous = (context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? [])
//            .Union(context.MethodInfo.GetCustomAttributes(true))
//            .OfType<AllowAnonymousAttribute>()
//            .Any();

//        if (hasAuthorize && !hasAllowAnonymous)
//            operation.Security = new List<OpenApiSecurityRequirement>
//            {
//                new()
//                {
//                    {
//                        new OpenApiSecuritySchemeReference("Bearer"),
//                        []
//                    }
//                }
//            };
//    }
//}

