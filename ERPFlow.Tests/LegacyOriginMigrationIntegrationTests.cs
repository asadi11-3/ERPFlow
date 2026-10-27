using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class LegacyImportPreparationIntegrationTests
{
    [Fact]
    public async Task SourceMigrationPreservesNullOriginsActiveObligationsAndAmbiguousHistory()
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
        byte[] version;
        await using (var context = database.Context())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO AppUsers(Id,Name,Email,Role,PasswordHash,CreatedAt)
                VALUES({actor},N'Admin',N'old-origins@example.test',0,N'preserved-hash',SYSUTCDATETIME());
                INSERT INTO Customers(Id,Name,Email,IsActive,CreatedAt)
                VALUES({customer},N'Customer',N'old-origins@example.test',1,SYSUTCDATETIME());
                INSERT INTO Products(Id,Name,Sku,UnitPrice,StockOnHand,ReservedQuantity,CreatedAt)
                VALUES({product},N'Product',N'OLD-ORIGINS',10,20,5,SYSUTCDATETIME());
                INSERT INTO SalesOrders(Id,CustomerId,CreatedByUserId,Status,CreatedAt)
                VALUES({pending},{customer},{actor},1,SYSUTCDATETIME()),
                      ({returned},{customer},{actor},0,SYSUTCDATETIME()),
                      ({shipped},{customer},{actor},2,SYSUTCDATETIME());
                INSERT INTO SalesOrderItems(Id,SalesOrderId,ProductId,Quantity,UnitPrice)
                VALUES({Guid.NewGuid()},{pending},{product},2,10),({Guid.NewGuid()},{shipped},{product},1,10);
                INSERT INTO StockReservations(Id,SalesOrderId,ProductId,Quantity,IsReleased,CreatedAt,ReleasedAt)
                VALUES({activeReservation},{pending},{product},2,0,SYSUTCDATETIME(),NULL),
                      ({returnedReservation},{returned},{product},1,1,SYSUTCDATETIME(),SYSUTCDATETIME()),
                      ({shippedReservation},{shipped},{product},1,1,SYSUTCDATETIME(),SYSUTCDATETIME());
                INSERT INTO ApprovalHistories(Id,SalesOrderId,UserId,Action,Comments,CreatedAt)
                VALUES({history},{returned},{actor},3,N'Original return history',SYSUTCDATETIME());
                INSERT INTO DeliveryNotes(Id,SalesOrderId,CustomerId,DeliveryNumber,Carrier,DispatchedAt)
                VALUES({delivery},{shipped},{customer},N'OLD-ORIGIN-DN',N'Carrier',SYSUTCDATETIME());
                INSERT INTO Jobcards(Id,TicketNumber,CustomerId,TechnicianName,ReportedFault,Priority,Status,CreatedAt,CompletedAt,Revision)
                VALUES({activeJobcard},N'OLD-ACTIVE',{customer},N'Tech',N'Fault',1,1,SYSUTCDATETIME(),NULL,1),
                      ({completedJobcard},N'OLD-DONE',{customer},N'Tech',N'Fault',1,3,SYSUTCDATETIME(),SYSUTCDATETIME(),1);
                INSERT INTO JobcardItems(Id,JobcardId,ProductId,Quantity,UnitPrice)
                VALUES({activePart},{activeJobcard},{product},3,10),({completedPart},{completedJobcard},{product},4,10);
                """);
            version = (await context.Products.SingleAsync()).RowVersion.ToArray();
            await context.Database.MigrateAsync();
            await context.Database.MigrateAsync();
        }
        await using var verification = database.Context();
        var persisted = await verification.Products.SingleAsync();
        Assert.Equal(20, persisted.StockOnHand);
        Assert.Equal(5, persisted.ReservedQuantity);
        Assert.Equal(version, persisted.RowVersion);
        Assert.Equal("preserved-hash", (await verification.AppUsers.SingleAsync()).PasswordHash);
        Assert.All(await verification.SalesOrderItems.ToListAsync(), x => Assert.Null(x.WarehouseId));
        Assert.All(await verification.StockReservations.ToListAsync(), x => Assert.Null(x.WarehouseId));
        Assert.All(await verification.JobcardItems.ToListAsync(), x => Assert.Null(x.WarehouseId));
        Assert.Equal("Original return history", (await verification.ApprovalHistories.FindAsync(history))!.Comments);
        Assert.NotNull(await verification.DeliveryNotes.FindAsync(delivery));
        var prepared = await Service(verification, actor).PrepareAsync(Guid.NewGuid());
        Assert.Equal(3, prepared.SalesReservationCount);
        Assert.Equal(2, prepared.JobcardItemCount);
        var rows = await verification.LegacyInventoryReservationImports.Where(x => x.BatchId == prepared.PreparationId).ToListAsync();
        Assert.Equal(5, rows.Where(x => x.Disposition == LegacyReservationDisposition.Active).Sum(x => x.Quantity));
        Assert.All(rows.Where(x => x.SourceId == returnedReservation || x.SourceId == shippedReservation || x.SourceId == completedPart),
            x => Assert.Equal(LegacyReservationDisposition.LegacyClosedUnknown, x.Disposition));
        Assert.All(rows.Where(x => x.SourceKind == LegacyReservationSource.SalesReservation), x => Assert.Equal(x.SourceId, x.TargetReservationId));
        Assert.Empty(await verification.WarehouseStocks.ToListAsync());
        Assert.Empty(await verification.InventoryReservations.ToListAsync());
        Assert.Empty(await verification.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task OriginMigrationIsAdditiveAndOnlySeedsDefaultMasterMetadata()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        await using var context = database.Context();
        var assembly = context.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.Single(x => x.Value.Name == "WarehouseSourcePreparation");
        var migration = assembly.CreateMigration(entry.Value, context.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DropColumnOperation or
            AlterColumnOperation or UpdateDataOperation or DeleteDataOperation or SqlOperation);
        var insert = Assert.Single(migration.UpOperations.OfType<InsertDataOperation>());
        Assert.Equal("Warehouses", insert.Table);
        Assert.Equal(1, insert.Values.GetLength(0));
        Assert.Contains(migration.UpOperations.OfType<AddColumnOperation>(), x => x.Table == "SalesOrderItems" && x.Name == "WarehouseId" && x.IsNullable);
        Assert.Contains(migration.UpOperations.OfType<AddColumnOperation>(), x => x.Table == "StockReservations" && x.Name == "WarehouseId" && x.IsNullable);
        Assert.Contains(migration.UpOperations.OfType<AddColumnOperation>(), x => x.Table == "JobcardItems" && x.Name == "WarehouseId" && x.IsNullable);
    }
}
