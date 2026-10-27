using System.Security.Cryptography;
using System.Text.Json;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Transfers;

namespace ERPFlow.Application.Transfers;

public sealed class StockTransferWorkflow(IStockTransferPersistence persistence, ICurrentUser current,
    IInventoryWriteGate gate, IInventoryPosting posting) : IStockTransferWorkflow
{
    public async Task<StockTransferResult> CreateAsync(string key, CreateStockTransferCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Key(key);
        ValidateCreate(command);
        var lines = command.Items.OrderBy(x => x.ProductId).ToArray();
        var hash = Hash(new
        {
            Version = 1,
            Operation = 0,
            command.SourceWarehouseId,
            command.DestinationWarehouseId,
            Lines = lines
        });

        await using var transaction = await persistence.BeginAsync(ct);
        var actor = await ActorAsync(ct);
        var replay = await ReplayAsync(actor, key, hash, ct);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct);
            return replay;
        }

        var productIds = lines.Select(x => x.ProductId).ToArray();
        await persistence.EnsureWarehousesAsync(command.SourceWarehouseId, command.DestinationWarehouseId, productIds, ct);
        var transfer = new StockTransfer(command.SourceWarehouseId, command.DestinationWarehouseId, actor,
            lines.Select(x => (x.ProductId, x.Quantity)).ToArray());
        await persistence.EnterScopeAsync(transfer.Id, ct);
        persistence.AddTransfer(transfer);
        await persistence.SaveAsync(ct);
        var result = await CompleteCommandAsync(actor, key, 0, hash, transfer.Id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<StockTransferResult> PostAsync(Guid id, string key, PostStockTransferCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProcurementAccess.Id(id);
        Key(key);
        var hash = Hash(new
        {
            Version = 1,
            Operation = 1,
            Id = id,
            Token = Convert.ToBase64String(command.RowVersion ?? [])
        });
        gate.EnsureWarehouseWritesAllowed();

        await using var transaction = await persistence.BeginAsync(ct);
        var actor = await ActorAsync(ct);
        RequireWarehouse();
        var replay = await ReplayAsync(actor, key, hash, ct);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct);
            return replay;
        }

        Token(command.RowVersion);
        var transfer = await persistence.FindAsync(id, true, ct)
            ?? throw new EntityNotFoundException(nameof(StockTransfer), id);
        ProcurementAccess.Version(command.RowVersion, transfer.RowVersion);
        ProcurementAccess.Domain(() => { transfer.EnsurePostable(); return true; });
        await persistence.EnsureWarehousesAsync(transfer.SourceWarehouseId, transfer.DestinationWarehouseId,
            transfer.Items.Select(x => x.ProductId).ToArray(), ct);
        await persistence.EnterScopeAsync(transfer.Id, ct);
        await posting.PostTransferAsync(new InventoryTransferRequest(transfer.Id), ct);
        ProcurementAccess.Domain(() => { transfer.Post(actor); return true; });
        await persistence.SaveAsync(ct);
        var result = await CompleteCommandAsync(actor, key, 1, hash, transfer.Id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<StockTransferResult> CancelAsync(Guid id, CancelStockTransferCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProcurementAccess.Id(id);
        Token(command.RowVersion);
        await using var transaction = await persistence.BeginAsync(ct);
        var actor = await ActorAsync(ct);
        var transfer = await persistence.FindAsync(id, true, ct)
            ?? throw new EntityNotFoundException(nameof(StockTransfer), id);
        ProcurementAccess.Version(command.RowVersion, transfer.RowVersion);
        ProcurementAccess.Domain(() => { transfer.Cancel(actor, command.Reason); return true; });
        await persistence.EnterScopeAsync(transfer.Id, ct);
        await persistence.SaveAsync(ct);
        var result = await persistence.ResultAsync(id, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<StockTransferResult> GetAsync(Guid id, CancellationToken ct = default)
    {
        ProcurementAccess.Id(id);
        await ActorAsync(ct);
        return await persistence.ResultAsync(id, ct);
    }

    public async Task<PageResult<StockTransferResult>> ListAsync(int page, int pageSize, Guid? warehouseId,
        Guid? productId, StockTransferStatus? status, CancellationToken ct = default)
    {
        ProcurementAccess.Page(page, pageSize);
        if (warehouseId == Guid.Empty || productId == Guid.Empty || (status.HasValue && !Enum.IsDefined(status.Value)))
            throw new WorkflowValidationException("Transfer filter is invalid.");
        await ActorAsync(ct);
        return await persistence.ListAsync(page, pageSize, warehouseId, productId, status, ct);
    }

    private async Task<Guid> ActorAsync(CancellationToken ct)
    {
        var actor = await persistence.FindActorAsync(current.UserId, ct);
        if (actor is null || actor.Role is not (UserRole.Administrator or UserRole.WarehouseOperator))
            throw new WorkflowAuthorizationException("Only persisted WarehouseOperators and Administrators may use stock transfers.");
        return actor.Id;
    }

    private void RequireWarehouse()
    {
        if (!persistence.WarehouseAuthority)
            throw new WorkflowConflictException("Posting stock transfers requires a completed inventory authority cutover.");
    }

    private async Task<StockTransferResult?> ReplayAsync(Guid actor, string key, byte[] hash, CancellationToken ct)
    {
        var previous = await persistence.LockCommandAsync(actor, key, ct);
        if (previous is null) return null;
        if (!previous.RequestHash.SequenceEqual(hash))
            throw new WorkflowConflictException("Transfer idempotency key already belongs to a different command.");
        return JsonSerializer.Deserialize<StockTransferResult>(previous.ResponseJson)
            ?? throw new InvalidOperationException("Stored transfer response is invalid.");
    }

    private async Task<StockTransferResult> CompleteCommandAsync(Guid actor, string key, int operation, byte[] hash,
        Guid id, CancellationToken ct)
    {
        var result = await persistence.ResultAsync(id, ct);
        persistence.AddCommand(new StockTransferCommand(actor, key, operation, hash, id, JsonSerializer.Serialize(result)));
        await persistence.SaveAsync(ct);
        return result;
    }

    private static void ValidateCreate(CreateStockTransferCommand command)
    {
        if (command.SourceWarehouseId == Guid.Empty || command.DestinationWarehouseId == Guid.Empty ||
            command.SourceWarehouseId == command.DestinationWarehouseId || command.Items is null ||
            command.Items.Count is < 1 or > 100 ||
            command.Items.Any(x => x is null || x.ProductId == Guid.Empty || x.Quantity <= 0) ||
            command.Items.Select(x => x.ProductId).Distinct().Count() != command.Items.Count)
            throw new WorkflowValidationException("A transfer requires distinct nonempty warehouses and 1-100 distinct products with positive quantities.");
    }

    private static byte[] Hash<T>(T payload) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload));

    private static void Token(byte[]? token)
    {
        if (token is not { Length: 8 })
            throw new WorkflowValidationException("An eight-byte concurrency token is required.");
    }

    private static void Key(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(x => char.IsWhiteSpace(x) || char.IsControl(x)))
            throw new WorkflowValidationException("A nonblank idempotency key of at most 128 non-whitespace characters is required.");
    }
}
