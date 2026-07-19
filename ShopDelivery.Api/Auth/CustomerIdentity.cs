using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace ShopDelivery.Api.Auth;

public sealed class CustomerIdentity
{
    public string GetRequiredCustomerId(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            throw new InvalidOperationException("An authenticated customer is required.");

        var subject = principal.FindFirstValue("sub")
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("oid");
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("The authenticated identity has no stable subject claim.");

        var issuer = principal.FindFirstValue("iss")
                     ?? principal.FindFirst(claim => claim.Type is "sub" or ClaimTypes.NameIdentifier or "oid")?.Issuer
                     ?? "unknown-issuer";

        var identityBytes = Encoding.UTF8.GetBytes($"{issuer.Trim()}\n{subject.Trim()}");
        return Convert.ToHexString(SHA256.HashData(identityBytes)).ToLowerInvariant();
    }
}
