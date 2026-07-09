using Microsoft.EntityFrameworkCore;
using ShopDelivery.Ai;
using ShopDelivery.Api.Data;
using ShopDelivery.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

var sqlConnection = builder.Configuration.GetConnectionString("Sql");

builder.Services.AddDbContext<ShopDbContext>(o =>
{
    if (string.IsNullOrWhiteSpace(sqlConnection))
        o.UseInMemoryDatabase("ShopDelivery");   // dev / Codespaces: no SQL needed
    else
        o.UseSqlServer(sqlConnection);            // production: Azure SQL via config
});

builder.Services.AddSignalR();
builder.Services.AddAiServices(builder.Configuration); // extension from ShopDelivery.Ai
builder.Services.AddCors();

builder.Services.AddDbContext<ShopDbContext>(o =>
{
    if (!string.IsNullOrWhiteSpace(sqlConnection))
        o.UseSqlServer(sqlConnection);              // production: Azure SQL
    else if (builder.Environment.IsDevelopment())
        o.UseSqlite("Data Source=shopdelivery.db"); // Codespaces: local file, persists
    else
        o.UseInMemoryDatabase("ShopDelivery");      // fallback
});

var app = builder.Build();

// Create the store and apply seed data (also works with the InMemory provider).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

app.MapGet("/api/products", (ShopDbContext db) => db.Products.ToListAsync());
app.MapHub<DeliveryHub>("/hubs/delivery");   // realtime tracking

app.Run();