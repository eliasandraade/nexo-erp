using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Nexo.Api.Filters;

/// <summary>
/// Runs the registered FluentValidation validator of every action argument that has one, before
/// the action (400 with the messages, via the exception middleware). For controllers that don't
/// call <c>ValidateAndThrowAsync</c> themselves.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ValidateBodyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator) continue;
            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (!result.IsValid) throw new ValidationException(result.Errors);
        }
        await next();
    }
}
