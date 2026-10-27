using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ERPFlow.Api;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Procurement;
using ERPFlow.Application.Receiving;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ERPFlow.Tests;

public sealed class GoodsReceiptApiTests : IAsyncLifetime
{
    private InventoryTestDatabase database = null!;
    private ReceivingSeed seed = null!;
    private ApiFactory factory = null!;
    private HttpClient client = null!;
    public async Task InitializeAsync()
    {
        database = await InventoryTestDatabase.CreateAsync();
        seed = await GoodsReceiptTestSupport.SeedAsync(database);
        await using var context = database.Context();
        factory = new(context.Database.GetConnectionString()!, true);
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    }
    public async Task DisposeAsync()
    {
        client.Dispose(); await factory.DisposeAsync(); await database.DisposeAsync();
    }
    [Fact]
    public async Task SessionForMissingPersistedUserReturnsUnauthorized()
    {
        var previousUser = new ERPFlow.Domain.Entities.AppUser("Previous session", "stale@example.test",
            UserRole.Administrator, "unused-hash");
        var token = factory.Services.GetRequiredService<JwtTokenService>().Create(previousUser).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/warehouses")).StatusCode);

        await Authenticate(seed.Procurement.Operator);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/warehouses")).StatusCode);
    }
    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.Created)]
    [InlineData(UserRole.WarehouseOperator, HttpStatusCode.Created)]
    [InlineData(UserRole.Purchasing, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Approver, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Sales, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Technician, HttpStatusCode.Forbidden)]
    public async Task ReceiptCreationEnforcesWarehouseRoles(UserRole role, HttpStatusCode expected)
    {
        await Authenticate(Actor(role));
        var response = await client.PostAsJsonAsync("/api/goods-receipts", seed.Draft(2));
        Assert.Equal(expected, response.StatusCode);
    }
    [Fact]
    public async Task ActualEndpointsReceiveTwoPartialsReplayAndReturnCostFreeOperationalProgress()
    {
        await Authenticate(seed.Procurement.Operator);
        var create = seed.Draft(3);
        var response = await client.PostAsJsonAsync("/api/goods-receipts", create);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<GoodsReceiptResult>())!;
        Assert.Equal(draft.Id, (await (await client.PostAsJsonAsync("/api/goods-receipts", create)).Content.ReadFromJsonAsync<GoodsReceiptResult>())!.Id);
        var command = new PostGoodsReceiptCommand("api-post-1", draft.RowVersion);
        var posted = await Read<GoodsReceiptResult>(await client.PostAsJsonAsync($"/api/goods-receipts/{draft.Id}/post", command));
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, posted.PurchaseOrderStatus);
        Assert.Equal(JsonSerializer.Serialize(posted), JsonSerializer.Serialize(await Read<GoodsReceiptResult>(await client.PostAsJsonAsync($"/api/goods-receipts/{draft.Id}/post", command))));
        var second = (await (await client.PostAsJsonAsync("/api/goods-receipts", seed.Draft(7, "api-create-2") with { PurchaseOrderRowVersion = posted.PurchaseOrderRowVersion }))
            .Content.ReadFromJsonAsync<GoodsReceiptResult>())!;
        var full = await Read<GoodsReceiptResult>(await client.PostAsJsonAsync($"/api/goods-receipts/{second.Id}/post", new PostGoodsReceiptCommand("api-post-2", second.RowVersion)));
        Assert.Equal(PurchaseOrderStatus.Received, full.PurchaseOrderStatus);
        var operation = await client.GetFromJsonAsync<JsonElement>($"/api/purchase-orders/operational/{seed.Order.Id}");
        Assert.Equal(10, operation.GetProperty("items")[0].GetProperty("receivedQuantity").GetInt32());
        Assert.Equal(0, operation.GetProperty("items")[0].GetProperty("remainingQuantity").GetInt32());
        Assert.False(operation.GetProperty("items")[0].TryGetProperty("unitCost", out _));
        var page = await client.GetFromJsonAsync<PageResult<GoodsReceiptResult>>($"/api/goods-receipts?purchaseOrderId={seed.Order.Id}&status=1&pageSize=1");
        Assert.Equal(2, page!.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(GoodsReceiptStatus.Posted, (await client.GetFromJsonAsync<GoodsReceiptResult>($"/api/goods-receipts/{draft.Id}"))!.Status);
    }
    [Fact]
    public async Task DraftCancellationAndInvalidStateHaveMeaningfulResponses()
    {
        await Authenticate(seed.Procurement.Operator);
        var draft = (await (await client.PostAsJsonAsync("/api/goods-receipts", seed.Draft(1))).Content.ReadFromJsonAsync<GoodsReceiptResult>())!;
        var cancelled = await Read<GoodsReceiptResult>(await client.PostAsJsonAsync($"/api/goods-receipts/{draft.Id}/cancel", new CancelGoodsReceiptCommand(draft.RowVersion, "Wrong delivery")));
        Assert.Equal(GoodsReceiptStatus.Cancelled, cancelled.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/goods-receipts/{draft.Id}/post", new PostGoodsReceiptCommand("cancelled-post", cancelled.RowVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/goods-receipts/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/goods-receipts?pageSize=101")).StatusCode);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task MalformedReceiptCommandsReturn400(int scenario)
    {
        await Authenticate(seed.Procurement.Operator);
        var command = scenario switch
        {
            0 => seed.Draft(0),
            1 => seed.Draft(1) with { IdempotencyKey = " " },
            2 => seed.Draft(1) with { PurchaseOrderRowVersion = [] },
            _ => seed.Draft(1) with { Items = [new(seed.Order.Items.Single().Id, 1), new(seed.Order.Items.Single().Id, 2)] }
        };
        var response = await client.PostAsJsonAsync("/api/goods-receipts", command);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
    [Fact]
    public async Task DefaultProductionCompositionRejectsPostEvenForAnAuthorizedReceipt()
    {
        GoodsReceiptResult draft;
        await using (var setup = database.Context()) draft = await GoodsReceiptTestSupport.Workflow(setup, seed.Procurement.Operator).CreateAsync(seed.Draft(1));
        await using var context = database.Context();
        await using var closed = new ApiFactory(context.Database.GetConnectionString()!, false);
        using var denied = closed.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        denied.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(seed.Procurement.Operator));
        Assert.Equal(HttpStatusCode.Conflict, (await denied.PostAsJsonAsync($"/api/goods-receipts/{draft.Id}/post", new PostGoodsReceiptCommand("production-denied", draft.RowVersion))).StatusCode);
        Assert.Equal(GoodsReceiptStatus.Draft, (await context.GoodsReceipts.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0, (await context.PurchaseOrderItems.AsNoTracking().SingleAsync()).ReceivedQuantity);
    }
    [Fact]
    public async Task AnonymousAndPersistedRoleRevokedUsersCannotReceive()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/goods-receipts")).StatusCode);
        await Authenticate(seed.Procurement.Operator);
        await using var context = database.Context();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE AppUsers SET Role=1 WHERE Id={seed.Procurement.Operator}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/goods-receipts", seed.Draft(1))).StatusCode);
    }
    private async Task Authenticate(Guid id) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(id));
    private async Task<string> Token(Guid id)
    {
        await using var context = database.Context();
        return factory.Services.GetRequiredService<JwtTokenService>().Create(await context.AppUsers.AsNoTracking().SingleAsync(x => x.Id == id)).AccessToken;
    }
    private Guid Actor(UserRole role) => role switch
    {
        UserRole.Administrator => seed.Procurement.Inventory.ActorId, UserRole.WarehouseOperator => seed.Procurement.Operator,
        UserRole.Purchasing => seed.Procurement.Purchasing, UserRole.Approver => seed.Procurement.Approver,
        UserRole.Sales => seed.Procurement.Sales, UserRole.Technician => seed.Procurement.Technician,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private sealed class ApiFactory(string connection, bool authorizeFixtureWrites) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(authorizeFixtureWrites ? "Development" : "Production");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ERPFlowDb"] = connection, ["DemoUsers:Password"] = "",
                ["Logging:LogLevel:Default"] = "Warning", ["Logging:LogLevel:Microsoft"] = "Warning",
                ["Jwt:Issuer"] = "ERPFlow.GRN.Tests", ["Jwt:Audience"] = "ERPFlow.GRN.Tests",
                ["Jwt:SigningKey"] = "isolated-grn-only-signing-key-at-least-32-characters", ["Jwt:ExpirationMinutes"] = "10"
            }));
            if (authorizeFixtureWrites) builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInventoryWriteGate>();
                services.AddScoped<IInventoryWriteGate>(sp => new GoodsReceiptTestSupport.OwnedWarehouseGate(sp.GetRequiredService<ERPFlowDbContext>()));
            });
        }
    }
}
