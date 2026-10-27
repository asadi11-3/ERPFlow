using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Service;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ServiceWorkflowIntegrationTests
{
    [Fact]
    public async Task RequestPartsReservesStockAndCompletionConsumesAllPartsOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var seed = await SeedAsync(context, stockOnHand: 10);
        var workflow = CreateWorkflow(context, seed.User.Id);
        var jobcard = await workflow.CreateJobcardAsync(CreateCommand(seed.Customer.Id));

        var withParts = await workflow.RequestPartsAsync(
            jobcard.Id,
            new RequestJobcardPartsCommand([new JobcardPartRequestItem(seed.Product.Id, 3)]));

        Assert.Equal(JobcardStatus.PartsRequested, withParts.Status);
        context.ChangeTracker.Clear();
        var reserved = await context.Products.SingleAsync(product => product.Id == seed.Product.Id);
        Assert.Equal(3, reserved.ReservedQuantity);
        Assert.Equal(10, reserved.StockOnHand);

        var completed = await workflow.CompleteJobcardAsync(
            jobcard.Id,
            new CompleteJobcardCommand("Installed requested parts.", null));

        Assert.Equal(JobcardStatus.Completed, completed.Status);
        context.ChangeTracker.Clear();
        var consumed = await context.Products.SingleAsync(product => product.Id == seed.Product.Id);
        Assert.Equal(0, consumed.ReservedQuantity);
        Assert.Equal(7, consumed.StockOnHand);
    }

    [Fact]
    public async Task RepeatedCompletionIsConflictAndDoesNotOverwriteCompletion()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var seed = await SeedAsync(context);
        var workflow = CreateWorkflow(context, seed.User.Id);
        var created = await workflow.CreateJobcardAsync(CreateCommand(seed.Customer.Id));
        var completed = await workflow.CompleteJobcardAsync(
            created.Id,
            new CompleteJobcardCommand("Original", 25));

        await Assert.ThrowsAsync<WorkflowConflictException>(() =>
            workflow.CompleteJobcardAsync(created.Id, new CompleteJobcardCommand("Replacement", 30)));

        context.ChangeTracker.Clear();
        var persisted = await context.Jobcards.SingleAsync(jobcard => jobcard.Id == created.Id);
        Assert.Equal(completed.CompletedAt, persisted.CompletedAt);
        Assert.Equal("Original", persisted.TechnicianNotes);
        Assert.Equal(25, persisted.FinalMeterReading);
    }

    [Fact]
    public async Task InvalidDuplicatePartsRequestRollsBackWithoutReservingValidPart()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var seed = await SeedAsync(context);
        var workflow = CreateWorkflow(context, seed.User.Id);
        var created = await workflow.CreateJobcardAsync(CreateCommand(seed.Customer.Id));
        var command = new RequestJobcardPartsCommand(
        [
            new JobcardPartRequestItem(seed.Product.Id, 2),
            new JobcardPartRequestItem(seed.Product.Id, 1),
            new JobcardPartRequestItem(Guid.NewGuid(), -1)
        ]);

        await Assert.ThrowsAsync<WorkflowValidationException>(() =>
            workflow.RequestPartsAsync(created.Id, command));

        context.ChangeTracker.Clear();
        Assert.Empty(await context.JobcardItems.Where(item => item.JobcardId == created.Id).ToListAsync());
        Assert.Equal(0, (await context.Products.SingleAsync(product => product.Id == seed.Product.Id)).ReservedQuantity);
    }

    [Fact]
    public async Task CreateRejectsAssetOwnedByAnotherCustomerAndEmptyAssetId()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var seed = await SeedAsync(context);
        var otherCustomer = new Customer("Other Customer", "other@example.test");
        var asset = new MachineAsset(
            "ASSET-1",
            "SERIAL-1",
            seed.Product.Id,
            otherCustomer.Id,
            null,
            DateTime.UtcNow.AddYears(1),
            50);
        context.AddRange(otherCustomer, asset);
        await context.SaveChangesAsync();
        var workflow = CreateWorkflow(context, seed.User.Id);

        await Assert.ThrowsAsync<WorkflowConflictException>(() =>
            workflow.CreateJobcardAsync(CreateCommand(seed.Customer.Id) with { MachineAssetId = asset.Id }));
        await Assert.ThrowsAsync<WorkflowValidationException>(() =>
            workflow.CreateJobcardAsync(CreateCommand(seed.Customer.Id) with { MachineAssetId = Guid.Empty }));

        Assert.Empty(await context.Jobcards.ToListAsync());
    }

    [Fact]
    public async Task CompletionRejectsMeterReadingBelowAssetReading()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var seed = await SeedAsync(context);
        var asset = new MachineAsset(
            "ASSET-1",
            "SERIAL-1",
            seed.Product.Id,
            seed.Customer.Id,
            null,
            DateTime.UtcNow.AddYears(1),
            50);
        context.Add(asset);
        await context.SaveChangesAsync();
        var workflow = CreateWorkflow(context, seed.User.Id);
        var created = await workflow.CreateJobcardAsync(
            CreateCommand(seed.Customer.Id) with { MachineAssetId = asset.Id });

        await Assert.ThrowsAsync<WorkflowConflictException>(() =>
            workflow.CompleteJobcardAsync(created.Id, new CompleteJobcardCommand("Invalid", 49)));

        context.ChangeTracker.Clear();
        var persisted = await context.Jobcards.SingleAsync(jobcard => jobcard.Id == created.Id);
        Assert.Equal(JobcardStatus.Open, persisted.Status);
        Assert.Null(persisted.CompletedAt);
        Assert.Equal(50, (await context.MachineAssets.SingleAsync()).MeterReading);
    }

    [Fact]
    public async Task PersistedRoleRestrictsServiceWorkflowToTechniciansAndAdministrators()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var seed = await SeedAsync(context, role: UserRole.Sales);
        var workflow = CreateWorkflow(context, seed.User.Id);

        Assert.Empty(await workflow.ListJobcardsAsync());
        await Assert.ThrowsAsync<WorkflowAuthorizationException>(() =>
            workflow.CreateJobcardAsync(CreateCommand(seed.Customer.Id)));
    }

    [Fact]
    public async Task ConcurrentJobcardsCannotOversellProduct()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var ids = await SeedJobcardsAsync(database, productCount: 1, jobcardCount: 2, stockOnHand: 5);

        await using (var firstContext = database.CreateContext())
        await using (var secondContext = database.CreateContext())
        {
            var firstPersistence = new EfServicePersistence(firstContext);
            var secondPersistence = new EfServicePersistence(secondContext);
            await using var firstTransaction = await firstPersistence.BeginTransactionAsync();
            await using var secondTransaction = await secondPersistence.BeginTransactionAsync();
            var firstJobcard = await firstPersistence.GetJobcardAsync(ids.JobcardIds[0], true) ?? throw new InvalidOperationException();
            var secondJobcard = await secondPersistence.GetJobcardAsync(ids.JobcardIds[1], true) ?? throw new InvalidOperationException();
            var firstProduct = Assert.Single(await firstPersistence.GetProductsAsync([ids.ProductIds[0]]));
            var secondProduct = Assert.Single(await secondPersistence.GetProductsAsync([ids.ProductIds[0]]));
            firstProduct.ReserveQuantity(4);
            firstJobcard.AddPart(firstProduct, 4);
            secondProduct.ReserveQuantity(4);
            secondJobcard.AddPart(secondProduct, 4);

            await firstPersistence.SaveChangesAsync();
            await firstTransaction.CommitAsync();
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondPersistence.SaveChangesAsync());
        }

        await using var verificationContext = database.CreateContext();
        Assert.Equal(4, (await verificationContext.Products.SingleAsync()).ReservedQuantity);
        Assert.Single(await verificationContext.JobcardItems.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentPartAddAfterCompletionCannotLeakReservation()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var ids = await SeedJobcardsAsync(database, productCount: 2, jobcardCount: 1, stockOnHand: 5);
        await using (var setupContext = database.CreateContext())
        {
            var setupPersistence = new EfServicePersistence(setupContext);
            var jobcard = await setupPersistence.GetJobcardAsync(ids.JobcardIds[0], true) ?? throw new InvalidOperationException();
            var product = Assert.Single(await setupPersistence.GetProductsAsync([ids.ProductIds[0]]));
            product.ReserveQuantity(2);
            jobcard.AddPart(product, 2);
            await setupPersistence.SaveChangesAsync();
        }

        await using (var addContext = database.CreateContext())
        await using (var completionContext = database.CreateContext())
        {
            var addPersistence = new EfServicePersistence(addContext);
            var completionPersistence = new EfServicePersistence(completionContext);
            await using var addTransaction = await addPersistence.BeginTransactionAsync();
            await using var completionTransaction = await completionPersistence.BeginTransactionAsync();
            var addingJobcard = await addPersistence.GetJobcardAsync(ids.JobcardIds[0], true) ?? throw new InvalidOperationException();
            var completingJobcard = await completionPersistence.GetJobcardAsync(ids.JobcardIds[0], true) ?? throw new InvalidOperationException();
            var addedProduct = Assert.Single(await addPersistence.GetProductsAsync([ids.ProductIds[1]]));
            var consumedProduct = Assert.Single(await completionPersistence.GetProductsAsync([ids.ProductIds[0]]));
            addedProduct.ReserveQuantity(1);
            addingJobcard.AddPart(addedProduct, 1);
            consumedProduct.ConsumeReservedQuantity(2);
            completingJobcard.Complete("Completed", null);

            await completionPersistence.SaveChangesAsync();
            await completionTransaction.CommitAsync();
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => addPersistence.SaveChangesAsync());
        }

        await using var verificationContext = database.CreateContext();
        var persistedConsumedProduct = await verificationContext.Products.SingleAsync(product => product.Id == ids.ProductIds[0]);
        var rejectedProduct = await verificationContext.Products.SingleAsync(product => product.Id == ids.ProductIds[1]);
        Assert.Equal(0, persistedConsumedProduct.ReservedQuantity);
        Assert.Equal(3, persistedConsumedProduct.StockOnHand);
        Assert.Equal(0, rejectedProduct.ReservedQuantity);
        Assert.Equal(5, rejectedProduct.StockOnHand);
        Assert.Single(await verificationContext.JobcardItems.ToListAsync());
        Assert.Equal(JobcardStatus.Completed, (await verificationContext.Jobcards.SingleAsync()).Status);
    }

    [Fact]
    public async Task ConcurrentCompletionsConsumePartsOnlyOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var ids = await SeedJobcardsAsync(database, productCount: 1, jobcardCount: 1, stockOnHand: 5);
        await using (var setup = database.CreateContext())
        {
            var jobcard = await setup.Jobcards.Include(job => job.Items).SingleAsync();
            var product = await setup.Products.SingleAsync();
            product.ReserveQuantity(2);
            jobcard.AddPart(product, 2);
            await setup.SaveChangesAsync();
        }

        await using (var first = database.CreateContext())
        await using (var second = database.CreateContext())
        {
            var firstPersistence = new EfServicePersistence(first);
            var secondPersistence = new EfServicePersistence(second);
            await using var firstTransaction = await firstPersistence.BeginTransactionAsync();
            await using var secondTransaction = await secondPersistence.BeginTransactionAsync();
            var firstJob = (await firstPersistence.GetJobcardAsync(ids.JobcardIds[0], true))!;
            var secondJob = (await secondPersistence.GetJobcardAsync(ids.JobcardIds[0], true))!;
            var firstProduct = Assert.Single(await firstPersistence.GetProductsAsync(ids.ProductIds));
            var secondProduct = Assert.Single(await secondPersistence.GetProductsAsync(ids.ProductIds));
            firstJob.Complete("First completion", null);
            firstProduct.ConsumeReservedQuantity(2);
            secondJob.Complete("Stale completion", null);
            secondProduct.ConsumeReservedQuantity(2);
            await firstPersistence.SaveChangesAsync();
            await firstTransaction.CommitAsync();
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondPersistence.SaveChangesAsync());
        }

        await using var verification = database.CreateContext();
        var persistedProduct = await verification.Products.SingleAsync();
        Assert.Equal(3, persistedProduct.StockOnHand);
        Assert.Equal(0, persistedProduct.ReservedQuantity);
        Assert.Equal("First completion", (await verification.Jobcards.SingleAsync()).TechnicianNotes);
    }

    [Fact]
    public async Task ConcurrentCompletionsForOneAssetCannotOverwriteNewerMeterReading()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        Guid assetId;
        Guid firstJobId;
        Guid secondJobId;
        await using (var setup = database.CreateContext())
        {
            var seed = await SeedAsync(setup);
            var asset = new MachineAsset("SHARED-ASSET", "SHARED-SERIAL", seed.Product.Id,
                seed.Customer.Id, null, DateTime.UtcNow.AddYears(1), 100);
            var firstJob = new Jobcard("FIRST-JOB", seed.Customer.Id, asset.Id, "Tech", "First fault");
            var secondJob = new Jobcard("SECOND-JOB", seed.Customer.Id, asset.Id, "Tech", "Second fault");
            setup.AddRange(asset, firstJob, secondJob);
            await setup.SaveChangesAsync();
            assetId = asset.Id;
            firstJobId = firstJob.Id;
            secondJobId = secondJob.Id;
        }

        await using (var first = database.CreateContext())
        await using (var second = database.CreateContext())
        {
            var firstPersistence = new EfServicePersistence(first);
            var secondPersistence = new EfServicePersistence(second);
            await using var firstTransaction = await firstPersistence.BeginTransactionAsync();
            await using var secondTransaction = await secondPersistence.BeginTransactionAsync();
            var firstJob = (await firstPersistence.GetJobcardAsync(firstJobId, true))!;
            var secondJob = (await secondPersistence.GetJobcardAsync(secondJobId, true))!;
            var firstAsset = (await firstPersistence.FindMachineAssetAsync(assetId))!;
            var secondAsset = (await secondPersistence.FindMachineAssetAsync(assetId))!;
            firstJob.Complete("Newer reading", 200);
            firstAsset.UpdateMeterReading(200);
            secondJob.Complete("Stale reading", 150);
            secondAsset.UpdateMeterReading(150);
            await firstPersistence.SaveChangesAsync();
            await firstTransaction.CommitAsync();
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondPersistence.SaveChangesAsync());
        }

        await using var verification = database.CreateContext();
        Assert.Equal(200, (await verification.MachineAssets.SingleAsync()).MeterReading);
        Assert.Equal(JobcardStatus.Completed, (await verification.Jobcards.SingleAsync(job => job.Id == firstJobId)).Status);
        var rejectedJob = await verification.Jobcards.SingleAsync(job => job.Id == secondJobId);
        Assert.Equal(JobcardStatus.Open, rejectedJob.Status);
        Assert.Null(rejectedJob.CompletedAt);
    }

    private static ServiceWorkflow CreateWorkflow(ERPFlowDbContext context, Guid userId) =>
        new(new EfServicePersistence(context), new TestCurrentUser(userId), new ProductAuthorityWarehouseSourcePolicy(context),
            new EfWorkflowInventory(context, new TestCurrentUser(userId), new ProductAuthorityWriteGate()));

    private static CreateJobcardCommand CreateCommand(Guid customerId) =>
        new(customerId, null, "Technician", "Reported fault", JobcardPriority.Medium);

    private static async Task<Seed> SeedAsync(
        ERPFlowDbContext context,
        int stockOnHand = 10,
        UserRole role = UserRole.Technician)
    {
        var customer = new Customer("Customer", $"customer-{Guid.NewGuid():N}@example.test");
        var product = new Product("Product", $"SKU-{Guid.NewGuid():N}", 10m, stockOnHand);
        var user = new AppUser("User", $"user-{Guid.NewGuid():N}@example.test", role, "test-hash");
        context.AddRange(customer, product, user);
        await context.SaveChangesAsync();
        return new Seed(customer, product, user);
    }

    private static async Task<JobcardSeed> SeedJobcardsAsync(
        SqlServerTestDatabase database,
        int productCount,
        int jobcardCount,
        int stockOnHand)
    {
        await using var context = database.CreateContext();
        var seed = await SeedAsync(context, stockOnHand);
        var products = Enumerable.Range(0, productCount)
            .Select(index => index == 0
                ? seed.Product
                : new Product($"Product {index}", $"SKU-{index}-{Guid.NewGuid():N}", 10m, stockOnHand))
            .ToArray();
        if (products.Length > 1)
        {
            context.Products.AddRange(products.Skip(1));
        }

        var jobcards = Enumerable.Range(0, jobcardCount)
            .Select(index => new Jobcard(
                $"SRV-{Guid.NewGuid():N}",
                seed.Customer.Id,
                null,
                "Technician",
                $"Fault {index}"))
            .ToArray();
        context.Jobcards.AddRange(jobcards);
        await context.SaveChangesAsync();
        return new JobcardSeed(
            products.Select(product => product.Id).ToArray(),
            jobcards.Select(jobcard => jobcard.Id).ToArray());
    }

    private sealed record Seed(Customer Customer, Product Product, AppUser User);

    private sealed record JobcardSeed(IReadOnlyList<Guid> ProductIds, IReadOnlyList<Guid> JobcardIds);

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId { get; } = userId;
    }

    private sealed class SqlServerTestDatabase(string connectionString) : IAsyncDisposable
    {
        public static async Task<SqlServerTestDatabase> CreateAsync()
        {
            var server = Environment.GetEnvironmentVariable("ERPFLOW_TEST_SQLSERVER") ?? @".\SQLEXPRESS";
            var databaseName = $"ERPFlow_ServiceTests_{Guid.NewGuid():N}";
            var connectionString = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = databaseName,
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true
            }.ConnectionString;
            var database = new SqlServerTestDatabase(connectionString);
            await using var context = database.CreateContext();
            await context.GetService<IMigrator>().MigrateAsync();
            return database;
        }

        public ERPFlowDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ERPFlowDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            return new ERPFlowDbContext(options);
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using var context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
    }
}
