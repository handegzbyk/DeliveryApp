using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace ShopDelivery.Web.Auth;

public sealed class ApiAuthorizationMessageHandler : AuthorizationMessageHandler
{
    public ApiAuthorizationMessageHandler(
        IAccessTokenProvider provider,
        NavigationManager navigation,
        IConfiguration configuration)
        : base(provider, navigation)
    {
        var apiBaseUrl = configuration["ResolvedApiBaseUrl"]
                         ?? throw new InvalidOperationException("The API base URL is not configured.");
        var apiScope = configuration["Authentication:ApiScope"]
                       ?? throw new InvalidOperationException("Authentication:ApiScope is not configured.");

        ConfigureHandler([apiBaseUrl], [apiScope]);
    }
}
