using System.Net;
using System.Net.Http.Json;
using ERPFlow.Application.Products;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductCatalogApiTests : IAsyncLifetime
{
    private readonly ProductImageFixture fixture = new();
    private string Path => $"/api/products/{fixture.ProductId}";
    public async Task InitializeAsync() { await fixture.InitializeAsync(); await fixture.AuthenticateAsync(); }
    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<ProductDetails> GetAsync() => (await fixture.Client.GetFromJsonAsync<ProductDetails>(Path))!;

    [Fact]
    public async Task CatalogPriceRangeMatchesTheBrowserSafeCentBoundary()
    {
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/products",
            new CreateProductCommand("Boundary", "PRICE-BOUNDARY", Product.MaxCatalogUnitPrice))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PostAsJsonAsync("/api/products",
            new CreateProductCommand("Too much", "PRICE-EXCESS", Product.MaxCatalogUnitPrice + 0.01m))).StatusCode);
    }

    [Fact]
    public async Task CreationStartsWithZeroStockAndRecordsActorAndPrice()
    {
        var response = await fixture.Client.PostAsJsonAsync("/api/products", new CreateProductCommand(" New scanner ", " NEW-001 ", 12.34m));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = (await response.Content.ReadFromJsonAsync<ProductDetails>())!;
        Assert.Equal($"/api/products/{product.Id}", response.Headers.Location!.OriginalString);
        Assert.Equal(("New scanner", "NEW-001", 12.34m, true), (product.Name, product.Sku, product.UnitPrice, product.IsActive));
        Assert.Equal(8, product.RowVersion.Length);
        var history = Assert.Single(product.History);
        Assert.Equal("Created", history.Action);
        Assert.NotEqual(Guid.Empty, history.ActorId);
        Assert.Equal(12.34m, history.UnitPrice);
        Assert.True(history.CreatedAt > DateTime.UtcNow.AddMinutes(-2));
        await using var context = fixture.Database.Context();
        var persisted = await context.Products.SingleAsync(x => x.Id == product.Id);
        Assert.Equal((0, 0), (persisted.StockOnHand, persisted.ReservedQuantity));
        Assert.Empty(await context.StockMovements.ToListAsync());
        Assert.Empty(await context.WarehouseStocks.Where(x => x.ProductId == product.Id).ToListAsync());
    }

    [Fact]
    public async Task UpdateAdvancesVersionAndAuditsOnlyMeaningfulChangesWithoutChangingStock()
    {
        var before = await GetAsync();
        var updated = await fixture.Client.PutAsJsonAsync(Path, new UpdateProductCommand("Renamed scanner", before.Sku, 200m, before.RowVersion));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = (await updated.Content.ReadFromJsonAsync<ProductDetails>())!;
        Assert.NotEqual(before.RowVersion, after.RowVersion);
        Assert.Equal((before.Id, before.Sku, "Renamed scanner", 200m), (after.Id, after.Sku, after.Name, after.UnitPrice));
        var history = Assert.Single(after.History);
        Assert.Equal(before.UnitPrice, history.PreviousUnitPrice);
        Assert.Equal(200m, history.UnitPrice);
        var noOp = await fixture.Client.PutAsJsonAsync(Path, new UpdateProductCommand(after.Name, after.Sku, after.UnitPrice, after.RowVersion));
        Assert.Single((await noOp.Content.ReadFromJsonAsync<ProductDetails>())!.History);
        await using var context = fixture.Database.Context();
        var product = await context.Products.SingleAsync(x => x.Id == before.Id);
        Assert.Equal((37, 0), (product.StockOnHand, product.ReservedQuantity));
    }

    [Theory]
    [InlineData("", "SKU", 1)]
    [InlineData("Name", "", 1)]
    [InlineData("Name", "SKU", -1)]
    [InlineData("Name", "SKU", 1.123)]
    public async Task RejectsInvalidCatalogInputs(string name, string sku, decimal price)
    {
        var response = await fixture.Client.PostAsJsonAsync("/api/products", new CreateProductCommand(name, sku, price));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RejectsOverlongNamesSkusAndMissingPrice()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PostAsJsonAsync("/api/products", new CreateProductCommand(new string('x', 201), "OK", 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PostAsJsonAsync("/api/products", new CreateProductCommand("OK", new string('x', 65), 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PostAsJsonAsync("/api/products", new { name = "Name", sku = "MISSING" })).StatusCode);
    }

    [Theory]
    [InlineData("stockOnHand")]
    [InlineData("reservedQuantity")]
    [InlineData("isActive")]
    public async Task RejectsOverpostedInventoryAndAvailabilityFields(string field)
    {
        var body = new Dictionary<string, object> { ["name"] = "Injected", ["sku"] = "INJECTED", ["unitPrice"] = 1, [field] = 99 };
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PostAsJsonAsync("/api/products", body)).StatusCode);
        var current = await GetAsync();
        body["sku"] = current.Sku; body["rowVersion"] = current.RowVersion;
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PutAsJsonAsync(Path, body)).StatusCode);
    }

    [Fact]
    public async Task DuplicateSkuAndSkuModificationAreConflicts()
    {
        var current = await GetAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.PostAsJsonAsync("/api/products", new CreateProductCommand("Duplicate", current.Sku, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.PutAsJsonAsync(Path, new UpdateProductCommand(current.Name, "CHANGED", 1, current.RowVersion))).StatusCode);
        Assert.Equal(current.Sku, (await GetAsync()).Sku);
    }

    [Fact]
    public async Task StaleEditsAndStatusTransitionsAreConflicts()
    {
        var old = await GetAsync();
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PutAsJsonAsync(Path, new UpdateProductCommand("Changed", old.Sku, 99, old.RowVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.PutAsJsonAsync(Path, new UpdateProductCommand("Stale", old.Sku, 10, old.RowVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.PostAsJsonAsync($"{Path}/deactivate", new ProductVersionCommand(old.RowVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PostAsJsonAsync($"{Path}/activate", new ProductVersionCommand([]))).StatusCode);
    }

    [Fact]
    public async Task ActivateDeactivatePreserveIdentityAndExposeReferenceStatus()
    {
        var before = await GetAsync();
        var response = await fixture.Client.PostAsJsonAsync($"{Path}/deactivate", new ProductVersionCommand(before.RowVersion));
        var inactive = (await response.Content.ReadFromJsonAsync<ProductDetails>())!;
        Assert.False(inactive.IsActive);
        Assert.Equal("Deactivated", Assert.Single(inactive.History).Action);
        var reference = await fixture.Client.GetFromJsonAsync<ERPFlow.Application.Abstractions.ProductReference[]>("/api/reference/products");
        Assert.False(Assert.Single(reference!).IsActive);
        var active = await fixture.Client.PostAsJsonAsync($"{Path}/activate", new ProductVersionCommand(inactive.RowVersion));
        Assert.True((await active.Content.ReadFromJsonAsync<ProductDetails>())!.IsActive);
        await using var context = fixture.Database.Context();
        var persisted = await context.Products.SingleAsync();
        Assert.Equal((before.Id, 37, 0), (persisted.Id, persisted.StockOnHand, persisted.ReservedQuantity));
    }

    [Theory]
    [InlineData(UserRole.Sales)]
    [InlineData(UserRole.Purchasing)]
    [InlineData(UserRole.Approver)]
    [InlineData(UserRole.WarehouseOperator)]
    [InlineData(UserRole.Technician)]
    public async Task NonAdministratorsCanReadButCannotMutate(UserRole role)
    {
        await fixture.AuthenticateAsync(role);
        var current = await GetAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.PostAsJsonAsync("/api/products", new CreateProductCommand("X", "X", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.PutAsJsonAsync(Path, new UpdateProductCommand("X", current.Sku, 1, current.RowVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.PostAsJsonAsync($"{Path}/deactivate", new ProductVersionCommand(current.RowVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.PostAsJsonAsync($"{Path}/activate", new ProductVersionCommand(current.RowVersion))).StatusCode);
    }

    [Fact]
    public async Task AnonymousRequestsAndDemotedAdministratorsAreRejected()
    {
        using var anonymous = fixture.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/products", new CreateProductCommand("X", "X", 1))).StatusCode);
        await using var context = fixture.Database.Context();
        await context.AppUsers.Where(x => x.Role == UserRole.Administrator).ExecuteUpdateAsync(set => set.SetProperty(x => x.Role, UserRole.Sales));
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.PostAsJsonAsync("/api/products", new CreateProductCommand("X", "X", 1))).StatusCode);
    }

    [Fact]
    public async Task CatalogEditingAndDeactivationPreserveIndependentImageVersionAndBytes()
    {
        using var content = ProductImageTestData.Upload(ProductImageTestData.Image(SKEncodedImageFormat.Png));
        var upload = await fixture.Client.PutAsync($"{Path}/image", content);
        var etag = upload.Headers.ETag!.Tag;
        var bytes = await fixture.Client.GetByteArrayAsync($"{Path}/image");
        var before = await GetAsync();
        Assert.Equal(etag, before.ImageVersion);
        var edit = await fixture.Client.PutAsJsonAsync(Path, new UpdateProductCommand("Catalog edit", before.Sku, 999, before.RowVersion));
        var after = (await edit.Content.ReadFromJsonAsync<ProductDetails>())!;
        await fixture.Client.PostAsJsonAsync($"{Path}/deactivate", new ProductVersionCommand(after.RowVersion));
        var image = await fixture.Client.GetAsync($"{Path}/image");
        Assert.Equal(etag, image.Headers.ETag!.Tag);
        Assert.Equal(bytes, await image.Content.ReadAsByteArrayAsync());
    }
}
