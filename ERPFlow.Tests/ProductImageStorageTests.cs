using ERPFlow.Infrastructure.ProductImages;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductImageStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "opencode", $"ERPFlow_ImageStorage_{Guid.NewGuid():N}");
    private LocalProductImageStorage Storage() => new(new ProductImageOptions { RootPath = root });

    [Fact]
    public async Task ImmutableObjectsRoundTripAndCannotBeOverwritten()
    {
        var storage = Storage();
        var key = Guid.NewGuid().ToString("N");
        byte[] bytes = [1, 2, 3, 4];
        await storage.WriteAsync(key, bytes);
        Assert.Equal(bytes, await storage.ReadAsync(key));
        await Assert.ThrowsAsync<IOException>(() => storage.WriteAsync(key, new byte[] { 5 }));
        Assert.Equal(bytes, await storage.ReadAsync(key));
        await storage.DeleteAsync(key);
        await storage.DeleteAsync(key);
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.ReadAsync(key));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("../../appsettings.json")]
    [InlineData("C:\\Windows\\system32\\cmd.exe")]
    [InlineData("0123456789012345678901234567890/ab")]
    public async Task StorageKeysCannotTraversePaths(string key)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Storage().WriteAsync(key, new byte[] { 1 }));
    }

    [Fact]
    public async Task IndependentStorageInstancesSerializeSameProductAndHonorCancellation()
    {
        var first = Storage();
        var second = Storage();
        var product = Guid.NewGuid();
        var lease = await first.AcquireLeaseAsync(product);
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await using var blocked = await second.AcquireLeaseAsync(product, cancellation.Token);
            });
        }
        finally { await lease.DisposeAsync(); }
        await using var acquired = await second.AcquireLeaseAsync(product);
        Assert.Equal(first.StoreIdentity, second.StoreIdentity);
    }

    [Fact]
    public void ApplicationContentCannotBeUsedAsPrivateStorage()
    {
        Assert.Throws<InvalidOperationException>(() => new LocalProductImageStorage(
            new ProductImageOptions { RootPath = Path.Combine(Directory.GetCurrentDirectory(), "images") }));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
