using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SmartLMS.Application.Common;

namespace SmartLMS.API.Filters;

/// <summary>Runs the registered FluentValidation validator for every action argument and answers 400 in the ApiResponse envelope.</summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new List<string>();
        foreach (var arg in context.ActionArguments.Values)
        {
            if (arg is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(arg.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;
            var result = await validator.ValidateAsync(new ValidationContext<object>(arg), context.HttpContext.RequestAborted);
            errors.AddRange(result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
        }
        if (errors.Count > 0)
        {
            context.Result = new ObjectResult(ApiResponse<object>.Fail("Validation failed.", errors, "VALIDATION_FAILED")) { StatusCode = 400 };
            return;
        }
        await next();
    }
}
