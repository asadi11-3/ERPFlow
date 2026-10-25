using System.Net;
using System.Net.Http.Json;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Application.Service;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class ApiIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldAndExplicitDefaultSalesRequestsPersistCompatibleSources(bool explicitWarehouse)
    {
        var sample = await CreateAuditDraftAsync();
        object line = explicitWarehouse
            ? new { productId = sample.Items[0].ProductId, quantity = 1, warehouseId = LegacyDefaultWarehouse.Id }
            : new { productId = sample.Items[0].ProductId, quantity = 1 };
        var response = await client.PostAsJsonAsync("/api/sales-orders", new { customerId = sample.CustomerId, items = new[] { line } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<SalesOrderResult>())!;
        Assert.Equal(LegacyDefaultWarehouse.Id, Assert.Single(draft.Items).WarehouseId);
        var submitted = await client.PostAsync($"/api/sales-orders/{draft.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal(LegacyDefaultWarehouse.Id, Assert.Single((await submitted.Content.ReadFromJsonAsync<SalesOrderResult>())!.Reservations).WarehouseId);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.BadRequest)]
    [InlineData(false, HttpStatusCode.Conflict)]
    public async Task EmptyAndNonDefaultWarehouseRequestsDoNotCreateOrders(bool empty, HttpStatusCode expected)
    {
        var draft = await CreateAuditDraftAsync();
        var before = (await client.GetFromJsonAsync<SalesOrderPage>("/api/sales-orders"))!.TotalCount;
        var response = await client.PostAsJsonAsync("/api/sales-orders", new CreateSalesOrderCommand(draft.CustomerId,
            [new CreateSalesOrderItem(draft.Items[0].ProductId, 1, empty ? Guid.Empty : Guid.NewGuid())]));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, (await client.GetFromJsonAsync<SalesOrderPage>("/api/sales-orders"))!.TotalCount);
    }

    [Fact]
    public async Task LegacyEditOmissionPreservesOriginAndExplicitDefaultCanAssignIt()
    {
        var draft = await CreateAuditDraftAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE SalesOrderItems SET WarehouseId=NULL WHERE SalesOrderId={draft.Id}");
        var omitted = await client.PutAsJsonAsync($"/api/sales-orders/{draft.Id}", new UpdateSalesOrderCommand(draft.CustomerId,
            [new CreateSalesOrderItem(draft.Items[0].ProductId, 2)], draft.RowVersion));
        omitted.EnsureSuccessStatusCode();
        var edited = (await omitted.Content.ReadFromJsonAsync<SalesOrderResult>())!;
        Assert.Null(edited.Items[0].WarehouseId);
        var explicitSource = await client.PutAsJsonAsync($"/api/sales-orders/{draft.Id}", new UpdateSalesOrderCommand(draft.CustomerId,
            [new CreateSalesOrderItem(draft.Items[0].ProductId, 2, LegacyDefaultWarehouse.Id)], edited.RowVersion));
        explicitSource.EnsureSuccessStatusCode();
        Assert.Equal(LegacyDefaultWarehouse.Id, (await explicitSource.Content.ReadFromJsonAsync<SalesOrderResult>())!.Items[0].WarehouseId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartsRequestsRetainDefaultSourceWithoutActivatingWarehouseBalances(bool explicitWarehouse)
    {
        var draft = await CreateAuditDraftAsync();
        SetToken(await LoginAsync("tech@erpflow.local"));
        var created = await client.PostAsJsonAsync("/api/service/jobcards", new CreateJobcardCommand(
            draft.CustomerId, null, "Tech", "Source test", JobcardPriority.Medium));
        var jobcard = (await created.Content.ReadFromJsonAsync<JobcardResult>())!;
        object part = explicitWarehouse
            ? new { productId = draft.Items[0].ProductId, quantity = 1, warehouseId = LegacyDefaultWarehouse.Id }
            : new { productId = draft.Items[0].ProductId, quantity = 1 };
        var requested = await client.PostAsJsonAsync($"/api/service/jobcards/{jobcard.Id}/request-parts", new { parts = new[] { part } });
        requested.EnsureSuccessStatusCode();
        Assert.Equal(LegacyDefaultWarehouse.Id, Assert.Single((await requested.Content.ReadFromJsonAsync<JobcardResult>())!.Items).WarehouseId);
        var completed = await client.PostAsJsonAsync($"/api/service/jobcards/{jobcard.Id}/complete", new CompleteJobcardCommand("Done", null));
        completed.EnsureSuccessStatusCode();
        Assert.Equal(LegacyDefaultWarehouse.Id, (await completed.Content.ReadFromJsonAsync<JobcardResult>())!.Items[0].WarehouseId);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
        Assert.Empty(await context.WarehouseStocks.ToListAsync());
        Assert.Empty(await context.InventoryReservations.ToListAsync());
        Assert.Empty(await context.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task MixedInvalidPartOriginsAbortBeforeAnyReservationIsPersisted()
    {
        var draft = await CreateAuditDraftAsync();
        SetToken(await LoginAsync("tech@erpflow.local"));
        var products = (await client.GetFromJsonAsync<ProductReferenceDto[]>("/api/reference/products"))!.Where(x => x.AvailableStock > 0).Take(2).ToArray();
        var response = await client.PostAsJsonAsync("/api/service/jobcards", new CreateJobcardCommand(
            draft.CustomerId, null, "Tech", "Mixed sources", JobcardPriority.Medium));
        var jobcard = (await response.Content.ReadFromJsonAsync<JobcardResult>())!;
        var parts = await client.PostAsJsonAsync($"/api/service/jobcards/{jobcard.Id}/request-parts", new RequestJobcardPartsCommand(
            [new JobcardPartRequestItem(products[0].Id, 1), new JobcardPartRequestItem(products[1].Id, 1, Guid.NewGuid())]));
        Assert.Equal(HttpStatusCode.Conflict, parts.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JobcardResult>($"/api/service/jobcards/{jobcard.Id}"))!.Items);
        var after = (await client.GetFromJsonAsync<ProductReferenceDto[]>("/api/reference/products"))!;
        Assert.All(products, before => Assert.Equal(before.AvailableStock, after.Single(x => x.Id == before.Id).AvailableStock));
    }

    [Fact]
    public async Task RepeatedCorrectionKeepsHistoricalIdsWhileReadsExposeOnlyCurrentCycle()
    {
        var draft = await CreateAuditDraftAsync();
        var sales = await LoginAsync("sales@erpflow.local");
        var approver = await LoginAsync("approver@erpflow.local");
        var ids = new List<Guid>();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            SetToken(sales);
            var submitted = await client.PostAsync($"/api/sales-orders/{draft.Id}/submit", null);
            submitted.EnsureSuccessStatusCode();
            ids.Add(Assert.Single((await submitted.Content.ReadFromJsonAsync<SalesOrderResult>())!.Reservations).Id);
            SetToken(approver);
            (await client.PostAsJsonAsync($"/api/sales-orders/{draft.Id}/return", new { reason = "Correction" })).EnsureSuccessStatusCode();
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
        var rows = await context.StockReservations.Where(x => x.SalesOrderId == draft.Id).ToListAsync();
        Assert.Equal(ids.Order(), rows.Select(x => x.Id).Order());
        Assert.Equal(2, rows.Count(x => x.IsHistorical));
        Assert.All(rows, x => Assert.True(x.IsReleased));
        Assert.Single((await client.GetFromJsonAsync<SalesOrderResult>($"/api/sales-orders/{draft.Id}"))!.Reservations);
        Assert.Equal(6, (await client.GetFromJsonAsync<ApprovalHistoryResult[]>($"/api/sales-orders/{draft.Id}/history"))!.Length);
    }
}
