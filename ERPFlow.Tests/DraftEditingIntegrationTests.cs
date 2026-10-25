using ERPFlow.Application.Common;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class SalesOrderWorkflowIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleTrackedEditCannotOverwriteAnEditOrSubmission(bool submitCompetitor)
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        Guid orderId;
        Guid actorId;
        Guid productId;
        await using (var context = database.CreateContext())
        {
            var customer = new Customer("Customer", "edit@example.test");
            var product = new Product("Product", "EDIT", 10m, 10);
            var actor = new AppUser("Sales", "edit@example.test", UserRole.Sales, "test-hash");
            context.AddRange(customer, product, actor);
            await context.SaveChangesAsync();
            var result = await CreateWorkflow(context, actor.Id).CreateDraftAsync(
                new CreateSalesOrderCommand(customer.Id, [new CreateSalesOrderItem(product.Id, 1)]));
            orderId = result.Id;
            actorId = actor.Id;
            productId = product.Id;
        }
        await using (var staleContext = database.CreateContext())
        {
            var stale = (await new EfSalesOrderPersistence(staleContext).GetOrderAsync(orderId, true))!;
            var originalVersion = stale.RowVersion.ToArray();
            await using (var competitor = database.CreateContext())
            {
                var workflow = CreateWorkflow(competitor, actorId);
                if (submitCompetitor) await workflow.SubmitAsync(orderId);
                else await workflow.UpdateDraftAsync(orderId, new UpdateSalesOrderCommand(stale.CustomerId,
                    [new CreateSalesOrderItem(productId, 2)], originalVersion));
            }
            var exception = await Record.ExceptionAsync(() => CreateWorkflow(staleContext, actorId)
                .UpdateDraftAsync(orderId, new UpdateSalesOrderCommand(stale.CustomerId,
                    [new CreateSalesOrderItem(productId, 3)], originalVersion)));
            Assert.True(exception is ConcurrencyConflictException or WorkflowConflictException);
        }
        await using var verification = database.CreateContext();
        var order = (await new EfSalesOrderPersistence(verification).GetOrderAsync(orderId, false))!;
        Assert.Equal(submitCompetitor ? OrderStatus.PendingApproval : OrderStatus.Draft, order.Status);
        Assert.Equal(submitCompetitor ? 1 : 2, Assert.Single(order.Items).Quantity);
        Assert.Equal(submitCompetitor ? 1 : 0, (await verification.Products.SingleAsync()).ReservedQuantity);
    }

    [Fact]
    public async Task EditCanRemoveAddAndRetainProductsWithoutChangingReleasedReservations()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var customer = new Customer("Customer", "replace@example.test");
        var products = Enumerable.Range(1, 3).Select(i => new Product($"Product{i}", $"SKU{i}", i * 10m, 10)).ToArray();
        var actor = new AppUser("Admin", "replace@example.test", UserRole.Administrator, "test-hash");
        context.AddRange(customer, actor);
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
        var workflow = CreateWorkflow(context, actor.Id);
        var draft = await workflow.CreateDraftAsync(new CreateSalesOrderCommand(customer.Id,
            [new CreateSalesOrderItem(products[0].Id, 1), new CreateSalesOrderItem(products[1].Id, 2)]));
        await workflow.SubmitAsync(draft.Id);
        var returned = await workflow.ReturnAsync(draft.Id, "Change products");
        var edited = await workflow.UpdateDraftAsync(draft.Id, new UpdateSalesOrderCommand(customer.Id,
            [new CreateSalesOrderItem(products[1].Id, 3), new CreateSalesOrderItem(products[2].Id, 4)], returned.RowVersion));
        Assert.Equal(products.Skip(1).Select(p => p.Id).Order(), edited.Items.Select(i => i.ProductId).Order());
        Assert.All(edited.Reservations, r => Assert.True(r.IsReleased));
        var resubmitted = await workflow.SubmitAsync(draft.Id);
        Assert.Equal(2, resubmitted.Reservations.Count);
        Assert.All(resubmitted.Reservations, r => Assert.False(r.IsReleased));
        context.ChangeTracker.Clear();
        Assert.Equal(0, (await context.Products.SingleAsync(p => p.Id == products[0].Id)).ReservedQuantity);
        Assert.Equal(3, (await context.Products.SingleAsync(p => p.Id == products[1].Id)).ReservedQuantity);
        Assert.Equal(4, (await context.Products.SingleAsync(p => p.Id == products[2].Id)).ReservedQuantity);
    }
}
