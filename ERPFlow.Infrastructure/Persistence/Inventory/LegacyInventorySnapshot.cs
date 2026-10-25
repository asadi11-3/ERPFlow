using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

internal sealed record LegacyInventorySnapshot(
    ProductOrigin[] Products, OrderOrigin[] Orders, SalesLineOrigin[] SalesLines,
    SalesReservationOrigin[] Reservations, JobcardOrigin[] Jobcards, JobcardLineOrigin[] JobcardLines,
    DeliveryOrigin[] Deliveries, WarehouseOrigin[] Warehouses, StockTargetOrigin[] StockTargets,
    bool HasWarehouseActivity)
{
    public byte[] Fingerprint() => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this));

    public static Guid StableTarget(string kind, Guid sourceId, Guid? secondId = null) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"ERPFlow.LegacyImport.v1|{kind}|{sourceId:N}|{secondId:N}"))[..16]);

    public static async Task<LegacyInventorySnapshot> ReadAsync(ERPFlowDbContext context, CancellationToken ct)
    {
        var products = await context.Products.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new ProductOrigin(x.Id, x.StockOnHand, x.ReservedQuantity, x.RowVersion)).ToArrayAsync(ct);
        var orders = await context.SalesOrders.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new OrderOrigin(x.Id, (int)x.Status, x.RowVersion)).ToArrayAsync(ct);
        var salesLines = await context.SalesOrderItems.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new SalesLineOrigin(x.Id, x.SalesOrderId, x.ProductId, x.Quantity, x.WarehouseId)).ToArrayAsync(ct);
        var reservations = await context.StockReservations.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new SalesReservationOrigin(x.Id, x.SalesOrderId, x.ProductId, x.Quantity,
                x.IsReleased, x.IsHistorical, (int?)x.ClosureKind, x.WarehouseId, x.CreatedAt, x.ReleasedAt)).ToArrayAsync(ct);
        var jobcards = await context.Jobcards.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new JobcardOrigin(x.Id, (int)x.Status, x.Revision, x.CompletedAt)).ToArrayAsync(ct);
        var jobcardLines = await context.JobcardItems.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new JobcardLineOrigin(x.Id, x.JobcardId, x.ProductId, x.Quantity, x.WarehouseId)).ToArrayAsync(ct);
        var deliveries = await context.DeliveryNotes.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new DeliveryOrigin(x.Id, x.SalesOrderId)).ToArrayAsync(ct);
        var warehouses = await context.Warehouses.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new WarehouseOrigin(x.Id, x.IsActive, x.RowVersion)).ToArrayAsync(ct);
        var stocks = await context.WarehouseStocks.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new StockTargetOrigin(x.Id, x.WarehouseId, x.ProductId, x.StockOnHand, x.ReservedQuantity, x.Sequence, x.RowVersion)).ToArrayAsync(ct);
        var activity = await context.InventoryReservations.AnyAsync(ct) || await context.InventoryPostings.AnyAsync(ct) ||
            await context.StockMovements.AnyAsync(ct) || stocks.Any(x => x.OnHand != 0 || x.Reserved != 0 || x.Sequence != 0);
        return new(products, orders, salesLines, reservations, jobcards, jobcardLines, deliveries, warehouses, stocks, activity);
    }
}

internal sealed record ProductOrigin(Guid Id, int OnHand, int Reserved, byte[] Version);
internal sealed record OrderOrigin(Guid Id, int Status, byte[] Version);
internal sealed record SalesLineOrigin(Guid Id, Guid OrderId, Guid ProductId, int Quantity, Guid? WarehouseId);
internal sealed record SalesReservationOrigin(Guid Id, Guid OrderId, Guid ProductId, int Quantity,
    bool Closed, bool Historical, int? ClosureKind, Guid? WarehouseId, DateTime CreatedAt, DateTime? ClosedAt);
internal sealed record JobcardOrigin(Guid Id, int Status, int Revision, DateTime? CompletedAt);
internal sealed record JobcardLineOrigin(Guid Id, Guid JobcardId, Guid ProductId, int Quantity, Guid? WarehouseId);
internal sealed record DeliveryOrigin(Guid Id, Guid OrderId);
internal sealed record WarehouseOrigin(Guid Id, bool Active, byte[] Version);
internal sealed record StockTargetOrigin(Guid Id, Guid WarehouseId, Guid ProductId, int OnHand, int Reserved, long Sequence, byte[] Version);
