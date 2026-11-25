using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SmartLMS.API.Swagger;

/// <summary>Adds the X-Tenant-Id and X-Device-Id headers every client sends, so Swagger UI can call secured endpoints.</summary>
public sealed class TenantDeviceHeaderFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter { Name = "X-Tenant-Id", In = ParameterLocation.Header, Required = false, Description = "Tenant id or slug. Optional when the JWT carries a tenant.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
        operation.Parameters.Add(new OpenApiParameter { Name = "X-Device-Id", In = ParameterLocation.Header, Required = false, Description = "Device fingerprint used at login. Required on every authenticated call.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
    }
}
