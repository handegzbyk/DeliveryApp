namespace ShopDelivery.CatalogScraper.Networking;

public sealed class PoliteHttpClient(
    HttpClient http,
    TimeSpan minimumDelay,
    IReadOnlySet<string> allowedHosts)
{
    private readonly Dictionary<string, DateTimeOffset> _lastRequests =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<HttpResponseMessage> GetAsync(
        Uri uri,
        TimeSpan? siteDelay,
        HttpCompletionOption completionOption,
        CancellationToken ct)
    {
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            await WaitForTurnAsync(uri, siteDelay, ct);
            var response = await http.GetAsync(uri, completionOption, ct);
            if (!IsRedirect(response) || response.Headers.Location is null)
                return response;

            var destination = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(uri, response.Headers.Location);
            if (!IsAllowed(destination)
                || !uri.GetLeftPart(UriPartial.Authority).Equals(
                    destination.GetLeftPart(UriPartial.Authority),
                    StringComparison.OrdinalIgnoreCase))
            {
                return response;
            }

            response.Dispose();
            uri = destination;
        }

        throw new HttpRequestException("The request exceeded five allowed redirects.");
    }

    private async Task WaitForTurnAsync(Uri uri, TimeSpan? siteDelay, CancellationToken ct)
    {
        var delay = siteDelay is { } requested && requested > minimumDelay
            ? requested
            : minimumDelay;
        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (_lastRequests.TryGetValue(origin, out var lastRequest))
        {
            var remaining = delay - (DateTimeOffset.UtcNow - lastRequest);
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, ct);
        }

        _lastRequests[origin] = DateTimeOffset.UtcNow;
    }

    private bool IsAllowed(Uri uri) =>
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        && allowedHosts.Contains(uri.Host.TrimEnd('.'));

    private static bool IsRedirect(HttpResponseMessage response) =>
        (int)response.StatusCode is >= 300 and <= 399;
}
