namespace Aggregator.Domain.Items;

/// <summary>Fields only one kind of source has; only the records below derive from it.</summary>
public abstract record ItemDetails
{
    private protected ItemDetails()
    {
    }
}
