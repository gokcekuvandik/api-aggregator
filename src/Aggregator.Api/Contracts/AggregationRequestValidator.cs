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
        RuleFor(request => request.Q).NoLongerThan(AggregationRequest.MaxTextLength);
        RuleFor(request => request.City).NoLongerThan(AggregationRequest.MaxCityLength);
        RuleFor(request => request.Sources).NoLongerThan(AggregationRequest.MaxTextLength);
        RuleFor(request => request.Category).NoLongerThan(AggregationRequest.MaxNameLength);
        RuleFor(request => request.SortBy).Cascade(CascadeMode.Stop)
            .NoLongerThan(AggregationRequest.MaxNameLength)
            .Must(value => SortParameters.TryParseField(value, out _)).WithMessage(ValidationMessages.UnknownSortField);
        RuleFor(request => request.SortOrder).Cascade(CascadeMode.Stop)
            .NoLongerThan(AggregationRequest.MaxNameLength)
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
        foreach (var (field, message) in service.Validate(query).SelectMany(error => error.Value, (error, message) => (error.Key, message)))
        {
            context.AddFailure(field, message);
        }
    }
}
