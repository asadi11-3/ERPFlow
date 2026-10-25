using System.Collections.ObjectModel;
using ERPFlow.Domain.Enums;

namespace ERPFlow.Domain.Entities;

public sealed class SalesOrder
{
    private readonly List<SalesOrderItem> _items = [];
    private readonly List<StockReservation> _reservations = [];
    private readonly ReadOnlyCollection<SalesOrderItem> _readOnlyItems;
    private readonly ReadOnlyCollection<StockReservation> _readOnlyReservations;

    public SalesOrder(Guid customerId, Guid createdByUserId)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("A customer ID is required.", nameof(customerId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("A creator user ID is required.", nameof(createdByUserId));
        }

        Id = Guid.NewGuid();
        CustomerId = customerId;
        CreatedByUserId = createdByUserId;
        Status = OrderStatus.Draft;
        CreatedAt = DateTime.UtcNow;
        _readOnlyItems = _items.AsReadOnly();
        _readOnlyReservations = _reservations.AsReadOnly();
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? SubmittedAt { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    public IReadOnlyList<SalesOrderItem> Items => _readOnlyItems;

    public IReadOnlyList<StockReservation> Reservations => _reservations.Where(reservation => !reservation.IsHistorical).ToArray();

    public IReadOnlyList<StockReservation> ReservationHistory => _readOnlyReservations;

    public decimal TotalAmount => _items.Sum(item => item.LineTotal);

    public byte[] RowVersion { get; private set; } = [];

    public SalesOrderItem AddItem(Product product, int quantity, Guid? warehouseId = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        EnsureDraft();

        if (_items.Any(item => item.ProductId == product.Id))
        {
            throw new InvalidOperationException("An order cannot contain duplicate products.");
        }

        var item = new SalesOrderItem(Id, product.Id, quantity, product.UnitPrice, warehouseId);
        _items.Add(item);
        return item;
    }

    public void RemoveItem(Guid productId)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("A product ID is required.", nameof(productId));
        }

        EnsureDraft();
        var item = _items.SingleOrDefault(candidate => candidate.ProductId == productId)
            ?? throw new InvalidOperationException("The product is not present in the order.");
        _items.Remove(item);
    }

    public void EditDraft(Guid customerId, IReadOnlyCollection<(Product Product, int Quantity)> lines) =>
        EditDraft(customerId, lines.Select(line => (line.Product, line.Quantity,
            _items.SingleOrDefault(item => item.ProductId == line.Product.Id)?.WarehouseId)).ToArray());

