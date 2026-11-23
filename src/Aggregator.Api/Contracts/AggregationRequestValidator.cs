using Aggregator.Application.Aggregation;
using Aggregator.Application.Paging;
using FluentValidation;

namespace Aggregator.Api.Contracts;

/// <summary>
/// Checks the query string of <c>GET /api/v1/aggregation</c> and its stream, and reports every failing rule at once.
/// Unknown sources and categories are checked by the registry.
/// </summary>
internal sealed class AggregationRequestValidator : AbstractValidator<AggregationRequest>
{
    public AggregationRequestValidator(AggregationService service)
    {
        RuleFor(request => request.Q).MaximumLength(AggregationRequest.MaxTextLength).WithMessage(ValidationMessages.TooLong);
        RuleFor(request => request.City).MaximumLength(AggregationRequest.MaxCityLength).WithMessage(ValidationMessages.TooLong);
        RuleFor(request => request.Sources).MaximumLength(AggregationRequest.MaxTextLength).WithMessage(ValidationMessages.TooLong);
        RuleFor(request => request.Category).MaximumLength(AggregationRequest.MaxNameLength).WithMessage(ValidationMessages.TooLong);
        RuleFor(request => request.SortBy).Cascade(CascadeMode.Stop)
            .MaximumLength(AggregationRequest.MaxNameLength).WithMessage(ValidationMessages.TooLong)
            .Must(value => SortParameters.TryParseField(value, out _)).WithMessage(ValidationMessages.UnknownSortField);
        RuleFor(request => request.SortOrder).Cascade(CascadeMode.Stop)
            .MaximumLength(AggregationRequest.MaxNameLength).WithMessage(ValidationMessages.TooLong)
            .Must(value => SortParameters.TryParseDirection(value, out _)).WithMessage(ValidationMessages.UnknownSortDirection);
        RuleFor(request => request.Page).InclusiveBetween(1, int.MaxValue).WithMessage(ValidationMessages.OutOfRange);
        RuleFor(request => request.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).WithMessage(ValidationMessages.OutOfRange);
        RuleFor(request => request.From).Must((request, from) => !(from > request.To)).WithMessage(ValidationMessages.FromAfterTo);
        RuleFor(request => request).Custom((request, context) => AddRegistryErrors(service, request, context));
    }

    // Sort values do not affect which sources or categories exist, so an invalid one is left to its own rule.
    private static void AddRegistryErrors(AggregationService service, AggregationRequest request, ValidationContext<AggregationRequest> context)
    {
        var query = (request with { SortBy = null, SortOrder = null }).ToQuery();
        foreach (var (field, messages) in service.Validate(query))
        {
            foreach (var message in messages)
            {
                context.AddFailure(field, message);
            }
        }
    }
}
