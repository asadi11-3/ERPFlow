using ERPFlow.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductImageMigrationTests
{
    [Fact]
    public void ProductImageMigrationIsAdditiveAndModelHasNoPendingChanges()
    {
        using var context = DesignContext();
        Assert.False(context.Database.HasPendingModelChanges());
        var (_, migration) = ImageMigration(context);

        Assert.DoesNotContain(migration.UpOperations, operation =>
            operation is DropTableOperation or DropColumnOperation or AlterColumnOperation or
                DeleteDataOperation or UpdateDataOperation);
        Assert.Contains(migration.UpOperations.OfType<CreateTableOperation>(), table => table.Name == "ProductImages");
        Assert.All(migration.UpOperations, operation => Assert.True(operation is
            CreateTableOperation or CreateIndexOperation or AddColumnOperation or AddForeignKeyOperation));
    }

    [Fact]
    public async Task UpgradePreservesExistingProductInventoryAndVersionAndStartsWithoutImages()
    {
        string previousMigration;
        await using (var design = DesignContext())
        {
            var (imageMigrationId, _) = ImageMigration(design);
            var migrations = design.GetService<IMigrationsAssembly>().Migrations.Keys.Order().ToArray();
            previousMigration = migrations[Array.IndexOf(migrations, imageMigrationId) - 1];
        }

        await using var database = await InventoryTestDatabase.CreateAsync(previousMigration);
        var productId = Guid.NewGuid();
        byte[] originalVersion;
        await using (var connection = new SqlConnection(database.Context().Database.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO Products(Id,Name,Sku,UnitPrice,StockOnHand,ReservedQuantity,CreatedAt)
                VALUES(@id,N'Migration image product',N'IMG-MIGRATION',12.34,37,5,SYSUTCDATETIME());
                SELECT RowVersion FROM Products WHERE Id=@id;
                """;
            insert.Parameters.AddWithValue("@id", productId);
            originalVersion = (byte[])(await insert.ExecuteScalarAsync())!;
        }

        await using var context = database.Context();
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();
        var product = await context.Products.AsNoTracking().SingleAsync(x => x.Id == productId);
        Assert.Equal((37, 5), (product.StockOnHand, product.ReservedQuantity));
        Assert.Equal(originalVersion, product.RowVersion);
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ProductImages").SingleAsync());
    }

    private static ERPFlowDbContext DesignContext() => new(
        new DbContextOptionsBuilder<ERPFlowDbContext>()
            .UseSqlServer("Server=unreachable;Database=DesignOnly;Integrated Security=True")
            .Options);

    private static (string Id, Migration Migration) ImageMigration(ERPFlowDbContext context)
    {
        var assembly = context.GetService<IMigrationsAssembly>();
        foreach (var entry in assembly.Migrations.Reverse())
        {
            var migration = assembly.CreateMigration(entry.Value, context.Database.ProviderName!);
            if (migration.UpOperations.OfType<CreateTableOperation>().Any(x => x.Name == "ProductImages"))
                return (entry.Key, migration);
        }

        throw new Xunit.Sdk.XunitException("No migration creates the ProductImages table.");
    }
}
