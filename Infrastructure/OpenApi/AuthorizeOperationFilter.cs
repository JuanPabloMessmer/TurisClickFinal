using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TurisClick.Api.Infrastructure.OpenApi;

public class AuthorizeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any() ||
            !metadata.OfType<IAuthorizeData>().Any())
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", null, null)] = []
        });

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse
        {
            Description = "Unauthorized"
        });

        operation.Responses.TryAdd("403", new OpenApiResponse
        {
            Description = "Forbidden"
        });
    }
}
