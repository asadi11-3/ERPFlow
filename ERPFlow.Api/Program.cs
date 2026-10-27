using System.Text;
using ERPFlow.Api;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Application.Inventory;
using ERPFlow.Infrastructure;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var activateInventory = args.Contains("--activate-warehouse-inventory", StringComparer.Ordinal);
var resetDevelopmentDatabase = args.Contains("--reset-development-database", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(arg => arg is not "--activate-warehouse-inventory" and not "--reset-development-database").ToArray());

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddPolicy("DevelopmentFrontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins);
        }

        policy.WithMethods("GET", "POST", "PUT")
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key");
    }));
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .Validate(options => options.IsValid(), "JWT configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<ERPFlow.Application.Abstractions.ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
        ?? throw new InvalidOperationException("JWT configuration is required.");
    jwt.Validate();
    options.MapInboundClaims = false;
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var subject = context.Principal?.FindFirst("sub")?.Value;
            var database = context.HttpContext.RequestServices.GetRequiredService<ERPFlowDbContext>();
            if (!Guid.TryParse(subject, out var userId) ||
                !await database.AppUsers.AsNoTracking().AnyAsync(user => user.Id == userId,
                    context.HttpContext.RequestAborted))
                context.Fail("The session user no longer exists. Sign in again.");
        }
    };
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        NameClaimType = "name",
        RoleClaimType = "role",
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(StockTransferEndpoints.Policy, policy => policy.RequireRole(UserRole.Administrator.ToString(), UserRole.WarehouseOperator.ToString()));
    options.AddPolicy(InventoryQueryEndpoints.Policy, policy => policy.RequireRole(UserRole.Administrator.ToString(), UserRole.WarehouseOperator.ToString(), UserRole.Purchasing.ToString(), UserRole.Approver.ToString()));
    options.AddPolicy(GoodsReceiptEndpoints.Policy, policy => policy.RequireRole(UserRole.Administrator.ToString(), UserRole.WarehouseOperator.ToString()));
    options.AddPolicy(ProcurementPolicies.Administrator, policy => policy.RequireRole(UserRole.Administrator.ToString()));
    options.AddPolicy(ProcurementPolicies.Write, policy => policy.RequireRole(UserRole.Administrator.ToString(), UserRole.Purchasing.ToString()));
    options.AddPolicy(ProcurementPolicies.CommercialRead, policy => policy.RequireRole(UserRole.Administrator.ToString(), UserRole.Purchasing.ToString(), UserRole.Approver.ToString()));
    options.AddPolicy(ProcurementPolicies.OperationalRead, policy => policy.RequireRole(UserRole.Administrator.ToString(), UserRole.Purchasing.ToString(), UserRole.Approver.ToString(), UserRole.WarehouseOperator.ToString()));
    options.AddPolicy(ApiPolicies.Sales, policy =>
        policy.RequireRole(UserRole.Sales.ToString(), UserRole.Administrator.ToString()));
    options.AddPolicy(ApiPolicies.Approver, policy =>
        policy.RequireRole(UserRole.Approver.ToString(), UserRole.Administrator.ToString()));
    options.AddPolicy(ApiPolicies.Technician, policy =>
        policy.RequireRole(UserRole.Technician.ToString(), UserRole.Administrator.ToString()));
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT returned by POST /api/auth/login"
        };
        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (!metadata.OfType<IAuthorizeData>().Any() || metadata.OfType<IAllowAnonymous>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(
                JwtBearerDefaults.AuthenticationScheme,
                context.Document)] = []
        });
        return Task.CompletedTask;
    });
});
builder.Services.AddInfrastructure(serviceProvider =>
    serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("ERPFlowDb")
        ?? throw new InvalidOperationException("Connection string 'ERPFlowDb' is not configured."));

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<DevelopmentInventory>();
    builder.Services.AddScoped<IInventoryWriteGate>(sp => sp.GetRequiredService<DevelopmentInventory>());
}

if (activateInventory && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Warehouse activation command requires Development environment.");
if (resetDevelopmentDatabase && (!activateInventory || !builder.Environment.IsDevelopment()))
    throw new InvalidOperationException("Development database reset requires the Development warehouse activation command.");

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseRouting();
if (app.Environment.IsDevelopment())
{
    app.UseCors("DevelopmentFrontend");
}
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "ERPFlow API v1");
        options.EnablePersistAuthorization();
    });
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ERPFlowDbContext>();
    if (resetDevelopmentDatabase)
        await context.Database.EnsureDeletedAsync();
    await context.Database.MigrateAsync();
    var demoPassword = builder.Configuration["DemoUsers:Password"] ?? DevelopmentDataSeeder.DefaultPassword;
    var warehouseActive = await context.InventoryAuthorities.AsNoTracking()
        .AnyAsync(a => a.Mode == InventoryAuthorityMode.Warehouse);
    if (!warehouseActive && !string.IsNullOrWhiteSpace(demoPassword))
    {
        await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>()
            .SeedAsync(app.Environment.EnvironmentName, demoPassword, allowProduction: true);
    }
    if (activateInventory)
    {
        await scope.ServiceProvider.GetRequiredService<DevelopmentInventory>().ActivateAsync();
        app.Logger.LogInformation("Development warehouse inventory activated and reconciled.");
    }
}

if (activateInventory) return;

app.MapApiEndpoints();
app.Run();

public partial class Program;
