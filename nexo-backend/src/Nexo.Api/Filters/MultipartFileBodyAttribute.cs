using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nexo.Api.Filters;

/// <summary>
/// Documents a multipart body with one file field for an action that reads the form itself (no <c>IFormFile</c>
/// parameter — see <see cref="DisableFormValueModelBindingAttribute"/>), so the OpenAPI document keeps describing the upload.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MultipartFileBodyAttribute : Attribute
{
    public MultipartFileBodyAttribute(string field) => Field = field;

    public string Field { get; }
}

/// <summary>Swagger: turns <see cref="MultipartFileBodyAttribute"/> into a <c>multipart/form-data</c> request body.</summary>
public sealed class MultipartFileBodyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var attribute = context.MethodInfo.GetCustomAttributes(typeof(MultipartFileBodyAttribute), false)
            .OfType<MultipartFileBodyAttribute>().FirstOrDefault();
        if (attribute is null) return;
        operation.RequestBody = new OpenApiRequestBody
        {
            Content =
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties = { [attribute.Field] = new OpenApiSchema { Type = "string", Format = "binary" } },
                    },
                    Encoding = { [attribute.Field] = new OpenApiEncoding { Style = ParameterStyle.Form } },
                },
            },
        };
    }
}
