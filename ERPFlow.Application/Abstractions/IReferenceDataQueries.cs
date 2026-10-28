namespace ERPFlow.Application.Abstractions;

public sealed record CustomerReference(Guid Id, string Name, string Email);

public sealed record ProductReference(
    Guid Id,
    string Name,
    string Sku,
    decimal UnitPrice,
    int AvailableStock,
    bool HasImage = false,
    string? ImageVersion = null,
    bool IsActive = true);

public interface IReferenceDataQueries
{
    Task<IReadOnlyList<CustomerReference>> ListActiveCustomersAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductReference>> ListProductsAsync(
        CancellationToken cancellationToken = default);
}
