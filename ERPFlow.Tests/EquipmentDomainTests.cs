using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Xunit;

namespace ERPFlow.Tests;

public sealed class EquipmentDomainTests
{
    [Theory]
    [InlineData(OrderStatus.Draft)]
    [InlineData(OrderStatus.PendingApproval)]
    [InlineData(OrderStatus.Rejected)]
    public void DispatchRejectsUnapprovedOrdersWithoutMovingStock(OrderStatus status)
    {
        var product = new Product("Machine", "MACHINE-1", 100m, 5);
        var order = new SalesOrder(Guid.NewGuid(), Guid.NewGuid());
        order.AddItem(product, 2);
        if (status != OrderStatus.Draft)
        {
            order.Submit([product]);
            if (status == OrderStatus.Rejected)
            {
                order.Reject(Guid.NewGuid(), [product], "Not approved");
            }
        }

        var reservedBefore = product.ReservedQuantity;
        Assert.Throws<InvalidOperationException>(() => order.Dispatch([product]));
        Assert.Equal(5, product.StockOnHand);
        Assert.Equal(reservedBefore, product.ReservedQuantity);
        Assert.Equal(status, order.Status);
    }

    [Fact]
    public void ConsumeReservedQuantityReducesPhysicalAndReservedStockTogether()
    {
        var product = new Product("Machine", "MACHINE-1", 100m, 5);
        product.ReserveQuantity(3);

        product.ConsumeReservedQuantity(2);

        Assert.Equal(3, product.StockOnHand);
        Assert.Equal(1, product.ReservedQuantity);
        Assert.Equal(2, product.AvailableStock);
    }

    [Fact]
    public void DispatchConsumesAndClosesTheExactOrderReservationOnce()
    {
        var product = new Product("Machine", "MACHINE-1", 100m, 5);
        var order = new SalesOrder(Guid.NewGuid(), Guid.NewGuid());
        order.AddItem(product, 3);
        order.Submit([product]);
        order.Approve(Guid.NewGuid());

        order.Dispatch([product]);

        Assert.Equal(2, product.StockOnHand);
        Assert.Equal(0, product.ReservedQuantity);
        Assert.True(Assert.Single(order.Reservations).IsReleased);
        Assert.Throws<InvalidOperationException>(() => order.Dispatch([product]));
    }
}
