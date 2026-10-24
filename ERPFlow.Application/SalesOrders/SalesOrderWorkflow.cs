using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;

namespace ERPFlow.Application.SalesOrders;

public sealed class SalesOrderWorkflow(
    ISalesOrderPersistence persistence,
    ICurrentUser currentUser) : ISalesOrderWorkflow
{
    public async Task<SalesOrderPage> ListAsync(
        int page,
        int pageSize,
        OrderStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 ||
            (status.HasValue && !Enum.IsDefined(status.Value)))
        {
            throw new WorkflowValidationException("Page must be positive, pageSize must be 1-100, and status must be a defined OrderStatus value.");
        }

        var actor = await GetActorAsync(cancellationToken);
        EnsureRole(actor, UserRole.Sales, UserRole.Approver, UserRole.Administrator);
        return await persistence.ListOrdersAsync(
            actor.Role == UserRole.Sales ? actor.Id : null, page, pageSize, status, cancellationToken);
    }

    public async Task<SalesOrderResult> CreateDraftAsync(
        CreateSalesOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCreateCommand(command);

        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        var actor = await GetActorAsync(cancellationToken);
        EnsureRole(actor, UserRole.Sales, UserRole.Administrator);
        var customer = await persistence.FindCustomerAsync(command.CustomerId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Customer), command.CustomerId);
        if (!customer.IsActive)
        {
            throw new WorkflowConflictException("Sales orders can only be created for active customers.");
        }

        var productIds = command.Items.Select(item => item.ProductId).ToArray();
        var products = await persistence.GetProductsAsync(productIds, cancellationToken);
        EnsureAllProductsFound(productIds, products);

        var productsById = products.ToDictionary(product => product.Id);
        var order = new SalesOrder(customer.Id, actor.Id);
        foreach (var item in command.Items)
        {
            order.AddItem(productsById[item.ProductId], item.Quantity);
        }

        persistence.AddOrder(order);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResult(order);
    }

    public async Task<SalesOrderResult> SubmitAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        var order = await GetTrackedOrderAsync(orderId, cancellationToken);
        var actor = await GetActorAsync(cancellationToken);
        EnsureRole(actor, UserRole.Sales, UserRole.Administrator);
        EnsureOwner(order, actor);
        var products = await persistence.GetProductsAsync(
            order.Items.Select(item => item.ProductId).ToArray(),
            cancellationToken);

        try
        {
            order.Submit(products);
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkflowConflictException(exception.Message, exception);
        }

        persistence.AddApprovalHistory(
            new ApprovalHistory(order.Id, actor.Id, ApprovalAction.Submitted, "Submitted for approval."));
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResult(order);
    }

    public async Task<SalesOrderResult> ApproveAsync(
        Guid orderId,
        string? comments = null,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        var order = await GetTrackedOrderAsync(orderId, cancellationToken);
        var actor = await GetActorAsync(cancellationToken);
        EnsureRole(actor, UserRole.Approver, UserRole.Administrator);

        if (order.TotalAmount > 10000m && actor.Role != UserRole.Administrator)
        {
            throw new WorkflowAuthorizationException("Orders exceeding $10,000 require Administrator approval.");
        }

        ApprovalHistory history;
        try
        {
            history = order.Approve(actor.Id, comments);
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkflowConflictException(exception.Message, exception);
        }

        persistence.AddApprovalHistory(history);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResult(order);
    }

    public async Task<SalesOrderResult> RejectAsync(
        Guid orderId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new WorkflowValidationException("A rejection reason is required.");
        }

        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        var order = await GetTrackedOrderAsync(orderId, cancellationToken);
        var actor = await GetActorAsync(cancellationToken);
        EnsureRole(actor, UserRole.Approver, UserRole.Administrator);
        var products = await persistence.GetProductsAsync(
            order.Reservations.Select(reservation => reservation.ProductId).ToArray(),
            cancellationToken);

        ApprovalHistory history;
        try
        {
            history = order.Reject(actor.Id, products, reason);
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkflowConflictException(exception.Message, exception);
        }

        persistence.AddApprovalHistory(history);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResult(order);
    }

    public async Task<SalesOrderResult> ReturnAsync(
        Guid orderId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new WorkflowValidationException("A reason is required when returning an order for correction.");
        }

        await using var transaction = await persistence.BeginTransactionAsync(cancellationToken);
        var order = await GetTrackedOrderAsync(orderId, cancellationToken);
        var actor = await GetActorAsync(cancellationToken);
        EnsureRole(actor, UserRole.Approver, UserRole.Administrator);
        var products = await persistence.GetProductsAsync(
            order.Reservations.Select(reservation => reservation.ProductId).ToArray(),
            cancellationToken);

        ApprovalHistory history;
        try
        {
            history = order.ReturnForCorrection(actor.Id, products, reason);
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkflowConflictException(exception.Message, exception);
        }

        persistence.AddApprovalHistory(history);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResult(order);
    }

    public async Task<SalesOrderResult> GetByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await persistence.GetOrderAsync(orderId, false, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SalesOrder), orderId);
        var actor = await GetActorAsync(cancellationToken);
        EnsureOwner(order, actor);
        return ToResult(order);
    }

    public async Task<IReadOnlyList<ApprovalHistoryResult>> GetApprovalHistoryAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var ownerId = await persistence.FindOrderOwnerIdAsync(orderId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SalesOrder), orderId);
        var actor = await GetActorAsync(cancellationToken);
        EnsureOwner(ownerId, actor);
        var history = await persistence.ListApprovalHistoryAsync(orderId, cancellationToken);
        return history.Select(entry => new ApprovalHistoryResult(
            entry.Action,
            entry.UserId,
            DateTime.SpecifyKind(entry.CreatedAt, DateTimeKind.Utc),
            entry.Comments)).ToArray();
    }

    public async Task<IReadOnlyList<SalesOrderResult>> ListPendingApprovalsAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        EnsureRole(actor, UserRole.Approver, UserRole.Administrator);
        var orders = await persistence.ListPendingOrdersAsync(cancellationToken);
        return orders.Select(ToResult).ToArray();
    }

    private async Task<SalesOrder> GetTrackedOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
        {
            throw new WorkflowValidationException("A sales order ID is required.");
        }

        return await persistence.GetOrderAsync(orderId, true, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SalesOrder), orderId);
    }

    private async Task<AppUser> GetActorAsync(CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId;
        var actor = actorId == Guid.Empty
            ? null
            : await persistence.FindAppUserAsync(actorId, cancellationToken);
        if (actor is null)
        {
            throw new EntityNotFoundException(nameof(AppUser), actorId);
        }

        return actor;
    }

    private static void EnsureOwner(SalesOrder order, AppUser actor)
    {
        EnsureOwner(order.CreatedByUserId, actor);
    }

    private static void EnsureOwner(Guid ownerId, AppUser actor)
    {
        if (actor.Role == UserRole.Sales && ownerId != actor.Id)
        {
            throw new WorkflowAuthorizationException("Sales users can only access their own orders.");
        }
    }

    private static void EnsureRole(AppUser actor, params UserRole[] allowedRoles)
    {
        if (!allowedRoles.Contains(actor.Role))
        {
            throw new WorkflowAuthorizationException("The current user is not authorized for this operation.");
        }
    }

    private static void ValidateCreateCommand(CreateSalesOrderCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CustomerId == Guid.Empty)
        {
            throw new WorkflowValidationException("A customer ID is required.");
        }

        if (command.Items is null || command.Items.Count == 0)
        {
            throw new WorkflowValidationException("At least one order item is required.");
        }

        var productIds = new HashSet<Guid>();
        foreach (var item in command.Items)
        {
            if (item.ProductId == Guid.Empty)
            {
                throw new WorkflowValidationException("Every order item requires a product ID.");
            }

            if (item.Quantity <= 0)
            {
                throw new WorkflowValidationException("Order item quantities must be greater than zero.");
            }

            if (!productIds.Add(item.ProductId))
            {
                throw new WorkflowValidationException("An order cannot contain duplicate products.");
            }
        }
    }

    private static void EnsureAllProductsFound(
        IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<Product> products)
    {
        if (products.Count == productIds.Count)
        {
            return;
        }

        var foundIds = products.Select(product => product.Id).ToHashSet();
        var missingId = productIds.First(id => !foundIds.Contains(id));
        throw new EntityNotFoundException(nameof(Product), missingId);
    }

    private static SalesOrderResult ToResult(SalesOrder order) =>
        new(
            order.Id,
            order.CustomerId,
            order.CreatedByUserId,
            order.Status,
            order.CreatedAt,
            order.SubmittedAt,
            order.DecidedAt,
            order.TotalAmount,
            order.Items.Select(item => new SalesOrderItemResult(
                item.Id,
                item.ProductId,
                item.Quantity,
                item.UnitPrice,
                item.LineTotal)).ToArray(),
            order.Reservations.Select(reservation => new StockReservationResult(
                reservation.Id,
                reservation.ProductId,
                reservation.Quantity,
                reservation.IsReleased,
                reservation.CreatedAt,
                reservation.ReleasedAt)).ToArray());
}
