using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Nexo.Api.Filters;

/// <summary>
/// Answers 413 before the model binding reads a body that says it is too large. It goes with <see cref="RequestSizeLimitAttribute"/>, which is what stops a
/// body that does not say its size, while it is being read.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RefusesLargeBodyAttribute : Attribute, IResourceFilter
{
    private readonly long _maxBytes;

    public RefusesLargeBodyAttribute(long maxBytes) => _maxBytes = maxBytes;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.HttpContext.Request.ContentLength > _maxBytes)
            context.Result = new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
