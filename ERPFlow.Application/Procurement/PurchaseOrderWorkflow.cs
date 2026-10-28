using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;

namespace ERPFlow.Application.Procurement;

public sealed class PurchaseOrderWorkflow(IProcurementPersistence persistence, ICurrentUser current) : IPurchaseOrderWorkflow
{
    private static readonly UserRole[] CommercialReaders = [UserRole.Administrator, UserRole.Purchasing, UserRole.Approver];
    private static readonly UserRole[] OperationalReaders = [.. CommercialReaders, UserRole.WarehouseOperator];
    private static readonly UserRole[] Writers = [UserRole.Administrator, UserRole.Purchasing];

    public async Task<PageResult<PurchaseOrderResult>> ListAsync(int page, int pageSize, PurchaseOrderStatus? status, CancellationToken ct = default)
    {
        ValidatePage(page, pageSize, status);
        var actor = await ProcurementAccess.ActorAsync(persistence, current, ct, CommercialReaders);
        var result = await persistence.ListOrdersAsync(OwnerFilter(actor), page, pageSize, status, false, ct);
        return new(result.Items.Select(View).ToArray(), page, pageSize, result.TotalCount, result.TotalPages);
    }
    public async Task<PurchaseOrderResult> GetAsync(Guid id, CancellationToken ct = default)
    {
        var actor = await ProcurementAccess.ActorAsync(persistence, current, ct, CommercialReaders);
        var order = await OrderAsync(id, false, ct);
        EnsureOwner(actor, order);
        return View(order);
    }
    public async Task<PageResult<PurchaseOrderOperationalResult>> ListOperationalAsync(int page, int pageSize, PurchaseOrderStatus? status, CancellationToken ct = default)
    {
        ValidatePage(page, pageSize, status);
        var actor = await ProcurementAccess.ActorAsync(persistence, current, ct, OperationalReaders);
        var result = await persistence.ListOrdersAsync(OwnerFilter(actor), page, pageSize, status,
            actor.Role == UserRole.WarehouseOperator, ct);
        return new(result.Items.Select(OperationalView).ToArray(), page, pageSize, result.TotalCount, result.TotalPages);
    }
    public async Task<PurchaseOrderOperationalResult> GetOperationalAsync(Guid id, CancellationToken ct = default)
    {
        var actor = await ProcurementAccess.ActorAsync(persistence, current, ct, OperationalReaders);
        var order = await OrderAsync(id, false, ct);
        EnsureOwner(actor, order);
        if (actor.Role == UserRole.WarehouseOperator && !OperationalStatus(order.Status))
            throw new WorkflowAuthorizationException("Only approved or receiving-state POs are available to warehouse operators.");
        return OperationalView(order);
    }
    public async Task<PurchaseOrderResult> CreateAsync(CreatePurchaseOrderCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var lines = ValidateLines(command.SupplierId, command.WarehouseId, command.Items);
        await using var tx = await persistence.BeginAsync(ct: ct);
        var actor = await ProcurementAccess.ActorAsync(persistence, current, ct, Writers);
        await ValidateSourcesAsync(command.SupplierId, command.WarehouseId, lines, ct, requireActive: true);
        var order = ProcurementAccess.Domain(() => new PurchaseOrder(command.SupplierId, command.WarehouseId, actor.Id, lines, command.Notes));
        persistence.AddOrder(order);
        await persistence.SaveAsync(ct);
        await tx.CommitAsync(ct);
        return View(order);
    }
    public async Task<PurchaseOrderResult> UpdateAsync(Guid id, UpdatePurchaseOrderCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var lines = ValidateLines(command.SupplierId, command.WarehouseId, command.Items);
        await using var tx = await persistence.BeginAsync(ct: ct);
        var actor = await ProcurementAccess.ActorAsync(persistence, current, ct, Writers);
        var order = await OrderAsync(id, true, ct);
        EnsureOwner(actor, order);
        ProcurementAccess.Version(command.RowVersion, order.RowVersion);
        if (order.Status != PurchaseOrderStatus.Draft) throw new WorkflowConflictException("Only Draft purchase orders can be edited.");
        await ValidateSourcesAsync(command.SupplierId, command.WarehouseId, lines, ct, requireActive: true);
        persistence.RemoveDraftItems(order.Items.Where(x => lines.All(line => line.ProductId != x.ProductId)).ToArray());
        ProcurementAccess.Domain(() => { order.Edit(command.SupplierId, command.WarehouseId, lines, command.Notes); return true; });
        await persistence.SaveAsync(ct);
        await tx.CommitAsync(ct);
        return View(order);
    }
    public async Task<PurchaseOrderResult> SubmitAsync(Guid id, VersionCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await TransitionAsync(id, command.RowVersion, PurchaseOrderAction.Submitted, null, ct);
    }
    public async Task<PurchaseOrderResult> ApproveAsync(Guid id, PurchaseOrderDecisionCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await TransitionAsync(id, command.RowVersion, PurchaseOrderAction.Approved, command.Comments, ct);
    }
    public async Task<PurchaseOrderResult> RejectAsync(Guid id, PurchaseOrderReasonCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await TransitionAsync(id, command.RowVersion, PurchaseOrderAction.Rejected, command.Reason, ct);
    }
    public async Task<PurchaseOrderResult> CancelAsync(Guid id, PurchaseOrderReasonCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await TransitionAsync(id, command.RowVersion, PurchaseOrderAction.Cancelled, command.Reason, ct);
    }
    public async Task<IReadOnlyList<PurchaseOrderHistoryResult>> HistoryAsync(Guid id, CancellationToken ct = default)
    {
        var actor = await ProcurementAccess.ActorAsync(persistence, current, ct, CommercialReaders);
        var order = await OrderAsync(id, false, ct);
        EnsureOwner(actor, order);
        return (await persistence.ListHistoryAsync(id, ct)).Select(x => new PurchaseOrderHistoryResult(x.Id, x.ActorId,
            x.Action, x.FromStatus, x.ToStatus, x.Comments, x.Revision, x.CreatedAt)).ToArray();
    }

