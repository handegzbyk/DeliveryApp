# DeliveryApp

## Customer authentication and ownership

The app uses OpenID Connect in the Blazor client and validates bearer access tokens in the API.
The API derives a pseudonymous customer id from the validated token issuer and subject; customer
ids are never accepted from request bodies or query parameters.

- Shared globally: products, brands, stores, store-product aliases, and product matching.
- Customer-owned: receipts, receipt prices, monthly budget settings, and budget dashboards.
- Existing receipts created before customer ownership was introduced are assigned to
  `legacy-unassigned` and are not exposed to any logged-in customer.

For local Development, both projects use the development-only `local-customer` identity. The API
also accepts `X-Development-Customer` in Development so isolation can be tested with two simulated
customers. This header is never enabled outside Development.

For a deployed environment, register a browser/OpenID Connect application and an API scope with
your identity provider. Add these redirect URIs to the browser application:

- `https://<web-host>/authentication/login-callback`
- `https://<web-host>/authentication/logout-callback`

Set the public browser values in `ShopDelivery.Web/wwwroot/appsettings.json`:

```json
"Authentication": {
  "Authority": "https://<identity-provider>/<tenant>",
  "ClientId": "<browser-client-id>",
  "ApiScope": "<api-scope>"
}
```

Set the API validation values before provisioning:

```bash
azd env set AUTHENTICATION_AUTHORITY "https://<identity-provider>/<tenant>"
azd env set AUTHENTICATION_AUDIENCE "<api-audience>"
```

Production startup intentionally fails if authentication is not configured; it never falls back
to a client-supplied customer id.
