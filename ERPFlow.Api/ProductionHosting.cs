using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

namespace ERPFlow.Api;

public static class ProductionHosting
{
    public const string CorsPolicy = "ProductionFrontend";
    public const string LoginPolicy = "LoginAbuseProtection";

    public static void AddServices(WebApplicationBuilder builder)
    {
        if (builder.Environment.IsProduction() && builder.Configuration["ProductImages:RootPath"] is null)
            builder.Configuration["ProductImages:RootPath"] = Path.GetFullPath(
                Path.Combine(builder.Environment.ContentRootPath, "..", "private", "ProductImages"));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<LoginAttemptLimiter>();
        builder.Services.AddSingleton<ProductionReadiness>();
        builder.Services.AddSingleton<ConstrainedRequestAdmission>();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(LoginPolicy, http => builder.Environment.IsDevelopment()
                ? RateLimitPartition.GetNoLimiter("development")
                : RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
                {
                    PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                }));
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await Results.Problem(statusCode: 429, title: "Too many login attempts",
                    detail: "Wait before trying again.").ExecuteAsync(context.HttpContext);
            };
        });
        if (builder.Environment.IsDevelopment()) return;
        builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            var origins = builder.Configuration.GetSection("Cors:ProductionOrigins").Get<string[]>() ?? [];
            if (origins.Length > 0) policy.WithOrigins(origins);
            policy.WithMethods("GET", "POST", "PUT", "DELETE")
                .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "If-Match", "If-None-Match")
                .WithExposedHeaders("ETag", "Location");
        }));
        builder.Services.AddHttpsRedirection(options => { options.HttpsPort = 443; options.RedirectStatusCode = 308; });
        if (Proxies(builder.Configuration).Length > 0) builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in Proxies(builder.Configuration)) options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
    }

    public static void Validate(IConfiguration configuration)
    {
        var hosts = (configuration["AllowedHosts"] ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (hosts.Length == 0 || hosts.Any(host => host.Contains('*') || Uri.CheckHostName(host) == UriHostNameType.Unknown))
            throw new InvalidOperationException("Production requires explicit AllowedHosts without wildcards or ports.");
        var origins = configuration.GetSection("Cors:ProductionOrigins").Get<string[]>() ?? [];
        if (origins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || origin.Contains('*') || uri.UserInfo.Length > 0 ||
            uri.GetLeftPart(UriPartial.Authority) != origin))
            throw new InvalidOperationException("Production CORS origins must be exact HTTPS origins without credentials, wildcards, paths or trailing slashes.");
        if (Proxies(configuration).Any(proxy => !IPAddress.TryParse(proxy, out _)))
            throw new InvalidOperationException("Reverse-proxy trust requires explicit IP addresses.");
        var root = configuration["ProductImages:RootPath"];
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("Production requires an absolute external product-image directory.");
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("ERPFlowDb")))
            throw new InvalidOperationException("Production database configuration is required.");
        (configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new()).Validate();
        if (configuration.GetValue<bool>("DemoUsers:EnableAutomaticSeed"))
            throw new InvalidOperationException("Demo seeding cannot be enabled in Production.");
    }

    public static string[] Proxies(IConfiguration configuration) => configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
}
