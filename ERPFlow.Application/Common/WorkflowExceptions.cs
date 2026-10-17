namespace ERPFlow.Application.Common;

public sealed class EntityNotFoundException(string entityName, Guid id)
    : Exception($"{entityName} '{id}' was not found.");

public sealed class WorkflowValidationException(string message) : Exception(message);

public sealed class WorkflowAuthorizationException(string message) : Exception(message);

public sealed class WorkflowConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class ConcurrencyConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class DuplicateReservationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
