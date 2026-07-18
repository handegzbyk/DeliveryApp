using System.Threading.Channels;

namespace ShopDelivery.Api.Enrichment;

// Thread-safe queue of product IDs waiting to be enriched.
public interface IEnrichmentQueue
{
    ValueTask EnqueueAsync(int productId, CancellationToken ct = default);
    IAsyncEnumerable<int> DequeueAllAsync(CancellationToken ct);
}

public class EnrichmentQueue : IEnrichmentQueue
{
    private readonly Channel<int> _channel =
        Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = true,   // one background worker
            SingleWriter = false   // many request threads may enqueue
        });

    public ValueTask EnqueueAsync(int productId, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(productId, ct);

    public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}