using ERPFlow.Application.Common;
using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProcurementIntegrationTests
{
    [Fact]
    public async Task PurchaseApprovalPreservesEveryInventoryBalanceAndSalesPrice()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var before = await context.Products.AsNoTracking().SingleAsync();
        var purchasing = ProcurementTestSupport.Orders(context, seed.Purchasing);
        var draft = await purchasing.CreateAsync(seed.Draft());
        draft = await purchasing.UpdateAsync(draft.Id, new(draft.SupplierId, draft.WarehouseId,
            [new(seed.Inventory.ProductId, 3, 2.1234m)], "Edited", draft.RowVersion));
        Assert.Equal(6.3702m, draft.TotalAmount);
        var submitted = await purchasing.SubmitAsync(draft.Id, new(draft.RowVersion));
        var approved = await ProcurementTestSupport.Orders(context, seed.Approver).ApproveAsync(draft.Id, new(submitted.RowVersion, "Approved"));
        Assert.Equal(PurchaseOrderStatus.Approved, approved.Status);
        var history = await purchasing.HistoryAsync(draft.Id);
        Assert.Equal(new[] { PurchaseOrderAction.Submitted, PurchaseOrderAction.Approved }, history.Select(x => x.Action));
        Assert.Equal(new[] { seed.Purchasing, seed.Approver }, history.Select(x => x.ActorId));
        var after = await context.Products.AsNoTracking().SingleAsync();
        Assert.Equal((before.StockOnHand, before.ReservedQuantity, before.UnitPrice), (after.StockOnHand, after.ReservedQuantity, after.UnitPrice));
        Assert.Equal(before.RowVersion, after.RowVersion);
        Assert.All(await context.WarehouseStocks.ToArrayAsync(), x => Assert.Equal((0, 0, 0L), (x.StockOnHand, x.ReservedQuantity, x.Sequence)));
        Assert.Empty(await context.InventoryPostings.ToListAsync());
        Assert.Empty(await context.InventoryReservations.ToListAsync());
        Assert.Empty(await context.StockMovements.ToListAsync());
        Assert.Equal(InventoryAuthorityMode.Product, (await context.InventoryAuthorities.SingleAsync()).Mode);
    }

    [Fact]
    public async Task SupplierLifecycleRejectsDuplicatesInactiveSourcesAndStaleVersions()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var masters = ProcurementTestSupport.Masters(context, seed.Purchasing);
        var supplier = await masters.CreateSupplierAsync(new("new-supplier", "Name", Email: "supply@example.test"));
        var old = supplier.RowVersion;
        supplier = await masters.UpdateSupplierAsync(supplier.Id, new("Renamed", "Contact", null, null, "Address", supplier.RowVersion));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => masters.SetSupplierActiveAsync(supplier.Id, new(old), false));
        await using (var duplicate = database.Context())
            await Assert.ThrowsAsync<WorkflowConflictException>(() => ProcurementTestSupport.Masters(duplicate, seed.Purchasing).CreateSupplierAsync(new("NEW-SUPPLIER", "Duplicate")));
        supplier = await masters.SetSupplierActiveAsync(supplier.Id, new(supplier.RowVersion), false);
        await Assert.ThrowsAsync<WorkflowConflictException>(() => ProcurementTestSupport.Orders(context, seed.Purchasing)
            .CreateAsync(seed.Draft() with { SupplierId = supplier.Id }));
        supplier = await masters.SetSupplierActiveAsync(supplier.Id, new(supplier.RowVersion), true);
        Assert.True(supplier.IsActive);
        var list = await masters.ListSuppliersAsync(1, 1, "Renamed", true);
        Assert.Equal(supplier.Id, Assert.Single(list.Items).Id);
    }

    [Fact]
    public async Task WarehouseLifecycleKeepsCodesImmutableAndRejectsStaleEdits()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var masters = ProcurementTestSupport.Masters(context, seed.Inventory.ActorId);
        var warehouse = await masters.CreateWarehouseAsync(new("NEW", "New warehouse"));
        var old = warehouse.RowVersion;
        warehouse = await masters.UpdateWarehouseAsync(warehouse.Id, new("Renamed", warehouse.RowVersion));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => masters.UpdateWarehouseAsync(warehouse.Id, new("Stale", old)));
        warehouse = await masters.SetWarehouseActiveAsync(warehouse.Id, new(warehouse.RowVersion), false);
        warehouse = await masters.SetWarehouseActiveAsync(warehouse.Id, new(warehouse.RowVersion), true);
        Assert.Equal(("NEW", "Renamed", true), (warehouse.Code, warehouse.Name, warehouse.IsActive));
        var duplicate = await Assert.ThrowsAsync<WorkflowConflictException>(() => masters.CreateWarehouseAsync(new("new", "Duplicate")));
        Assert.NotNull(duplicate);
        await using var sql = database.Context();
        Assert.Equal(51010, (await Assert.ThrowsAsync<SqlException>(() => sql.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Warehouses SET Code=N'CHANGED' WHERE Id={warehouse.Id}"))).Number);
    }

    [Fact]
    public async Task LegacyDefaultIsProtectedThroughWorkflowAndSql()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var masters = ProcurementTestSupport.Masters(context, seed.Inventory.ActorId);
        var warehouse = await masters.GetWarehouseAsync(LegacyDefaultWarehouse.Id);
        await Assert.ThrowsAsync<WorkflowConflictException>(() => masters.SetWarehouseActiveAsync(warehouse.Id, new(warehouse.RowVersion), false));
        await using var sql = database.Context();
        Assert.Equal(51010, (await Assert.ThrowsAsync<SqlException>(() => sql.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Warehouses SET IsActive=0 WHERE Id={warehouse.Id}"))).Number);
        await Assert.ThrowsAsync<SqlException>(() => sql.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Warehouses WHERE Id={warehouse.Id}"));
    }

    [Theory]
    [InlineData("stock")]
    [InlineData("sales")]
    [InlineData("service")]
    [InlineData("purchase")]
    [InlineData("owner")]
    public async Task WarehouseDeactivationRejectsExistingCommitments(string kind)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using (var setup = database.Context())
        {
            if (kind == "stock") await InventoryTestDatabase.Poster(setup, seed.Inventory.ActorId).PostAsync(seed.Inventory.Opening());
            if (kind == "sales") await setup.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE SalesOrderItems SET WarehouseId={seed.Inventory.WarehouseId} WHERE SalesOrderId={seed.Inventory.OtherOrderId}");
            if (kind == "service") await setup.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE JobcardItems SET WarehouseId={seed.Inventory.WarehouseId} WHERE Id={seed.Inventory.JobcardItemId}");
            if (kind == "owner") await setup.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO InventoryReservations(Id,WarehouseStockId,ProductId,Quantity,SalesOrderId,Status,CreatedAt,IsLegacyImport)
                VALUES({Guid.NewGuid()},{seed.Inventory.StockId},{seed.Inventory.ProductId},1,{seed.Inventory.SalesOrderId},0,SYSUTCDATETIME(),0)
                """);
            if (kind == "purchase") await ProcurementTestSupport.Orders(setup, seed.Purchasing).CreateAsync(seed.Draft());
        }
        await using var context = database.Context();
        var masters = ProcurementTestSupport.Masters(context, seed.Inventory.ActorId);
        var warehouse = await masters.GetWarehouseAsync(seed.Inventory.WarehouseId);
        await Assert.ThrowsAsync<WorkflowConflictException>(() => masters.SetWarehouseActiveAsync(warehouse.Id, new(warehouse.RowVersion), false));
        Assert.True((await masters.GetWarehouseAsync(warehouse.Id)).IsActive);
    }

    [Fact]
    public async Task OwnOrderAccessAndPersistedRoleChecksCannotBeBypassed()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var order = await ProcurementTestSupport.Orders(context, seed.Purchasing).CreateAsync(seed.Draft());
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => ProcurementTestSupport.Orders(context, seed.OtherPurchasing).GetAsync(order.Id));
        Assert.Empty((await ProcurementTestSupport.Orders(context, seed.OtherPurchasing).ListAsync(1, 20, null)).Items);
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => ProcurementTestSupport.Orders(context, seed.Sales).CreateAsync(seed.Draft()));
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => ProcurementTestSupport.Masters(context, seed.Purchasing).CreateWarehouseAsync(new("DENIED", "Denied")));
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE AppUsers SET Role=1 WHERE Id={seed.Purchasing}");
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() => ProcurementTestSupport.Orders(context, seed.Purchasing).SubmitAsync(order.Id, new(order.RowVersion)));
    }

    [Fact]
    public async Task SubmittedTermsAndHistoryRejectOrdinaryAndRawMutations()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        Guid id;
        await using (var context = database.Context())
        {
            var workflow = ProcurementTestSupport.Orders(context, seed.Purchasing);
            var order = await workflow.CreateAsync(seed.Draft());
            id = order.Id;
            order = await workflow.SubmitAsync(id, new(order.RowVersion));
            await Assert.ThrowsAsync<WorkflowConflictException>(() => workflow.UpdateAsync(id, new(order.SupplierId, order.WarehouseId, [new(seed.Inventory.ProductId, 1, 1m)], null, order.RowVersion)));
        }
        await using var sql = database.Context();
        Assert.Equal(51011, (await Assert.ThrowsAsync<SqlException>(() => sql.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PurchaseOrderItems SET UnitCost=1 WHERE PurchaseOrderId={id}"))).Number);
        Assert.Equal(51011, (await Assert.ThrowsAsync<SqlException>(() => sql.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PurchaseOrders SET Notes=N'Changed' WHERE Id={id}"))).Number);
        Assert.Equal(51012, (await Assert.ThrowsAsync<SqlException>(() => sql.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PurchaseOrderHistories SET Comments=N'Changed' WHERE PurchaseOrderId={id}"))).Number);
        var history = await sql.PurchaseOrderHistories.SingleAsync(x => x.PurchaseOrderId == id);
        sql.Entry(history).Property(x => x.Comments).CurrentValue = "Ordinary bypass";
        await Assert.ThrowsAsync<InvalidOperationException>(() => sql.SaveChangesAsync());
    }

    [Fact]
    public async Task FailedSqlTransitionRollsBackBothAuditAndParentThenAllowsRetry()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        PurchaseOrderResult order;
        await using (var setup = database.Context())
        {
            order = await ProcurementTestSupport.Orders(setup, seed.Purchasing).CreateAsync(seed.Draft());
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER TR_Phase3_TestFailure ON PurchaseOrders AFTER UPDATE AS
                BEGIN
                    IF EXISTS(SELECT 1 FROM inserted WHERE Status=1) THROW 51100, 'Injected PO transition failure.', 1;
                END;
                """);
        }
        await using (var failing = database.Context())
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => ProcurementTestSupport.Orders(failing, seed.Purchasing)
                .SubmitAsync(order.Id, new(order.RowVersion)));
            Assert.Equal(51100, error.Number);
        }
        await using var verification = database.Context();
        var stored = await verification.PurchaseOrders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        Assert.Equal(PurchaseOrderStatus.Draft, stored.Status);
        Assert.Equal(order.RowVersion, stored.RowVersion);
        Assert.Empty(await verification.PurchaseOrderHistories.ToListAsync());
        await verification.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_Phase3_TestFailure;");
        var retried = await ProcurementTestSupport.Orders(verification, seed.Purchasing).SubmitAsync(order.Id, new(order.RowVersion));
        Assert.Equal(PurchaseOrderStatus.PendingApproval, retried.Status);
        Assert.Single(await verification.PurchaseOrderHistories.ToListAsync());
    }

    [Fact]
    public async Task RejectionAndCancellationPreserveAuditAndFreeWarehouseCommitment()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        var purchasing = ProcurementTestSupport.Orders(context, seed.Purchasing);
        var order = await purchasing.CreateAsync(seed.Draft());
        order = await purchasing.SubmitAsync(order.Id, new(order.RowVersion));
        order = await ProcurementTestSupport.Orders(context, seed.Approver).RejectAsync(order.Id, new(order.RowVersion, "Wrong specification"));
        Assert.Equal(PurchaseOrderStatus.Rejected, order.Status);
        var second = await purchasing.CreateAsync(seed.Draft());
        second = await purchasing.CancelAsync(second.Id, new(second.RowVersion, "Not needed"));
        Assert.Equal(PurchaseOrderStatus.Cancelled, second.Status);
        var masters = ProcurementTestSupport.Masters(context, seed.Inventory.ActorId);
        var warehouse = await masters.GetWarehouseAsync(order.WarehouseId);
        Assert.False((await masters.SetWarehouseActiveAsync(warehouse.Id, new(warehouse.RowVersion), false)).IsActive);
        Assert.Equal(3, await context.PurchaseOrderHistories.CountAsync());
    }
}
