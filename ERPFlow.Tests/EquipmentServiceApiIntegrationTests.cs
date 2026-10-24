using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ERPFlow.Api;
using ERPFlow.Application.Equipment;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Application.Service;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ERPFlow.Tests;

public sealed class EquipmentServiceApiIntegrationTests : IAsyncLifetime
{
    private const string Password = "Integration-Test-Password-42!";
    private readonly string databaseName = $"ERPFlow_EquipmentServiceApiTests_{Guid.NewGuid():N}";
    private ApiFactory factory = null!;
    private HttpClient client = null!;

    public Task InitializeAsync()
    {
        factory = new ApiFactory(CreateConnectionString(databaseName));
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost:7165")
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var context = CreateContext(databaseName);
        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task MutationsRejectAnonymousAndOtherRolesWithoutPersistingWrites()
    {
        var mutations = new (string Path, object Body)[]
        {
            ($"/api/equipment/sales-orders/{Guid.NewGuid()}/dispatch", new DispatchOrderCommand("Carrier", null, null)),
            ("/api/service/jobcards", new CreateJobcardCommand(Guid.NewGuid(), null, "Technician", "Fault", JobcardPriority.Medium)),
            ($"/api/service/jobcards/{Guid.NewGuid()}/request-parts", new RequestJobcardPartsCommand([new JobcardPartRequestItem(Guid.NewGuid(), 1)])),
            ($"/api/service/jobcards/{Guid.NewGuid()}/complete", new CompleteJobcardCommand("Completed", 1))
        };
        var before = await GetPersistenceSnapshotAsync();

        foreach (var mutation in mutations)
        {
            SetToken(null);
            await AssertProblemAsync(await client.PostAsJsonAsync(mutation.Path, mutation.Body), HttpStatusCode.Unauthorized);

            foreach (var email in new[] { "sales@erpflow.local", "approver@erpflow.local" })
            {
                SetToken(await LoginAsync(email));
                await AssertProblemAsync(await client.PostAsJsonAsync(mutation.Path, mutation.Body), HttpStatusCode.Forbidden);
            }
        }

        Assert.Equal(before, await GetPersistenceSnapshotAsync());
    }

    [Theory]
    [InlineData("sales@erpflow.local")]
    [InlineData("approver@erpflow.local")]
    public async Task EquipmentAndServiceReadsRemainAvailableToAnyAuthenticatedRole(string email)
    {
        SetToken(await LoginAsync(email));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/equipment/delivery-notes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/equipment/machine-assets")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/service/jobcards")).StatusCode);
    }

    [Fact]
    public async Task AdministratorPassesTechnicianPolicyAndMissingAssetReturnsNotFound()
    {
        SetToken(await LoginAsync("admin@erpflow.local"));
        var customerId = await GetCustomerIdAsync();

        var response = await client.PostAsJsonAsync(
            "/api/service/jobcards",
            new CreateJobcardCommand(
                customerId,
                Guid.NewGuid(),
                "ERP Administrator",
                "Asset lookup authorization check",
                JobcardPriority.Low));

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TechnicianCompletesApprovedOrderDispatchAndServiceWithPersistedQuantities()
    {
        var customerId = await GetCustomerIdAsync();
        var orderProduct = await GetProductAsync("SCAN-100");
        var partProduct = await GetProductAsync("LABEL-500");

        SetToken(await LoginAsync("sales@erpflow.local"));
        var createOrderResponse = await client.PostAsJsonAsync(
            "/api/sales-orders",
            new CreateSalesOrderCommand(customerId, [new CreateSalesOrderItem(orderProduct.Id, 2)]));
        Assert.Equal(HttpStatusCode.Created, createOrderResponse.StatusCode);
        var order = Assert.IsType<SalesOrderResult>(await createOrderResponse.Content.ReadFromJsonAsync<SalesOrderResult>());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/sales-orders/{order.Id}/submit", null)).StatusCode);

        SetToken(await LoginAsync("approver@erpflow.local"));
        var approvalResponse = await client.PostAsJsonAsync(
            $"/api/sales-orders/{order.Id}/approve",
            new ApproveOrderRequest("Approved for equipment integration test."));
        Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);

        SetToken(await LoginAsync("tech@erpflow.local"));
        var dispatchResponse = await client.PostAsJsonAsync(
            $"/api/equipment/sales-orders/{order.Id}/dispatch",
            new DispatchOrderCommand("Integration Carrier", "TRACK-001", null));
        Assert.Equal(HttpStatusCode.OK, dispatchResponse.StatusCode);
        var delivery = Assert.IsType<DeliveryNoteResult>(await dispatchResponse.Content.ReadFromJsonAsync<DeliveryNoteResult>());
        Assert.Equal(order.Id, delivery.SalesOrderId);

        var asset = await GetDispatchedAssetAsync(order.Id);
        var createJobcardResponse = await client.PostAsJsonAsync(
            "/api/service/jobcards",
            new CreateJobcardCommand(
                customerId,
                asset.Id,
                "Tariq Al-Mansoor",
                "Replace worn consumable",
                JobcardPriority.High));
        Assert.Equal(HttpStatusCode.OK, createJobcardResponse.StatusCode);
        var jobcard = Assert.IsType<JobcardResult>(await createJobcardResponse.Content.ReadFromJsonAsync<JobcardResult>());

        var requestPartsResponse = await client.PostAsJsonAsync(
            $"/api/service/jobcards/{jobcard.Id}/request-parts",
            new RequestJobcardPartsCommand([new JobcardPartRequestItem(partProduct.Id, 3)]));
        Assert.Equal(HttpStatusCode.OK, requestPartsResponse.StatusCode);
        var withParts = Assert.IsType<JobcardResult>(await requestPartsResponse.Content.ReadFromJsonAsync<JobcardResult>());
        Assert.Equal(JobcardStatus.PartsRequested, withParts.Status);
        Assert.Equal(3, Assert.Single(withParts.Items).Quantity);

        var completionResponse = await client.PostAsJsonAsync(
            $"/api/service/jobcards/{jobcard.Id}/complete",
            new CompleteJobcardCommand("Consumable replaced and tested.", asset.MeterReading + 25));
        Assert.Equal(HttpStatusCode.OK, completionResponse.StatusCode);
        var completed = Assert.IsType<JobcardResult>(await completionResponse.Content.ReadFromJsonAsync<JobcardResult>());
        Assert.Equal(JobcardStatus.Completed, completed.Status);

        await AssertProblemAsync(
            await client.PostAsJsonAsync(
                $"/api/service/jobcards/{jobcard.Id}/complete",
                new CompleteJobcardCommand("Repeated completion", asset.MeterReading + 25)),
            HttpStatusCode.Conflict);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
        var persistedOrderProduct = await context.Products.SingleAsync(product => product.Id == orderProduct.Id);
        var persistedPartProduct = await context.Products.SingleAsync(product => product.Id == partProduct.Id);
        var persistedJobcard = await context.Jobcards.SingleAsync(candidate => candidate.Id == jobcard.Id);
        var persistedAsset = await context.MachineAssets.SingleAsync(candidate => candidate.Id == asset.Id);
        Assert.Equal(orderProduct.StockOnHand - 2, persistedOrderProduct.StockOnHand);
        Assert.Equal(orderProduct.ReservedQuantity, persistedOrderProduct.ReservedQuantity);
        Assert.Equal(partProduct.StockOnHand - 3, persistedPartProduct.StockOnHand);
        Assert.Equal(partProduct.ReservedQuantity, persistedPartProduct.ReservedQuantity);
        Assert.Equal(JobcardStatus.Completed, persistedJobcard.Status);
        Assert.Equal(asset.MeterReading + 25, persistedAsset.MeterReading);
    }

    private async Task<string> LoginAsync(string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccessTokenResult>())!.AccessToken;
    }

    private async Task<Guid> GetCustomerIdAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
        return await context.Customers
            .Where(customer => customer.Email == "orders@contoso.local")
            .Select(customer => customer.Id)
            .SingleAsync();
    }

    private async Task<ProductSnapshot> GetProductAsync(string sku)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
        return await context.Products
            .Where(product => product.Sku == sku)
            .Select(product => new ProductSnapshot(product.Id, product.StockOnHand, product.ReservedQuantity))
            .SingleAsync();
    }

    private async Task<AssetSnapshot> GetDispatchedAssetAsync(Guid orderId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
        return await context.MachineAssets
            .Where(asset => asset.SalesOrderId == orderId)
            .Select(asset => new AssetSnapshot(asset.Id, asset.MeterReading))
            .FirstAsync();
    }

    private async Task<PersistenceSnapshot> GetPersistenceSnapshotAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
        return new PersistenceSnapshot(
            await context.DeliveryNotes.CountAsync(),
            await context.MachineAssets.CountAsync(),
            await context.Jobcards.CountAsync(),
            await context.JobcardItems.CountAsync(),
            await context.Products.SumAsync(product => product.StockOnHand),
            await context.Products.SumAsync(product => product.ReservedQuantity));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal((int)expectedStatus, problem?.Status);
    }

    private void SetToken(string? token) =>
        client.DefaultRequestHeaders.Authorization = token is null
            ? null
            : new AuthenticationHeaderValue("Bearer", token);

    private static string CreateConnectionString(string name) =>
        new SqlConnectionStringBuilder
        {
            DataSource = Environment.GetEnvironmentVariable("ERPFLOW_TEST_SQLSERVER") ?? @".\SQLEXPRESS",
            InitialCatalog = name,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            MultipleActiveResultSets = true
        }.ConnectionString;

    private static ERPFlowDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<ERPFlowDbContext>()
            .UseSqlServer(CreateConnectionString(name))
            .Options;
        return new ERPFlowDbContext(options);
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:ERPFlowDb"] = connectionString,
                    ["Jwt:Issuer"] = "ERPFlow.Tests",
                    ["Jwt:Audience"] = "ERPFlow.Api.Tests",
                    ["Jwt:SigningKey"] = "integration-test-only-signing-key-at-least-32-characters",
                    ["Jwt:ExpirationMinutes"] = "10",
                    ["DemoUsers:Password"] = Password
                }));
        }
    }

    private sealed record PersistenceSnapshot(
        int DeliveryNotes,
        int MachineAssets,
        int Jobcards,
        int JobcardItems,
        int StockOnHand,
        int ReservedQuantity);

    private sealed record ProductSnapshot(Guid Id, int StockOnHand, int ReservedQuantity);

    private sealed record AssetSnapshot(Guid Id, int MeterReading);
}
