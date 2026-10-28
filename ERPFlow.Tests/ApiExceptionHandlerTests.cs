using System.Data.Common;
using ERPFlow.Api;
using ERPFlow.Application.ProductImages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ApiExceptionHandlerTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task CancellationRequiresAnAbortedRequest(bool aborted, bool databaseException)
    {
        using var cancellation = new CancellationTokenSource();
        if (aborted) cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var problems = new RecordingProblemDetailsService();
        var handler = new ApiExceptionHandler(problems, NullLogger<ApiExceptionHandler>.Instance);
        Exception exception = databaseException ? new CommandFailure() : new OperationCanceledException();

        Assert.True(await handler.TryHandleAsync(context, exception, cancellation.Token));

        Assert.Equal(aborted ? StatusCodes.Status499ClientClosedRequest : StatusCodes.Status500InternalServerError,
            context.Response.StatusCode);
        Assert.Equal(!aborted, problems.Context is not null);
        if (!aborted)
            Assert.Equal("An unexpected error occurred.", problems.Context!.ProblemDetails.Detail);
    }

    [Theory]
    [InlineData(ProductImageError.NotFound, "product_image_missing", "Product image not found")]
    [InlineData(ProductImageError.ProductNotFound, "product_not_found", "Product not found")]
    public async Task ProductImageAbsenceHasStableProblemCode(
        ProductImageError error,
        string code,
        string title)
    {
        var context = new DefaultHttpContext();
        var problems = new RecordingProblemDetailsService();
        var handler = new ApiExceptionHandler(problems, NullLogger<ApiExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context,
            new ProductImageException(error, "Distinct absence detail."), CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(title, problems.Context!.ProblemDetails.Title);
        Assert.Equal(code, problems.Context.ProblemDetails.Extensions["code"]);
    }

    private sealed class CommandFailure : DbException;

    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Context { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Context = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Context = context;
            return ValueTask.FromResult(true);
        }
    }
}
