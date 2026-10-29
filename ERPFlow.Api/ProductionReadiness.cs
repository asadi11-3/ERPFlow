using ERPFlow.Application.ProductImages;
using ERPFlow.Infrastructure.Deployment;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.ProductImages;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace ERPFlow.Api;

public sealed class ProductionReadiness(IServiceScopeFactory scopes, TimeProvider clock)
{
    private readonly SemaphoreSlim mutex = new(1, 1);
    private DateTimeOffset expires;
    private bool ready;

    public async Task<bool> CheckAsync(CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            if (clock.GetUtcNow() < expires) return ready;
            ready = false;
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
            await new DatabaseSchemaVerifier(context).VerifyAsync(ct);
            await VerifyImagesAsync(context, scope.ServiceProvider.GetRequiredService<IProductImageStorage>(), ct);
            ready = await scope.ServiceProvider.GetRequiredService<ProductionInventoryAdmission>().IsReadyAsync(context, ct);
            return ready;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
        finally { expires = clock.GetUtcNow().AddSeconds(15); mutex.Release(); }
    }

    public static async Task VerifyImagesAsync(ERPFlowDbContext context, IProductImageStorage storage, CancellationToken ct,
        Action<ProductionStartupStage>? onStage = null)
    {
        onStage?.Invoke(ProductionStartupStage.ImageStorage);
        if (storage is not LocalProductImageStorage local) throw new InvalidOperationException("Production storage adapter is incompatible.");
        await local.ProbeAsync(ct);
        onStage?.Invoke(ProductionStartupStage.ImageBinding);
        var binding = await context.ProductImageStores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (binding is not null && binding.Identity != storage.StoreIdentity ||
            binding is null && (await context.ProductImages.AnyAsync(ct) || await context.ProductImageObjects.AnyAsync(ct)))
            throw new InvalidOperationException("The image store binding requires an approved recovery procedure.");
        onStage?.Invoke(ProductionStartupStage.ImageCodec);
        using var bitmap = new SKBitmap(1, 1);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var decoded = SKBitmap.Decode(encoded);
        if (decoded is null || decoded.Width != 1) throw new InvalidOperationException("The native image codec is unavailable.");
    }
}
