namespace ERPFlow.Domain.Entities;

public sealed class ProductCatalogChange
{
    private ProductCatalogChange() { }

    public ProductCatalogChange(Product product, Guid actorId, string action,
        string? previousName, decimal? previousPrice, bool? previousActive)
    {
        Id = Guid.NewGuid();
        ProductId = product.Id;
        ActorId = actorId;
        Action = action;
        PreviousName = previousName;
        Name = product.Name;
        Sku = product.Sku;
        PreviousUnitPrice = previousPrice;
        UnitPrice = product.UnitPrice;
        PreviousIsActive = previousActive;
        IsActive = product.IsActive;
        CreatedAt = BusinessClock.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid ActorId { get; private set; }
    public string Action { get; private set; } = "";
    public string? PreviousName { get; private set; }
    public string Name { get; private set; } = "";
    public string Sku { get; private set; } = "";
    public decimal? PreviousUnitPrice { get; private set; }
    public decimal UnitPrice { get; private set; }
    public bool? PreviousIsActive { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
}
