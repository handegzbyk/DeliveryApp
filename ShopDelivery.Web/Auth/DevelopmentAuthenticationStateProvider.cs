using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace ShopDelivery.Web.Auth;

public sealed class DevelopmentAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState State = new(new ClaimsPrincipal(
        new ClaimsIdentity(
        [
            new Claim("sub", "local-customer"),
            new Claim(ClaimTypes.Name, "Local customer"),
        ],
        authenticationType: "DevelopmentCustomer")));

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(State);
}
