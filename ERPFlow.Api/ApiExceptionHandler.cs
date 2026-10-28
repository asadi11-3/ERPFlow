using System.Data.Common;
using ERPFlow.Application.Common;
using ERPFlow.Application.ProductImages;
using Microsoft.EntityFrameworkCore;
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
            IOException or DbException or DbUpdateException or TimeoutException
                when exception is not BadHttpRequestException &&
                    (httpContext.GetEndpoint() ?? httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint)
                    ?.Metadata.GetMetadata<ProductImageEndpointMetadata>() is not null =>
                (StatusCodes.Status503ServiceUnavailable, "Product image service unavailable"),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
                (StatusCodes.Status413PayloadTooLarge, "Request payload too large"),
            ProductImageException image => image.Error switch
            {
                ProductImageError.TooLarge => (413, "Image payload too large"),
                ProductImageError.UnsupportedMediaType => (415, "Unsupported image format"),
                ProductImageError.PreconditionRequired => (428, "Image precondition required"),
                ProductImageError.PreconditionFailed => (412, "Image precondition failed"),
                ProductImageError.ProductNotFound => (404, "Product not found"),
                ProductImageError.NotFound => (404, "Product image not found"),
                _ => (400, "Invalid product image")
            },
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

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "An unhandled API exception occurred.");
        }

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == StatusCodes.Status413PayloadTooLarge
                ? "The request exceeds the allowed size."
                : status >= StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred."
                : exception.Message
        };
        if (exception is ProductImageException { Error: ProductImageError.ProductNotFound })
            problemDetails.Extensions["code"] = "product_not_found";
        else if (exception is ProductImageException { Error: ProductImageError.NotFound })
            problemDetails.Extensions["code"] = "product_image_missing";

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }
}
