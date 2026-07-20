using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ShopDelivery.Web;
using ShopDelivery.Web.Auth;
using ShopDelivery.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Must match the API's https port (HttpsPort in ShopDelivery.Api)
const int apiPort = 5277;

var apiBaseUrl = ResolveApiBaseUrl(
    builder.HostEnvironment.BaseAddress,
    apiPort,
    builder.Configuration["ApiBaseUrl"]);
builder.Configuration["ResolvedApiBaseUrl"] = apiBaseUrl;

var apiClient = builder.Services.AddHttpClient<ShopApiClient>(client =>
    client.BaseAddress = new Uri(apiBaseUrl));

if (builder.HostEnvironment.IsDevelopment())
{
    builder.Services.AddAuthorizationCore();
    builder.Services.AddScoped<AuthenticationStateProvider, DevelopmentAuthenticationStateProvider>();
}
else
{
    var authority = builder.Configuration["Authentication:Authority"];
    var clientId = builder.Configuration["Authentication:ClientId"];
    var apiScope = builder.Configuration["Authentication:ApiScope"];
    var roleClaim = builder.Configuration["Authentication:RoleClaim"] ?? "roles";
    if (string.IsNullOrWhiteSpace(authority)
        || string.IsNullOrWhiteSpace(clientId)
        || string.IsNullOrWhiteSpace(apiScope))
    {
        throw new InvalidOperationException(
            "Authentication:Authority, Authentication:ClientId, and Authentication:ApiScope are required.");
    }

    builder.Services.AddOidcAuthentication(options =>
    {
        options.ProviderOptions.Authority = authority;
        options.ProviderOptions.ClientId = clientId;
        options.ProviderOptions.ResponseType = "code";
        options.ProviderOptions.DefaultScopes.Add(apiScope);
        options.UserOptions.RoleClaim = roleClaim;
    });
    builder.Services.AddScoped<ApiAuthorizationMessageHandler>();
    apiClient.AddHttpMessageHandler<ApiAuthorizationMessageHandler>();
}

await builder.Build().RunAsync();

static string ResolveApiBaseUrl(string webBaseAddress, int apiPort, string? configured)
{
    var uri = new Uri(webBaseAddress);

    // Codespaces host "<codespace>-<port>.app.github.dev":
    // always derive the forwarded API URL — a localhost config value is unreachable here.
    if (uri.Host.EndsWith(".app.github.dev", StringComparison.OrdinalIgnoreCase))
    {
        var dot = uri.Host.IndexOf('.');
        var subdomain = uri.Host[..dot];                 // "<codespace>-<webPort>"
        var lastDash = subdomain.LastIndexOf('-');
        var apiHost = $"{subdomain[..lastDash]}-{apiPort}{uri.Host[dot..]}";
        return $"https://{apiHost}/";
    }

    // Local dev: prefer explicit config, otherwise swap the port.
    return configured is { Length: > 0 }
        ? configured
        : new UriBuilder(uri.Scheme, uri.Host, apiPort) { Path = "/" }.Uri.ToString();
}
