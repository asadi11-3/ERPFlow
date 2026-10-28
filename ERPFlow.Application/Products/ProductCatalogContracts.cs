using System.Text.Json.Serialization;

namespace ERPFlow.Application.Products;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateProductCommand(string Name, string Sku, [property: JsonRequired] decimal UnitPrice);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateProductCommand(string Name, string Sku, [property: JsonRequired] decimal UnitPrice, byte[] RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductVersionCommand(byte[] RowVersion);

public sealed record ProductCatalogChangeResult(Guid Id, Guid ActorId, string ActorName, string Action,
    string? PreviousName, string Name, decimal? PreviousUnitPrice, decimal UnitPrice,
    bool? PreviousIsActive, bool IsActive, DateTime CreatedAt);

public sealed record ProductDetails(Guid Id, string Name, string Sku, decimal UnitPrice, bool IsActive,
    byte[] RowVersion, DateTime CreatedAt, bool HasImage, string? ImageVersion,
    IReadOnlyList<ProductCatalogChangeResult> History);

public interface IProductCatalog
{
    Task<ProductDetails> GetAsync(Guid id, CancellationToken ct = default);
    Task<ProductDetails> CreateAsync(CreateProductCommand command, CancellationToken ct = default);
    Task<ProductDetails> UpdateAsync(Guid id, UpdateProductCommand command, CancellationToken ct = default);
    Task<ProductDetails> SetActiveAsync(Guid id, ProductVersionCommand command, bool active, CancellationToken ct = default);
}
