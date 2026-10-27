using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;

namespace ERPFlow.Application.Procurement;

public sealed class ProcurementMasters(IProcurementPersistence persistence, ICurrentUser current) : IProcurementMasters
{
    private static readonly UserRole[] SupplierReaders = [UserRole.Administrator, UserRole.Purchasing, UserRole.Approver];
    private static readonly UserRole[] SupplierWriters = [UserRole.Administrator, UserRole.Purchasing];

    public async Task<PageResult<WarehouseResult>> ListWarehousesAsync(int page, int pageSize, bool? active, CancellationToken ct = default)
    {
        ProcurementAccess.Page(page, pageSize);
        await ProcurementAccess.ActorAsync(persistence, current, ct, Enum.GetValues<UserRole>());
        var result = await persistence.ListWarehousesAsync(page, pageSize, active, ct);
        return new(result.Items.Select(WarehouseView).ToArray(), page, pageSize, result.TotalCount, result.TotalPages);
    }
    public async Task<WarehouseResult> GetWarehouseAsync(Guid id, CancellationToken ct = default)
    {
        ProcurementAccess.Id(id);
        await ProcurementAccess.ActorAsync(persistence, current, ct, Enum.GetValues<UserRole>());
        return WarehouseView(await persistence.FindWarehouseAsync(id, false, ct) ?? throw new EntityNotFoundException(nameof(Warehouse), id));
    }
    public async Task<WarehouseResult> CreateWarehouseAsync(CreateWarehouseCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var tx = await persistence.BeginAsync(true, ct);
        await ProcurementAccess.ActorAsync(persistence, current, ct, UserRole.Administrator);
        var warehouse = new Warehouse(command.Code, command.Name);
        persistence.AddWarehouse(warehouse);
        await persistence.SaveAsync(ct);
        await tx.CommitAsync(ct);
        return WarehouseView(warehouse);
    }
    public async Task<WarehouseResult> UpdateWarehouseAsync(Guid id, UpdateWarehouseCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProcurementAccess.Id(id);
        await using var tx = await persistence.BeginAsync(true, ct);
        await ProcurementAccess.ActorAsync(persistence, current, ct, UserRole.Administrator);
        var warehouse = await persistence.FindWarehouseAsync(id, true, ct) ?? throw new EntityNotFoundException(nameof(Warehouse), id);
        ProcurementAccess.Version(command.RowVersion, warehouse.RowVersion);
        warehouse.Rename(command.Name);
        persistence.TouchWarehouse(warehouse);
        await persistence.SaveAsync(ct);
        await tx.CommitAsync(ct);
        return WarehouseView(warehouse);
    }
    public async Task<WarehouseResult> SetWarehouseActiveAsync(Guid id, VersionCommand command, bool active, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProcurementAccess.Id(id);
        await using var tx = await persistence.BeginAsync(true, ct);
        await ProcurementAccess.ActorAsync(persistence, current, ct, UserRole.Administrator);
        var warehouse = await persistence.FindWarehouseAsync(id, true, ct) ?? throw new EntityNotFoundException(nameof(Warehouse), id);
        ProcurementAccess.Version(command.RowVersion, warehouse.RowVersion);
        if (!active && await persistence.HasWarehouseCommitmentsAsync(id, ct))
            throw new WorkflowConflictException("Warehouse has stock, reservations or open business commitments.");
        ProcurementAccess.Domain(() => { warehouse.SetActive(active); return true; });
        persistence.TouchWarehouse(warehouse);
        await persistence.SaveAsync(ct);
        await tx.CommitAsync(ct);
        return WarehouseView(warehouse);
    }
    public async Task<PageResult<SupplierResult>> ListSuppliersAsync(int page, int pageSize, string? search, bool? active, CancellationToken ct = default)
    {
        ProcurementAccess.Page(page, pageSize, search);
        await ProcurementAccess.ActorAsync(persistence, current, ct, SupplierReaders);
        var result = await persistence.ListSuppliersAsync(page, pageSize, search, active, ct);
        return new(result.Items.Select(SupplierView).ToArray(), page, pageSize, result.TotalCount, result.TotalPages);
    }
    public async Task<SupplierResult> GetSupplierAsync(Guid id, CancellationToken ct = default)
    {
        ProcurementAccess.Id(id);
        await ProcurementAccess.ActorAsync(persistence, current, ct, SupplierReaders);
        return SupplierView(await persistence.FindSupplierAsync(id, false, ct) ?? throw new EntityNotFoundException(nameof(Supplier), id));
    }
    public async Task<SupplierResult> CreateSupplierAsync(CreateSupplierCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var tx = await persistence.BeginAsync(ct: ct);
        await ProcurementAccess.ActorAsync(persistence, current, ct, SupplierWriters);
        var supplier = new Supplier(command.SupplierCode, command.Name, command.ContactName, command.Email, command.Phone, command.Address);
        persistence.AddSupplier(supplier);
        await persistence.SaveAsync(ct);
        await tx.CommitAsync(ct);
        return SupplierView(supplier);
    }
    public async Task<SupplierResult> UpdateSupplierAsync(Guid id, UpdateSupplierCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProcurementAccess.Id(id);
        await using var tx = await persistence.BeginAsync(ct: ct);
        await ProcurementAccess.ActorAsync(persistence, current, ct, SupplierWriters);
        var supplier = await persistence.FindSupplierAsync(id, true, ct) ?? throw new EntityNotFoundException(nameof(Supplier), id);
        ProcurementAccess.Version(command.RowVersion, supplier.RowVersion);
        supplier.Edit(command.Name, command.ContactName, command.Email, command.Phone, command.Address);
        persistence.TouchSupplier(supplier);
        await persistence.SaveAsync(ct);
        await tx.CommitAsync(ct);
        return SupplierView(supplier);
    }
    public async Task<SupplierResult> SetSupplierActiveAsync(Guid id, VersionCommand command, bool active, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProcurementAccess.Id(id);
        await using var tx = await persistence.BeginAsync(ct: ct);
        await ProcurementAccess.ActorAsync(persistence, current, ct, SupplierWriters);
        var supplier = await persistence.FindSupplierAsync(id, true, ct) ?? throw new EntityNotFoundException(nameof(Supplier), id);
        ProcurementAccess.Version(command.RowVersion, supplier.RowVersion);
        supplier.SetActive(active);
        persistence.TouchSupplier(supplier);
        await persistence.SaveAsync(ct);
        await tx.CommitAsync(ct);
        return SupplierView(supplier);
    }
    private static WarehouseResult WarehouseView(Warehouse x) => new(x.Id, x.Code, x.Name, x.IsActive, x.CreatedAt, x.RowVersion);
    private static SupplierResult SupplierView(Supplier x) => new(x.Id, x.SupplierCode, x.Name, x.ContactName,
        x.Email, x.Phone, x.Address, x.IsActive, x.CreatedAt, x.RowVersion);
}
