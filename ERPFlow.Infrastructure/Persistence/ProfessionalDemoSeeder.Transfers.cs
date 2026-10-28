using System.Globalization;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Transfers;
using ERPFlow.Infrastructure.Persistence.Inventory;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class ProfessionalDemoSeeder
{
    private async Task RunTransferAsync(ProfessionalDemoTransferSeed seed, Guid product, Guid source, Guid destination,
        Guid warehouseOperator, IInventoryWriteGate gate, CancellationToken ct)
    {
        var fingerprint = Fingerprint(seed.Key, source.ToString(), destination.ToString(), product.ToString(),
            seed.Quantity.ToString(CultureInfo.InvariantCulture), seed.Status.ToString());
        var entry = await BeginOperationAsync(seed.Key, "StockTransfer", fingerprint, warehouseOperator, ct);
        if (entry.CompletedAt is not null) return;

        Guid documentId = Guid.Empty;
        await RunAtAsync(seed.CreatedAt, async () =>
        {
            var workflow = new StockTransferWorkflow(new EfStockTransferPersistence(context), new SeedActor(warehouseOperator), gate,
                new EfInventoryPosting(context, gate, new SeedActor(warehouseOperator)));
            var createKey = $"professional-demo-{seed.Key}";
            var command = new CreateStockTransferCommand(source, destination, [new(product, seed.Quantity)]);
            var created = await workflow.CreateAsync(createKey, command, ct);
            var createReplay = await workflow.CreateAsync(createKey, command, ct);
            if (createReplay.Id != created.Id)
                throw new InvalidOperationException($"Professional demo transfer '{seed.Key}' create replay changed its document.");

            var current = await workflow.GetAsync(created.Id, ct);
            switch (seed.Status)
            {
                case StockTransferStatus.Draft when current.Status == StockTransferStatus.Draft:
                    break;
                case StockTransferStatus.Posted when current.Status == StockTransferStatus.Draft:
                {
                    var postKey = $"professional-demo-{seed.Key}-post";
                    var posted = await workflow.PostAsync(current.Id, postKey, new(current.RowVersion), ct);
                    var postReplay = await workflow.PostAsync(current.Id, postKey, new(current.RowVersion), ct);
                    if (postReplay.Id != posted.Id)
                        throw new InvalidOperationException($"Professional demo transfer '{seed.Key}' post replay changed its document.");
                    break;
                }
                case StockTransferStatus.Cancelled when current.Status == StockTransferStatus.Draft:
                    await workflow.CancelAsync(current.Id, new(current.RowVersion, "Professional demo cancellation"), ct);
                    break;
                case var status when status == current.Status:
                    break;
                default:
                    throw new InvalidOperationException($"Professional demo transfer '{seed.Key}' cannot reconcile {current.Status} to {seed.Status}.");
            }
            documentId = current.Id;
        });
        await CompleteOperationAsync(entry.Id, documentId, ct);
    }
}
