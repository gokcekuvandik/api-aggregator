using FluentValidation;

namespace Aggregator.Api.Contracts;

/// <summary>The maximum-length rule with the shared message, so validators state only the limit.</summary>
internal static class LengthRules
{
    public static IRuleBuilderOptions<T, string?> NoLongerThan<T>(this IRuleBuilder<T, string?> rule, int maxLength) =>
        rule.MaximumLength(maxLength).WithMessage(ValidationMessages.TooLong);
}
