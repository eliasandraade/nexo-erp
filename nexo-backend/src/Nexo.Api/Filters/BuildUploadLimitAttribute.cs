using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.Api.Filters;

public enum BuildUploadKind
{
    Model,
    RealityCapture,
    /// <summary>Project documents (Wave 5): 20 MB.</summary>
    Document,
}

/// <summary>
/// Raises the request body and multipart limits for ONE action to the configured Build upload size (models: Build:Models:MaxUploadMb;
/// reality capture: the largest of its per-family limits) — what [RequestSizeLimit]/[RequestFormLimits] do, but read from
/// configuration. Every other endpoint keeps the server defaults. Runs before model binding (resource filter).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class BuildUploadLimitAttribute : Attribute, IResourceFilter, IOrderedFilter
{
    private const long MultipartOverhead = 1024 * 1024;

    public BuildUploadLimitAttribute(BuildUploadKind kind) => Kind = kind;

    public BuildUploadKind Kind { get; }

    // before the framework's own size-limit filters (order 900) so this one wins
    public int Order => 800;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<BuildTwinOptions>();
        var limit = Kind switch
        {
            BuildUploadKind.Model => options.MaxModelBytes,
            BuildUploadKind.Document => Nexo.Domain.Modules.Build.BuildDocument.MaxBytes,
            _ => options.MaxCaptureBytes,
        } + MultipartOverhead;

        var bodySize = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = limit;

        var formFeature = context.HttpContext.Features.Get<IFormFeature>();
        if (formFeature?.Form is null)
            context.HttpContext.Features.Set<IFormFeature>(new FormFeature(context.HttpContext.Request,
                new FormOptions { MultipartBodyLengthLimit = limit }));
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { /* nothing to undo */ }
}

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
