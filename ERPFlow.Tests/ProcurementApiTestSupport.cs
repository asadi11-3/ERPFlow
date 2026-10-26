using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ERPFlow.Api;
using ERPFlow.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ERPFlow.Tests;

public sealed partial class ProcurementApiTests
{
    private Guid Actor(UserRole role) => role switch
    {
        UserRole.Administrator => seed.Inventory.ActorId, UserRole.Purchasing => seed.Purchasing,
        UserRole.Approver => seed.Approver, UserRole.WarehouseOperator => seed.Operator,
        UserRole.Sales => seed.Sales, UserRole.Technician => seed.Technician,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
    private async Task Authenticate(Guid actor)
    {
        await using var context = database.Context();
        var user = await context.AppUsers.AsNoTracking().SingleAsync(x => x.Id == actor);
        var token = factory.Services.GetRequiredService<JwtTokenService>().Create(user).AccessToken;
        Assert.Equal(user.Role.ToString(), new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Single(x => x.Type == "role").Value);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<T> ReadCreated<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
    private sealed class ApiFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ERPFlowDb"] = connection, ["DemoUsers:Password"] = "",
                ["Jwt:Issuer"] = "ERPFlow.Procurement.Tests", ["Jwt:Audience"] = "ERPFlow.Procurement.Tests",
                ["Jwt:SigningKey"] = "isolated-procurement-only-signing-key-at-least-32-characters", ["Jwt:ExpirationMinutes"] = "10"
            }));
        }
    }
}
