using System.Text;
using ERPFlow.Api;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Application.Inventory;
using ERPFlow.Infrastructure;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.ProductImages;
using ERPFlow.Application.ProductImages;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ERPFlow.Infrastructure.Deployment;
using Microsoft.AspNetCore.Http.Features;

var activateInventory = args.Contains("--activate-warehouse-inventory", StringComparer.Ordinal);
var resetDevelopmentDatabase = args.Contains("--reset-development-database", StringComparer.Ordinal);
var seedProfessionalDemo = args.Contains("--seed-professional-demo", StringComparer.Ordinal);
var dryRunProfessionalDemo = args.Contains("--dry-run", StringComparer.Ordinal);
var demoDatabaseName = ReadOption(args, "--demo-database");
var demoPassword = ReadOption(args, "--demo-password") ?? Environment.GetEnvironmentVariable("ERPFLOW_DEMO_PASSWORD");
var builderArgs = args.Where(arg => arg is not "--activate-warehouse-inventory" and not "--reset-development-database" and
    not "--seed-professional-demo" and not "--dry-run" && !arg.StartsWith("--demo-database=", StringComparison.Ordinal) &&
    !arg.StartsWith("--demo-password=", StringComparison.Ordinal)).ToArray();
if (demoDatabaseName is not null)
    builderArgs = builderArgs.Where(arg => arg != "--demo-database" && arg != demoDatabaseName).ToArray();
if (demoPassword is not null)
    builderArgs = builderArgs.Where(arg => arg != "--demo-password" && arg != demoPassword).ToArray();
var builder = WebApplication.CreateBuilder(builderArgs);
ProductionHosting.AddServices(builder);

if (seedProfessionalDemo && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Professional demo seeding requires the Development environment.");
if (seedProfessionalDemo && string.IsNullOrWhiteSpace(demoDatabaseName))
    throw new InvalidOperationException("Professional demo seeding requires --demo-database with a new local database name.");
if (seedProfessionalDemo && !dryRunProfessionalDemo && string.IsNullOrWhiteSpace(demoPassword))
    throw new InvalidOperationException("Professional demo seeding requires ERPFLOW_DEMO_PASSWORD or --demo-password.");

var connectionString = seedProfessionalDemo
    ? ProfessionalDemoDatabaseGuard.BuildLocalConnectionString(demoDatabaseName!)
    : string.Empty;

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddPolicy("DevelopmentFrontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins);
        }

        policy.WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "If-Match", "If-None-Match")
            .WithExposedHeaders("ETag", "Location");
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
    options.AddPolicy(ProductImageEndpoints.WritePolicy, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser().RequireRole(UserRole.Administrator.ToString()));
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
    seedProfessionalDemo
        ? connectionString
        : serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("ERPFlowDb")
            ?? throw new InvalidOperationException("Connection string 'ERPFlowDb' is not configured."));
builder.Services.AddSingleton<IProductImageStorage>(_ => new LocalProductImageStorage(new ProductImageOptions
{
    RootPath = builder.Configuration["ProductImages:RootPath"],
    ForbiddenRoots = [builder.Environment.ContentRootPath, builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot")]
}));
builder.Services.AddHostedService<ProductImageCleanupWorker>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<DevelopmentDataSeeder>();
    builder.Services.AddScoped<ProfessionalDemoSeeder>();
    builder.Services.AddScoped<DevelopmentInventory>();
    builder.Services.AddScoped<IInventoryWriteGate>(sp => sp.GetRequiredService<DevelopmentInventory>());
}
else builder.Services.AddProductionInventory();

if (activateInventory && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Warehouse activation command requires Development environment.");
if (resetDevelopmentDatabase && (!activateInventory || !builder.Environment.IsDevelopment()))
    throw new InvalidOperationException("Development database reset requires the Development warehouse activation command.");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    ProductionHosting.Validate(app.Configuration);
    if (ProductionHosting.Proxies(app.Configuration).Length > 0) app.UseForwardedHeaders();
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseRouting();
app.Use(async (http, next) =>
{
    if (http.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
        http.Request.Path.Equals("/api/auth/login/", StringComparison.OrdinalIgnoreCase))
    {
        var size = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (size is { IsReadOnly: false }) size.MaxRequestBodySize = 16 * 1024;
        if (http.Request.ContentLength > 16 * 1024)
        {
            await Results.Problem(statusCode: 413, title: "Request payload too large").ExecuteAsync(http);
            return;
        }
        http.Request.Body = new BoundedRequestStream(http.Request.Body, 16 * 1024);
    }
    await next(http);
});
if (app.Environment.IsDevelopment())
{
    app.UseCors("DevelopmentFrontend");
}
else app.UseCors(ProductionHosting.CorsPolicy);
if (!app.Environment.IsDevelopment() && !Environment.Is64BitProcess)
    app.UseMiddleware<ConstrainedRequestMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ProductImageRequestMiddleware>();

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
    if (seedProfessionalDemo)
    {
        var result = await scope.ServiceProvider.GetRequiredService<ProfessionalDemoSeeder>()
            .SeedAsync(new ProfessionalDemoSeedOptions(demoDatabaseName!, demoPassword ?? string.Empty, dryRunProfessionalDemo));
        app.Logger.LogInformation("Professional demo seed {Status}: {Summary}", result.Applied ? "applied" : "validated", result.Summary);
        return;
    }

    if (resetDevelopmentDatabase)
        await context.Database.EnsureDeletedAsync();
    if (app.Environment.IsDevelopment()) await context.Database.MigrateAsync();
    else
    {
        var stage = ProductionStartupStage.Schema;
        try
        {
            await scope.ServiceProvider.GetRequiredService<ProductionInventoryAdmission>()
                .InitializeAsync(context, schemaVerified: () => stage = ProductionStartupStage.InventoryAuthority);
            stage = ProductionStartupStage.ImagePath;
            var storage = scope.ServiceProvider.GetRequiredService<IProductImageStorage>();
            await ProductionReadiness.VerifyImagesAsync(context, storage, CancellationToken.None, current => stage = current);
        }
        catch (Exception exception)
        {
            var code = ProductionStartupFailure.Code(stage, exception);
            app.Logger.LogError("Production prerequisites failed. FailureCode={FailureCode}", code);
            throw new InvalidOperationException($"Production prerequisites failed ({code}). Runtime startup does not apply migrations.");
        }
    }
    if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("DemoUsers:EnableAutomaticSeed"))
    {
        var configuredDemoPassword = builder.Configuration["DemoUsers:Password"];
        if (!string.IsNullOrWhiteSpace(configuredDemoPassword))
            await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>()
                .SeedAsync(app.Environment.EnvironmentName, configuredDemoPassword);
    }
    if (activateInventory)
    {
        await scope.ServiceProvider.GetRequiredService<DevelopmentInventory>().ActivateAsync();
        app.Logger.LogInformation("Development warehouse inventory activated and reconciled.");
    }
}

if (activateInventory) return;

app.MapApiEndpoints();
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous().ExcludeFromDescription();
app.MapGet("/health/ready", async (ProductionReadiness readiness, CancellationToken ct) =>
    await readiness.CheckAsync(ct) ? Results.Ok(new { status = "ready" }) :
        Results.Json(new { status = "not-ready" }, statusCode: 503)).AllowAnonymous().ExcludeFromDescription();
app.Run();

static string? ReadOption(string[] arguments, string name)
{
    var prefix = name + "=";
    var inline = arguments.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal));
    if (inline is not null) return inline[prefix.Length..];
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

public partial class Program;
