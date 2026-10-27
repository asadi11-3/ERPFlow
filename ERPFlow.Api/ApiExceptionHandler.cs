using System.Data.Common;
using ERPFlow.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ERPFlow.Api;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.RequestAborted.IsCancellationRequested &&
            exception is OperationCanceledException or DbException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        var (status, title) = exception switch
        {
            WorkflowValidationException or ArgumentException or BadHttpRequestException =>
                (StatusCodes.Status400BadRequest, "Request validation failed"),
            WorkflowAuthorizationException =>
                (StatusCodes.Status403Forbidden, "Access denied"),
            EntityNotFoundException =>
                (StatusCodes.Status404NotFound, "Resource not found"),
            WorkflowConflictException or ConcurrencyConflictException or DuplicateReservationException =>
                (StatusCodes.Status409Conflict, "Workflow conflict"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "An unhandled API exception occurred.");
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status == StatusCodes.Status500InternalServerError
                    ? "An unexpected error occurred."
                    : exception.Message
            },
            Exception = exception
        });
    }
}