    private async Task<PurchaseOrderResult> TransitionAsync(Guid id, byte[] version, PurchaseOrderAction action, string? text, CancellationToken ct)
    {
        var decision = action is PurchaseOrderAction.Approved or PurchaseOrderAction.Rejected;
        PurchaseOrder.ValidateText(text, action is PurchaseOrderAction.Rejected or PurchaseOrderAction.Cancelled);
        await using var tx = await persistence.BeginAsync(ct: ct);
        var actor = await ProcurementAccess.ActorAsync(persistence, current, ct,
            decision ? [UserRole.Administrator, UserRole.Approver] : Writers);
        var order = await OrderAsync(id, true, ct);
        if (!decision) EnsureOwner(actor, order);
        else if (actor.Id == order.CreatedByUserId)
            throw new WorkflowAuthorizationException("A user cannot decide their own purchase order.");
        ProcurementAccess.Version(version, order.RowVersion);
        if (action == PurchaseOrderAction.Cancelled && order.Status == PurchaseOrderStatus.Approved && actor.Role != UserRole.Administrator)
            throw new WorkflowAuthorizationException("Only Administrators can cancel an Approved PO.");
        if (action is PurchaseOrderAction.Submitted or PurchaseOrderAction.Approved)
            await ValidateSourcesAsync(order.SupplierId, order.WarehouseId,
                order.Items.Select(x => (x.ProductId, x.Quantity, x.UnitCost)).ToArray(), ct,
                requireActive: action == PurchaseOrderAction.Submitted);
        var history = ProcurementAccess.Domain(() => action switch
        {
            PurchaseOrderAction.Submitted => order.Submit(actor.Id),
            PurchaseOrderAction.Approved => order.Approve(actor.Id, text),
            PurchaseOrderAction.Rejected => order.Reject(actor.Id, text!),
            PurchaseOrderAction.Cancelled => order.Cancel(actor.Id, text!),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        });
        await persistence.TransitionAsync(order, history, version, ct);
        await tx.CommitAsync(ct);
        return View(order);
    }
    private async Task ValidateSourcesAsync(Guid supplierId, Guid warehouseId,
        IReadOnlyCollection<(Guid ProductId, int Quantity, decimal UnitCost)> lines, CancellationToken ct, bool requireActive = false)
    {
        var warehouse = await persistence.FindWarehouseAsync(warehouseId, true, ct) ?? throw new EntityNotFoundException(nameof(Warehouse), warehouseId);
        if (!warehouse.IsActive) throw new WorkflowConflictException("The destination warehouse is inactive.");
        var supplier = await persistence.FindSupplierAsync(supplierId, true, ct) ?? throw new EntityNotFoundException(nameof(Supplier), supplierId);
        if (!supplier.IsActive) throw new WorkflowConflictException("The supplier is inactive.");
        var missing = await persistence.FindMissingProductAsync(lines.Select(x => x.ProductId).ToArray(), ct, requireActive);
        if (missing.HasValue) throw new EntityNotFoundException(nameof(Product), missing.Value);
    }
    private async Task<PurchaseOrder> OrderAsync(Guid id, bool tracking, CancellationToken ct)
    {
        ProcurementAccess.Id(id);
        return await persistence.FindOrderAsync(id, tracking, ct) ?? throw new EntityNotFoundException(nameof(PurchaseOrder), id);
    }
    private static (Guid ProductId, int Quantity, decimal UnitCost)[] ValidateLines(Guid supplier, Guid warehouse,
        IReadOnlyCollection<PurchaseOrderLineCommand>? lines)
    {
        ProcurementAccess.Id(supplier);
        ProcurementAccess.Id(warehouse);
        if (lines is null || lines.Count is < 1 or > PurchaseOrder.MaxLines || lines.Any(x => x is null) ||
            lines.Select(x => x.ProductId).Distinct().Count() != lines.Count || lines.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0 ||
                x.UnitCost < 0 || x.UnitCost > PurchaseOrderItem.MaxUnitCost || decimal.Round(x.UnitCost, 4) != x.UnitCost))
            throw new WorkflowValidationException("PO requires 1-100 distinct valid products, positive quantities and bounded nonnegative four-decimal costs.");
        return lines.Select(x => (x.ProductId, x.Quantity, x.UnitCost)).ToArray();
    }
    private static void ValidatePage(int page, int size, PurchaseOrderStatus? status)
    {
        ProcurementAccess.Page(page, size);
        if (status.HasValue && !Enum.IsDefined(status.Value)) throw new WorkflowValidationException("Unknown PO status.");
    }
    private static Guid? OwnerFilter(AppUser actor) => actor.Role == UserRole.Purchasing ? actor.Id : null;
    private static void EnsureOwner(AppUser actor, PurchaseOrder order)
    {
        if (actor.Role == UserRole.Purchasing && actor.Id != order.CreatedByUserId)
            throw new WorkflowAuthorizationException("Purchasing users may access only their own purchase orders.");
    }
    private static bool OperationalStatus(PurchaseOrderStatus status) => status is PurchaseOrderStatus.Approved or
        PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received or PurchaseOrderStatus.ShortClosed;
    private static PurchaseOrderResult View(PurchaseOrder x) => new(x.Id, x.PONumber, x.SupplierId, x.WarehouseId,
        x.CreatedByUserId, x.Status, x.CreatedAt, x.SubmittedAt, x.DecidedAt, x.Notes, x.TotalAmount,
        x.Items.Select(i => new PurchaseOrderItemResult(i.Id, i.ProductId, i.Quantity, i.UnitCost, i.LineTotal, i.ReceivedQuantity, i.RemainingQuantity)).ToArray(), x.RowVersion);
    private static PurchaseOrderOperationalResult OperationalView(PurchaseOrder x) => new(x.Id, x.PONumber, x.SupplierId,
        x.WarehouseId, x.Status, x.CreatedAt, x.Items.Select(i => new PurchaseOrderOperationalItem(i.ProductId, i.Quantity, i.Id, i.ReceivedQuantity, i.RemainingQuantity)).ToArray(), x.RowVersion);
}
