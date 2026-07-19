using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ShopDelivery.Api.Auth;

public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevelopmentCustomer";
    public const string CustomerHeader = "X-Development-Customer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var requestedCustomer = Request.Headers[CustomerHeader].FirstOrDefault();
        var customer = string.IsNullOrWhiteSpace(requestedCustomer)
            ? configuration["Authentication:DevelopmentCustomerId"] ?? "local-customer"
            : requestedCustomer.Trim();

        var claims = new[]
        {
            new Claim("sub", customer, ClaimValueTypes.String, "development"),
            new Claim("iss", "development"),
            new Claim(ClaimTypes.Name, customer),
        };
        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
