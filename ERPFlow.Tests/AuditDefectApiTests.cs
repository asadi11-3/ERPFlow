using System.Net;
using System.Net.Http.Json;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class ApiIntegrationTests
{
    private async Task<SalesOrderResult> CreateAuditDraftAsync(int quantity = 1)
    {
        SetToken(await LoginAsync("sales@erpflow.local"));
        var customers = await client.GetFromJsonAsync<CustomerReferenceDto[]>("/api/reference/customers");
        var products = await client.GetFromJsonAsync<ProductReferenceDto[]>("/api/reference/products");
        var response = await client.PostAsJsonAsync("/api/sales-orders", new CreateSalesOrderCommand(
            customers![0].Id, [new CreateSalesOrderItem(products!.First(p => p.Sku == "SCAN-100").Id, quantity)]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SalesOrderResult>())!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullOrderLinesReturnBadRequestWithoutCreatingOrders(bool mixed)
    {
        var draft = await CreateAuditDraftAsync();
        var before = await client.GetFromJsonAsync<SalesOrderPage>("/api/sales-orders");
        var lines = mixed ? new object?[] { new { productId = draft.Items[0].ProductId, quantity = 1 }, null } : [null];
        var response = await client.PostAsJsonAsync("/api/sales-orders", new { customerId = draft.CustomerId, items = lines });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var after = await client.GetFromJsonAsync<SalesOrderPage>("/api/sales-orders");
        Assert.Equal(before!.TotalCount, after!.TotalCount);
    }

    [Theory]
    [InlineData("approve", 1000, HttpStatusCode.OK)]
    [InlineData("approve", 1001, HttpStatusCode.BadRequest)]
    [InlineData("reject", 1000, HttpStatusCode.OK)]
    [InlineData("reject", 1001, HttpStatusCode.BadRequest)]
    [InlineData("return", 1000, HttpStatusCode.OK)]
    [InlineData("return", 1001, HttpStatusCode.BadRequest)]
    public async Task DecisionTextHonorsStorageBoundary(string action, int length, HttpStatusCode expected)
    {
        var draft = await CreateAuditDraftAsync();
        (await client.PostAsync($"/api/sales-orders/{draft.Id}/submit", null)).EnsureSuccessStatusCode();
        SetToken(await LoginAsync("approver@erpflow.local"));
        var text = new string('x', length);
        var response = await client.PostAsJsonAsync($"/api/sales-orders/{draft.Id}/{action}", new { comments = text, reason = text });
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            var current = await client.GetFromJsonAsync<SalesOrderResult>($"/api/sales-orders/{draft.Id}");
            Assert.Equal(OrderStatus.PendingApproval, current!.Status);
            Assert.False(Assert.Single(current.Reservations).IsReleased);
            Assert.Single((await client.GetFromJsonAsync<ApprovalHistoryResult[]>($"/api/sales-orders/{draft.Id}/history"))!);
        }
    }

    [Fact]
    public async Task ReturnedDraftCanBeEditedRepricedAndResubmittedWithoutLosingHistory()
    {
        var draft = await CreateAuditDraftAsync(2);
        (await client.PostAsync($"/api/sales-orders/{draft.Id}/submit", null)).EnsureSuccessStatusCode();
        SetToken(await LoginAsync("approver@erpflow.local"));
        (await client.PostAsJsonAsync($"/api/sales-orders/{draft.Id}/return", new { reason = "Correct quantity" })).EnsureSuccessStatusCode();
        SetToken(await LoginAsync("sales@erpflow.local"));
        var returned = (await client.GetFromJsonAsync<SalesOrderResult>($"/api/sales-orders/{draft.Id}"))!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET UnitPrice=123 WHERE Id={draft.Items[0].ProductId}");
        }
        var command = new UpdateSalesOrderCommand(draft.CustomerId,
            [new CreateSalesOrderItem(draft.Items[0].ProductId, 3)], returned.RowVersion);
        var response = await client.PutAsJsonAsync($"/api/sales-orders/{draft.Id}", command);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<SalesOrderResult>())!;
        Assert.Equal(draft.Id, edited.Id);
        Assert.Equal(123m, Assert.Single(edited.Items).UnitPrice);
        Assert.Equal(3, edited.Items[0].Quantity);
        Assert.True(Assert.Single(edited.Reservations).IsReleased);
        Assert.NotEqual(returned.RowVersion, edited.RowVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/sales-orders/{draft.Id}", command)).StatusCode);
        var submit = await client.PostAsync($"/api/sales-orders/{draft.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var submitted = (await submit.Content.ReadFromJsonAsync<SalesOrderResult>())!;
        Assert.Equal(3, Assert.Single(submitted.Reservations).Quantity);
        Assert.False(submitted.Reservations[0].IsReleased);
        var history = await client.GetFromJsonAsync<ApprovalHistoryResult[]>($"/api/sales-orders/{draft.Id}/history");
        Assert.Equal([ApprovalAction.Submitted, ApprovalAction.Returned, ApprovalAction.Submitted], history!.Select(h => h.Action));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/sales-orders/{draft.Id}", command with { RowVersion = submitted.RowVersion })).StatusCode);
    }

    [Theory]
    [InlineData("sales@erpflow.local", HttpStatusCode.OK)]
    [InlineData("sales2@erpflow.local", HttpStatusCode.Forbidden)]
    [InlineData("approver@erpflow.local", HttpStatusCode.Forbidden)]
    [InlineData("tech@erpflow.local", HttpStatusCode.Forbidden)]
    [InlineData("admin@erpflow.local", HttpStatusCode.OK)]
    public async Task DraftEditingEnforcesRoleAndOwner(string email, HttpStatusCode expected)
    {
        var draft = await CreateAuditDraftAsync();
        SetToken(await LoginAsync(email));
        var response = await client.PutAsJsonAsync($"/api/sales-orders/{draft.Id}", new UpdateSalesOrderCommand(
            draft.CustomerId, [new CreateSalesOrderItem(draft.Items[0].ProductId, 2)], draft.RowVersion));
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("sales@erpflow.local")]
    [InlineData("approver@erpflow.local")]
    [InlineData("tech@erpflow.local")]
    [InlineData("admin@erpflow.local")]
    public async Task EveryRoleCanDiscoverPermittedOrders(string email)
    {
        SetToken(await LoginAsync(email));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/sales-orders")).StatusCode);
    }

    [Fact]
    public async Task HugeDraftDispatchIsRejectedBeforeSerialExpansion()
    {
        var draft = await CreateAuditDraftAsync(int.MaxValue);
        SetToken(await LoginAsync("tech@erpflow.local"));
        var response = await client.PostAsJsonAsync($"/api/equipment/sales-orders/{draft.Id}/dispatch",
            new { carrier = "Carrier", serialNumbers = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var current = (await client.GetFromJsonAsync<SalesOrderResult>($"/api/sales-orders/{draft.Id}"))!;
        Assert.Equal(OrderStatus.Draft, current.Status);
        Assert.Empty(current.Reservations);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"AQ==\"")]
    public async Task InvalidEditVersionReturnsBadRequest(string versionJson)
    {
        var draft = await CreateAuditDraftAsync();
        var json = $$"""{"customerId":"{{draft.CustomerId}}","items":[{"productId":"{{draft.Items[0].ProductId}}","quantity":2}],"rowVersion":{{versionJson}}} """;
        var response = await client.PutAsync($"/api/sales-orders/{draft.Id}", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
