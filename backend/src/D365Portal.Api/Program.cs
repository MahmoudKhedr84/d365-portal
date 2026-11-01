using System.Threading.RateLimiting;
using Azure.Identity;
using D365Portal.Api;
using D365Portal.Api.Admin;
using D365Portal.Core.D365;
using FluentValidation;
using System.Text.Json.Serialization;
using D365Portal.Api.Auth;
using D365Portal.Api.Endpoints;
using D365Portal.Api.Tenancy;
using D365Portal.Core.Identity;
using D365Portal.Infrastructure;
using D365Portal.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Secrets: Azure Key Vault in production (managed identity / DefaultAzureCredential) ----------
if (config["KeyVault:Uri"] is { Length: > 0 } keyVaultUri)
{
    config.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

// ---------- Infrastructure (DB, tenancy, secret protection, audit) ----------
builder.Services.AddInfrastructure(config);
var dataProtection = builder.Services.AddPortalDataProtection();
if (config["DataProtection:KeyVaultKeyId"] is { Length: > 0 } dpKeyId)
{
    dataProtection.ProtectKeysWithAzureKeyVault(new Uri(dpKeyId), new DefaultAzureCredential());
}
builder.Services.Configure<PlatformOptions>(config.GetSection(PlatformOptions.SectionName));

// ---------- Authentication: Entra ID access tokens for this API (multi-tenant) ----------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(config.GetSection("AzureAd"));

var requiredScope = config["AzureAd:Scope"] ?? "access_as_user";
builder.Services.AddAuthorization(o => Policies.Configure(o, requiredScope));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IAuthorizationHandler, TenantAuthorizationHandler>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

// ---------- CORS: only the configured frontend origin(s) ----------
var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .WithHeaders("Authorization", "Content-Type", TenantResolutionMiddleware.TenantHeader, "X-Company")
    .WithMethods("GET", "POST", "PATCH", "PUT", "DELETE")
    .SetPreflightMaxAge(TimeSpan.FromHours(1))));

// ---------- Rate limiting: per signed-in user (oid), else per IP ----------
var permitPerMinute = config.GetValue("RateLimiting:PermitPerMinute", 300);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
        var key = http.User.ToSignedInIdentity()?.ObjectId
                  ?? http.Connection.RemoteIpAddress?.ToString()
                  ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
    o.OnRejected = (ctx, _) =>
        new ValueTask(Problems.Write(ctx.HttpContext, 429, "rate_limited", "Too many requests. Please slow down."));
});

// ---------- Application services ----------
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<D365Portal.Api.Modules.PurchaseRequisitionService>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: false);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---------- Errors, health, OpenAPI ----------
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "D365 Portal API", Version = "v1" });
    var bearer = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Entra ID access token for this API (scope api://<api-client-id>/access_as_user).",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    o.AddSecurityDefinition("Bearer", bearer);
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = Array.Empty<string>() });
    o.AddSecurityDefinition("Tenant", new OpenApiSecurityScheme
    {
        Name = TenantResolutionMiddleware.TenantHeader,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Tenant id (GUID) for tenant-scoped endpoints.",
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Tenant" } }]
            = Array.Empty<string>(),
    });
});

// Behind nginx / Front Door / App Service: trust X-Forwarded-* only from configured proxies
// (loopback by default). ForwardedHeaders:KnownNetworks = CIDR list, e.g. "172.16.0.0/12" for a Docker network.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    foreach (var cidr in config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? Array.Empty<string>())
    {
        var parts = cidr.Split('/');
        o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
            System.Net.IPAddress.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : 32));
    }
});

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services, config);

app.UseForwardedHeaders();
app.UseExceptionHandler(errorApp => errorApp.Run(async http =>
{
    var error = http.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled");
    if (error is AdminException admin)
    {
        await Problems.Write(http, admin.Status, admin.Code, admin.Message);
        return;
    }
    if (error is D365Exception d365)
    {
        logger.LogWarning("D365 call failed: {Code} {Status}", d365.Code, d365.StatusCode);
        var status = D365ErrorCodes.IsConfiguration(d365.Code) ? 409
            : d365.Code == D365ErrorCodes.Validation ? 422
            : d365.Code == "d365_entity_not_found" ? 404
            : 502;
        await Problems.Write(http, status, d365.Code ?? "d365_error", d365.Message);
        return;
    }
    if (error is BadHttpRequestException)
    {
        await Problems.Write(http, 400, "bad_request", "The request body is missing or malformed.");
        return;
    }
    if (error is TenantIsolationException)
    {
        logger.LogWarning(error, "Tenant isolation violation blocked");
        await Problems.Write(http, 403, "tenant_isolation", "Access to another tenant's data is not allowed.");
        return;
    }
    logger.LogError(error, "Unhandled exception");
    await Problems.Write(http, 500, "server_error", "Something went wrong. Reference: " + http.TraceIdentifier);
}));

if (config.GetValue("Hosting:RequireHttps", !app.Environment.IsDevelopment()))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (config.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "D365 Portal API");
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter(); // before tenant resolution so tenant lookups are rate limited too
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/health/db", async (AppDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct) ? Results.Ok(new { database = "ok" }) : Results.StatusCode(503))
    .AllowAnonymous()
    .ExcludeFromDescription();

app.MapSessionEndpoints();
app.MapAdminEndpoints();
app.MapLookupEndpoints();
app.MapPurchaseRequisitionEndpoints();

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
