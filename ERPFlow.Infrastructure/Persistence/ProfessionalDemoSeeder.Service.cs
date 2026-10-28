using ERPFlow.Application.Inventory;
using ERPFlow.Application.Service;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class ProfessionalDemoSeeder
{
    private async Task RunServiceJobcardAsync(ProfessionalDemoServiceSeed seed, Guid customer, Guid product,
        Guid warehouse, MachineAsset? asset, Guid technician, IInventoryWriteGate gate, CancellationToken ct)
    {
        var technicianName = "Yousef Tawalbeh";
        var fault = $"Professional demo service case {seed.Key}.";
        const string completionNotes = "Professional demo repair completed and verified.";
        var fingerprint = Fingerprint(seed.Key, customer.ToString(), product.ToString(), warehouse.ToString(),
            asset?.Id.ToString() ?? string.Empty, seed.Priority.ToString(), seed.Status.ToString());
        var entry = await BeginOperationAsync(seed.Key, "ServiceJobcard", fingerprint, technician, ct);
        if (entry.CompletedAt is not null) return;

        var matches = await context.Jobcards.Include(x => x.Items).Where(x => x.CreatedAt == seed.CreatedAt.UtcDateTime &&
            x.CustomerId == customer && x.TechnicianName == technicianName && x.ReportedFault == fault).ToArrayAsync(ct);
        if (matches.Length > 1)
            throw new InvalidOperationException($"Professional demo service case '{seed.Key}' has an ambiguous durable identity.");

        var workflow = ServiceWorkflow(technician, gate);
        var jobcard = matches.SingleOrDefault();
        if (jobcard is null)
        {
            await RunAtAsync(seed.CreatedAt, async () =>
            {
                var created = await workflow.CreateJobcardAsync(new(customer, asset?.Id, technicianName, fault, seed.Priority), ct);
                jobcard = await context.Jobcards.Include(x => x.Items).SingleAsync(x => x.Id == created.Id, ct);
            });
        }

        if (jobcard!.CustomerId != customer || jobcard.MachineAssetId != asset?.Id || jobcard.Priority != seed.Priority ||
            jobcard.CreatedAt != seed.CreatedAt.UtcDateTime || jobcard.TechnicianName != technicianName || jobcard.ReportedFault != fault)
            throw new InvalidOperationException($"Professional demo service case '{seed.Key}' does not match its durable business identity.");

        if (seed.Status != JobcardStatus.Open)
        {
            if (jobcard.Items.Count == 0)
            {
                await RunAtAsync(seed.CreatedAt.AddMinutes(30), async () =>
                    await workflow.RequestPartsAsync(jobcard.Id, new([new(product, 1, warehouse)]), ct));
                jobcard = await ReloadJobcardAsync(jobcard.Id, ct);
            }
            else if (jobcard.Items.Count != 1 || jobcard.Items[0].ProductId != product ||
                     jobcard.Items[0].Quantity != 1 || jobcard.Items[0].WarehouseId != warehouse)
                throw new InvalidOperationException($"Professional demo service parts for '{seed.Key}' do not match their durable input.");
        }

        if (seed.Status == JobcardStatus.Completed && jobcard.Status != JobcardStatus.Completed)
        {
            if (jobcard.Status != JobcardStatus.PartsRequested)
                throw new InvalidOperationException($"Professional demo service case '{seed.Key}' cannot reconcile {jobcard.Status} to Completed.");
            int? meter = asset is null ? null : 1000 + seed.AssetIndex!.Value * 100;
            await RunAtAsync(seed.CreatedAt.AddHours(2), async () =>
                await workflow.CompleteJobcardAsync(jobcard.Id, new(completionNotes, meter), ct));
            jobcard = await ReloadJobcardAsync(jobcard.Id, ct);
        }

        if (jobcard.Status != seed.Status)
            throw new InvalidOperationException($"Professional demo service case '{seed.Key}' ended in {jobcard.Status}, expected {seed.Status}.");
        if (seed.Status == JobcardStatus.Completed)
        {
            int? expectedMeter = asset is null ? null : 1000 + seed.AssetIndex!.Value * 100;
            if (jobcard.CompletedAt != seed.CreatedAt.AddHours(2).UtcDateTime ||
                jobcard.TechnicianNotes != completionNotes || jobcard.FinalMeterReading != expectedMeter)
                throw new InvalidOperationException($"Professional demo completed service case '{seed.Key}' does not match its completion contract.");
            if (asset is not null)
            {
                var persistedMeter = await context.MachineAssets.AsNoTracking().Where(x => x.Id == asset.Id)
                    .Select(x => x.MeterReading).SingleAsync(ct);
                if (persistedMeter != expectedMeter)
                    throw new InvalidOperationException($"Professional demo completed service case '{seed.Key}' did not persist its asset meter reading.");
            }
        }
        await CompleteOperationAsync(entry.Id, jobcard.Id, ct);
    }

    private ServiceWorkflow ServiceWorkflow(Guid actor, IInventoryWriteGate gate) => new(new EfServicePersistence(context),
        new SeedActor(actor), new DemoWarehousePolicy(context), new EfWorkflowInventory(context, new SeedActor(actor), gate));

    private Task<Jobcard> ReloadJobcardAsync(Guid id, CancellationToken ct) => context.Jobcards.Include(x => x.Items)
        .SingleAsync(x => x.Id == id, ct);
}
