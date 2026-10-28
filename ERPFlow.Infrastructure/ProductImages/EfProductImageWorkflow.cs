using System.Data;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.ProductImages;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERPFlow.Infrastructure.ProductImages;

public sealed class EfProductImageWorkflow(ERPFlowDbContext context, IProductImageStorage storage,
    IProductImageNormalizer normalizer, ICurrentUser currentUser, ILogger<EfProductImageWorkflow> logger,
    TimeProvider? timeProvider = null)
    : IProductImageWorkflow, IProductImageCleanup
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ProductImageWriteResult> PutAsync(Guid productId, Stream source, string fileName,
        string contentType, ProductImageCondition condition, CancellationToken cancellationToken = default)
    {
        try { return await PutCoreAsync(productId, source, fileName, contentType, condition, cancellationToken); }
        finally { DetachImageEntries(); }
    }

    private async Task<ProductImageWriteResult> PutCoreAsync(Guid productId, Stream source, string fileName,
        string contentType, ProductImageCondition condition, CancellationToken cancellationToken)
    {
        ProductImageVersions.Validate(condition);
        await AuthorizeMutationAsync(cancellationToken);
        await RequireProductAsync(productId, cancellationToken);
        var normalized = await normalizer.NormalizeAsync(source, fileName, contentType, cancellationToken);
        await using var lease = await storage.AcquireLeaseAsync(productId, cancellationToken);
        await VerifyStoreAsync(cancellationToken);
        await RequireProductAsync(productId, cancellationToken);
        var previous = await context.ProductImages.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProductId == productId, cancellationToken);
        CheckCondition(previous, condition);

        var now = clock.GetUtcNow();
        var key = Guid.NewGuid().ToString("N");
        context.ProductImageObjects.Add(new ProductImageObject
        {
            StorageKey = key, ProductId = productId, State = ProductImageObjectState.Pending,
            CreatedAt = now, ExpiresAt = now.AddHours(1)
        });
        await context.SaveChangesAsync(cancellationToken);

        await storage.WriteAsync(key, normalized.Bytes, cancellationToken);
        DetachImageEntries();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var candidate = await context.ProductImageObjects.SingleAsync(x => x.StorageKey == key, cancellationToken);
        if (candidate.State != ProductImageObjectState.Pending || candidate.ExpiresAt <= clock.GetUtcNow())
            throw new ProductImageException(ProductImageError.PreconditionFailed, "The upload reservation is no longer publishable.");
        var image = await context.ProductImages.SingleOrDefaultAsync(x => x.ProductId == productId, cancellationToken);
        CheckCondition(image, condition);
        var created = image is null;
        if (image is null)
        {
            image = new ProductImage { Id = Guid.NewGuid(), ProductId = productId, CreatedAt = now };
            context.ProductImages.Add(image);
        }
        else
        {
            var superseded = await context.ProductImageObjects.SingleAsync(x => x.StorageKey == image.StorageKey, cancellationToken);
            superseded.State = ProductImageObjectState.DeletePending;
            superseded.ExpiresAt = now;
        }
        image.StorageKey = key;
        image.ContentType = normalized.ContentType;
        image.FileSize = normalized.Bytes.LongLength;
        image.Width = normalized.Width;
        image.Height = normalized.Height;
        image.UpdatedAt = clock.GetUtcNow();
        candidate.State = ProductImageObjectState.Active;
        await SaveImageAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProductImageWriteResult(Metadata(image), created);
    }

    public async Task<ProductImageDownload> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        await RequireProductAsync(productId, cancellationToken);
        await using var lease = await storage.AcquireLeaseAsync(productId, cancellationToken);
        var image = await context.ProductImages.AsNoTracking().SingleOrDefaultAsync(x => x.ProductId == productId, cancellationToken)
            ?? throw Missing();
        await VerifyStoreAsync(cancellationToken);
        if (!await context.ProductImageObjects.AsNoTracking().AnyAsync(item => item.StorageKey == image.StorageKey &&
            item.ProductId == productId && item.State == ProductImageObjectState.Active, cancellationToken))
            throw new IOException("Image registry is inconsistent.");
        var bytes = await storage.ReadAsync(image.StorageKey, cancellationToken);
        if (bytes.LongLength != image.FileSize) throw new IOException("The stored image does not match its metadata.");
        return new ProductImageDownload(Metadata(image), bytes);
    }

    public async Task DeleteAsync(Guid productId, ProductImageCondition condition, CancellationToken cancellationToken = default)
    {
        try { await DeleteCoreAsync(productId, condition, cancellationToken); }
        finally { DetachImageEntries(); }
    }

    private async Task DeleteCoreAsync(Guid productId, ProductImageCondition condition, CancellationToken cancellationToken)
    {
        ProductImageVersions.Validate(condition);
        if (condition.CreateOnly)
            throw new ProductImageException(ProductImageError.InvalidImage, "DELETE does not support If-None-Match.");
        await AuthorizeMutationAsync(cancellationToken);
        await RequireProductAsync(productId, cancellationToken);
        await using var lease = await storage.AcquireLeaseAsync(productId, cancellationToken);
        await VerifyStoreAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var image = await context.ProductImages.SingleOrDefaultAsync(x => x.ProductId == productId, cancellationToken);
        if (image is null && condition.IfMatch is null) throw Missing();
        CheckCondition(image, condition);
        var stored = await context.ProductImageObjects.SingleAsync(x => x.StorageKey == image!.StorageKey, cancellationToken);
        stored.State = ProductImageObjectState.DeletePending;
        stored.ExpiresAt = clock.GetUtcNow();
        context.ProductImages.Remove(image!);
        await SaveImageAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var candidates = await context.ProductImageObjects.AsNoTracking()
            .Where(x => x.State == ProductImageObjectState.DeletePending ||
                (x.State == ProductImageObjectState.Pending && x.ExpiresAt <= now))
            .OrderBy(x => x.ExpiresAt).Take(64).Select(x => new { x.StorageKey, x.ProductId })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0) return 0;
        var removed = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                await using var lease = await storage.AcquireLeaseAsync(candidate.ProductId, cancellationToken);
                DetachImageEntries();
                await VerifyStoreAsync(cancellationToken);
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var stored = await context.ProductImageObjects.SingleOrDefaultAsync(x => x.StorageKey == candidate.StorageKey, cancellationToken);
                if (stored is null || stored.State == ProductImageObjectState.Active ||
                    (stored.State == ProductImageObjectState.Pending && stored.ExpiresAt > clock.GetUtcNow())) continue;
                if (await context.ProductImages.AnyAsync(x => x.StorageKey == candidate.StorageKey, cancellationToken))
                {
                    logger.LogCritical("Image cleanup refused a referenced object {StorageKey}.", candidate.StorageKey);
                    continue;
                }
                stored.State = ProductImageObjectState.DeletePending;
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                context.Entry(stored).State = EntityState.Detached;
                await using var deletion = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var deleting = await context.ProductImageObjects.SingleOrDefaultAsync(item => item.StorageKey == candidate.StorageKey, cancellationToken);
                if (deleting is null || deleting.State != ProductImageObjectState.DeletePending) continue;
                if (await context.ProductImages.AnyAsync(x => x.StorageKey == candidate.StorageKey, cancellationToken)) continue;
                await storage.DeleteAsync(candidate.StorageKey, cancellationToken);
                context.ProductImageObjects.Remove(deleting);
                await context.SaveChangesAsync(cancellationToken);
                await deletion.CommitAsync(cancellationToken);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or System.Data.Common.DbException or DbUpdateException or TimeoutException)
            {
                logger.LogWarning(exception, "Product image cleanup deferred for {StorageKey}.", candidate.StorageKey);
            }
            finally { DetachImageEntries(); }
        }
        return removed;
    }

    private async Task VerifyStoreAsync(CancellationToken cancellationToken)
    {
        var identity = storage.StoreIdentity;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'ERPFlow.ProductImages.StoreIdentity',
                @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51060, 'Product image storage coordination is unavailable.', 1;
            """, cancellationToken);
        var store = await context.ProductImageStores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (store is null)
        {
            var binding = new ProductImageStore { Id = 1, Identity = identity };
            context.ProductImageStores.Add(binding);
            try { await context.SaveChangesAsync(cancellationToken); }
            finally { context.Entry(binding).State = EntityState.Detached; }
        }
        else if (!string.Equals(store.Identity, identity, StringComparison.Ordinal))
            throw new IOException("The configured image store does not match this database.");
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task AuthorizeMutationAsync(CancellationToken cancellationToken)
    {
        if (!await context.AppUsers.AsNoTracking().AnyAsync(x => x.Id == currentUser.UserId &&
            x.Role == UserRole.Administrator, cancellationToken))
            throw new WorkflowAuthorizationException("Only a current Administrator may modify product images.");
    }

    private async Task RequireProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (!await context.Products.AsNoTracking().AnyAsync(x => x.Id == productId, cancellationToken))
        {
            throw Missing();
        }
    }

    private async Task SaveImageAsync(CancellationToken cancellationToken)
    {
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        { throw new ProductImageException(ProductImageError.PreconditionFailed, "The image version no longer matches."); }
    }

    private void DetachImageEntries()
    {
        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.Entity is ProductImage or ProductImageObject).ToArray())
            entry.State = EntityState.Detached;
    }

    private static void CheckCondition(ProductImage? image, ProductImageCondition condition)
    {
        if (condition.CreateOnly && image is not null || condition.IfMatch is not null &&
            (image is null || condition.IfMatch != ProductImageVersions.Encode(image.RowVersion)))
            throw new ProductImageException(ProductImageError.PreconditionFailed, "The image version has changed. Fetch the current image before retrying.");
        if (image is not null && condition.IfMatch is null && !condition.CreateOnly)
            throw new ProductImageException(ProductImageError.PreconditionRequired, "Use the current image ETag in If-Match.");
    }

    private static ProductImageMetadata Metadata(ProductImage image) => new(image.Id, image.ProductId,
        image.ContentType, image.FileSize, image.Width, image.Height, image.CreatedAt, image.UpdatedAt,
        ProductImageVersions.Encode(image.RowVersion));
    private static ProductImageException Missing() => new(ProductImageError.NotFound, "The product or its image was not found.");
}
