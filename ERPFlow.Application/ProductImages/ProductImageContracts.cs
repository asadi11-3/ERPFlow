namespace ERPFlow.Application.ProductImages;

public static class ProductImageLimits
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxRequestBytes = 6 * 1024 * 1024;
    public const int MaxEdge = 8192;
    public const long MaxPixels = 16_000_000;
}

public sealed record ProductImageCondition(string? IfMatch = null, bool CreateOnly = false);
public sealed record NormalizedProductImage(byte[] Bytes, string ContentType, int Width, int Height);
public sealed record ProductImageMetadata(Guid Id, Guid ProductId, string ContentType, long FileSize,
    int Width, int Height, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string Version);
public sealed record ProductImageWriteResult(ProductImageMetadata Image, bool Created);
public sealed record ProductImageDownload(ProductImageMetadata Image, byte[] Bytes);

public enum ProductImageError
{
    InvalidImage, TooLarge, UnsupportedMediaType, PreconditionRequired, PreconditionFailed, NotFound, ProductNotFound
}

public sealed class ProductImageException(ProductImageError error, string message) : Exception(message)
{
    public ProductImageError Error { get; } = error;
}

public interface IProductImageNormalizer
{
    Task<NormalizedProductImage> NormalizeAsync(Stream source, string fileName, string contentType,
        CancellationToken cancellationToken = default);
}

public interface IProductImageStorage
{
    string StoreIdentity { get; }
    ValueTask<IAsyncDisposable> AcquireLeaseAsync(Guid productId, CancellationToken cancellationToken = default);
    Task WriteAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default);
    Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public interface IProductImageWorkflow
{
    Task<ProductImageWriteResult> PutAsync(Guid productId, Stream source, string fileName, string contentType,
        ProductImageCondition condition, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid productId, ProductImageCondition condition, CancellationToken cancellationToken = default);
    Task<ProductImageDownload?> GetAsync(Guid productId, CancellationToken cancellationToken = default);
}

public interface IProductImageCleanup
{
    Task<int> RunOnceAsync(CancellationToken cancellationToken = default);
}

public static class ProductImageVersions
{
    public static string Encode(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    public static bool IsStrongVersion(string value)
    {
        if (value.Length != 14 || value[0] != '"' || value[^1] != '"') return false;
        Span<byte> bytes = stackalloc byte[8];
        return Convert.TryFromBase64String(value[1..^1], bytes, out var length) && length == 8 &&
            Encode(bytes.ToArray()) == value;
    }

    public static void Validate(ProductImageCondition condition)
    {
        if ((condition.CreateOnly && condition.IfMatch is not null) ||
            (condition.IfMatch is not null && !IsStrongVersion(condition.IfMatch)))
            throw new ProductImageException(ProductImageError.InvalidImage, "Invalid or conflicting image preconditions.");
    }
}
