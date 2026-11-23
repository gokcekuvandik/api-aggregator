using FluentValidation;
using FluentValidation.Results;

namespace Aggregator.Api.Http;

/// <summary>
/// Validates every endpoint argument that has an <see cref="IValidator{T}"/> registered and answers 400 with all failures before the handler runs.
/// Validators are found by type, so an endpoint needs no validation code of its own.
/// </summary>
internal sealed class ValidationInterceptor : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var failures = new List<ValidationFailure>();
        foreach (var argument in context.Arguments.OfType<object>())
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is IValidator validator)
            {
                var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
                failures.AddRange(result.Errors);
            }
        }

        if (failures.Count == 0)
        {
            return await next(context);
        }

        return TypedResults.ValidationProblem(failures
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray(), StringComparer.Ordinal));
    }
}
