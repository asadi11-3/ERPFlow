using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Xunit;

namespace ERPFlow.Tests;

public sealed class SalesOrderTests
{
    [Fact]
    public void NewOrderStartsAsDraft()
    {
        var order = CreateOrder();

        Assert.Equal(OrderStatus.Draft, order.Status);
    }

    [Fact]
    public void SubmitReservesStockAndMovesOrderToPendingApproval()
    {
        var product = CreateProduct(stockOnHand: 10);
        var order = CreateOrder();
        order.AddItem(product, 3);

        order.Submit([product]);

        Assert.Equal(OrderStatus.PendingApproval, order.Status);
        Assert.Equal(3, product.ReservedQuantity);
        Assert.Equal(7, product.AvailableStock);
        Assert.NotNull(order.SubmittedAt);
    }

    [Fact]
    public void ApproveKeepsReservation()
    {
        var product = CreateProduct(stockOnHand: 10);
        var order = CreateOrder();
        order.AddItem(product, 3);
        order.Submit([product]);

        var history = order.Approve(Guid.NewGuid());

        Assert.Equal(OrderStatus.Approved, order.Status);
        Assert.Equal(ApprovalAction.Approved, history.Action);
        Assert.Equal(3, product.ReservedQuantity);
    }

    [Fact]
    public void RejectReleasesReservationExactlyOnce()
    {
        var product = CreateProduct(stockOnHand: 10);
        var order = CreateOrder();
        order.AddItem(product, 3);
        order.Submit([product]);

        var history = order.Reject(Guid.NewGuid(), [product]);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(ApprovalAction.Rejected, history.Action);
        Assert.Equal(0, product.ReservedQuantity);
        Assert.Equal(10, product.AvailableStock);

        Assert.Throws<InvalidOperationException>(() => order.Reject(Guid.NewGuid(), [product]));
        Assert.Equal(0, product.ReservedQuantity);
    }

    [Fact]
    public void SubmitRejectsDuplicateProductsBeforeStockChanges()
    {
        var firstProduct = CreateProduct("FIRST", stockOnHand: 10);
        var secondProduct = CreateProduct("SECOND", stockOnHand: 10);
        var order = CreateOrder();
        order.AddItem(firstProduct, 2);
        order.AddItem(secondProduct, 4);

        Assert.Throws<InvalidOperationException>(() => order.Submit([firstProduct, firstProduct]));

        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Equal(0, firstProduct.ReservedQuantity);
        Assert.Equal(0, secondProduct.ReservedQuantity);
    }

    [Fact]
    public void SubmitRejectsInsufficientStockBeforeAnyStockChanges()
    {
        var availableProduct = CreateProduct("AVAILABLE", stockOnHand: 10);
        var unavailableProduct = CreateProduct("UNAVAILABLE", stockOnHand: 1);
        var order = CreateOrder();
        order.AddItem(availableProduct, 2);
        order.AddItem(unavailableProduct, 2);

        Assert.Throws<InvalidOperationException>(() => order.Submit([availableProduct, unavailableProduct]));

        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Equal(0, availableProduct.ReservedQuantity);
        Assert.Equal(0, unavailableProduct.ReservedQuantity);
    }

    [Fact]
    public void InvalidTransitionsAreRejected()
    {
        var product = CreateProduct(stockOnHand: 10);
        var order = CreateOrder();
        order.AddItem(product, 1);

        Assert.Throws<InvalidOperationException>(() => order.Approve(Guid.NewGuid()));

        order.Submit([product]);

        Assert.Throws<InvalidOperationException>(() => order.Submit([product]));
        order.Approve(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => order.Approve(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => order.Reject(Guid.NewGuid(), [product]));
    }

    [Fact]
    public void AddItemRejectsNonPositiveQuantity()
    {
        var product = CreateProduct();
        var order = CreateOrder();

        Assert.Throws<ArgumentOutOfRangeException>(() => order.AddItem(product, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => order.AddItem(product, -1));
    }

    private static SalesOrder CreateOrder() => new(Guid.NewGuid());

    private static Product CreateProduct(string sku = "SKU-001", int stockOnHand = 10) =>
        new("Product", sku, 25m, stockOnHand);
}
