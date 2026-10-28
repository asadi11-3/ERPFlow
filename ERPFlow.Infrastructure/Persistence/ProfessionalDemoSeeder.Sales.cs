using ERPFlow.Application.Equipment;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class ProfessionalDemoSeeder
{
    private async Task RunSalesOrderAsync(ProfessionalDemoSalesOrderSeed seed, Guid customer, Guid correctionCustomer, Guid product, Guid warehouse,
        Guid sales, Guid approver, IInventoryWriteGate gate, CancellationToken ct)
    {
        var initialCustomer = seed.ReturnedForCorrection ? correctionCustomer : customer;
        var fingerprint = Fingerprint(seed.Key, customer.ToString(), initialCustomer.ToString(), product.ToString(), warehouse.ToString(),
            seed.Quantity.ToString(), seed.Status.ToString(), seed.ReturnedForCorrection.ToString(), seed.Dispatch.ToString());
        var entry = await BeginOperationAsync(seed.Key, "SalesOrder", fingerprint, sales, ct);
        if (entry.CompletedAt is null)
        {
            Guid orderId = Guid.Empty;
            await RunAtAsync(seed.CreatedAt, async () =>
            {
                var workflow = SalesWorkflow(sales, gate);
                var order = await context.SalesOrders.Include(x => x.Items).Include(x => x.Reservations)
                    .SingleOrDefaultAsync(x => x.CreatedByUserId == sales && x.CreatedAt == seed.CreatedAt.UtcDateTime, ct);
                if (order is null)
                {
                    var created = await workflow.CreateDraftAsync(new(initialCustomer, [new(product, seed.Quantity, warehouse)]), ct);
                    await ProbeAsync(seed.Key, "Created");
                    order = await context.SalesOrders.Include(x => x.Items).Include(x => x.Reservations).SingleAsync(x => x.Id == created.Id, ct);
                }
                ValidateSalesOrderIdentity(seed, order, customer, initialCustomer, product, warehouse,
                    allowInitialCustomer: seed.ReturnedForCorrection && order.Status == OrderStatus.Draft);

                try
                {
                    await AdvanceSalesOrderAsync(seed, order, customer, product, warehouse, sales, approver, gate, ct);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw new InvalidOperationException($"Professional demo sales order '{seed.Key}' could not advance.", exception);
                }
                order = await ReloadAsync(order.Id, ct);
                ValidateSalesOrderIdentity(seed, order, customer, initialCustomer, product, warehouse, allowInitialCustomer: false);
                if (order.Status != seed.Status)
                    throw new InvalidOperationException($"Professional demo sales order '{seed.Key}' ended in {order.Status}, expected {seed.Status}.");
                orderId = order.Id;
            });
            await CompleteOperationAsync(entry.Id, orderId, ct);
        }
    }

    private async Task AdvanceSalesOrderAsync(ProfessionalDemoSalesOrderSeed seed, SalesOrder order, Guid customer, Guid product,
        Guid warehouse, Guid sales, Guid approver, IInventoryWriteGate gate, CancellationToken ct)
    {
        var salesWorkflow = SalesWorkflow(sales, gate);
        if (seed.Status == OrderStatus.Draft && !seed.ReturnedForCorrection) return;
        if (order.Status == OrderStatus.Draft)
        {
            var returned = await context.ApprovalHistories.AsNoTracking().AnyAsync(x => x.SalesOrderId == order.Id && x.Action == ApprovalAction.Returned, ct);
            if (seed.ReturnedForCorrection && returned)
            {
                var draft = await ReloadAsync(order.Id, ct);
                await salesWorkflow.UpdateDraftAsync(order.Id,
                    new(customer, [new(product, seed.Quantity, warehouse)], draft.RowVersion), ct);
            }
            var submitted = await salesWorkflow.SubmitAsync(order.Id, ct);
            await ProbeAsync(seed.Key, "Submitted");
            order = await ReloadAsync(submitted.Id, ct);
        }
        if (seed.ReturnedForCorrection && !await context.ApprovalHistories.AsNoTracking().AnyAsync(x => x.SalesOrderId == order.Id && x.Action == ApprovalAction.Returned, ct))
        {
            await SalesWorkflow(approver, gate).ReturnAsync(order.Id, "Professional demo correction requested.", ct);
            await ProbeAsync(seed.Key, "Returned");
            var draft = await ReloadAsync(order.Id, ct);
            var revised = await salesWorkflow.UpdateDraftAsync(order.Id, new(customer, [new(product, seed.Quantity, warehouse)], draft.RowVersion), ct);
            await salesWorkflow.SubmitAsync(revised.Id, ct);
            order = await ReloadAsync(order.Id, ct);
        }
        if (seed.Status == OrderStatus.PendingApproval)
        {
            if (order.Status != OrderStatus.PendingApproval)
                throw new InvalidOperationException($"Professional demo sales order '{seed.Key}' ended in {order.Status}, expected PendingApproval.");
            return;
        }
        if (seed.Status == OrderStatus.Approved && order.Status == OrderStatus.PendingApproval)
            await SalesWorkflow(approver, gate).ApproveAsync(order.Id, "Professional demo approval.", ct);
        else if (seed.Status == OrderStatus.Rejected && order.Status == OrderStatus.PendingApproval)
            await SalesWorkflow(approver, gate).RejectAsync(order.Id, "Professional demo rejection.", ct);
        else if (order.Status != seed.Status)
            throw new InvalidOperationException($"Professional demo sales order '{seed.Key}' cannot reconcile {order.Status} to {seed.Status}.");
    }

    private async Task RunDispatchAsync(ProfessionalDemoSalesOrderSeed seed, Guid sales, Guid technician, IInventoryWriteGate gate, CancellationToken ct)
    {
        var key = $"dispatch-{seed.Key}";
        var fingerprint = Fingerprint(key, seed.Key, technician.ToString());
        var entry = await BeginOperationAsync(key, "EquipmentDispatch", fingerprint, technician, ct);
        if (entry.CompletedAt is not null) return;
        DeliveryNote? delivery = null;
        await RunAtAsync(seed.CreatedAt.AddHours(2), async () =>
        {
            var order = await context.SalesOrders.AsNoTracking().Include(x => x.Items)
                .SingleAsync(x => x.CreatedByUserId == sales && x.CreatedAt == seed.CreatedAt.UtcDateTime, ct);
            delivery = await context.DeliveryNotes.SingleOrDefaultAsync(x => x.SalesOrderId == order.Id, ct);
            if (delivery is null)
            {
                var serials = order.Items.SelectMany(item => Enumerable.Range(1, item.Quantity)
                    .Select(index => new MachineSerialInput(item.ProductId, $"PD-6B2B-{seed.Key}-{index:D2}"))).ToArray();
                var result = await new EquipmentWorkflow(new EfEquipmentPersistence(context), new SeedActor(technician),
                    new DemoWarehousePolicy(context), new EfWorkflowInventory(context, new SeedActor(technician), gate))
                    .DispatchOrderAsync(order.Id, new("JOT Logistics", $"PD-6B2B-{seed.Key}", serials), ct);
                delivery = await context.DeliveryNotes.SingleAsync(x => x.Id == result.Id, ct);
            }
            var expectedSerials = order.Items.SelectMany(item => Enumerable.Range(1, item.Quantity)
                .Select(index => (item.ProductId, Serial: $"PD-6B2B-{seed.Key}-{index:D2}".ToUpperInvariant()))).ToArray();
            var assets = await context.MachineAssets.AsNoTracking().Where(x => x.SalesOrderId == order.Id)
                .OrderBy(x => x.SerialNumber).ToArrayAsync(ct);
            if (delivery.CustomerId != order.CustomerId || delivery.Carrier != "JOT Logistics" ||
                delivery.TrackingNumber != $"PD-6B2B-{seed.Key}" || assets.Length != expectedSerials.Length ||
                assets.Any(asset => asset.CustomerId != order.CustomerId || asset.SalesOrderId != order.Id) ||
                !assets.Select(x => (x.ProductId, x.SerialNumber)).SequenceEqual(expectedSerials.OrderBy(x => x.Serial)))
                throw new InvalidOperationException($"Professional demo dispatch '{seed.Key}' does not match its persisted order, customer, tracking or serial contract.");
        });
        await CompleteOperationAsync(entry.Id, delivery!.Id, ct);
    }

    private SalesOrderWorkflow SalesWorkflow(Guid actor, IInventoryWriteGate gate) => new(new EfSalesOrderPersistence(context),
        new SeedActor(actor), new DemoWarehousePolicy(context), new EfWorkflowInventory(context, new SeedActor(actor), gate));

    private Task<SalesOrder> ReloadAsync(Guid id, CancellationToken ct) => context.SalesOrders.Include(x => x.Items)
        .Include(x => x.Reservations).SingleAsync(x => x.Id == id, ct);

    private static void ValidateSalesOrderIdentity(ProfessionalDemoSalesOrderSeed seed, SalesOrder order,
        Guid customer, Guid initialCustomer, Guid product, Guid warehouse, bool allowInitialCustomer)
    {
        var item = order.Items.SingleOrDefault();
        var validCustomer = order.CustomerId == customer || allowInitialCustomer && order.CustomerId == initialCustomer;
        if (!validCustomer || order.CreatedAt != seed.CreatedAt.UtcDateTime || item is null ||
            item.ProductId != product || item.Quantity != seed.Quantity || item.WarehouseId != warehouse)
            throw new InvalidOperationException($"Professional demo sales order '{seed.Key}' does not match its deterministic customer, product, quantity or source.");
    }
}
