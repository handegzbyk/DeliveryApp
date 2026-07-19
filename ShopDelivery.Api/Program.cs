using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShopDelivery.Ai;
using ShopDelivery.Api.Auth;
using ShopDelivery.Api.Budget;
using ShopDelivery.Api.Data;
using ShopDelivery.Api.Enrichment;
using ShopDelivery.Api.Products;
using ShopDelivery.Api.Receipts;

var builder = WebApplication.CreateBuilder(args);

// Codespaces tunnels mishandle HTTP/2 POST bodies (multipart upload) → force HTTP/1.1
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(listen => listen.Protocols = HttpProtocols.Http1);
});

// --- Database: pick a provider based on what's configured ---
var sqlConnection = builder.Configuration.GetConnectionString("Sql");

builder.Services.AddDbContext<ShopDbContext>(o =>
{
    if (!string.IsNullOrWhiteSpace(sqlConnection))
        o.UseSqlServer(sqlConnection);
    else if (builder.Environment.IsDevelopment())
        o.UseSqlite("Data Source=shopdelivery.db");
    else
        o.UseInMemoryDatabase("ShopDelivery");
});

builder.Services.AddReceiptScanning(builder.Configuration);
var useDevelopmentIdentity = builder.Environment.IsDevelopment()
                             && builder.Configuration.GetValue("Authentication:UseDevelopmentIdentity", true);
if (useDevelopmentIdentity)
{
    builder.Services
        .AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
            DevelopmentAuthenticationHandler.SchemeName,
            _ => { });
}
else
{
    var authority = builder.Configuration["Authentication:Authority"];
    var audience = builder.Configuration["Authentication:Audience"];
    if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
    {
        throw new InvalidOperationException(
            "Authentication:Authority and Authentication:Audience are required outside local development.");
    }

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = "name",
                RoleClaimType = "role",
            };
        });
}

builder.Services.AddAuthorization();
builder.Services.AddSingleton<CustomerIdentity>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(origin =>
                {
                    var host = new Uri(origin).Host;
                    return host.EndsWith(".azurestaticapps.net", StringComparison.OrdinalIgnoreCase)
                        || host == "localhost";
                })
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddOpenApi();
builder.Services.AddScoped<ProductMatcher>();
builder.Services.AddScoped<ProductCandidateSeeder>();
builder.Services.AddScoped<ProductCatalogSeeder>();
builder.Services.AddSingleton<IEnrichmentQueue, EnrichmentQueue>();
builder.Services.AddHostedService<EnrichmentWorker>();
builder.Services.AddHttpClient<IProductEnricher, OpenFoodFactsEnricher>(http =>
{
    // OpenFoodFacts requires a descriptive User-Agent; anonymous callers get rate-limited/503'd.
    http.BaseAddress = new Uri("https://world.openfoodfacts.org");
    http.DefaultRequestHeaders.UserAgent.ParseAdd("ShopDelivery/1.0 (+https://github.com/DeliveryApp)");
    http.Timeout = TimeSpan.FromSeconds(10);
});
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    await DatabaseSchemaInitializer.EnsureAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", at = DateTimeOffset.UtcNow }))
   .WithName("HealthCheck");

app.MapBudgetEndpoints();
app.MapProductEndpoints();
app.MapReceiptEndpoints();

app.Run();
