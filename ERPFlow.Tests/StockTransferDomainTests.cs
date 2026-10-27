using System.Text.RegularExpressions;
using ERPFlow.Domain.Transfers;
using Xunit;

namespace ERPFlow.Tests;

public sealed class StockTransferDomainTests
{
    private static StockTransfer Draft(int quantity = 1) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        [(Guid.NewGuid(), quantity)]);

    [Fact]
    public void TransferRequiresDifferentNonemptyWarehousesAndActor()
    {
        var warehouse = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var lines = new[] { (Guid.NewGuid(), 1) };

        Assert.Throws<ArgumentException>(() => new StockTransfer(warehouse, warehouse, actor, lines));
        Assert.Throws<ArgumentException>(() => new StockTransfer(Guid.Empty, Guid.NewGuid(), actor, lines));
        Assert.Throws<ArgumentException>(() => new StockTransfer(Guid.NewGuid(), Guid.Empty, actor, lines));
        Assert.Throws<ArgumentException>(() => new StockTransfer(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, lines));
    }

    [Fact]
    public void TransferRequiresDistinctNonemptyProducts()
    {
        var product = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => New([(product, 1), (product, 2)]));
        Assert.Throws<ArgumentException>(() => New([(Guid.Empty, 1)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TransferRequiresPositiveQuantity(int quantity) => Assert.Throws<ArgumentException>(() => Draft(quantity));

    [Fact]
    public void TransferRequiresOneToOneHundredLines()
    {
        Assert.Throws<ArgumentException>(() => New([]));
        Assert.Equal(100, New(Enumerable.Range(0, 100).Select(_ => (Guid.NewGuid(), 1)).ToArray()).Items.Count);
        Assert.Throws<ArgumentException>(() => New(Enumerable.Range(0, 101).Select(_ => (Guid.NewGuid(), 1)).ToArray()));
    }

    [Fact]
    public void PostedTransferIsTerminalAndAttributable()
    {
        var transfer = Draft();
        var actor = Guid.NewGuid();

        transfer.Post(actor);

        Assert.Equal(StockTransferStatus.Posted, transfer.Status);
        Assert.Equal(actor, transfer.PostedByUserId);
        Assert.NotNull(transfer.PostedAt);
        Assert.Throws<InvalidOperationException>(() => transfer.Post(actor));
        Assert.Throws<InvalidOperationException>(() => transfer.Cancel(actor, "No reversals"));
    }

    [Fact]
    public void CancelledTransferIsTerminalWithNormalizedReason()
    {
        var transfer = Draft();
        var actor = Guid.NewGuid();

        transfer.Cancel(actor, "  Wrong paperwork  ");

        Assert.Equal(StockTransferStatus.Cancelled, transfer.Status);
        Assert.Equal(actor, transfer.CancelledByUserId);
        Assert.NotNull(transfer.CancelledAt);
        Assert.Equal("Wrong paperwork", transfer.CancellationReason);
        Assert.Throws<InvalidOperationException>(() => transfer.Post(actor));
        Assert.Throws<InvalidOperationException>(() => transfer.Cancel(actor, "Again"));
    }

    [Fact]
    public void CancellationReasonIsRequiredAndBounded()
    {
        Assert.Throws<ArgumentException>(() => Draft().Cancel(Guid.NewGuid(), " "));
        Assert.Throws<ArgumentException>(() => Draft().Cancel(Guid.NewGuid(), new string('x', 1001)));
        Assert.Throws<ArgumentException>(() => Draft().Cancel(Guid.NewGuid(), "invalid\nreason"));
    }

    [Fact]
    public void NumberAndLinesAreNormalizedWithoutMutatingInput()
    {
        var product = Guid.NewGuid();
        var input = new List<(Guid ProductId, int Quantity)> { (product, 3) };
        var transfer = new StockTransfer(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), input);

        input[0] = (Guid.NewGuid(), 9);
        input.Clear();

        Assert.Matches(new Regex("^TRF-[0-9]{8}-[0-9a-f]{32}$", RegexOptions.CultureInvariant), transfer.Number);
        Assert.True(transfer.Number.Length <= 48);
        var item = Assert.Single(transfer.Items);
        Assert.Equal((product, 3), (item.ProductId, item.Quantity));
        Assert.Throws<NotSupportedException>(() => ((IList<StockTransferItem>)transfer.Items).Clear());
    }

    private static StockTransfer New(IReadOnlyCollection<(Guid ProductId, int Quantity)> items) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), items);
}
