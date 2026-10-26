using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ERPFlow.Api;
using ERPFlow.Application.Procurement;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Procurement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class ProcurementApiTests : IAsyncLifetime
{
    private InventoryTestDatabase database = null!;
    private ProcurementSeed seed = null!;
    private ApiFactory factory = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        database = await InventoryTestDatabase.CreateAsync();
        seed = await ProcurementTestSupport.SeedAsync(database);
        await using var context = database.Context();
        factory = new ApiFactory(context.Database.GetConnectionString()!);
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    }
    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task AnonymousProcurementEndpointsRequireAuthentication()
    {
        foreach (var path in new[] { "/api/warehouses", "/api/suppliers", "/api/purchase-orders", "/api/purchase-orders/operational" })
            await Problem(await client.GetAsync(path), HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.Created)]
    [InlineData(UserRole.Purchasing, HttpStatusCode.Created)]
    [InlineData(UserRole.Approver, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.WarehouseOperator, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Sales, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Technician, HttpStatusCode.Forbidden)]
    public async Task SupplierAndPurchaseCreationRespectRolePolicies(UserRole role, HttpStatusCode expected)
    {
        await Authenticate(Actor(role));
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/suppliers", new CreateSupplierCommand("API-SUP", "API Supplier"))).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/purchase-orders", seed.Draft())).StatusCode);
        if (role != UserRole.Administrator)
            await Problem(await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseCommand("NO", "No permission")), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WarehouseAndSupplierEndpointsSupportLifecyclePaginationAndErrors()
    {
        await Authenticate(seed.Inventory.ActorId);
        var created = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseCommand("API", "Warehouse"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var warehouse = (await created.Content.ReadFromJsonAsync<WarehouseResult>())!;
        warehouse = await Read<WarehouseResult>(await client.PutAsJsonAsync($"/api/warehouses/{warehouse.Id}", new UpdateWarehouseCommand("Edited", warehouse.RowVersion)));
        warehouse = await Read<WarehouseResult>(await client.PostAsJsonAsync($"/api/warehouses/{warehouse.Id}/deactivate", new VersionCommand(warehouse.RowVersion)));
        Assert.False(warehouse.IsActive);
        warehouse = await Read<WarehouseResult>(await client.PostAsJsonAsync($"/api/warehouses/{warehouse.Id}/activate", new VersionCommand(warehouse.RowVersion)));
        Assert.True(warehouse.IsActive);
        await Problem(await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseCommand("api", "Duplicate")), HttpStatusCode.Conflict);
        await Problem(await client.PutAsJsonAsync($"/api/warehouses/{warehouse.Id}", new UpdateWarehouseCommand("Invalid", [])), HttpStatusCode.BadRequest);
        var page = await client.GetFromJsonAsync<PageResult<WarehouseResult>>("/api/warehouses?pageSize=1&active=true");
        Assert.Single(page!.Items);
        Assert.True(page.TotalCount >= 4);
        await Problem(await client.GetAsync("/api/warehouses?pageSize=101"), HttpStatusCode.BadRequest);
        await Problem(await client.GetAsync($"/api/warehouses/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
        var supplier = await ReadCreated<SupplierResult>(await client.PostAsJsonAsync("/api/suppliers", new CreateSupplierCommand("API", "Supplier")));
        supplier = await Read<SupplierResult>(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}", new UpdateSupplierCommand("Updated", null, null, null, null, supplier.RowVersion)));
        supplier = await Read<SupplierResult>(await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/deactivate", new VersionCommand(supplier.RowVersion)));
        Assert.False(supplier.IsActive);
        supplier = await Read<SupplierResult>(await client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/activate", new VersionCommand(supplier.RowVersion)));
        Assert.True(supplier.IsActive);
        var suppliers = await client.GetFromJsonAsync<PageResult<SupplierResult>>("/api/suppliers?search=Updated&pageSize=1");
        Assert.Equal(supplier.Id, Assert.Single(suppliers!.Items).Id);
        await Problem(await client.GetAsync($"/api/suppliers/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task InvalidPurchasePayloadsReturn400(int invalid)
    {
        await Authenticate(seed.Purchasing);
        var command = invalid switch
        {
            0 => seed.Draft() with { Items = [new(seed.Inventory.ProductId, 0, 1)] },
            1 => seed.Draft() with { Items = [new(seed.Inventory.ProductId, 1, -1)] },
            2 => seed.Draft() with { Items = [new(seed.Inventory.ProductId, 1, 1), new(seed.Inventory.ProductId, 2, 1)] },
            3 => seed.Draft() with { Items = [null!] },
            4 => seed.Draft() with { Notes = new string('n', 1001) },
            _ => seed.Draft() with { SupplierId = Guid.Empty }
        };
        await Problem(await client.PostAsJsonAsync("/api/purchase-orders", command), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PurchaseFlowUsesVersionTokensAndWarehouseProjectionCannotExposeCosts()
    {
        await Authenticate(seed.Purchasing);
        var order = await ReadCreated<PurchaseOrderResult>(await client.PostAsJsonAsync("/api/purchase-orders", seed.Draft()));
        var original = order.RowVersion;
        order = await Read<PurchaseOrderResult>(await client.PutAsJsonAsync($"/api/purchase-orders/{order.Id}",
            new UpdatePurchaseOrderCommand(order.SupplierId, order.WarehouseId, [new(seed.Inventory.ProductId, 5, 1.25m)], "Commercial", order.RowVersion)));
        Assert.Equal(6.25m, order.TotalAmount);
        await Problem(await client.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/submit", new VersionCommand(original)), HttpStatusCode.Conflict);
        order = await Read<PurchaseOrderResult>(await client.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/submit", new VersionCommand(order.RowVersion)));
        await Authenticate(seed.OtherPurchasing);
        await Problem(await client.GetAsync($"/api/purchase-orders/{order.Id}"), HttpStatusCode.Forbidden);
        await Problem(await client.GetAsync($"/api/purchase-orders/{order.Id}/history"), HttpStatusCode.Forbidden);
        await Authenticate(seed.Approver);
        order = await Read<PurchaseOrderResult>(await client.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/approve", new PurchaseOrderDecisionCommand(order.RowVersion, "Approved")));
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.Equal(2, (await client.GetFromJsonAsync<PurchaseOrderHistoryResult[]>($"/api/purchase-orders/{order.Id}/history"))!.Length);
        await Authenticate(seed.Operator);
        await Problem(await client.GetAsync($"/api/purchase-orders/{order.Id}"), HttpStatusCode.Forbidden);
        await Problem(await client.GetAsync($"/api/purchase-orders/{order.Id}/history"), HttpStatusCode.Forbidden);
        await Problem(await client.GetAsync("/api/purchase-orders"), HttpStatusCode.Forbidden);
        await Problem(await client.GetAsync("/api/suppliers"), HttpStatusCode.Forbidden);
        var operational = await client.GetFromJsonAsync<JsonElement>($"/api/purchase-orders/operational/{order.Id}");
        Assert.Equal(order.Id, operational.GetProperty("id").GetGuid());
        Assert.False(operational.TryGetProperty("totalAmount", out _));
        Assert.False(operational.TryGetProperty("notes", out _));
        Assert.False(operational.GetProperty("items")[0].TryGetProperty("unitCost", out _));
        Assert.False(operational.GetProperty("items")[0].TryGetProperty("lineTotal", out _));
    }

    [Fact]
    public async Task SelfApprovalAndChangedPersistedRoleReturn403()
    {
        await Authenticate(seed.Inventory.ActorId);
        var order = await ReadCreated<PurchaseOrderResult>(await client.PostAsJsonAsync("/api/purchase-orders", seed.Draft()));
        order = await Read<PurchaseOrderResult>(await client.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/submit", new VersionCommand(order.RowVersion)));
        await Problem(await client.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/approve", new PurchaseOrderDecisionCommand(order.RowVersion)), HttpStatusCode.Forbidden);
        await Authenticate(seed.Approver);
        await using var context = database.Context();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE AppUsers SET Role=1 WHERE Id={seed.Approver}");
        await Problem(await client.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/approve", new PurchaseOrderDecisionCommand(order.RowVersion)), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RejectionAndCancellationRoutesRequireReasonsAndAdvanceVersions()
    {
        await Authenticate(seed.Purchasing);
        var order = await ReadCreated<PurchaseOrderResult>(await client.PostAsJsonAsync("/api/purchase-orders", seed.Draft()));
        order = await Read<PurchaseOrderResult>(await client.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/submit", new VersionCommand(order.RowVersion)));
        await Authenticate(seed.Approver);
        await Problem(await client.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/reject", new PurchaseOrderReasonCommand(order.RowVersion, " ")), HttpStatusCode.BadRequest);
        order = await Read<PurchaseOrderResult>(await client.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/reject", new PurchaseOrderReasonCommand(order.RowVersion, "Rejected")));
        Assert.Equal(PurchaseOrderStatus.Rejected, order.Status);
        await Authenticate(seed.Purchasing);
        var cancelled = await ReadCreated<PurchaseOrderResult>(await client.PostAsJsonAsync("/api/purchase-orders", seed.Draft()));
        cancelled = await Read<PurchaseOrderResult>(await client.PostAsJsonAsync($"/api/purchase-orders/{cancelled.Id}/cancel", new PurchaseOrderReasonCommand(cancelled.RowVersion, "Cancelled")));
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task NewRolesCannotGainExistingSalesOrServiceMutationPermissions()
    {
        foreach (var actor in new[] { seed.Purchasing, seed.Operator })
        {
            await Authenticate(actor);
            await Problem(await client.PostAsJsonAsync($"/api/sales-orders/{seed.Inventory.SalesOrderId}/approve", new ApproveOrderRequest(null)), HttpStatusCode.Forbidden);
            await Problem(await client.PostAsJsonAsync($"/api/sales-orders/{seed.Inventory.SalesOrderId}/submit", new { }), HttpStatusCode.Forbidden);
            await Problem(await client.GetAsync($"/api/sales-orders/{seed.Inventory.SalesOrderId}"), HttpStatusCode.Forbidden);
        }
    }

}
