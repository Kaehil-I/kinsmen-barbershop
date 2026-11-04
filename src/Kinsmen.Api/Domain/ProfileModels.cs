namespace Kinsmen.Api.Domain;

// A customer's own details. Keyed by their identity-provider user id, so there is at most one per account.
// Every field is optional: a customer who has never saved a profile simply gets an empty one.
public sealed record CustomerProfile(string Id, string? DisplayName, string? Phone, string? PreferredBarberId, DateTime UpdatedUtc);

// What the customer sends when saving. Blank strings clear a field.
public sealed record ProfileRequest(string? DisplayName, string? Phone, string? PreferredBarberId);

public interface IProfileStore
{
    Task<CustomerProfile?> Get(string userId, CancellationToken ct);
    Task Save(CustomerProfile profile, CancellationToken ct);
}
