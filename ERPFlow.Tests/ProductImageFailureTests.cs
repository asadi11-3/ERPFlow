using System.Net;
using System.Data.Common;
using ERPFlow.Application.ProductImages;
using ERPFlow.Domain.Entities;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.ProductImages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductImageFailureTests : IAsyncLifetime
{
    private readonly ProductImageFixture fixture;
    private readonly FailingStorage storage;
    private readonly PublishFailure databaseFailure = new();
    private readonly CommitFailure commitFailure = new();
    private readonly CleanupDatabaseFailure cleanupDatabaseFailure = new();
    private string ImagePath => $"/api/products/{fixture.ProductId}/image";

    public ProductImageFailureTests()
    {
        ProductImageFixture? ownedFixture = null;
        storage = new FailingStorage(() => new LocalProductImageStorage(
            new ProductImageOptions { RootPath = ownedFixture!.StorageRoot }));
        fixture = ownedFixture = new ProductImageFixture(services =>
        {
            services.RemoveAll<IProductImageStorage>();
            services.AddSingleton<IProductImageStorage>(storage);
            services.ConfigureDbContext<ERPFlowDbContext>(options => options.AddInterceptors(databaseFailure, commitFailure, cleanupDatabaseFailure));
        });
    }

    public async Task InitializeAsync()
    {
        await fixture.InitializeAsync();
        await fixture.AuthenticateAsync();
    }

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task FailedFileWriteCannotPublishInaccessibleMetadataAndRecoveryReclaimsCandidate()
    {
        storage.FailAfterWrite = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await UploadAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync(ImagePath)).StatusCode);
        await using (var context = fixture.Database.Context()) Assert.Empty(await context.ProductImages.ToListAsync());
        storage.FailAfterWrite = false;
        await RecoverAsync();
        Assert.Empty(fixture.StoredObjects());
    }

    [Fact]
    public async Task FailedReplacementRetainsOldBytesAndValidator()
    {
        var created = await UploadAsync();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var etag = created.Headers.ETag!.Tag;
        var bytes = await (await fixture.Client.GetAsync(ImagePath)).Content.ReadAsByteArrayAsync();
        storage.FailAfterWrite = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await UploadAsync(etag)).StatusCode);
        storage.FailAfterWrite = false;
        var current = await fixture.Client.GetAsync(ImagePath);
        Assert.Equal(etag, current.Headers.ETag!.Tag);
        Assert.Equal(bytes, await current.Content.ReadAsByteArrayAsync());
        await RecoverAsync();
        Assert.Single(fixture.StoredObjects());
    }

    [Fact]
    public async Task DatabasePublicationFailureRetainsExistingAssociationAndReclaimsUnpublishedFile()
    {
        var created = await UploadAsync();
        var etag = created.Headers.ETag!.Tag;
        databaseFailure.Enabled = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await UploadAsync(etag)).StatusCode);
        databaseFailure.Enabled = false;
        var current = await fixture.Client.GetAsync(ImagePath);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(etag, current.Headers.ETag!.Tag);
        await RecoverAsync();
        Assert.Single(fixture.StoredObjects());
        await using var context = fixture.Database.Context();
        var image = Assert.Single(await context.ProductImages.ToListAsync());
        Assert.Equal(await File.ReadAllBytesAsync(System.IO.Path.Combine(fixture.StorageRoot, image.StorageKey)),
            await current.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task CleanupDeletionFailureIsRetryableAndDoesNotRestoreDeletedAssociation()
    {
        var created = await UploadAsync();
        using var delete = new HttpRequestMessage(HttpMethod.Delete, ImagePath);
        delete.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Client.SendAsync(delete)).StatusCode);
        storage.FailDelete = true;
        await fixture.CleanupAsync();
        Assert.Single(fixture.StoredObjects());
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync(ImagePath)).StatusCode);
        storage.FailDelete = false;
        await fixture.CleanupAsync();
        Assert.Empty(fixture.StoredObjects());
        await using var context = fixture.Database.Context();
        Assert.Empty(await context.ProductImages.ToListAsync());
        Assert.Empty(await context.ProductImageObjects.ToListAsync());
    }

    [Fact]
    public async Task MissingCurrentFileReturnsSanitizedFailureWithoutDiscardingMetadata()
    {
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync()).StatusCode);
        File.Delete(Assert.Single(fixture.StoredObjects()));
        var response = await fixture.Client.GetAsync(ImagePath);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(fixture.StorageRoot, await response.Content.ReadAsStringAsync());
        await using var context = fixture.Database.Context();
        Assert.Single(await context.ProductImages.ToListAsync());
        await fixture.CleanupAsync();
        Assert.Single(await context.ProductImages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AmbiguousCommitNeverDeletesAnImageThatActuallyCommitted()
    {
        commitFailure.Enabled = true;
        var upload = await UploadAsync();
        Assert.Contains(upload.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.ServiceUnavailable });
        Assert.True(commitFailure.Triggered, "The test must inject an error after the database committed publication.");
        var current = await fixture.Client.GetAsync(ImagePath);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.NotEmpty(await current.Content.ReadAsByteArrayAsync());
        await RecoverAsync();
        Assert.Single(fixture.StoredObjects());
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync(ImagePath)).StatusCode);
    }

    [Fact]
    public async Task CleanupFailsClosedWhenDatabaseCannotEstablishOwnership()
    {
        var created = await UploadAsync();
        using var delete = new HttpRequestMessage(HttpMethod.Delete, ImagePath);
        delete.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Client.SendAsync(delete)).StatusCode);
        cleanupDatabaseFailure.Enabled = true;
        await Assert.ThrowsAsync<SimulatedDatabaseUnavailable>(() => fixture.CleanupAsync());
        Assert.Single(fixture.StoredObjects());
        cleanupDatabaseFailure.Enabled = false;
        await fixture.CleanupAsync();
        Assert.Empty(fixture.StoredObjects());
    }

    [Fact]
    public async Task ExpiredSlowWriterCannotRaceCleanupIntoPublishingADeletedFile()
    {
        storage.BlockWrite = true;
        var upload = UploadAsync();
        await storage.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using (var context = fixture.Database.Context())
                await context.ProductImageObjects.ExecuteUpdateAsync(setters =>
                    setters.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)));
            var cleanup = fixture.CleanupAsync();
            Assert.False(cleanup.IsCompleted);
            Assert.Single(fixture.StoredObjects());
            storage.ReleaseWrite.TrySetResult();
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await upload).StatusCode);
            await cleanup;
            Assert.Empty(fixture.StoredObjects());
            Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync(ImagePath)).StatusCode);
        }
        finally { storage.ReleaseWrite.TrySetResult(); }
    }

    private async Task RecoverAsync()
    {
        await using (var context = fixture.Database.Context())
            await context.ProductImageObjects.Where(x => x.State == ProductImageObjectState.Pending)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)));
        await fixture.CleanupAsync();
        await fixture.CleanupAsync();
    }

    private Task<HttpResponseMessage> UploadAsync(string? etag = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, ImagePath)
        {
            Content = ProductImageTestData.Upload(ProductImageTestData.Image(SKEncodedImageFormat.Png))
        };
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        return fixture.Client.SendAsync(request);
    }

    private sealed class PublishFailure : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<ProductImage>()
                .Any(x => x.State is EntityState.Added or EntityState.Modified))
                throw new DbUpdateException("Injected isolated publication failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class CommitFailure : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public bool Triggered { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<ProductImage>().Any())
            {
                Enabled = false;
                Triggered = true;
                throw new TimeoutException("Injected lost acknowledgement after isolated commit.");
            }
            return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
        }
    }

    private sealed class SimulatedDatabaseUnavailable() : DbException("Injected isolated database outage.");

    private sealed class CleanupDatabaseFailure : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("ProductImageObjects", StringComparison.Ordinal))
                throw new SimulatedDatabaseUnavailable();
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class FailingStorage(Func<IProductImageStorage> create) : IProductImageStorage
    {
        private readonly Lazy<IProductImageStorage> inner = new(create);
        public bool FailAfterWrite { get; set; }
        public bool FailDelete { get; set; }
        public bool BlockWrite { get; set; }
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string StoreIdentity => inner.Value.StoreIdentity;
        public ValueTask<IAsyncDisposable> AcquireLeaseAsync(Guid productId, CancellationToken cancellationToken = default) =>
            inner.Value.AcquireLeaseAsync(productId, cancellationToken);
        public async Task WriteAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        {
            await inner.Value.WriteAsync(key, bytes, cancellationToken);
            if (BlockWrite)
            {
                WriteStarted.TrySetResult();
                await ReleaseWrite.Task.WaitAsync(cancellationToken);
            }
            if (FailAfterWrite) throw new IOException("Injected isolated storage failure.");
        }
        public Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            inner.Value.ReadAsync(key, cancellationToken);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => FailDelete
            ? Task.FromException(new IOException("Injected isolated cleanup failure."))
            : inner.Value.DeleteAsync(key, cancellationToken);
    }
}
