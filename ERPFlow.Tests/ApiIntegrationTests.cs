using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ERPFlow.Api;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ApiIntegrationTests : IAsyncLifetime
{
    private const string Password = "Integration-Test-Password-42!";
    private readonly string databaseName = $"ERPFlow_Phase4ApiTests_{Guid.NewGuid():N}";
    private ApiFactory factory = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        factory = new ApiFactory(CreateConnectionString(databaseName));
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
        if (!await context.AppUsers.AnyAsync(user => user.Email == "sales2@erpflow.local"))
        {
            var hasher = new PasswordHasher<object>();
            context.AppUsers.Add(new AppUser(
                "Second Sales User",
                "sales2@erpflow.local",
                UserRole.Sales,
                hasher.HashPassword(new object(), Password)));
            await context.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var context = CreateContext(databaseName);
        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task ProtectedEndpointWithoutTokenReturnsProblemDetails()
    {
        var response = await client.GetAsync("/api/reference/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(StatusCodes.Status401Unauthorized, problem?.Status);
    }

    [Fact]
    public async Task InvalidCredentialsReturnUnauthorized()
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("sales@erpflow.local", "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MalformedJsonReturnsProblemDetails()
    {
        using var content = new StringContent("{ invalid", null, "application/json");
        var response = await client.PostAsync("/api/auth/login", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DevelopmentOpenApiAndScalarEndpointsAreAvailable()
    {
        var openApiResponse = await client.GetAsync("/openapi/v1.json");
        var scalarResponse = await client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scalarResponse.StatusCode);
        Assert.Equal("text/html", scalarResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RolePoliciesAndOwnershipProtectOrderWorkflow()
    {
        var salesToken = await LoginAsync("sales@erpflow.local");
        var approverToken = await LoginAsync("approver@erpflow.local");
        var secondSalesToken = await LoginAsync("sales2@erpflow.local");

        SetToken(salesToken);
        var customers = await client.GetFromJsonAsync<CustomerReferenceDto[]>("/api/reference/customers");
        var products = await client.GetFromJsonAsync<ProductReferenceDto[]>("/api/reference/products");
        var customer = Assert.Single(customers!);
        var product = Assert.Single(products!, candidate => candidate.Sku == "SCAN-100");

        var createResponse = await client.PostAsJsonAsync(
            "/api/sales-orders",
            new CreateSalesOrderCommand(
                customer.Id,
                [new CreateSalesOrderItem(product.Id, 2)]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var draft = await createResponse.Content.ReadFromJsonAsync<SalesOrderResult>();
        Assert.NotNull(draft);
        Assert.Equal(product.UnitPrice, Assert.Single(draft.Items).UnitPrice);

        var salesPendingResponse = await client.GetAsync("/api/sales-orders/pending");
        Assert.Equal(HttpStatusCode.Forbidden, salesPendingResponse.StatusCode);

        SetToken(secondSalesToken);
        var otherUserResponse = await client.GetAsync($"/api/sales-orders/{draft.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, otherUserResponse.StatusCode);

        SetToken(approverToken);
        var approverCreateResponse = await client.PostAsJsonAsync(
            "/api/sales-orders",
            new CreateSalesOrderCommand(
                customer.Id,
                [new CreateSalesOrderItem(product.Id, 1)]));
        Assert.Equal(HttpStatusCode.Forbidden, approverCreateResponse.StatusCode);

        SetToken(salesToken);
        var submitResponse = await client.PostAsync($"/api/sales-orders/{draft.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        SetToken(approverToken);
        var pending = await client.GetFromJsonAsync<SalesOrderResult[]>("/api/sales-orders/pending");
        Assert.Contains(pending!, order => order.Id == draft.Id);
        var approveResponse = await client.PostAsJsonAsync(
            $"/api/sales-orders/{draft.Id}/approve",
            new ApproveOrderRequest("Approved by API integration test."));
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = await approveResponse.Content.ReadFromJsonAsync<SalesOrderResult>();
        Assert.Equal(OrderStatus.Approved, approved?.Status);
    }

    private async Task<string> LoginAsync(string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccessTokenResult>())!.AccessToken;
    }

    private void SetToken(string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string CreateConnectionString(string name) =>
        new SqlConnectionStringBuilder
        {
            DataSource = Environment.GetEnvironmentVariable("ERPFLOW_TEST_SQLSERVER") ?? @".\SQLEXPRESS",
            InitialCatalog = name,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            MultipleActiveResultSets = true
        }.ConnectionString;

    private static ERPFlowDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<ERPFlowDbContext>()
            .UseSqlServer(CreateConnectionString(name))
            .Options;
        return new ERPFlowDbContext(options);
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:ERPFlowDb"] = connectionString,
                    ["Jwt:Issuer"] = "ERPFlow.Tests",
                    ["Jwt:Audience"] = "ERPFlow.Api.Tests",
                    ["Jwt:SigningKey"] = "integration-test-only-signing-key-at-least-32-characters",
                    ["Jwt:ExpirationMinutes"] = "10",
                    ["DemoUsers:Password"] = Password
                }));
        }
    }

    private sealed record CustomerReferenceDto(Guid Id, string Name, string Email);

    private sealed record ProductReferenceDto(
        Guid Id,
        string Name,
        string Sku,
        decimal UnitPrice,
        int AvailableStock);
}
