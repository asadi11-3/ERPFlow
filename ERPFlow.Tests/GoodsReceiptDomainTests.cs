using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
using Xunit;

namespace ERPFlow.Tests;

public sealed class GoodsReceiptDomainTests
{
    private static GoodsReceipt Draft(int quantity = 1) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        [(Guid.NewGuid(), Guid.NewGuid(), quantity)]);
    [Fact]
    public void PostedReceiptIsTerminalAndAttributable()
    {
        var receipt = Draft();
        var actor = Guid.NewGuid();
        receipt.Post(actor);
        Assert.Equal(GoodsReceiptStatus.Posted, receipt.Status);
        Assert.Equal(actor, receipt.PostedByUserId);
        Assert.NotNull(receipt.PostedAt);
        Assert.Throws<InvalidOperationException>(() => receipt.Post(actor));
        Assert.Throws<InvalidOperationException>(() => receipt.Cancel(actor, "No reversals"));
    }
    [Fact]
    public void DraftCanBeCancelledButNotPostedAfterCancellation()
    {
        var receipt = Draft();
        var actor = Guid.NewGuid();
        receipt.Cancel(actor, "Wrong paperwork");
        Assert.Equal(GoodsReceiptStatus.Cancelled, receipt.Status);
        Assert.Equal(actor, receipt.CancelledByUserId);
        Assert.Throws<InvalidOperationException>(() => receipt.Post(actor));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidReceiptQuantitiesAreRejected(int quantity) => Assert.Throws<ArgumentException>(() => Draft(quantity));
    [Fact]
    public void DuplicateLinesAndOversizedReferencesAreRejected()
    {
        var line = (Guid.NewGuid(), Guid.NewGuid(), 1);
        Assert.Throws<ArgumentException>(() => new GoodsReceipt(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [line, line]));
        Assert.Throws<ArgumentException>(() => new GoodsReceipt(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [line], new string('x', 101)));
    }
    [Fact]
    public void ReceiptKindCanStartAStockLedgerWithoutInventingAnOpening()
    {
        var stock = new WarehouseStock(Guid.NewGuid(), Guid.NewGuid());
        var first = stock.Apply(InventoryPostingKind.Receipt, 3);
        var second = stock.Apply(InventoryPostingKind.Receipt, 4);
        Assert.Equal((7, 0, 2L), (stock.StockOnHand, stock.ReservedQuantity, stock.Sequence));
        Assert.Equal((0, 3), (first.BeforeOnHand, first.AfterOnHand));
        Assert.Equal((3, 7), (second.BeforeOnHand, second.AfterOnHand));
        Assert.Throws<InvalidOperationException>(() => stock.Apply(InventoryPostingKind.Opening, 1));
    }
    [Fact]
    public void ReceiptOverflowCannotInflateOrWrapStock()
    {
        var stock = new WarehouseStock(Guid.NewGuid(), Guid.NewGuid());
        stock.Apply(InventoryPostingKind.Receipt, int.MaxValue);
        Assert.Throws<OverflowException>(() => stock.Apply(InventoryPostingKind.Receipt, 1));
        Assert.Equal(int.MaxValue, stock.StockOnHand);
        Assert.Equal(1, stock.Sequence);
    }
    [Fact]
    public void OnlyApprovedPoCanBeValidatedForReceipt()
    {
        var creator = Guid.NewGuid();
        var po = new PurchaseOrder(Guid.NewGuid(), Guid.NewGuid(), creator, [(Guid.NewGuid(), 5, 1m)]);
        var line = po.Items.Single();
        Assert.Throws<InvalidOperationException>(() => po.ValidateReceipt([(line.Id, 1)]));
        po.Submit(creator); po.Approve(Guid.NewGuid(), null);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, po.ValidateReceipt([(line.Id, 2)]));
        Assert.Equal(PurchaseOrderStatus.Received, po.ValidateReceipt([(line.Id, 5)]));
        Assert.Throws<InvalidOperationException>(() => po.ValidateReceipt([(line.Id, 6)]));
    }
}
