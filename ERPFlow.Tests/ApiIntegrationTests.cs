using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
            BaseAddress = new Uri("https://localhost:7165")
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

    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("http://localhost:5173")]
    public async Task AllowedOriginPreflightSupportsBearerHeadersWithoutCredentials(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/sales-orders");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var headers = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
        Assert.Contains("authorization", headers, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("content-type", headers, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("POST", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods")));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task DisallowedOriginPreflightDoesNotGrantBrowserAccess()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/sales-orders");
        request.Headers.Add("Origin", "https://untrusted.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task CorsDoesNotBypassJwtOrRoleAuthorization()
    {
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:4200");
        var anonymous = await client.GetAsync("/api/sales-orders");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("http://localhost:4200", Assert.Single(anonymous.Headers.GetValues("Access-Control-Allow-Origin")));
        SetToken("invalid-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/sales-orders")).StatusCode);
        SetToken(await LoginAsync("sales@erpflow.local"));
        var authorized = await client.GetAsync("/api/sales-orders");
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        Assert.False(authorized.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/sales-orders/pending")).StatusCode);
    }

    [Fact]
    public async Task ProductionDoesNotEnableDevelopmentCorsEvenWithConfiguredOrigins()
    {
        await using var production = new ApiFactory(CreateConnectionString(databaseName), "Production");
        using var productionClient = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost:7165")
        });
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/sales-orders");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await productionClient.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task ApprovalHistoryEndpointRequiresAuthentication()
    {
        var response = await client.GetAsync($"/api/sales-orders/{Guid.NewGuid()}/history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ApprovalHistoryEndpointReturnsChronologicalAuditTrail()
    {
        var expected = await SeedOrderHistoryAsync("sales@erpflow.local");
        SetToken(await LoginAsync("sales@erpflow.local"));

        var history = await client.GetFromJsonAsync<ApprovalHistoryResult[]>(
            $"/api/sales-orders/{expected.OrderId}/history");

        Assert.NotNull(history);
        Assert.Collection(
            history,
            submitted =>
            {
                Assert.Equal(ApprovalAction.Submitted, submitted.Action);
                Assert.Equal(expected.ActorId, submitted.ActorId);
                Assert.Equal(expected.SubmittedAt, submitted.TimestampUtc);
                Assert.Equal(DateTimeKind.Utc, submitted.TimestampUtc.Kind);
                Assert.Equal("Submitted first.", submitted.Remarks);
            },
            rejected =>
            {
                Assert.Equal(ApprovalAction.Rejected, rejected.Action);
                Assert.Equal(expected.ActorId, rejected.ActorId);
                Assert.Equal(expected.RejectedAt, rejected.TimestampUtc);
                Assert.Equal(DateTimeKind.Utc, rejected.TimestampUtc.Kind);
                Assert.Equal("Budget approval is required.", rejected.Remarks);
            });
    }

    [Fact]
    public async Task ApprovalHistoryEndpointReturnsNotFoundForMissingOrder()
    {
        SetToken(await LoginAsync("sales@erpflow.local"));

        var response = await client.GetAsync($"/api/sales-orders/{Guid.NewGuid()}/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ApprovalHistoryEndpointEnforcesSalesOwnership()
    {
        var expected = await SeedOrderHistoryAsync("sales@erpflow.local");
        SetToken(await LoginAsync("sales2@erpflow.local"));

        var response = await client.GetAsync($"/api/sales-orders/{expected.OrderId}/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
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
    public async Task OrderListRequiresAuthentication()
    {
        var response = await client.GetAsync("/api/sales-orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("sales@erpflow.local", 5)]
    [InlineData("sales2@erpflow.local", 5)]
    [InlineData("sales3@erpflow.local", 5)]
    [InlineData("approver@erpflow.local", 15)]
    [InlineData("admin@erpflow.local", 15)]
    public async Task OrderListScopesItemsAndCountsToPersistedRole(string email, int expectedCount)
    {
        SetToken(await LoginAsync(email));
        var result = await client.GetFromJsonAsync<SalesOrderPage>("/api/sales-orders?pageSize=100");
        Assert.NotNull(result);
        Assert.Equal(expectedCount, result.TotalCount);
        Assert.Equal(expectedCount, result.Items.Count);
        Assert.Equal(1, result.TotalPages);
        if (expectedCount == 5)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
            var ownerId = await context.AppUsers.Where(user => user.Email == email).Select(user => user.Id).SingleAsync();
            Assert.All(result.Items, order => Assert.Equal(ownerId, order.CreatedByUserId));
        }
    }

    [Fact]
    public async Task OrderListPaginatesWithStableTimestampAndIdOrderingAndReusesOrderDto()
    {
        SetToken(await LoginAsync("admin@erpflow.local"));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
        var tiedTimestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await context.SalesOrders.ExecuteUpdateAsync(update => update.SetProperty(order => order.CreatedAt, tiedTimestamp));
        var expectedIds = await context.SalesOrders.OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id).Select(order => order.Id).ToArrayAsync();

        var first = await client.GetFromJsonAsync<SalesOrderPage>("/api/sales-orders");
        var second = await client.GetFromJsonAsync<SalesOrderPage>("/api/sales-orders?page=2");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, first.Page);
        Assert.Equal(10, first.PageSize);
        Assert.Equal(15, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(5, second.Items.Count);
        Assert.Equal(expectedIds, first.Items.Concat(second.Items).Select(order => order.Id));
        foreach (var order in first.Items)
        {
            var detail = await client.GetFromJsonAsync<SalesOrderResult>($"/api/sales-orders/{order.Id}");
            Assert.NotNull(detail);
            Assert.Equal(detail.TotalAmount, order.TotalAmount);
            Assert.Equal(detail.Items.Count, order.Items.Count);
            Assert.Equal(detail.Reservations.Count, order.Reservations.Count);
        }
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 5)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    public async Task OrderListFiltersStatusBeforeCountingAndPaging(int status, int expectedCount)
    {
        SetToken(await LoginAsync("approver@erpflow.local"));
        var result = await client.GetFromJsonAsync<SalesOrderPage>($"/api/sales-orders?status={status}&pageSize=2");
        Assert.NotNull(result);
        Assert.Equal(expectedCount, result.TotalCount);
        Assert.Equal((expectedCount + 1) / 2, result.TotalPages);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, order => Assert.Equal((OrderStatus)status, order.Status));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=4")]
    [InlineData("status=-1")]
    [InlineData("status=Draft")]
    [InlineData("page=abc")]
    public async Task OrderListRejectsInvalidQueryWithProblemDetails(string query)
    {
        SetToken(await LoginAsync("admin@erpflow.local"));
        var response = await client.GetAsync($"/api/sales-orders?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OrderListReturnsEmptyPageForUnmatchedFilterAndOutOfRangePage()
    {
        SetToken(await LoginAsync("admin@erpflow.local"));
        var beyond = await client.GetFromJsonAsync<SalesOrderPage>("/api/sales-orders?page=2147483647&pageSize=100");
        Assert.NotNull(beyond);
        Assert.Empty(beyond.Items);
        Assert.Equal(15, beyond.TotalCount);
        Assert.Equal(1, beyond.TotalPages);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
        var hasher = new PasswordHasher<object>();
        context.AppUsers.Add(new AppUser("New Sales", "empty@example.test", UserRole.Sales,
            hasher.HashPassword(new object(), Password)));
        await context.SaveChangesAsync();
        SetToken(await LoginAsync("empty@example.test"));
        var empty = await client.GetFromJsonAsync<SalesOrderPage>("/api/sales-orders?status=2");
        Assert.NotNull(empty);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(0, empty.TotalPages);
    }

    [Theory]
    [InlineData("sales@erpflow.local", false)]
    [InlineData("sales2@erpflow.local", false)]
    [InlineData("sales3@erpflow.local", false)]
    [InlineData("approver@erpflow.local", true)]
    [InlineData("admin@erpflow.local", true)]
    public async Task SeededUsersAuthenticateAndAccessPermittedDemoData(string email, bool canApprove)
    {
        SetToken(await LoginAsync(email));
        var customers = await client.GetFromJsonAsync<CustomerReferenceDto[]>("/api/reference/customers");
        Assert.Equal(8, customers!.Length);
        var response = await client.GetAsync("/api/sales-orders/pending");
        Assert.Equal(canApprove ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (canApprove)
        {
            var orders = await response.Content.ReadFromJsonAsync<SalesOrderResult[]>();
            Assert.Equal(5, orders!.Length);
            var history = await client.GetFromJsonAsync<ApprovalHistoryResult[]>(
                $"/api/sales-orders/{orders[0].Id}/history");
            Assert.Equal(ApprovalAction.Submitted, Assert.Single(history!).Action);
        }
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
    public async Task DevelopmentOpenApiAndSwaggerUiEndpointsAreAvailable()
    {
        var openApiResponse = await client.GetAsync("/openapi/v1.json");
        var swaggerResponse = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);
        Assert.Equal("text/html", swaggerResponse.Content.Headers.ContentType?.MediaType);
        using var openApi = JsonDocument.Parse(await openApiResponse.Content.ReadAsStreamAsync());
        var exportPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../docs/openapi/erpflow-v1.json"));
        if (Environment.GetEnvironmentVariable("ERPFLOW_EXPORT_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
            await File.WriteAllTextAsync(exportPath, openApi.RootElement.GetRawText() + Environment.NewLine);
        }
        using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(exportPath));
        Assert.True(JsonElement.DeepEquals(snapshot.RootElement, openApi.RootElement), "Regenerate the OpenAPI snapshot after API contract changes.");
        Assert.DoesNotContain(Password, openApi.RootElement.GetRawText());
        Assert.DoesNotContain("integration-test-only-signing-key", openApi.RootElement.GetRawText());
        var expectedOperations = new (string Path, string Method)[]
        {
            ("/api/auth/login", "post"),
            ("/api/reference/customers", "get"),
            ("/api/reference/products", "get"),
            ("/api/sales-orders", "get"),
            ("/api/sales-orders", "post"),
            ("/api/sales-orders/{id}", "get"),
            ("/api/sales-orders/{id}/history", "get"),
            ("/api/sales-orders/{id}/submit", "post"),
            ("/api/sales-orders/pending", "get"),
            ("/api/sales-orders/{id}/approve", "post"),
            ("/api/sales-orders/{id}/reject", "post")
        };
        foreach (var (path, method) in expectedOperations)
        {
            var operation = openApi.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
            if (path != "/api/auth/login")
            {
                Assert.Contains(operation.GetProperty("security").EnumerateArray(),
                    requirement => requirement.TryGetProperty("Bearer", out _));
            }
        }
        var bearerScheme = openApi.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");
        Assert.Equal("http", bearerScheme.GetProperty("type").GetString());
        Assert.Equal("bearer", bearerScheme.GetProperty("scheme").GetString());
        var pendingOperation = openApi.RootElement
            .GetProperty("paths")
            .GetProperty("/api/sales-orders/pending")
            .GetProperty("get");
        Assert.Contains(
            pendingOperation.GetProperty("security").EnumerateArray(),
            requirement => requirement.TryGetProperty("Bearer", out _));
        var loginOperation = openApi.RootElement
            .GetProperty("paths")
            .GetProperty("/api/auth/login")
            .GetProperty("post");
        Assert.False(loginOperation.TryGetProperty("security", out _));
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
        var customer = Assert.Single(customers!, candidate => candidate.Email == "orders@contoso.local");
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

    private async Task<HistorySeedResult> SeedOrderHistoryAsync(string ownerEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
        var owner = await context.AppUsers.SingleAsync(user => user.Email == ownerEmail);
        var customer = await context.Customers.SingleAsync(candidate => candidate.Email == "orders@contoso.local");
        var order = new SalesOrder(customer.Id, owner.Id);
        context.SalesOrders.Add(order);
        await context.SaveChangesAsync();

        var submittedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var rejectedAt = submittedAt.AddMinutes(10);
        var submittedAction = (int)ApprovalAction.Submitted;
        var rejectedAction = (int)ApprovalAction.Rejected;
        var submittedRemarks = "Submitted first.";
        var rejectedReason = "Budget approval is required.";

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO ApprovalHistories (Id, SalesOrderId, UserId, Action, Comments, CreatedAt)
            VALUES ({Guid.NewGuid()}, {order.Id}, {owner.Id}, {rejectedAction}, {rejectedReason}, {rejectedAt});
            INSERT INTO ApprovalHistories (Id, SalesOrderId, UserId, Action, Comments, CreatedAt)
            VALUES ({Guid.NewGuid()}, {order.Id}, {owner.Id}, {submittedAction}, {submittedRemarks}, {submittedAt});
            """);

        return new HistorySeedResult(order.Id, owner.Id, submittedAt, rejectedAt);
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

    private sealed class ApiFactory(string connectionString, string environment = "Development") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:ERPFlowDb"] = connectionString,
                    ["Jwt:Issuer"] = "ERPFlow.Tests",
                    ["Jwt:Audience"] = "ERPFlow.Api.Tests",
                    ["Jwt:SigningKey"] = "integration-test-only-signing-key-at-least-32-characters",
                    ["Jwt:ExpirationMinutes"] = "10",
                    ["DemoUsers:Password"] = Password,
                    ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
                    ["Cors:AllowedOrigins:1"] = "http://localhost:5173"
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

    private sealed record HistorySeedResult(
        Guid OrderId,
        Guid ActorId,
        DateTime SubmittedAt,
        DateTime RejectedAt);
}
