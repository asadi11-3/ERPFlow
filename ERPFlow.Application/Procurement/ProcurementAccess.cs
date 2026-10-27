using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;

namespace ERPFlow.Application.Procurement;

internal static class ProcurementAccess
{
    public static async Task<AppUser> ActorAsync(IProcurementPersistence persistence, ICurrentUser current,
        CancellationToken ct, params UserRole[] roles)
    {
        var actor = await persistence.FindActorAsync(current.UserId, ct);
        if (actor is null || !roles.Contains(actor.Role))
            throw new WorkflowAuthorizationException("The persisted user is not authorized for this procurement operation.");
        return actor;
    }

    public static void Page(int page, int size, string? search = null)
    {
        if (page < 1 || size is < 1 or > 100 || search?.Length > 200)
            throw new WorkflowValidationException("Page must be positive, pageSize 1-100, and search at most 200 characters.");
    }

    public static void Id(Guid id)
    {
        if (id == Guid.Empty) throw new WorkflowValidationException("A nonempty resource ID is required.");
    }

    public static void Version(byte[]? expected, byte[] actual)
    {
        if (expected is not { Length: 8 }) throw new WorkflowValidationException("An eight-byte RowVersion is required.");
        if (!expected.SequenceEqual(actual)) throw new ConcurrencyConflictException("The resource changed. Reload before retrying.");
    }

    public static T Domain<T>(Func<T> operation)
    {
        try { return operation(); }
        catch (InvalidOperationException exception) { throw new WorkflowConflictException(exception.Message, exception); }
        catch (OverflowException) { throw new WorkflowValidationException("Procurement arithmetic exceeds supported bounds."); }
    }
}
