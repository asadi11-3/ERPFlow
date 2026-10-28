namespace ERPFlow.Domain.Entities;

public sealed class ProductImage
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string StorageKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public enum ProductImageObjectState { Pending, Active, DeletePending }

public sealed class ProductImageObject
{
    public string StorageKey { get; set; } = "";
    public Guid ProductId { get; set; }
    public ProductImageObjectState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class ProductImageStore
{
    public int Id { get; set; }
    public string Identity { get; set; } = "";
}
