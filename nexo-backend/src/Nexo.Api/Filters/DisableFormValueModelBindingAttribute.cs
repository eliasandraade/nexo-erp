using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Nexo.Api.Filters;

/// <summary>
/// Keeps model binding from reading (and buffering) a form body before the action runs: the form value providers are
/// removed, so the action decides when — and whether — to read <c>Request.Form</c> (e.g. only after authorizing an
/// anonymous upload). Route and query parameters still bind.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { /* nothing to undo */ }
}