    public void EditDraft(Guid customerId, IReadOnlyCollection<(Product Product, int Quantity, Guid? WarehouseId)> lines)
    {
        EnsureDraft();
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("A customer ID is required.", nameof(customerId));
        }

        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Select(line => line.Product.Id).Distinct().Count() != lines.Count)
        {
            throw new InvalidOperationException("An order requires at least one line and unique products.");
        }

        if (_reservations.Any(reservation => !reservation.IsReleased))
        {
            throw new InvalidOperationException("A draft cannot be edited while stock is reserved.");
        }

        var replacements = lines.Select(line =>
            new SalesOrderItem(Id, line.Product.Id, line.Quantity, line.Product.UnitPrice, line.WarehouseId)).ToArray();
        var productIds = replacements.Select(item => item.ProductId).ToHashSet();
        _items.RemoveAll(item => !productIds.Contains(item.ProductId));
        foreach (var replacement in replacements)
        {
            var existing = _items.SingleOrDefault(item => item.ProductId == replacement.ProductId);
            if (existing is null)
            {
                _items.Add(replacement);
            }
            else
            {
                existing.Revise(replacement.Quantity, replacement.UnitPrice, replacement.WarehouseId);
            }
        }

        CustomerId = customerId;
    }

    public void Submit(IReadOnlyCollection<Product> products)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(products);

        if (_items.Count == 0)
        {
            throw new InvalidOperationException("An order must contain at least one item before submission.");
        }

        var productsById = ValidateProductsForReservation(products);
        ArchiveClosedReservations();
        foreach (var item in _items)
        {
            _reservations.Add(productsById[item.ProductId].Reserve(Id, item.Quantity, item.WarehouseId));
        }

        Status = OrderStatus.PendingApproval;
        SubmittedAt = DateTime.UtcNow;
    }

    public ApprovalHistory Approve(Guid approverId, string? comments = null)
    {
        EnsurePendingApproval();
        ValidateUserId(approverId);

        var history = new ApprovalHistory(Id, approverId, ApprovalAction.Approved, comments);
        Status = OrderStatus.Approved;
        DecidedAt = history.CreatedAt;
        return history;
    }

    public ApprovalHistory Reject(
        Guid approverId,
        IReadOnlyCollection<Product> products,
        string? comments = null)
    {
        EnsurePendingApproval();
        ValidateUserId(approverId);
        ArgumentNullException.ThrowIfNull(products);
        var productsById = ValidateProductsForRelease(products);

        foreach (var reservation in Reservations)
        {
            reservation.Release(productsById[reservation.ProductId]);
        }

        var history = new ApprovalHistory(Id, approverId, ApprovalAction.Rejected, comments);
        Status = OrderStatus.Rejected;
        DecidedAt = history.CreatedAt;
        return history;
    }

    public ApprovalHistory ReturnForCorrection(
        Guid approverId,
        IReadOnlyCollection<Product> products,
        string? comments = null)
    {
        EnsurePendingApproval();
        ValidateUserId(approverId);
        ArgumentNullException.ThrowIfNull(products);
        var productsById = ValidateProductsForRelease(products);

        foreach (var reservation in Reservations)
        {
            reservation.Release(productsById[reservation.ProductId]);
        }

        var history = new ApprovalHistory(Id, approverId, ApprovalAction.Returned, comments);
        Status = OrderStatus.Draft;
        DecidedAt = null;
        return history;
    }

    public void ArchiveClosedReservations()
    {
        EnsureDraft();
        if (Reservations.Any(reservation => !reservation.IsReleased))
            throw new InvalidOperationException("Active reservations cannot be archived for resubmission.");
        foreach (var reservation in _reservations.Where(reservation => reservation.IsReleased && !reservation.IsHistorical))
        {
            reservation.Archive();
        }
    }

    public void Dispatch(IReadOnlyCollection<Product> products)
    {
        if (Status != OrderStatus.Approved)
        {
            throw new InvalidOperationException("Only approved orders can be dispatched.");
        }

        ArgumentNullException.ThrowIfNull(products);
        var productsById = ValidateProductsForDispatch(products);

        foreach (var reservation in Reservations)
        {
            reservation.Consume(productsById[reservation.ProductId]);
        }
    }

    private Dictionary<Guid, Product> ValidateProductsForReservation(IReadOnlyCollection<Product> products)
    {
        if (products.Count != _items.Count)
        {
            throw new InvalidOperationException("Exactly one product is required for each order item.");
        }

        var productsById = new Dictionary<Guid, Product>();
        foreach (var product in products)
        {
            if (product is null)
            {
                throw new ArgumentException("Product values are required.", nameof(products));
            }

            if (!productsById.TryAdd(product.Id, product))
            {
                throw new InvalidOperationException("A product can only be reserved once per order.");
            }

            var item = _items.SingleOrDefault(candidate => candidate.ProductId == product.Id)
                ?? throw new InvalidOperationException("Each product must match an order item.");
            if (item.Quantity > product.AvailableStock)
            {
                throw new InvalidOperationException("Insufficient available stock.");
            }
        }

        return productsById;
    }

    private Dictionary<Guid, Product> ValidateProductsForRelease(IReadOnlyCollection<Product> products)
    {
        if (products.Count != Reservations.Count)
        {
            throw new InvalidOperationException("Every order reservation must have one matching product.");
        }

        var productsById = new Dictionary<Guid, Product>();
        foreach (var product in products)
        {
            if (product is null)
            {
                throw new ArgumentException("Product values are required.", nameof(products));
            }

            if (!productsById.TryAdd(product.Id, product))
            {
                throw new InvalidOperationException("Each reservation must have one unique product.");
            }
        }

        foreach (var reservation in Reservations)
        {
            if (!productsById.TryGetValue(reservation.ProductId, out var product) ||
                reservation.IsReleased ||
                product.ReservedQuantity < reservation.Quantity)
            {
                throw new InvalidOperationException("Each active order reservation must match a product with sufficient reserved stock.");
            }
        }

        return productsById;
    }

    private Dictionary<Guid, Product> ValidateProductsForDispatch(IReadOnlyCollection<Product> products)
    {
        if (_items.Count == 0 || Reservations.Count != _items.Count || products.Count != _items.Count)
        {
            throw new InvalidOperationException("Every order item must have one matching active reservation and product.");
        }

        var productsById = new Dictionary<Guid, Product>();
        foreach (var product in products)
        {
            if (product is null)
            {
                throw new ArgumentException("Product values are required.", nameof(products));
            }

            if (!productsById.TryAdd(product.Id, product))
            {
                throw new InvalidOperationException("Each order item must have one unique product.");
            }
        }

        foreach (var item in _items)
        {
            var reservation = Reservations.SingleOrDefault(candidate => candidate.ProductId == item.ProductId);
            if (reservation is null ||
                reservation.SalesOrderId != Id ||
                reservation.IsReleased ||
                reservation.Quantity != item.Quantity ||
                !productsById.TryGetValue(item.ProductId, out var product) ||
                product.ReservedQuantity < reservation.Quantity ||
                product.StockOnHand < reservation.Quantity)
            {
                throw new InvalidOperationException("Every order item must have one exact active reservation backed by physical stock.");
            }
        }

        return productsById;
    }

    private void EnsureDraft()
    {
        if (Status != OrderStatus.Draft)
        {
            throw new InvalidOperationException("Order items can only be changed while the order is in Draft status.");
        }
    }

    private void EnsurePendingApproval()
    {
        if (Status != OrderStatus.PendingApproval)
        {
            throw new InvalidOperationException("The order must be pending approval.");
        }
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user ID is required.", nameof(userId));
        }
    }
}
