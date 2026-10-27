using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ERPFlow.Tests;

public sealed class InventoryCutoverMigrationTests
{
    [Fact]
    public async Task LegacyUpgradeAndCutoverPreserveActiveOwnersAndUnknownClosedHistory()
    {
        await using var database = await InventoryTestDatabase.CreateAsync("20261025184342_InventoryFoundation");
        var actor = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var product = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var returned = Guid.NewGuid();
        var shipped = Guid.NewGuid();
        var activeReservation = Guid.NewGuid();
        var returnedReservation = Guid.NewGuid();
        var shippedReservation = Guid.NewGuid();
        var activeJobcard = Guid.NewGuid();
        var completedJobcard = Guid.NewGuid();
        var activePart = Guid.NewGuid();
        var completedPart = Guid.NewGuid();
        var history = Guid.NewGuid();
        var delivery = Guid.NewGuid();
        var created = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var closed = created.AddDays(1);
        byte[] version;
        await using (var context = database.Context())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO AppUsers(Id,Name,Email,Role,PasswordHash,CreatedAt)
                VALUES({actor},N'Admin',N'cutover-upgrade@example.test',0,N'preserved-hash',{created});
                INSERT INTO Customers(Id,Name,Email,IsActive,CreatedAt)
                VALUES({customer},N'Customer',N'cutover-upgrade@example.test',1,{created});
                INSERT INTO Products(Id,Name,Sku,UnitPrice,StockOnHand,ReservedQuantity,CreatedAt)
                VALUES({product},N'Product',N'CUTOVER-UPGRADE',10,20,5,{created});
                INSERT INTO SalesOrders(Id,CustomerId,CreatedByUserId,Status,CreatedAt)
                VALUES({pending},{customer},{actor},1,{created}),
                      ({returned},{customer},{actor},0,{created}),
                      ({shipped},{customer},{actor},2,{created});
                INSERT INTO SalesOrderItems(Id,SalesOrderId,ProductId,Quantity,UnitPrice)
                VALUES({Guid.NewGuid()},{pending},{product},2,10),({Guid.NewGuid()},{shipped},{product},1,10);
                INSERT INTO StockReservations(Id,SalesOrderId,ProductId,Quantity,IsReleased,CreatedAt,ReleasedAt)
                VALUES({activeReservation},{pending},{product},2,0,{created},NULL),
                      ({returnedReservation},{returned},{product},1,1,{created},{closed}),
                      ({shippedReservation},{shipped},{product},1,1,{created},{closed});
                INSERT INTO ApprovalHistories(Id,SalesOrderId,UserId,Action,Comments,CreatedAt)
                VALUES({history},{returned},{actor},3,N'Preserve original return approval',{closed});
                INSERT INTO DeliveryNotes(Id,SalesOrderId,CustomerId,DeliveryNumber,Carrier,DispatchedAt)
                VALUES({delivery},{shipped},{customer},N'CUTOVER-OLD-DN',N'Carrier',{closed});
                INSERT INTO Jobcards(Id,TicketNumber,CustomerId,TechnicianName,ReportedFault,Priority,Status,CreatedAt,CompletedAt,Revision)
                VALUES({activeJobcard},N'CUTOVER-OLD-ACTIVE',{customer},N'Tech',N'Fault',1,1,{created},NULL,1),
                      ({completedJobcard},N'CUTOVER-OLD-DONE',{customer},N'Tech',N'Fault',1,3,{created},{closed},1);
                INSERT INTO JobcardItems(Id,JobcardId,ProductId,Quantity,UnitPrice)
                VALUES({activePart},{activeJobcard},{product},3,10),({completedPart},{completedJobcard},{product},4,10);
                """);
            version = (await context.Products.SingleAsync()).RowVersion.ToArray();
            await context.Database.MigrateAsync();
            await context.Database.MigrateAsync();
            Assert.Equal(InventoryAuthorityMode.Product, (await context.InventoryAuthorities.SingleAsync()).Mode);
            Assert.Empty(await context.InventoryPostings.ToListAsync());
            Assert.Equal(version, (await context.Products.AsNoTracking().SingleAsync()).RowVersion);
        }
        var approval = await InventoryCutoverTestSupport.PrepareAsync(database, actor);
        await using var verification = database.Context();
        var cutover = InventoryCutoverTestSupport.Cutover(verification, actor);
        var result = await cutover.RehearseAsync(approval);
        Assert.Equal(result, await cutover.RehearseAsync(approval));
        Assert.Equal(1, result.StockRows);
        Assert.Equal(5, result.ReservationRows);
        var stock = await verification.WarehouseStocks.SingleAsync();
        Assert.Equal((20, 5, 1L), (stock.StockOnHand, stock.ReservedQuantity, stock.Sequence));
        var movement = Assert.Single(await verification.StockMovements.ToListAsync());
        Assert.Equal((20, 5), (movement.PhysicalDelta, movement.ReservedDelta));
        var owners = await verification.InventoryReservations.ToArrayAsync();
        Assert.Equal(5, owners.Where(x => x.Status == InventoryReservationStatus.Active).Sum(x => (long)x.Quantity));
        Assert.Contains(owners, x => x.Id == activeReservation && x.SalesOrderId == pending && x.SourceCreatedAt == created);
        Assert.Contains(owners, x => x.JobcardItemId == activePart && x.Status == InventoryReservationStatus.Active && x.SourceCreatedAt == null);
        foreach (var id in new[] { returnedReservation, shippedReservation })
        {
            var owner = Assert.Single(owners, x => x.Id == id);
            Assert.Equal(InventoryReservationStatus.LegacyClosedUnknown, owner.Status);
            Assert.Equal(created, owner.SourceCreatedAt);
            Assert.Equal(closed, owner.ClosedAt);
            Assert.Null((await verification.StockReservations.FindAsync(id))!.ClosureKind);
        }
        Assert.Equal(InventoryReservationStatus.LegacyClosedUnknown, Assert.Single(owners, x => x.JobcardItemId == completedPart).Status);
        Assert.Equal("Preserve original return approval", (await verification.ApprovalHistories.FindAsync(history))!.Comments);
        Assert.Equal(closed, (await verification.ApprovalHistories.FindAsync(history))!.CreatedAt);
        Assert.Equal(closed, (await verification.DeliveryNotes.FindAsync(delivery))!.DispatchedAt);
        var archived = await verification.Products.SingleAsync();
        Assert.Equal((20, 5), (archived.StockOnHand, archived.ReservedQuantity));
        Assert.Equal(version, archived.RowVersion);
        Assert.All(await verification.StockReservations.ToListAsync(), x => Assert.Equal(LegacyDefaultWarehouse.Id, x.WarehouseId));
        await new EfInventoryReconciliation(verification).ReconcileAsync();
    }

    [Fact]
    public void AuthorityMigrationIsInactiveAdditiveAndCannotDestructivelyDowngrade()
    {
        using var context = new ERPFlowDbContext(new DbContextOptionsBuilder<ERPFlowDbContext>().UseSqlServer(
            "Server=unreachable;Database=NoDatabaseAccess;Integrated Security=True").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var assembly = context.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Single(x => x.Value.Name == "AtomicInventoryCutoverRehearsal");
        var migration = assembly.CreateMigration(entry.Value, context.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DropColumnOperation or
            AlterColumnOperation or UpdateDataOperation or DeleteDataOperation);
        Assert.Equal("InventoryAuthorities", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var seed = Assert.Single(migration.UpOperations.OfType<InsertDataOperation>());
        Assert.Equal("InventoryAuthorities", seed.Table);
        Assert.Equal(0, seed.Values[0, Array.IndexOf(seed.Columns, "Mode")]);
        Assert.Null(seed.Values[0, Array.IndexOf(seed.Columns, "CompletedAt")]);
        Assert.Contains(migration.UpOperations.OfType<AddColumnOperation>(), x => x.Table == "InventoryReservations" && x.Name == "IsLegacyImport");
        Assert.Contains(migration.UpOperations.OfType<AddColumnOperation>(), x => x.Table == "InventoryReservations" && x.Name == "SourceCreatedAt" && x.IsNullable);
        var guards = migration.UpOperations.OfType<SqlOperation>().ToArray();
        Assert.Equal(8, guards.Length);
        Assert.All(guards, x => Assert.StartsWith("CREATE TRIGGER", x.Sql.TrimStart()));
        Assert.Contains(guards, x => x.Sql.Contains("TR_Products_FrozenLegacyBalances", StringComparison.Ordinal));
        Assert.Contains(guards, x => x.Sql.Contains("TR_InventoryAuthorities_ExplicitTransition", StringComparison.Ordinal));
        Assert.Throws<NotSupportedException>(() => { _ = migration.DownOperations; });
    }
}
