using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using ERPFlow.Application.ProductImages;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.ProductImages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductImageApiTests : IAsyncLifetime
{
    private readonly AsyncLocal<bool> firstChanceObservation = new();
    private readonly ProductImageFixture fixture;
    private const string StaleEtag = "\"AAAAAAAAAAA=\"";
    private string Path => $"/api/products/{fixture.ProductId}/image";

    public ProductImageApiTests()
    {
        fixture = new ProductImageFixture(services =>
        {
            services.RemoveAll<IProductImageWorkflow>();
            services.AddScoped<IProductImageWorkflow>(provider => new FirstChanceObservingWorkflow(
                provider.GetRequiredService<EfProductImageWorkflow>(), firstChanceObservation));
        });
    }

    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task AnonymousRequestsCannotUploadReadOrDelete()
    {
        using var anonymous = fixture.NewClient();
        using var upload = ProductImageTestData.Upload(ProductImageTestData.Image(SKEncodedImageFormat.Png));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsync(Path, upload)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(Path)).StatusCode);
    }

    [Fact]
    public async Task EveryNonAdministratorRoleCanReadButCannotMutate()
    {
        await fixture.AuthenticateAsync();
        var created = await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var etag = StrongEtag(created);

        foreach (var role in Enum.GetValues<UserRole>().Where(x => x != UserRole.Administrator))
        {
            using var client = fixture.NewClient();
            await fixture.AuthenticateAsync(role, client);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Path)).StatusCode);

            using var upload = ProductImageTestData.Upload(ProductImageTestData.Image(SKEncodedImageFormat.Png));
            using var put = new HttpRequestMessage(HttpMethod.Put, Path) { Content = upload };
            put.Headers.TryAddWithoutValidation("If-Match", etag);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(put)).StatusCode);

            using var delete = new HttpRequestMessage(HttpMethod.Delete, Path);
            delete.Headers.TryAddWithoutValidation("If-Match", etag);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(delete)).StatusCode);
        }
    }

    [Theory]
    [InlineData("jpg", "image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("png", "image/png", SKEncodedImageFormat.Png)]
    [InlineData("webp", "image/webp", SKEncodedImageFormat.Webp)]
    public async Task CreateNormalizesSupportedFormatAndGetReturnsStablePrivateBinary(
        string extension,
        string mediaType,
        SKEncodedImageFormat format)
    {
        await fixture.AuthenticateAsync();
        var uploaded = format == SKEncodedImageFormat.Png
            ? ProductImageTestData.PaddedPng(512)
            : ProductImageTestData.Image(format, quality: 41);
        var created = await UploadAsync(uploaded, $"source.{extension}", mediaType);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var etag = StrongEtag(created);

        var first = await fixture.Client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(mediaType, first.Content.Headers.ContentType?.MediaType);
        Assert.Equal(etag, StrongEtag(first));
        Assert.Contains("private", first.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-cache", first.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("nosniff", Assert.Single(first.Headers.GetValues("X-Content-Type-Options")));
        var downloaded = await first.Content.ReadAsByteArrayAsync();
        Assert.False(uploaded.SequenceEqual(downloaded));
        AssertImage(downloaded, 19, 13);

        var persisted = Assert.Single(fixture.StoredObjects());
        Assert.Equal(await File.ReadAllBytesAsync(persisted), downloaded);
        Assert.Equal(downloaded, await (await fixture.Client.GetAsync(Path)).Content.ReadAsByteArrayAsync());

        using var conditional = new HttpRequestMessage(HttpMethod.Get, Path);
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var notModified = await fixture.Client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Empty(await notModified.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task OptionalCreateOnlyPreconditionAndReplacePreconditionsAreEnforced()
    {
        await fixture.AuthenticateAsync();
        var initial = ProductImageTestData.Image(SKEncodedImageFormat.Png);
        var created = await UploadAsync(initial, ifNoneMatch: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var firstEtag = StrongEtag(created);

        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png), ifNoneMatch: true)).StatusCode);
        Assert.Equal((HttpStatusCode)428,
            (await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png))).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png), ifMatch: StaleEtag)).StatusCode);

        var replaced = await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png, 23, 17), ifMatch: firstEtag);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.NotEqual(firstEtag, StrongEtag(replaced));
        AssertImage(await (await fixture.Client.GetAsync(Path)).Content.ReadAsByteArrayAsync(), 23, 17);
    }

    [Fact]
    public async Task DeleteRequiresExactCurrentEtagAndRemovesImage()
    {
        await fixture.AuthenticateAsync();
        var created = await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png));
        var etag = StrongEtag(created);

        Assert.Equal((HttpStatusCode)428, (await fixture.Client.DeleteAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await DeleteAsync(StaleEtag)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(etag)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync(Path)).StatusCode);
        await fixture.CleanupAsync();
        Assert.Empty(fixture.StoredObjects());
    }

    [Fact]
    public async Task ConcurrentInitialUploadsHaveOneCreatorAndOnePreconditionLoser()
    {
        await fixture.AuthenticateAsync();
        using var firstClient = fixture.NewClient();
        using var secondClient = fixture.NewClient();
        await fixture.AuthenticateAsync(client: firstClient);
        await fixture.AuthenticateAsync(client: secondClient);

        var outcomes = await Task.WhenAll(
            UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png, 20, 11), client: firstClient, ifNoneMatch: true),
            UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png, 21, 12), client: secondClient, ifNoneMatch: true));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.PreconditionFailed],
            outcomes.Select(x => x.StatusCode).Order().ToArray());
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync(Path)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentReplaceAndDeleteHaveOneWinnerAndOneStaleLoser()
    {
        await fixture.AuthenticateAsync();
        var etag = StrongEtag(await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png)));
        using var replaceClient = fixture.NewClient();
        using var deleteClient = fixture.NewClient();
        await fixture.AuthenticateAsync(client: replaceClient);
        await fixture.AuthenticateAsync(client: deleteClient);

        var outcomes = await Task.WhenAll(
            UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png, 31, 19), client: replaceClient, ifMatch: etag),
            DeleteAsync(etag, deleteClient));

        Assert.Single(outcomes, x => x.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent);
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task ConcurrentReplacementsWithSameVersionCannotBothSucceed()
    {
        await fixture.AuthenticateAsync();
        var etag = StrongEtag(await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png)));
        using var other = fixture.NewClient();
        await fixture.AuthenticateAsync(client: other);
        var outcomes = await Task.WhenAll(
            UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png, 25, 15), ifMatch: etag),
            UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png, 29, 17), ifMatch: etag, client: other));
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync(Path)).StatusCode);
        await fixture.CleanupAsync();
        Assert.Single(fixture.StoredObjects());
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"AAAAAAAAAAA=\"")]
    [InlineData("\"malformed\"")]
    [InlineData("\"AAAAAAAAAAA=\", \"AAAAAAAAAAE=\"")]
    public async Task MutationRequiresOneWellFormedStrongValidator(string invalid)
    {
        await fixture.AuthenticateAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png), ifMatch: invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DeleteAsync(invalid)).StatusCode);
    }

    [Fact]
    public async Task PersistedDemotionInvalidatesAdministratorMutationPermission()
    {
        await fixture.AuthenticateAsync();
        await using (var context = fixture.Database.Context())
            await context.AppUsers.Where(x => x.Role == UserRole.Administrator)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Role, UserRole.Sales));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png))).StatusCode);
    }

    [Fact]
    public async Task ClientFilenameCannotControlStorageLocation()
    {
        await fixture.AuthenticateAsync();
        var created = await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png), "../../untrusted.png");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var stored = Assert.Single(fixture.StoredObjects());
        Assert.Equal(fixture.StorageRoot, System.IO.Path.GetDirectoryName(stored));
        Assert.DoesNotContain("untrusted", System.IO.Path.GetFileName(stored));
    }

    [Fact]
    public async Task DeletedEtagCannotBeReusedAfterRecreate()
    {
        await fixture.AuthenticateAsync();
        var oldEtag = StrongEtag(await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png)));
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(oldEtag)).StatusCode);
        var recreated = await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png, 29, 17));
        Assert.Equal(HttpStatusCode.Created, recreated.StatusCode);
        Assert.NotEqual(oldEtag, StrongEtag(recreated));
        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png), ifMatch: oldEtag)).StatusCode);
    }

    [Theory]
    [InlineData("image.gif", "image/gif", "GIF89a", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("image.png", "image/jpeg", "not-a-jpeg", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("image.jpg", "image/png", "not-a-png", HttpStatusCode.UnsupportedMediaType)]
    public async Task InvalidExtensionMimeAndSignatureAreRejected(
        string fileName,
        string mediaType,
        string content,
        HttpStatusCode expected)
    {
        await fixture.AuthenticateAsync();
        Assert.Equal(expected, (await UploadAsync(System.Text.Encoding.ASCII.GetBytes(content), fileName, mediaType)).StatusCode);
    }

    [Fact]
    public async Task EmptyTruncatedAndDimensionBombImagesAreRejectedWithoutStorageEffects()
    {
        await fixture.AuthenticateAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync([])).StatusCode);
        var valid = ProductImageTestData.Image(SKEncodedImageFormat.Png);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(valid[..(valid.Length / 2)])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(ProductImageTestData.DimensionBombPng())).StatusCode);
        Assert.Empty(fixture.StoredObjects());
    }

    [Fact]
    public async Task MultipartRequiresExactlyOneFileField()
    {
        await fixture.AuthenticateAsync();
        var png = ProductImageTestData.Image(SKEncodedImageFormat.Png);
        using var wrongField = ProductImageTestData.Upload(png, fieldName: "image");
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PutAsync(Path, wrongField)).StatusCode);

        using var multiple = ProductImageTestData.Upload(png);
        var extra = new ByteArrayContent(png);
        extra.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multiple.Add(extra, "file", "second.png");
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PutAsync(Path, multiple)).StatusCode);
    }

    [Fact]
    public async Task FiveMiBPayloadIsAcceptedButLargerPayloadIsRejected()
    {
        await fixture.AuthenticateAsync();
        var atLimit = ProductImageTestData.PaddedPng(5 * 1024 * 1024);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(atLimit)).StatusCode);
        var etag = StrongEtag(await fixture.Client.GetAsync(Path));
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(etag)).StatusCode);

        var overLimit = ProductImageTestData.PaddedPng(5 * 1024 * 1024 + 1);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await UploadAsync(overLimit)).StatusCode);
    }

    [Fact]
    public async Task TotalMultipartRequestOverSixMiBIsRejected()
    {
        await fixture.AuthenticateAsync();
        using var content = ProductImageTestData.Upload(ProductImageTestData.Image(SKEncodedImageFormat.Png));
        content.Add(new ByteArrayContent(new byte[6 * 1024 * 1024]), "metadata");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await fixture.Client.PutAsync(Path, content)).StatusCode);
    }

    [Fact]
    public async Task MissingProductReturnsNotFoundForEveryOperation()
    {
        await fixture.AuthenticateAsync();
        var missing = $"/api/products/{Guid.NewGuid()}/image";
        using var upload = ProductImageTestData.Upload(ProductImageTestData.Image(SKEncodedImageFormat.Png));
        using var put = await fixture.Client.PutAsync(missing, upload);
        await AssertNotFoundAsync(put, "product_not_found", "product");
        using var get = await fixture.Client.GetAsync(missing);
        await AssertNotFoundAsync(get, "product_not_found", "product");
        using var delete = new HttpRequestMessage(HttpMethod.Delete, missing);
        delete.Headers.TryAddWithoutValidation("If-Match", StaleEtag);
        using var deleted = await fixture.Client.SendAsync(delete);
        await AssertNotFoundAsync(deleted, "product_not_found", "product");
    }

    [Fact]
    public async Task ExistingProductWithoutImageReturnsCodedNotFoundWithoutThrowingAndHostContinues()
    {
        await fixture.AuthenticateAsync();
        var thrown = 0;
        void Record(object? _, FirstChanceExceptionEventArgs eventArgs)
        {
            if (firstChanceObservation.Value && eventArgs.Exception is ProductImageException)
                Interlocked.Increment(ref thrown);
        }

        AppDomain.CurrentDomain.FirstChanceException += Record;
        HttpResponseMessage missing;
        try
        {
            missing = await fixture.Client.GetAsync(Path);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Record;
            firstChanceObservation.Value = false;
        }

        using (missing)
            await AssertNotFoundAsync(missing, "product_image_missing", "image");
        Assert.Equal(0, Volatile.Read(ref thrown));

        using var created = await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var available = await fixture.Client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, available.StatusCode);
    }

    [Fact]
    public async Task ImageLifecycleUpdatesReferenceFieldsWithoutChangingInventoryOrProductRowVersion()
    {
        await fixture.AuthenticateAsync();
        await using var beforeContext = fixture.Database.Context();
        var before = await beforeContext.Products.AsNoTracking().SingleAsync(x => x.Id == fixture.ProductId);
        var rowVersion = before.RowVersion.ToArray();

        var created = await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png));
        var etag = StrongEtag(created);
        using (var reference = await ProductReferenceAsync())
        {
            Assert.True(reference.RootElement.GetProperty("hasImage").GetBoolean());
            Assert.NotEqual(JsonValueKind.Null, reference.RootElement.GetProperty("imageVersion").ValueKind);
        }

        var replaced = await UploadAsync(ProductImageTestData.Image(SKEncodedImageFormat.Png, 27, 15), ifMatch: etag);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(StrongEtag(replaced))).StatusCode);
        using (var reference = await ProductReferenceAsync())
            Assert.False(reference.RootElement.GetProperty("hasImage").GetBoolean());

        await using var afterContext = fixture.Database.Context();
        var after = await afterContext.Products.AsNoTracking().SingleAsync(x => x.Id == fixture.ProductId);
        Assert.Equal((before.StockOnHand, before.ReservedQuantity), (after.StockOnHand, after.ReservedQuantity));
        Assert.Equal(rowVersion, after.RowVersion);
    }

    private async Task<HttpResponseMessage> UploadAsync(
        byte[] bytes,
        string fileName = "image.png",
        string mediaType = "image/png",
        string? ifMatch = null,
        bool ifNoneMatch = false,
        HttpClient? client = null)
    {
        var content = ProductImageTestData.Upload(bytes, fileName, mediaType);
        var request = new HttpRequestMessage(HttpMethod.Put, Path) { Content = content };
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (ifNoneMatch)
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return await (client ?? fixture.Client).SendAsync(request);
    }

    private Task<HttpResponseMessage> DeleteAsync(string etag, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, Path);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return (client ?? fixture.Client).SendAsync(request);
    }

    private async Task<JsonDocument> ProductReferenceAsync()
    {
        using var response = await fixture.Client.GetAsync("/api/reference/products");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var all = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var product = all.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == fixture.ProductId);
        return JsonDocument.Parse(product.GetRawText());
    }

    private static string StrongEtag(HttpResponseMessage response)
    {
        var etag = response.Headers.ETag;
        Assert.NotNull(etag);
        Assert.False(etag.IsWeak);
        Assert.StartsWith("\"", etag.Tag);
        Assert.EndsWith("\"", etag.Tag);
        return etag.Tag;
    }

    private static void AssertImage(byte[] bytes, int width, int height)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        Assert.Equal((width, height), (codec.Info.Width, codec.Info.Height));
    }

    private static async Task AssertNotFoundAsync(HttpResponseMessage response, string code, string detailFragment)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        Assert.Contains(detailFragment, problem.RootElement.GetProperty("detail").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FirstChanceObservingWorkflow(
        IProductImageWorkflow inner,
        AsyncLocal<bool> observation) : IProductImageWorkflow
    {
        public Task<ProductImageWriteResult> PutAsync(Guid productId, Stream source, string fileName,
            string contentType, ProductImageCondition condition, CancellationToken cancellationToken = default) =>
            inner.PutAsync(productId, source, fileName, contentType, condition, cancellationToken);

        public Task DeleteAsync(Guid productId, ProductImageCondition condition,
            CancellationToken cancellationToken = default) => inner.DeleteAsync(productId, condition, cancellationToken);

        public async Task<ProductImageDownload?> GetAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            observation.Value = true;
            try
            {
                return await inner.GetAsync(productId, cancellationToken);
            }
            finally
            {
                observation.Value = false;
            }
        }
    }
}
