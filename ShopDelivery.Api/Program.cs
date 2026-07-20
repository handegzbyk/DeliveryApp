using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using ShopDelivery.Ai;
using ShopDelivery.Api.Auth;
using ShopDelivery.Api.Budget;
using ShopDelivery.Api.Data;
using ShopDelivery.Api.Products;
using ShopDelivery.Api.Receipts;

var builder = WebApplication.CreateBuilder(args);
var catalogPath = GetOption(args, "--import-catalog");
var sqlitePath = GetOption(args, "--sqlite");
if (catalogPath is not null)
    Console.WriteLine("Catalog import: configuring services...");
builder.WebHost.ConfigureKestrel(options =>
    options.ConfigureEndpointDefaults(listen => listen.Protocols = HttpProtocols.Http1));

var sqlConnection = builder.Configuration.GetConnectionString("Sql");
var sqliteConnection = sqlitePath is null
    ? builder.Configuration.GetConnectionString("Sqlite")
    : $"Data Source={Path.GetFullPath(sqlitePath)}";
builder.Services.AddDbContext<ShopDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(sqliteConnection))
    {
        options.UseSqlite(sqliteConnection);
    }
    else if (!string.IsNullOrWhiteSpace(sqlConnection))
    {
        // The migration operations are provider-neutral and carry both identity annotations.
        // The target snapshot was scaffolded with SQLite, so SQL Server otherwise reports a
        // false pending-model warning even though `dotnet ef` reports no model changes.
        options.UseSqlServer(sqlConnection)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
    else if (builder.Environment.IsDevelopment())
    {
        var sqlitePath = Path.Combine(builder.Environment.ContentRootPath, "shopdelivery.db");
        options.UseSqlite($"Data Source={sqlitePath}");
    }
    else
    {
        options.UseInMemoryDatabase("ShopDelivery");
    }
});

builder.Services.AddReceiptScanning(builder.Configuration);
var useDevelopmentIdentity = catalogPath is not null
                             || (builder.Environment.IsDevelopment()
                                 && builder.Configuration.GetValue("Authentication:UseDevelopmentIdentity", true));
if (useDevelopmentIdentity)
{
    builder.Services.AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
            DevelopmentAuthenticationHandler.SchemeName,
            _ => { });
}
else
{
    var authority = builder.Configuration["Authentication:Authority"];
    var audience = builder.Configuration["Authentication:Audience"];
    var roleClaim = builder.Configuration["Authentication:RoleClaim"] ?? "roles";
    if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
        throw new InvalidOperationException(
            "Authentication:Authority and Authentication:Audience are required outside local development.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = "name",
                RoleClaimType = roleClaim,
            };
        });
}

builder.Services.AddAuthorization(options =>
    options.AddPolicy("CatalogAdmin", policy => policy.RequireRole("admin")));
builder.Services.AddSingleton<CustomerIdentity>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.SetIsOriginAllowed(origin =>
        {
            var host = new Uri(origin).Host;
            return host.EndsWith(".azurestaticapps.net", StringComparison.OrdinalIgnoreCase)
                   || host == "localhost";
        })
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddOpenApi();
builder.Services.AddScoped<ProductMatcher>();
builder.Services.AddScoped<IProductImageStore, DatabaseProductImageStore>();
builder.Services.AddScoped<CatalogImportService>();

var app = builder.Build();
if (catalogPath is not null)
    Console.WriteLine("Catalog import: applying database migrations...");
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    await DatabaseSchemaInitializer.EnsureAsync(db);
}

if (catalogPath is not null)
{
    Console.WriteLine("Catalog import: reading and compressing catalog images...");
    using var scope = app.Services.CreateScope();
    var importer = scope.ServiceProvider.GetRequiredService<CatalogImportService>();
    var result = await importer.ImportAsync(catalogPath, CancellationToken.None);
    Console.WriteLine($"Products created:  {result.CreatedProducts}");
    Console.WriteLine($"Products updated:  {result.UpdatedProducts}");
    Console.WriteLine($"Aliases created:   {result.CreatedAliases}");
    Console.WriteLine($"Review items:      {result.ReviewItems}");
    Console.WriteLine($"Images stored:     {result.StoredImages}");
    Console.WriteLine($"Image source:      {result.SourceImageBytes:N0} bytes");
    Console.WriteLine($"Image stored:      {result.StoredImageBytes:N0} bytes");
    Console.WriteLine($"Image failures:    {result.FailedImages}");
    return;
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", at = DateTimeOffset.UtcNow }));
app.MapBudgetEndpoints();
app.MapProductEndpoints();
app.MapReceiptEndpoints();
await app.RunAsync();

static string? GetOption(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            return arguments[index + 1];
    }
    return null;
}
