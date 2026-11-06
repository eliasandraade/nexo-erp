using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Filters;

/// <summary>Maps the Build file/storage refusals to their HTTP status with a stable code.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class BuildFileErrorsAttribute : Attribute, IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        (int Status, string Code)? mapped = context.Exception switch
        {
            BuildStorageUnavailableException => (StatusCodes.Status503ServiceUnavailable, "storage_unavailable"),
            BuildFileTooLargeException => (StatusCodes.Status413PayloadTooLarge, "file_too_large"),
            _ => null,
        };
        if (mapped is not { } m) return;
        context.Result = new ObjectResult(new { error = context.Exception.Message, code = m.Code, requestId = context.HttpContext.TraceIdentifier })
        {
            StatusCode = m.Status,
        };
        context.ExceptionHandled = true;
    }
}
