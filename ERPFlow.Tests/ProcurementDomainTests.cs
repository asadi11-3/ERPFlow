using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProcurementDomainTests
{
    [Theory]
    [InlineData("", "Supplier")]
    [InlineData("BAD CODE", "Supplier")]
    [InlineData("OK", " ")]
    public void InvalidSupplierIdentityIsRejected(string code, string name) =>
        Assert.Throws<ArgumentException>(() => new Supplier(code, name));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SupplierFieldBoundariesAreEnforced(int field)
    {
        Assert.Throws<ArgumentException>(() => new Supplier(field == 0 ? new string('x', 33) : "SUP",
            field == 1 ? new string('n', 201) : "Supplier", field == 2 ? new string('c', 151) : null,
            field == 3 ? "not-an-email" : null, field == 4 ? new string('p', 51) : null, field == 5 ? new string('a', 1001) : null));
    }

    [Fact]
    public void SupplierNormalizesIdentityAndRetainsItThroughEdits()
    {
        var supplier = new Supplier(" sup-01 ", "Supplier", "Contact", "contact@example.test");
        var id = supplier.Id;
        supplier.Edit("Renamed", null, null, "123", "Address");
        supplier.SetActive(false);
        Assert.Equal((id, "SUP-01", "Renamed", false), (supplier.Id, supplier.SupplierCode, supplier.Name, supplier.IsActive));
    }

    [Fact]
    public void LegacyDefaultCannotBeDeactivated() => Assert.Throws<InvalidOperationException>(() =>
        new Warehouse(LegacyDefaultWarehouse.Code, "Protected").SetActive(false));

    [Fact]
    public void PurchaseOrderTotalsAndCommercialTermsAreIndependentOfSalesPrices()
    {
        var creator = Guid.NewGuid();
        var order = new PurchaseOrder(Guid.NewGuid(), Guid.NewGuid(), creator,
            [(Guid.NewGuid(), 3, 1.2345m), (Guid.NewGuid(), 2, 0m)]);
        var number = order.PONumber;
        var submitted = order.Submit(creator);
        var approved = order.Approve(Guid.NewGuid(), "Approved");
        Assert.Equal(3.7035m, order.TotalAmount);
        Assert.Equal(number, order.PONumber);
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.Equal((PurchaseOrderStatus.Draft, PurchaseOrderStatus.PendingApproval), (submitted.FromStatus, submitted.ToStatus));
        Assert.Equal(PurchaseOrderAction.Approved, approved.Action);
        Assert.Throws<InvalidOperationException>(() => order.Edit(order.SupplierId, order.WarehouseId, [(Guid.NewGuid(), 1, 1m)], null));
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(-1, "1")]
    [InlineData(1, "-0.01")]
    [InlineData(1, "0.00001")]
    [InlineData(1, "1000000000000")]
    public void InvalidPurchaseQuantitiesAndCostsAreRejected(int quantity, string cost) => Assert.Throws<ArgumentException>(() =>
        new PurchaseOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [(Guid.NewGuid(), quantity, decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture))]));

    [Fact]
    public void DuplicateProductsAndExcessiveLineCountsAreRejected()
    {
        var product = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new PurchaseOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [(product, 1, 1m), (product, 2, 2m)]));
        Assert.Throws<ArgumentException>(() => new PurchaseOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Enumerable.Range(0, 101).Select(_ => (Guid.NewGuid(), 1, 1m)).ToArray()));
    }

    [Fact]
    public void BoundedMaximumCommercialTotalFitsWithoutIntegerOverflow()
    {
        var order = new PurchaseOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Enumerable.Range(0, 100).Select(_ => (Guid.NewGuid(), int.MaxValue, PurchaseOrderItem.MaxUnitCost)).ToArray());
        Assert.Equal(214748364699999978525163.5300m, order.TotalAmount);
    }

    [Fact]
    public void SelfDecisionAndTerminalReentryAreRejected()
    {
        var creator = Guid.NewGuid();
        var order = new PurchaseOrder(Guid.NewGuid(), Guid.NewGuid(), creator, [(Guid.NewGuid(), 1, 0m)]);
        order.Submit(creator);
        Assert.Throws<InvalidOperationException>(() => order.Approve(creator, null));
        Assert.Throws<ArgumentException>(() => order.Reject(Guid.NewGuid(), " "));
        order.Reject(Guid.NewGuid(), "Rejected");
        Assert.Throws<InvalidOperationException>(() => order.Submit(creator));
        Assert.Throws<InvalidOperationException>(() => order.Cancel(creator, "Cannot cancel rejection"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreceivedDraftOrApprovedOrderCanBeCancelled(bool approved)
    {
        var creator = Guid.NewGuid();
        var order = new PurchaseOrder(Guid.NewGuid(), Guid.NewGuid(), creator, [(Guid.NewGuid(), 1, 0m)]);
        if (approved) { order.Submit(creator); order.Approve(Guid.NewGuid(), null); }
        var history = order.Cancel(creator, "No longer needed");
        Assert.Equal(PurchaseOrderStatus.Cancelled, order.Status);
        Assert.Equal(PurchaseOrderAction.Cancelled, history.Action);
    }

    [Fact]
    public void ExistingRoleNumbersRemainCompatible()
    {
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, Enum.GetValues<UserRole>().Select(x => (int)x));
        Assert.Equal(UserRole.Technician, (UserRole)3);
    }
}
