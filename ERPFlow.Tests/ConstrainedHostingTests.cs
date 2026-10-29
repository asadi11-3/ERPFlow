using ERPFlow.Api;
using ERPFlow.Application.ProductImages;
using ERPFlow.Infrastructure.ProductImages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ConstrainedHostingTests
{
    [Fact]
    public void AdmissionCapsFourRequestsAndOneImageWithoutWaiting()
    {
        using var admission = new ConstrainedRequestAdmission(() => 0);
        using var image = admission.TryAcquire(true);
        Assert.NotNull(image);
        Assert.Null(admission.TryAcquire(true));
        var others = Enumerable.Range(0, 3).Select(_ => admission.TryAcquire(false)).ToArray();
        Assert.All(others, lease => Assert.NotNull(lease));
        Assert.Null(admission.TryAcquire(false));
        foreach (var lease in others) { lease!.Dispose(); lease.Dispose(); }
        image.Dispose();
        using var recovered = admission.TryAcquire(true);
        Assert.NotNull(recovered);
    }

    [Fact]
    public void AdmissionRefusesMemoryPressureAndResumesWhenPressureFalls()
    {
        var bytes = ConstrainedRequestAdmission.MemoryAdmissionThresholdBytes;
        using var admission = new ConstrainedRequestAdmission(() => bytes);
        Assert.Null(admission.TryAcquire(false));
        bytes--;
        using var lease = admission.TryAcquire(false);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task OverloadReturnsSafe503ButLivenessStillRuns()
    {
        using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        using var admission = new ConstrainedRequestAdmission(() => ConstrainedRequestAdmission.MemoryAdmissionThresholdBytes);
        var ran = false;
        var middleware = new ConstrainedRequestMiddleware(_ => { ran = true; return Task.CompletedTask; }, admission);
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/products";
        await middleware.InvokeAsync(context);
        Assert.False(ran);
        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("5", context.Response.Headers.RetryAfter);
        context.Request.Path = "/health/live";
        await middleware.InvokeAsync(context);
        Assert.True(ran);
    }

    [Fact]
    public async Task FailedRequestReleasesImageAdmission()
    {
        using var admission = new ConstrainedRequestAdmission(() => 0);
        var middleware = new ConstrainedRequestMiddleware(_ => throw new InvalidOperationException("owned test"), admission);
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ProductImageEndpointMetadata()), "image"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
        using var recovered = admission.TryAcquire(true);
        Assert.NotNull(recovered);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "png", "image/png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "jpg", "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Webp, "webp", "image/webp")]
    public async Task SmallHostImageLimitAcceptsBoundaryAndRejectsLargerPixelBufferBeforeDecode(SKEncodedImageFormat format, string extension, string mime)
    {
        var normalizer = new SkiaProductImageNormalizer(SkiaProductImageNormalizer.SmallHostMaxPixels);
        using var accepted = new MemoryStream(ProductImageTestData.Image(format, width: 2000, height: 1000));
        var image = await normalizer.NormalizeAsync(accepted, "owned." + extension, mime);
        Assert.Equal((2000, 1000), (image.Width, image.Height));
        using var rejected = new MemoryStream(ProductImageTestData.Image(format, width: 2001, height: 1000));
        var error = await Assert.ThrowsAsync<ProductImageException>(() => normalizer.NormalizeAsync(rejected, "owned." + extension, mime));
        Assert.Equal(ProductImageError.TooLarge, error.Error);
    }

    [Fact]
    public async Task HostingPrivateSiblingIsUsableButPrivateInsideContentIsRefused()
    {
        var owned = Path.Combine(Path.GetTempPath(), "ERPFlow-PrivateLayout-" + Guid.NewGuid().ToString("N"));
        var siteContent = Path.Combine(owned, "wwwroot");
        var privateRoot = Path.Combine(owned, "private", "ProductImages");
        try
        {
            Directory.CreateDirectory(siteContent);
            var storage = new LocalProductImageStorage(new() { RootPath = privateRoot, ForbiddenRoots = [siteContent] });
            await storage.ProbeAsync();
            var key = Guid.NewGuid().ToString("N");
            await storage.WriteAsync(key, new byte[] { 1, 2, 3 });
            var reopened = new LocalProductImageStorage(new() { RootPath = privateRoot, ForbiddenRoots = [siteContent] });
            Assert.Equal(storage.StoreIdentity, reopened.StoreIdentity);
            Assert.Equal(new byte[] { 1, 2, 3 }, await reopened.ReadAsync(key));
            Assert.Throws<InvalidOperationException>(() => new LocalProductImageStorage(new() { RootPath = privateRoot, ForbiddenRoots = [owned] }));
        }
        finally { if (Directory.Exists(owned)) Directory.Delete(owned, true); }
    }
}
