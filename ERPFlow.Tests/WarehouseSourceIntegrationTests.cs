using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using Xunit;

namespace ERPFlow.Tests;

public sealed class WarehouseSourceIntegrationTests
{
    [Fact]
    public void ReturnEditAndResubmitArchivesClosedReservationAndRetainsItsOrigin()
    {
        var product = new Product("Product", "SKU-1", 10m, 10);
        var order = new SalesOrder(Guid.NewGuid(), Guid.NewGuid());
        order.AddItem(product, 2, LegacyDefaultWarehouse.Id);
        order.Submit([product]);
        order.ReturnForCorrection(Guid.NewGuid(), [product]);

        var returnedReservation = Assert.Single(order.Reservations);
        Assert.Equal(LegacyDefaultWarehouse.Id, returnedReservation.WarehouseId);

        order.Submit([product]);

        Assert.True(returnedReservation.IsHistorical);
        Assert.Equal(StockReservationClosureKind.Released, returnedReservation.ClosureKind);
        var currentReservation = Assert.Single(order.Reservations);
        Assert.NotEqual(returnedReservation.Id, currentReservation.Id);
        Assert.Equal(LegacyDefaultWarehouse.Id, currentReservation.WarehouseId);
        Assert.Equal(2, order.ReservationHistory.Count);
    }
}
