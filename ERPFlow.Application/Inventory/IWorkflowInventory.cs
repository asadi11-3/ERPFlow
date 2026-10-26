using ERPFlow.Domain.Entities;

namespace ERPFlow.Application.Inventory;

public interface IWorkflowInventory
{
    bool UsesWarehouseStock { get; }
    Task ReserveSalesAsync(SalesOrder order, CancellationToken cancellationToken = default);
    Task ReleaseSalesAsync(SalesOrder order, CancellationToken cancellationToken = default);
    Task ConsumeSalesAsync(SalesOrder order, CancellationToken cancellationToken = default);
    Task ReservePartsAsync(Jobcard jobcard, IReadOnlyCollection<Guid> newItemIds, CancellationToken cancellationToken = default);
    Task ConsumePartsAsync(Jobcard jobcard, CancellationToken cancellationToken = default);
}
