using System.Data;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.ProductImages;
using ERPFlow.Application.Products;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class EfProductCatalog(ERPFlowDbContext context, ICurrentUser current) : IProductCatalog
{
    public async Task<ProductDetails> GetAsync(Guid id, CancellationToken ct = default)
    {
        await ActorAsync(false, ct);
        var product = await context.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException(nameof(Product), id);
        return await ViewAsync(product, ct);
    }

    public async Task<ProductDetails> CreateAsync(CreateProductCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command.Name, command.Sku, command.UnitPrice);
        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await InventoryAuthoritySession.EnterAsync(context, false, ct);
        var actor = await ActorAsync(true, ct);
        var sku = command.Sku.Trim();
        if (await context.Products.FromSqlRaw("SELECT * FROM Products WITH (UPDLOCK,HOLDLOCK)").AnyAsync(x => x.Sku == sku, ct)) throw Duplicate();
        var product = new Product(command.Name, sku, command.UnitPrice, 0);
        context.Products.Add(product);
        context.ProductCatalogChanges.Add(new ProductCatalogChange(product, actor.Id, "Created", null, null, null));
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await ViewAsync(product, ct);
    }

    public async Task<ProductDetails> UpdateAsync(Guid id, UpdateProductCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command.Name, command.Sku, command.UnitPrice);
        ValidateVersion(command.RowVersion);
        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await InventoryAuthoritySession.EnterAsync(context, false, ct);
        var actor = await ActorAsync(true, ct);
        var product = await GetLockedAsync(id, ct);
        CheckVersion(product, command.RowVersion);
        if (!string.Equals(command.Sku.Trim(), product.Sku, StringComparison.Ordinal))
            throw new WorkflowConflictException("SKU is immutable after creation. Create a new product for a different identifier.");
        if (product.Name != command.Name.Trim() || product.UnitPrice != command.UnitPrice)
        {
            var name = product.Name;
            var price = product.UnitPrice;
            product.EditCatalog(command.Name, command.UnitPrice);
            context.ProductCatalogChanges.Add(new ProductCatalogChange(product, actor.Id, "Updated", name, price, product.IsActive));
            await SaveAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await ViewAsync(product, ct);
    }

    public async Task<ProductDetails> SetActiveAsync(Guid id, ProductVersionCommand command, bool active, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateVersion(command.RowVersion);
        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await InventoryAuthoritySession.EnterAsync(context, false, ct);
        var actor = await ActorAsync(true, ct);
        var product = await GetLockedAsync(id, ct);
        CheckVersion(product, command.RowVersion);
        if (product.IsActive != active)
        {
            var before = product.IsActive;
            product.SetActive(active);
            context.ProductCatalogChanges.Add(new ProductCatalogChange(product, actor.Id, active ? "Activated" : "Deactivated",
                product.Name, product.UnitPrice, before));
            await SaveAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await ViewAsync(product, ct);
    }

    private async Task<Product> GetLockedAsync(Guid id, CancellationToken ct) =>
        await context.Products.FromSqlInterpolated($"SELECT * FROM Products WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}")
            .SingleOrDefaultAsync(ct) ?? throw new EntityNotFoundException(nameof(Product), id);

    private async Task<AppUser> ActorAsync(bool write, CancellationToken ct)
    {
        var actor = await context.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == current.UserId, ct);
        if (actor is null || write && actor.Role != UserRole.Administrator)
            throw new WorkflowAuthorizationException("Only a current Administrator may manage the product catalog.");
        return actor;
    }

    private async Task<ProductDetails> ViewAsync(Product product, CancellationToken ct)
    {
        var image = await context.ProductImages.AsNoTracking().SingleOrDefaultAsync(x => x.ProductId == product.Id, ct);
        var history = await (from change in context.ProductCatalogChanges.AsNoTracking()
            join actor in context.AppUsers on change.ActorId equals actor.Id
            where change.ProductId == product.Id
            orderby change.CreatedAt descending, change.Id
            select new ProductCatalogChangeResult(change.Id, actor.Id, actor.Name, change.Action,
                change.PreviousName, change.Name, change.PreviousUnitPrice, change.UnitPrice,
                change.PreviousIsActive, change.IsActive, change.CreatedAt)).Take(100).ToListAsync(ct);
        return new ProductDetails(product.Id, product.Name, product.Sku, product.UnitPrice, product.IsActive,
            product.RowVersion, product.CreatedAt, image is not null,
            image is null ? null : ProductImageVersions.Encode(image.RowVersion), history);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException exception) { throw new WorkflowConflictException("The product changed. Reload before saving.", exception); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        { throw Duplicate(); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 })
        { throw new WorkflowConflictException("The catalog changed concurrently. Reload and retry.", exception); }
        catch (SqlException exception) when (exception.Number == 1205)
        { throw new WorkflowConflictException("The catalog changed concurrently. Reload and retry.", exception); }
    }

    private static void Validate(string name, string sku, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(sku) || sku.Trim().Length > 64 || sku.Any(char.IsControl) ||
            price < 0 || price > Product.MaxCatalogUnitPrice || decimal.Round(price, 2) != price)
            throw new WorkflowValidationException("Name (1–200), SKU (1–64 without control characters), and unit price from 0 to 9,999,999,999,999.99 with at most two decimals are required.");
    }
    private static void ValidateVersion(byte[] version)
    {
        if (version is not { Length: 8 }) throw new WorkflowValidationException("A valid product row version is required.");
    }
    private static void CheckVersion(Product product, byte[] version)
    {
        if (!product.RowVersion.SequenceEqual(version)) throw new WorkflowConflictException("The product changed. Reload before saving.");
    }
    private static WorkflowConflictException Duplicate() => new("A product with this SKU already exists.");
}
