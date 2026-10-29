using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Deployment;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductionHttpTests(ProductionHttpFixture fixture) : IClassFixture<ProductionHttpFixture>
{
    [Fact]
    public async Task HealthContainsOnlyStatusAndProductionHasNoDemoOrOpenApiExposure()
    {
        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            using var response = await fixture.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Single(body.RootElement.EnumerateObject());
            Assert.True(body.RootElement.TryGetProperty("status", out _));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync("/swagger/index.html")).StatusCode);
        await using var context = fixture.Database.Context();
        Assert.Equal(2, await context.AppUsers.CountAsync());
        Assert.Empty(await context.ProfessionalDemoSeedManifests.ToArrayAsync());
    }

    [Fact]
    public async Task HttpRedirectsAndUntrustedForwardedHeadersCannotBypassHttpsOrHostFiltering()
    {
        using var http = fixture.Factory.CreateClient(new() { BaseAddress = new("http://api.erpflow.test"), AllowAutoRedirect = false });
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        using var response = await http.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location!.Scheme);
        using var secure = await fixture.Client.GetAsync("/health/live");
        Assert.True(secure.Headers.Contains("Strict-Transport-Security"));
        using var wrongHost = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        wrongHost.Headers.Host = "unapproved.example";
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.SendAsync(wrongHost)).StatusCode);
    }

    [Fact]
    public async Task CorsGrantsOnlyTheExplicitHttpsFrontendIncludingImageValidators()
    {
        foreach (var origin in new[] { "https://angular.erpflow.test", "https://unapproved.example" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, $"/api/products/{fixture.ProductId}/image");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "PUT");
            request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,if-match");
            using var response = await fixture.Client.SendAsync(request);
            if (origin == "https://angular.erpflow.test")
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
                Assert.Contains("if-match", string.Join(',', response.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant());
                Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
            }
            else Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }
    }

    [Fact]
    public async Task ProductionJwtAndCurrentRoleChecksProtectImageWrites()
    {
        using var anonymous = fixture.Factory.CreateClient(new() { BaseAddress = new("https://api.erpflow.test") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/reference/products")).StatusCode);
        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", "invalid.audit.token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/reference/products")).StatusCode);
        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", fixture.SalesToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.DeleteAsync($"/api/products/{fixture.ProductId}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("/api/reference/products")).StatusCode);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("expired")]
    public async Task ProductionJwtRejectsWrongIssuerAudienceSignatureAndExpiry(string fault)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/reference/products");
        request.Headers.Authorization = new("Bearer", fixture.InvalidToken(fault));
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task ProductionUploadRejectsExecutableAndOversizedFileContent()
    {
        var path = $"/api/products/{fixture.ProductId}/image";
        using var executable = ProductImageTestData.Upload(Encoding.ASCII.GetBytes("MZ-not-an-image"));
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PutAsync(path, executable)).StatusCode);
        using var oversized = ProductImageTestData.Upload(new byte[5 * 1024 * 1024 + 1]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await fixture.Client.PutAsync(path, oversized)).StatusCode);
    }

    [Fact]
    public async Task ImageAndStrongValidatorSurviveRestartAndStillEnforceAtomicReplacement()
    {
        var path = $"/api/products/{fixture.ProductId}/image";
        using var create = await fixture.Client.PutAsync(path, ProductImageTestData.Upload(ProductImageTestData.Image(SKEncodedImageFormat.Png)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var first = create.Headers.ETag!.ToString();
        using var before = await fixture.Client.GetAsync(path);
        var bytes = await before.Content.ReadAsByteArrayAsync();
        await fixture.RestartAsync();
        using var after = await fixture.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(first, after.Headers.ETag!.ToString());
        Assert.Equal(bytes, await after.Content.ReadAsByteArrayAsync());
        using var condition = new HttpRequestMessage(HttpMethod.Get, path);
        condition.Headers.TryAddWithoutValidation("If-None-Match", first);
        Assert.Equal(HttpStatusCode.NotModified, (await fixture.Client.SendAsync(condition)).StatusCode);
        using var replace = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = ProductImageTestData.Upload(ProductImageTestData.Image(SKEncodedImageFormat.Png, width: 27))
        };
        replace.Headers.TryAddWithoutValidation("If-Match", first);
        using var changed = await fixture.Client.SendAsync(replace);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(first, changed.Headers.ETag!.ToString());
        using var stale = new HttpRequestMessage(HttpMethod.Delete, path);
        stale.Headers.TryAddWithoutValidation("If-Match", first);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await fixture.Client.SendAsync(stale)).StatusCode);
        using var remove = new HttpRequestMessage(HttpMethod.Delete, path);
        remove.Headers.TryAddWithoutValidation("If-Match", changed.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Client.SendAsync(remove)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task LoginAbuseIsRejectedBeforeUnboundedCredentialVerification()
    {
        var email = $"unknown-{Guid.NewGuid():N}@production.test";
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.PostAsJsonAsync("/api/auth/login", new { email, password = "not-a-real-credential" })).StatusCode);
        using var limited = await fixture.Client.PostAsJsonAsync("/api/auth/login", new { email, password = "not-a-real-credential" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        using var oversized = await fixture.Client.PostAsJsonAsync("/api/auth/login", new { email, password = new string('x', 17 * 1024) });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    [Fact]
    public async Task ProductionStartupRefusesAnOldDatabaseWithoutMigratingIt()
    {
        await using var database = await InventoryTestDatabase.CreateAsync(InventoryTestDatabase.LegacyMigration);
        await using var context = database.Context();
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        await using var factory = new ProductionApiFactory(context.Database.GetConnectionString()!, fixture.StorageRoot,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("(SCHEMA_INCOMPATIBLE)", error.Message);
        Assert.Equal(before, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
    }

    [Fact]
    public void ProductionStartupIdentifiesRejectedImageRootWithoutChangingTheDatabase()
    {
        using var context = fixture.Database.Context();
        using var factory = new ProductionApiFactory(context.Database.GetConnectionString()!,
            Path.GetPathRoot(fixture.StorageRoot)!, Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("(IMAGE_PATH_REJECTED)", error.Message);
        Assert.DoesNotContain(fixture.StorageRoot, error.Message);
    }

    [Theory]
    [InlineData("AllowedHosts", "*")]
    [InlineData("Cors:ProductionOrigins:0", "https://*.example.test")]
    [InlineData("Cors:ProductionOrigins:0", "http://frontend.example.test")]
    [InlineData("Cors:ProductionOrigins:0", "https://frontend.example.test/path")]
    [InlineData("ProductImages:RootPath", "relative/images")]
    [InlineData("DemoUsers:EnableAutomaticSeed", "true")]
    public void UnsafeProductionConfigurationIsRejected(string key, string value)
    {
        var values = ProductionApiFactory.Settings("fixture-only-not-used", fixture.StorageRoot,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        values[key] = value;
        Assert.Throws<InvalidOperationException>(() => ERPFlow.Api.ProductionHosting.Validate(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }
}

public sealed class ProductionHttpFixture : IAsyncLifetime
{
    internal InventoryTestDatabase Database { get; private set; } = null!;
    internal ProductionApiFactory Factory { get; private set; } = null!;
    internal HttpClient Client { get; private set; } = null!;
    internal string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "ERPFlow-ProductionImages-" + Guid.NewGuid().ToString("N"));
    internal Guid ProductId { get; private set; }
    internal string SalesToken { get; private set; } = "";
    private readonly string signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private string connection = "", administratorToken = "";

    public async Task InitializeAsync()
    {
        Database = await InventoryTestDatabase.CreateAsync();
        await using var context = Database.Context();
        connection = context.Database.GetConnectionString()!;
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var administrator = await new FirstAdministratorProvisioner(context).ProvisionAsync("Owner", "owner@production.test", password);
        var approval = await InventoryCutoverTestSupport.PrepareAsync(Database, administrator);
        await InventoryCutoverTestSupport.Cutover(context, administrator).RehearseAsync(approval);
        var sales = new AppUser("Sales", "sales@production.test", UserRole.Sales,
            new PasswordHasher<object>().HashPassword(new object(), password));
        var product = new Product("Production image product", "PROD-IMAGE", 1, 0);
        ProductId = product.Id;
        context.AddRange(sales, product);
        await context.SaveChangesAsync();
        Start();
        administratorToken = await Login("owner@production.test", password);
        SalesToken = await Login("sales@production.test", password);
        Client.DefaultRequestHeaders.Authorization = new("Bearer", administratorToken);
    }

    private void Start()
    {
        Factory = new(connection, StorageRoot, signingKey);
        Client = Factory.CreateClient(new() { BaseAddress = new("https://api.erpflow.test"), AllowAutoRedirect = false });
        if (administratorToken.Length > 0) Client.DefaultRequestHeaders.Authorization = new("Bearer", administratorToken);
    }
    public async Task RestartAsync() { Client.Dispose(); await Factory.DisposeAsync(); Start(); }
    internal string InvalidToken(string fault)
    {
        var original = new JwtSecurityTokenHandler().ReadJwtToken(administratorToken);
        var token = new JwtSecurityToken(fault == "issuer" ? "wrong" : "Production.Tests",
            fault == "audience" ? "wrong" : "Production.Tests", original.Claims.Where(claim => claim.Type is "sub" or "email" or "name" or "role"),
            expires: DateTime.UtcNow.AddMinutes(fault == "expired" ? -5 : 5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                fault == "signature" ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) : signingKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    private async Task<string> Login(string email, string password)
    {
        using var response = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }
    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null) await Factory.DisposeAsync();
        if (Database is not null) await Database.DisposeAsync();
        if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, true);
    }
}

internal sealed class ProductionApiFactory(string connection, string root, string key) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings(connection, root, key)));
    }
    internal static Dictionary<string, string?> Settings(string connection, string root, string key) => new()
    {
        ["ConnectionStrings:ERPFlowDb"] = connection, ["Jwt:Issuer"] = "Production.Tests", ["Jwt:Audience"] = "Production.Tests",
        ["Jwt:SigningKey"] = key, ["DemoUsers:EnableAutomaticSeed"] = "false", ["AllowedHosts"] = "api.erpflow.test;localhost",
        ["ProductImages:RootPath"] = root, ["Cors:ProductionOrigins:0"] = "https://angular.erpflow.test"
    };
}
