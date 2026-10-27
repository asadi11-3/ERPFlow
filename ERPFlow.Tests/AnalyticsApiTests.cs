using System.Net;
using ERPFlow.Application.Analytics;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class ProcurementApiTests
{
    [Theory]
    [InlineData("sales")]
    [InlineData("approvals")]
    [InlineData("procurement")]
    [InlineData("receiving")]
    [InlineData("transfers")]
    [InlineData("inventory")]
    [InlineData("dispatch")]
    [InlineData("service")]
    public async Task AnalyticsRequireAuthentication(string endpoint) =>
        await Problem(await client.GetAsync($"/api/analytics/{endpoint}"), HttpStatusCode.Unauthorized);

    [Fact]
    public async Task AnalyticsExecuteSqlAndZeroFillEmptyBuckets()
    {
        await Authenticate(seed.Inventory.ActorId);
        const string range = "?from=2020-01-01&to=2020-01-04";
        var sales = await Read<SalesAnalytics>(await client.GetAsync("/api/analytics/sales" + range));
        Assert.Equal(3, sales.Created.Count);
        Assert.All(sales.Created, point => Assert.Equal(0, point.Count));
        Assert.Equal(0, sales.ApprovedOrderValue);
        await Read<ApprovalsAnalytics>(await client.GetAsync("/api/analytics/approvals" + range));
        await Read<ProcurementAnalytics>(await client.GetAsync("/api/analytics/procurement" + range));
        await Read<ReceivingAnalytics>(await client.GetAsync("/api/analytics/receiving" + range));
        await Read<TransfersAnalytics>(await client.GetAsync("/api/analytics/transfers" + range));
        await Read<DispatchAnalytics>(await client.GetAsync("/api/analytics/dispatch" + range));
        await Read<ServiceAnalytics>(await client.GetAsync("/api/analytics/service" + range));
        await Problem(await client.GetAsync("/api/analytics/inventory" + range), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("?from=2026-01-01&to=2026-01-01")]
    [InlineData("?from=2020-01-01&to=2026-01-01")]
    [InlineData("?groupBy=year")]
    [InlineData("?from=not-a-date")]
    public async Task AnalyticsRejectInvalidWindows(string query)
    {
        await Authenticate(seed.Inventory.ActorId);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/analytics/sales" + query)).StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Sales, "approvals")]
    [InlineData(UserRole.Sales, "procurement")]
    [InlineData(UserRole.Technician, "inventory")]
    [InlineData(UserRole.WarehouseOperator, "procurement")]
    [InlineData(UserRole.Purchasing, "transfers")]
    [InlineData(UserRole.Purchasing, "sales")]
    public async Task AnalyticsEnforceRoleBoundaries(UserRole role, string endpoint)
    {
        await Authenticate(Actor(role));
        await Problem(await client.GetAsync($"/api/analytics/{endpoint}"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SalesAnalyticsScopeOwnersExcludeDraftValueAndRespectExclusiveEnd()
    {
        await using (var context = database.Context())
        {
            var customer = await context.Customers.FirstAsync();
            var product = await context.Products.SingleAsync();
            foreach (var (owner, date, approved) in new[]
            {
                (Actor(UserRole.Sales), new DateTime(2026, 1, 5), true),
                (Actor(UserRole.Sales), new DateTime(2026, 1, 6), false),
                (seed.Inventory.ActorId, new DateTime(2026, 1, 5), true),
                (Actor(UserRole.Sales), new DateTime(2026, 1, 8), true)
            })
            {
                var order = new SalesOrder(customer.Id, owner);
                order.AddItem(product, 2);
                if (approved)
                {
                    order.Submit([product]);
                    context.ApprovalHistories.Add(order.Approve(Actor(UserRole.Approver)));
                }
                context.SalesOrders.Add(order);
                context.Entry(order).Property(entry => entry.CreatedAt).CurrentValue = date;
            }
            await context.SaveChangesAsync();
        }
        await Authenticate(Actor(UserRole.Sales));
        var result = await Read<SalesAnalytics>(await client.GetAsync(
            "/api/analytics/sales?from=2026-01-05&to=2026-01-08&groupBy=week"));
        Assert.Equal(2, result.OrderCount);
        Assert.Equal(20m, result.ApprovedOrderValue);
        Assert.Equal(new DateOnly(2026, 1, 5), Assert.Single(result.Created).Date);
        Assert.Equal(2, Assert.Single(result.TopProducts).Quantity);
    }

    [Fact]
    public async Task ProcurementAnalyticsScopePurchasingOwners()
    {
        var buyer = Actor(UserRole.Purchasing);
        await using (var context = database.Context())
        {
            await ProcurementTestSupport.Orders(context, buyer).CreateAsync(seed.Draft());
            await ProcurementTestSupport.Orders(context, seed.Inventory.ActorId).CreateAsync(seed.Draft());
        }
        await Authenticate(buyer);
        var own = await Read<ProcurementAnalytics>(await client.GetAsync("/api/analytics/procurement"));
        Assert.Equal(1, own.OrderCount);
        Assert.Equal(0, own.CommittedOrderValue);
        Assert.Empty(own.TopSuppliers);
        await Authenticate(seed.Inventory.ActorId);
        var all = await Read<ProcurementAnalytics>(await client.GetAsync("/api/analytics/procurement"));
        Assert.Equal(2, all.OrderCount);
    }
}
