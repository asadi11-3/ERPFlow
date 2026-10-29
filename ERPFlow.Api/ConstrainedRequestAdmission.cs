using System.Diagnostics;

namespace ERPFlow.Api;

public sealed class ConstrainedRequestAdmission : IDisposable
{
    public const long MemoryAdmissionThresholdBytes = 192L * 1024 * 1024;
    private readonly SemaphoreSlim requests = new(4, 4);
    private readonly SemaphoreSlim images = new(1, 1);
    private readonly Func<long> privateBytes;

    public ConstrainedRequestAdmission() : this(() =>
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }) { }

    public ConstrainedRequestAdmission(Func<long> privateBytes) => this.privateBytes = privateBytes;

    public IDisposable? TryAcquire(bool image)
    {
        if (privateBytes() >= MemoryAdmissionThresholdBytes || !requests.Wait(0)) return null;
        if (image && !images.Wait(0)) { requests.Release(); return null; }
        return new Lease(requests, image ? images : null);
    }

    public void Dispose() { requests.Dispose(); images.Dispose(); }

    private sealed class Lease(SemaphoreSlim requests, SemaphoreSlim? images) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            images?.Release();
            requests.Release();
        }
    }
}

public sealed class ConstrainedRequestMiddleware(RequestDelegate next, ConstrainedRequestAdmission admission)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals("/health/live", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }
        using var lease = admission.TryAcquire(context.GetEndpoint()?.Metadata.GetMetadata<ProductImageEndpointMetadata>() is not null);
        if (lease is null)
        {
            context.Response.Headers.RetryAfter = "5";
            await Results.Problem(statusCode: 503, title: "Service temporarily busy",
                detail: "Retry after the indicated delay.").ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}
