using ERPFlow.Domain.Inventory;
using Xunit;

namespace ERPFlow.Tests;

public sealed class InventoryFoundationDomainTests
{
    [Fact]
    public void StockReservationAndConsumptionPreserveAvailableStock()
    {
        var stock = Stock();
        stock.Apply(InventoryPostingKind.Opening, 10);
        stock.Apply(InventoryPostingKind.Reserve, 4);
        Assert.Equal(6, stock.AvailableStock);
        stock.Apply(InventoryPostingKind.Consume, 4);
        Assert.Equal(6, stock.StockOnHand);
        Assert.Equal(0, stock.ReservedQuantity);
        Assert.Equal(6, stock.AvailableStock);
        Assert.Equal(3, stock.Sequence);
    }

    [Fact]
    public void ReleaseFreesReservationWithoutChangingPhysicalStock()
    {
        var stock = Stock();
        stock.Apply(InventoryPostingKind.Opening, 5);
        stock.Apply(InventoryPostingKind.Reserve, 3);
        stock.Apply(InventoryPostingKind.Release, 3);
        Assert.Equal(5, stock.StockOnHand);
        Assert.Equal(5, stock.AvailableStock);
    }

    [Theory]
    [InlineData(InventoryPostingKind.Reserve, 6)]
    [InlineData(InventoryPostingKind.Release, 1)]
    [InlineData(InventoryPostingKind.Consume, 1)]
    [InlineData(InventoryPostingKind.Opening, 1)]
    public void InvalidEffectsLeaveStockUnchanged(InventoryPostingKind kind, int quantity)
    {
        var stock = Stock();
        stock.Apply(InventoryPostingKind.Opening, 5);
        Assert.Throws<InvalidOperationException>(() => stock.Apply(kind, quantity));
        Assert.Equal(5, stock.StockOnHand);
        Assert.Equal(0, stock.ReservedQuantity);
        Assert.Equal(1, stock.Sequence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveReservationIsRejected(int quantity)
    {
        var stock = Stock();
        stock.Apply(InventoryPostingKind.Opening, 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Apply(InventoryPostingKind.Reserve, quantity));
        Assert.Equal(0, stock.ReservedQuantity);
    }

    [Fact]
    public void ZeroOpeningAndMaximumQuantityHaveNoOverflow()
    {
        var empty = Stock();
        empty.Apply(InventoryPostingKind.Opening, 0);
        Assert.Equal(1, empty.Sequence);
        var maximum = Stock();
        maximum.Apply(InventoryPostingKind.Opening, int.MaxValue);
        maximum.Apply(InventoryPostingKind.Reserve, int.MaxValue);
        Assert.Throws<OverflowException>(() => maximum.Apply(InventoryPostingKind.Reserve, 1));
        Assert.Equal(int.MaxValue, maximum.ReservedQuantity);
        maximum.Apply(InventoryPostingKind.Consume, int.MaxValue);
        Assert.Equal(0, maximum.StockOnHand);
    }

    [Fact]
    public void ReservationRequiresOneOwnerAndCanOnlyCloseOnce()
    {
        var stock = Stock();
        Assert.Throws<ArgumentException>(() => new InventoryReservation(stock, 1, null, null));
        Assert.Throws<ArgumentException>(() => new InventoryReservation(stock, 1, Guid.NewGuid(), Guid.NewGuid()));
        var reservation = new InventoryReservation(stock, 1, Guid.NewGuid(), null);
        reservation.Close(InventoryPostingKind.Consume);
        Assert.Equal(InventoryReservationStatus.Consumed, reservation.Status);
        Assert.NotNull(reservation.ClosedAt);
        Assert.Throws<InvalidOperationException>(() => reservation.Close(InventoryPostingKind.Release));
    }

    [Fact]
    public void WarehouseNormalizesCodeAndRejectsInvalidMasterData()
    {
        var warehouse = new Warehouse(" main ", " Main Warehouse ");
        Assert.Equal("MAIN", warehouse.Code);
        Assert.Equal("Main Warehouse", warehouse.Name);
        Assert.True(warehouse.IsActive);
        Assert.Throws<ArgumentException>(() => new Warehouse(" ", "Name"));
        Assert.Throws<ArgumentException>(() => new Warehouse("Code", " "));
    }

    private static WarehouseStock Stock() => new(Guid.NewGuid(), Guid.NewGuid());
}
