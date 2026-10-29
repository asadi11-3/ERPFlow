using ERPFlow.Api;
using ERPFlow.Infrastructure.ProductImages;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ERPFlow.Tests;

public sealed class MonsterAspHostingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ERPFlow-MonsterAsp-" + Guid.NewGuid().ToString("N"));

    private WebApplicationBuilder Builder(string environment = "Production")
    {
        var content = Path.Combine(root, "wwwroot");
        Directory.CreateDirectory(Path.Combine(content, "wwwroot"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment, ContentRootPath = content, WebRootPath = "wwwroot", Args = []
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(ProductionApiFactory.Settings("not-used", "not-used", new string('x', 64)));
        builder.Configuration["ProductImages:RootPath"] = null;
        return builder;
    }

    [Fact]
    public async Task MissingProductionImageRootUsesPrivateSiblingAndRetainsStoreIdentity()
    {
        var builder = Builder();
        ProductionHosting.AddServices(builder);
        ProductionHosting.Validate(builder.Configuration);
        var options = new ProductImageOptions
        {
            RootPath = builder.Configuration["ProductImages:RootPath"],
            ForbiddenRoots = [builder.Environment.ContentRootPath, builder.Environment.WebRootPath]
        };
        var storage = new LocalProductImageStorage(options);

        await storage.ProbeAsync();

        Assert.True(File.Exists(Path.Combine(root, "private", "ProductImages", ".store-id")));
        Assert.Equal(storage.StoreIdentity, new LocalProductImageStorage(options).StoreIdentity);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void DefaultIsNotAppliedOutsideProduction(string environment)
    {
        var builder = Builder(environment);
        ProductionHosting.AddServices(builder);
        Assert.Null(builder.Configuration["ProductImages:RootPath"]);
    }

    [Fact]
    public void ExplicitImageRootTakesPrecedence()
    {
        var builder = Builder();
        var configured = Path.Combine(root, "separate-store");
        builder.Configuration["ProductImages:RootPath"] = configured;
        ProductionHosting.AddServices(builder);
        Assert.Equal(configured, builder.Configuration["ProductImages:RootPath"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/images")]
    public void ExplicitInvalidRootIsNotSilentlyReplaced(string configured)
    {
        var builder = Builder();
        builder.Configuration["ProductImages:RootPath"] = configured;
        ProductionHosting.AddServices(builder);
        Assert.Throws<InvalidOperationException>(() => ProductionHosting.Validate(builder.Configuration));
        Assert.Equal(configured, builder.Configuration["ProductImages:RootPath"]);
    }

    [Fact]
    public void ExplicitPublicRootStillFailsStorageValidation()
    {
        var builder = Builder();
        builder.Configuration["ProductImages:RootPath"] = Path.Combine(builder.Environment.ContentRootPath, "images");
        ProductionHosting.AddServices(builder);
        Assert.Throws<InvalidOperationException>(() => new LocalProductImageStorage(new()
        {
            RootPath = builder.Configuration["ProductImages:RootPath"], ForbiddenRoots = [builder.Environment.ContentRootPath]
        }));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
