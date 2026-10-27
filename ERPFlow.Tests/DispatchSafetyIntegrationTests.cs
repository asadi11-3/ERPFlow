using ERPFlow.Application.Common;
using ERPFlow.Application.Equipment;
using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class EquipmentWorkflowIntegrationTests
{
    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public async Task DispatchUnitBoundaryPreservesStockAndAtomicity(int quantity, bool accepted)
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        Guid orderId;
        Guid actorId;
        await using (var context = database.CreateContext())
        {
            var customer = new Customer("Customer", "bounds@example.test");
            var product = new Product("Product", "BOUNDS", 1m, quantity);
            var actor = new AppUser("Tech", "tech@example.test", Domain.Enums.UserRole.Technician, "test-hash");
            var order = new SalesOrder(customer.Id, actor.Id);
            order.AddItem(product, quantity);
            order.Submit([product]);
            order.Approve(actor.Id);
            context.AddRange(customer, product, actor, order);
            await context.SaveChangesAsync();
            orderId = order.Id;
            actorId = actor.Id;
        }
        await using (var context = database.CreateContext())
        {
            var workflow = CreateWorkflow(context, actorId);
            var command = new DispatchOrderCommand("Carrier", null, null);
            if (accepted) await workflow.DispatchOrderAsync(orderId, command);
            else await Assert.ThrowsAsync<WorkflowValidationException>(() => workflow.DispatchOrderAsync(orderId, command));
        }
        await using var verification = database.CreateContext();
        var persisted = await verification.Products.SingleAsync();
        Assert.Equal(accepted ? 0 : quantity, persisted.StockOnHand);
        Assert.Equal(accepted ? 0 : quantity, persisted.ReservedQuantity);
        Assert.Equal(accepted ? quantity : 0, await verification.MachineAssets.CountAsync());
        Assert.Equal(accepted ? 1 : 0, await verification.DeliveryNotes.CountAsync());
    }
}
