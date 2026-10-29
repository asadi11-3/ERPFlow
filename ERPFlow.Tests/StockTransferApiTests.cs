using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ERPFlow.Api;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Procurement;
using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Transfers;
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

public sealed class StockTransferApiTests : IAsyncLifetime
{
    private InventoryTestDatabase database = null!;
    private TransferSeed seed = null!;
    private ApiFactory factory = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        database = await InventoryTestDatabase.CreateAsync();
        seed = await StockTransferTestSupport.SeedAsync(database);
        factory = FactoryFor(database, true);
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task TransferRoutesEnforceRolesForReadsAndMutations()
    {
        foreach (var role in Enum.GetValues<UserRole>())
        {
            await Authenticate(Actor(role));
            var allowed = role is UserRole.Administrator or UserRole.WarehouseOperator;
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/stock-transfers")).StatusCode);
            var response = await Create(seed.Draft(), $"role-{(int)role}");
            Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task InventoryQueriesAuthorizeOperationalReadersOnly()
    {
        foreach (var role in Enum.GetValues<UserRole>())
        {
            await Authenticate(Actor(role));
            var allowed = role is UserRole.Administrator or UserRole.Approver or UserRole.Purchasing or UserRole.WarehouseOperator;
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/inventory/stocks")).StatusCode);
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/inventory/movements")).StatusCode);
        }
    }

    [Fact]
    public async Task WarehouseOperatorPostsTransferAndReturnsPairedCostFreeInventoryReferences()
    {
        await Authenticate(seed.Procurement.Operator);
        var draft = await ReadCreated(await Create(seed.Draft(5), "happy-create"));
        var posted = await ReadOk<StockTransferResult>(await Post(draft.Id, draft.RowVersion, "happy-post"));

        Assert.Equal(StockTransferStatus.Posted, posted.Status);
        Assert.All(posted.Items, item =>
        {
            Assert.NotNull(item.SourcePostingId); Assert.NotNull(item.SourceMovementId);
            Assert.NotNull(item.DestinationPostingId); Assert.NotNull(item.DestinationMovementId);
            Assert.NotEqual(item.SourcePostingId, item.DestinationPostingId);
            Assert.NotEqual(item.SourceMovementId, item.DestinationMovementId);
        });

        var stocks = await client.GetFromJsonAsync<PageResult<InventoryStockResult>>($"/api/inventory/stocks?productId={seed.Product}");
        var source = stocks!.Items.Single(x => x.WarehouseId == seed.Source);
        var destination = stocks.Items.Single(x => x.WarehouseId == seed.Destination);
        Assert.Equal((5, 3, 2), (source.StockOnHand, source.ReservedQuantity, source.AvailableStock));
        Assert.Equal((7, 0, 7), (destination.StockOnHand, destination.ReservedQuantity, destination.AvailableStock));

        var movements = await client.GetFromJsonAsync<PageResult<InventoryMovementResult>>($"/api/inventory/movements?productId={seed.Product}");
        var transferMovements = movements!.Items.Where(x => x.SourceDocumentId == posted.Id).ToArray();
        Assert.Equal(2, transferMovements.Length);
        Assert.All(transferMovements, x => { Assert.Equal("StockTransfer", x.SourceType); Assert.Equal(posted.Number, x.SourceNumber); });
        var json = await (await client.GetAsync("/api/inventory/movements")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("unitcost", json.ToLowerInvariant());
        Assert.DoesNotContain("totalamount", json.ToLowerInvariant());
        Assert.DoesNotContain("supplier", json.ToLowerInvariant());
    }

    [Fact]
    public async Task CreateIdempotencyReplaysSameHeaderAndRejectsDifferentPayload()
    {
        await Authenticate(seed.Procurement.Operator);
        var first = await ReadCreated(await Create(seed.Draft(5), "same-header"));
        var replay = await ReadCreated(await Create(seed.Draft(5), "same-header"));
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Create(seed.Draft(4), "same-header")).StatusCode);
    }

    [Fact]
    public async Task DetailAndFilteredListReturnCreatedTransfer()
    {
        await Authenticate(seed.Procurement.Operator);
        var created = await ReadCreated(await Create(seed.Draft(), "detail-list"));
        var detail = await client.GetFromJsonAsync<StockTransferResult>($"/api/stock-transfers/{created.Id}");
        var list = await client.GetFromJsonAsync<PageResult<StockTransferResult>>($"/api/stock-transfers?warehouseId={seed.Source}&productId={seed.Product}&status={(int)StockTransferStatus.Draft}");
        Assert.Equal(created.Id, detail!.Id);
        Assert.Contains(list!.Items, x => x.Id == created.Id);
    }

    [Fact]
    public async Task MissingIdempotencyHeadersAndBadConcurrencyTokenReturn400()
    {
        await Authenticate(seed.Procurement.Operator);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(seed.Draft(), null)).StatusCode);
        var draft = await ReadCreated(await Create(seed.Draft(), "token-draft"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(draft.Id, [], "bad-token")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(draft.Id, draft.RowVersion, null)).StatusCode);
    }

    [Fact]
    public async Task StaleCancelledAndMissingTransfersHaveMeaningfulResponses()
    {
        await Authenticate(seed.Procurement.Operator);
        var postedDraft = await ReadCreated(await Create(seed.Draft(), "stale-draft"));
        var posted = await ReadOk<StockTransferResult>(await Post(postedDraft.Id, postedDraft.RowVersion, "stale-post"));
        Assert.Equal(HttpStatusCode.Conflict, (await Cancel(posted.Id, postedDraft.RowVersion, "stale")).StatusCode);

        var draft = await ReadCreated(await Create(seed.Draft(), "cancel-draft"));
        var cancelled = await ReadOk<StockTransferResult>(await Cancel(draft.Id, draft.RowVersion, "wrong warehouse"));
        Assert.Equal(StockTransferStatus.Cancelled, cancelled.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(cancelled.Id, cancelled.RowVersion, "cancelled-post")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/stock-transfers/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/stock-transfers?pageSize=101")).StatusCode);
    }

    [Fact]
    public async Task InventoryFiltersAndInvalidBoundsAreApplied()
    {
        await Authenticate(seed.Procurement.Operator);
        var onlySource = await client.GetFromJsonAsync<PageResult<InventoryStockResult>>($"/api/inventory/stocks?warehouseId={seed.Source}&productId={seed.Product}");
        Assert.Single(onlySource!.Items);
        Assert.Equal((seed.Source, seed.Product), (onlySource.Items[0].WarehouseId, onlySource.Items[0].ProductId));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/inventory/stocks?page=0")).StatusCode);
        var now = DateTimeOffset.UtcNow;
        var encodedNow = Uri.EscapeDataString(now.ToString("O"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/inventory/movements?from={encodedNow}&to={encodedNow}")).StatusCode);
    }

    [Fact]
    public async Task MovementDatesUseInclusiveFromExclusiveToAndStablePaging()
    {
        await Authenticate(seed.Procurement.Operator);
        var all = await client.GetFromJsonAsync<PageResult<InventoryMovementResult>>("/api/inventory/movements?pageSize=100");
        Assert.NotEmpty(all!.Items);
        var boundary = DateTime.SpecifyKind(all.Items[0].CreatedAt, DateTimeKind.Utc);
        var encodedBoundary = Uri.EscapeDataString(new DateTimeOffset(boundary).ToString("O"));
        var from = await client.GetFromJsonAsync<PageResult<InventoryMovementResult>>($"/api/inventory/movements?pageSize=100&from={encodedBoundary}");
        Assert.Contains(from!.Items, x => x.Id == all.Items[0].Id);
        var to = await client.GetFromJsonAsync<PageResult<InventoryMovementResult>>($"/api/inventory/movements?pageSize=100&to={encodedBoundary}");
        Assert.DoesNotContain(to!.Items, x => x.Id == all.Items[0].Id);
        var pageOne = await client.GetFromJsonAsync<PageResult<InventoryMovementResult>>("/api/inventory/movements?pageSize=1");
        var pageOneAgain = await client.GetFromJsonAsync<PageResult<InventoryMovementResult>>("/api/inventory/movements?pageSize=1");
        Assert.Equal(pageOne!.Items.Single().Id, pageOneAgain!.Items.Single().Id);
    }

    [Fact]
    public async Task PrecutoverInventoryReadsReturn409()
    {
        await using var uncutover = await InventoryTestDatabase.CreateAsync();
        var uncutoverSeed = await StockTransferTestSupport.SeedAsync(uncutover, cutover: false);
        await using var uncutoverFactory = FactoryFor(uncutover, true);
        using var uncutoverClient = uncutoverFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        uncutoverClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(uncutoverFactory, uncutover, uncutoverSeed.Procurement.Operator));
        Assert.Equal(HttpStatusCode.Created, (await Send(uncutoverClient, "/api/stock-transfers", uncutoverSeed.Draft(), "precutover-create")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await uncutoverClient.GetAsync("/api/inventory/stocks")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await uncutoverClient.GetAsync("/api/inventory/movements")).StatusCode);
    }

    [Fact]
    public async Task ProductionGateAllowsPostingOnlyAfterVerifiedCutover()
    {
        await using var context = database.Context();
        await using var production = FactoryFor(database, false);
        using var denied = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        denied.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(production, database, seed.Procurement.Operator));
        var draft = await ReadCreated(await Send(denied, "/api/stock-transfers", seed.Draft(), "production-draft"));
        var before = await context.WarehouseStocks.AsNoTracking().Where(x => x.ProductId == seed.Product)
            .OrderBy(x => x.WarehouseId).Select(x => new { x.WarehouseId, x.StockOnHand, x.ReservedQuantity }).ToArrayAsync();
        Assert.Equal(HttpStatusCode.OK, (await Send(denied, $"/api/stock-transfers/{draft.Id}/post", new PostStockTransferCommand(draft.RowVersion), "production-post")).StatusCode);
        var after = await context.WarehouseStocks.AsNoTracking().Where(x => x.ProductId == seed.Product)
            .OrderBy(x => x.WarehouseId).Select(x => new { x.WarehouseId, x.StockOnHand, x.ReservedQuantity }).ToArrayAsync();
        Assert.Equal(before.Sum(x => x.StockOnHand), after.Sum(x => x.StockOnHand));
        Assert.Equal(before.Single(x => x.WarehouseId == seed.Source).StockOnHand - 5, after.Single(x => x.WarehouseId == seed.Source).StockOnHand);
        Assert.Equal(before.Single(x => x.WarehouseId == seed.Destination).StockOnHand + 5, after.Single(x => x.WarehouseId == seed.Destination).StockOnHand);
        Assert.Equal(StockTransferStatus.Posted, (await context.StockTransfers.AsNoTracking().SingleAsync(x => x.Id == draft.Id)).Status);
    }

    [Fact]
    public async Task PersistedRoleRevocationForbidsValidJwt()
    {
        await Authenticate(seed.Procurement.Operator);
        await using var context = database.Context();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE AppUsers SET Role=1 WHERE Id={seed.Procurement.Operator}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/stock-transfers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/inventory/stocks")).StatusCode);
    }

    private async Task Authenticate(Guid id) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(factory, database, id));
    private Guid Actor(UserRole role) => role switch
    {
        UserRole.Administrator => seed.Procurement.Inventory.ActorId, UserRole.WarehouseOperator => seed.Procurement.Operator,
        UserRole.Purchasing => seed.Procurement.Purchasing, UserRole.Approver => seed.Procurement.Approver,
        UserRole.Sales => seed.Procurement.Sales, UserRole.Technician => seed.Procurement.Technician,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
    private Task<HttpResponseMessage> Create(CreateStockTransferCommand command, string? key) => Send(client, "/api/stock-transfers", command, key);
    private Task<HttpResponseMessage> Post(Guid id, byte[] token, string? key) => Send(client, $"/api/stock-transfers/{id}/post", new PostStockTransferCommand(token), key);
    private Task<HttpResponseMessage> Cancel(Guid id, byte[] token, string reason) => Send(client, $"/api/stock-transfers/{id}/cancel", new CancelStockTransferCommand(token, reason));
    private static Task<HttpResponseMessage> Send(HttpClient target, string path, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return target.SendAsync(request);
    }
    private static async Task<StockTransferResult> ReadCreated(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StockTransferResult>())!;
    }
    private static async Task<T> ReadOk<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<string> Token(ApiFactory target, InventoryTestDatabase targetDatabase, Guid id)
    {
        await using var context = targetDatabase.Context();
        return target.Services.GetRequiredService<JwtTokenService>().Create(await context.AppUsers.AsNoTracking().SingleAsync(x => x.Id == id)).AccessToken;
    }
    private static ApiFactory FactoryFor(InventoryTestDatabase targetDatabase, bool fixtureOwnedGate)
    {
        using var context = targetDatabase.Context();
        if (!InventoryTestDatabase.Owns(context)) throw new InvalidOperationException("API test factory requires an owned database.");
        return new(context.Database.GetConnectionString()!, fixtureOwnedGate);
    }

    private sealed class ApiFactory(string connection, bool fixtureOwnedGate) : WebApplicationFactory<Program>
    {
        private readonly string storageRoot = Path.Combine(Path.GetTempPath(), "ERPFlow-TransferProduction-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(fixtureOwnedGate ? "Development" : "Production");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ERPFlowDb"] = connection, ["DemoUsers:Password"] = "",
                ["AllowedHosts"] = "localhost", ["ProductImages:RootPath"] = storageRoot,
                ["Logging:LogLevel:Default"] = "Warning", ["Logging:LogLevel:Microsoft"] = "Warning",
                ["Jwt:Issuer"] = "ERPFlow.StockTransfer.Tests", ["Jwt:Audience"] = "ERPFlow.StockTransfer.Tests",
                ["Jwt:SigningKey"] = "isolated-stock-transfer-signing-key-at-least-32-characters", ["Jwt:ExpirationMinutes"] = "10"
            }));
            if (fixtureOwnedGate) builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInventoryWriteGate>();
                services.AddScoped<IInventoryWriteGate>(sp => new GoodsReceiptTestSupport.OwnedWarehouseGate(sp.GetRequiredService<ERPFlowDbContext>()));
            });
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (Directory.Exists(storageRoot)) Directory.Delete(storageRoot, true);
        }
    }
}
