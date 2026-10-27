using System.Security.Cryptography;
using System.Text.Json;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;

namespace ERPFlow.Application.Receiving;

public sealed class GoodsReceiptWorkflow(IGoodsReceiptPersistence persistence, ICurrentUser current,
    IInventoryWriteGate gate, IInventoryPosting posting) : IGoodsReceiptWorkflow
{
    public async Task<GoodsReceiptResult> CreateAsync(CreateGoodsReceiptCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Key(command.IdempotencyKey);
        ProcurementAccess.Id(command.PurchaseOrderId);
        Token(command.PurchaseOrderRowVersion);
        if (command.Items is null || command.Items.Count is < 1 or > 100 || command.Items.Any(x => x is null || x.PurchaseOrderItemId == Guid.Empty || x.Quantity <= 0) ||
            command.Items.Select(x => x.PurchaseOrderItemId).Distinct().Count() != command.Items.Count)
            throw new WorkflowValidationException("A GRN requires 1-100 distinct original PO lines with positive quantities.");
        var reference = GoodsReceipt.Text(command.SupplierDeliveryReference, 100)?.ToUpperInvariant();
        var notes = GoodsReceipt.Text(command.Notes, 1000);
        var hash = Hash(new { Version = 1, Operation = 0, command.PurchaseOrderId,
            Token = Convert.ToBase64String(command.PurchaseOrderRowVersion), Reference = reference, Notes = notes,
            Lines = command.Items.OrderBy(x => x.PurchaseOrderItemId).ToArray() });
        gate.EnsureWarehouseWritesAllowed();
        await using var transaction = await persistence.BeginAsync(ct);
        var actor = await ActorAsync(ct);
        RequireWarehouse();
        var replay = await ReplayAsync(actor, command.IdempotencyKey, hash, ct);
        if (replay is not null) { await transaction.CommitAsync(ct); return replay; }
        var order = await persistence.LockOrderAsync(command.PurchaseOrderId, ct) ?? throw new EntityNotFoundException(nameof(PurchaseOrder), command.PurchaseOrderId);
        ProcurementAccess.Version(command.PurchaseOrderRowVersion, order.RowVersion);
        ProcurementAccess.Domain(() => order.ValidateReceipt(command.Items.Select(x => (x.PurchaseOrderItemId, x.Quantity)).ToArray()));
        await persistence.EnsureSourcesAsync(order, ct);
        var lines = command.Items.Select(x => (x.PurchaseOrderItemId, order.Items.Single(i => i.Id == x.PurchaseOrderItemId).ProductId, x.Quantity)).ToArray();
        var receipt = new GoodsReceipt(order.Id, order.SupplierId, order.WarehouseId, actor, lines, reference, notes);
        await persistence.EnterPostingScopeAsync(receipt.Id, ct);
        persistence.AddReceipt(receipt);
        await persistence.SaveAsync(ct);
        await persistence.AdmitReceivingAsync(receipt, command.PurchaseOrderRowVersion, actor, ct);
        var result = await CompleteCommandAsync(actor, command.IdempotencyKey, 0, hash, receipt.Id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<GoodsReceiptResult> PostAsync(Guid id, PostGoodsReceiptCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProcurementAccess.Id(id);
        Key(command.IdempotencyKey);
        Token(command.RowVersion);
        var hash = Hash(new { Version = 1, Operation = 1, Id = id, Token = Convert.ToBase64String(command.RowVersion) });
        gate.EnsureWarehouseWritesAllowed();
        await using var transaction = await persistence.BeginAsync(ct);
        var actor = await ActorAsync(ct);
        RequireWarehouse();
        var replay = await ReplayAsync(actor, command.IdempotencyKey, hash, ct);
        if (replay is not null) { await transaction.CommitAsync(ct); return replay; }
        var header = await persistence.FindReceiptAsync(id, false, ct) ?? throw new EntityNotFoundException(nameof(GoodsReceipt), id);
        var order = await persistence.LockOrderAsync(header.PurchaseOrderId, ct) ?? throw new EntityNotFoundException(nameof(PurchaseOrder), header.PurchaseOrderId);
        var receipt = await persistence.FindReceiptAsync(id, true, ct) ?? throw new EntityNotFoundException(nameof(GoodsReceipt), id);
        ProcurementAccess.Version(command.RowVersion, receipt.RowVersion);
        ProcurementAccess.Domain(() => { receipt.EnsurePostable(); return order.ValidateReceipt(receipt.Items.Select(x => (x.PurchaseOrderItemId, x.Quantity)).ToArray()); });
        await persistence.EnsureSourcesAsync(order, ct);
        await persistence.EnterPostingScopeAsync(receipt.Id, ct);
        await persistence.LockStocksAsync(receipt, ct);
        foreach (var item in receipt.Items.OrderBy(x => x.ProductId))
            await posting.PostReceiptAsync(new(item.Id, $"grn:{receipt.Id:N}:{item.Id:N}"), ct);
        receipt.Post(actor);
        await persistence.FinalizePostingAsync(receipt, command.RowVersion, actor, ct);
        var result = await CompleteCommandAsync(actor, command.IdempotencyKey, 1, hash, receipt.Id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<GoodsReceiptResult> CancelAsync(Guid id, CancelGoodsReceiptCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProcurementAccess.Id(id);
        Token(command.RowVersion);
        await using var transaction = await persistence.BeginAsync(ct);
        var actor = await ActorAsync(ct);
        var header = await persistence.FindReceiptAsync(id, false, ct) ?? throw new EntityNotFoundException(nameof(GoodsReceipt), id);
        await persistence.LockOrderAsync(header.PurchaseOrderId, ct);
        var receipt = await persistence.FindReceiptAsync(id, true, ct) ?? throw new EntityNotFoundException(nameof(GoodsReceipt), id);
        ProcurementAccess.Version(command.RowVersion, receipt.RowVersion);
        ProcurementAccess.Domain(() => { receipt.Cancel(actor, command.Reason); return true; });
        await persistence.EnterPostingScopeAsync(receipt.Id, ct);
        await persistence.SaveAsync(ct);
        var result = await persistence.ResultAsync(id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<GoodsReceiptResult> GetAsync(Guid id, CancellationToken ct = default)
    {
        ProcurementAccess.Id(id);
        await ActorAsync(ct);
        return await persistence.ResultAsync(id, ct);
    }
    public async Task<PageResult<GoodsReceiptResult>> ListAsync(int page, int pageSize, Guid? purchaseOrderId, GoodsReceiptStatus? status, CancellationToken ct = default)
    {
        ProcurementAccess.Page(page, pageSize);
        if (purchaseOrderId == Guid.Empty || (status.HasValue && !Enum.IsDefined(status.Value)))
            throw new WorkflowValidationException("Receipt filter is invalid.");
        await ActorAsync(ct);
        return await persistence.ListAsync(page, pageSize, purchaseOrderId, status, ct);
    }
    private async Task<Guid> ActorAsync(CancellationToken ct)
    {
        var actor = await persistence.FindActorAsync(current.UserId, ct);
        if (actor is null || actor.Role is not (UserRole.Administrator or UserRole.WarehouseOperator))
            throw new WorkflowAuthorizationException("Only persisted WarehouseOperators and Administrators may receive goods.");
        return actor.Id;
    }
    private void RequireWarehouse()
    {
        if (!persistence.WarehouseAuthority) throw new WorkflowConflictException("GRN commands require a completed inventory authority cutover.");
    }
    private async Task<GoodsReceiptResult?> ReplayAsync(Guid actor, string key, byte[] hash, CancellationToken ct)
    {
        var previous = await persistence.LockCommandAsync(actor, key, ct);
        if (previous is null) return null;
        if (!previous.RequestHash.SequenceEqual(hash)) throw new WorkflowConflictException("GRN idempotency key already belongs to a different command.");
        return JsonSerializer.Deserialize<GoodsReceiptResult>(previous.ResponseJson) ?? throw new InvalidOperationException("Stored GRN response is invalid.");
    }
    private async Task<GoodsReceiptResult> CompleteCommandAsync(Guid actor, string key, int operation, byte[] hash, Guid id, CancellationToken ct)
    {
        var result = await persistence.ResultAsync(id, ct);
        persistence.AddCommand(new(actor, key, operation, hash, id, JsonSerializer.Serialize(result)));
        await persistence.SaveAsync(ct);
        return result;
    }
    private static byte[] Hash<T>(T payload) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload));
    private static void Token(byte[]? token)
    {
        if (token is not { Length: 8 }) throw new WorkflowValidationException("An eight-byte concurrency token is required.");
    }
    private static void Key(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsWhiteSpace))
            throw new WorkflowValidationException("A nonblank idempotency key of at most 128 non-whitespace characters is required.");
    }
}
