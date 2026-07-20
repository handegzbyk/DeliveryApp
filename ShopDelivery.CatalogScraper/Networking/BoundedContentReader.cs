using System.Text;

namespace ShopDelivery.CatalogScraper.Networking;

public static class BoundedContentReader
{
    public static async Task<byte[]> ReadBytesAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException($"Response exceeds the {maximumBytes:N0}-byte limit.");

        await using var source = await content.ReadAsStreamAsync(ct);
        await using var target = new MemoryStream(
            content.Headers.ContentLength is > 0 and <= int.MaxValue
                ? (int)content.Headers.ContentLength.Value
                : 0);
        var buffer = new byte[81_920];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0)
                break;
            total += read;
            if (total > maximumBytes)
                throw new InvalidDataException($"Response exceeds the {maximumBytes:N0}-byte limit.");
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return target.ToArray();
    }

    public static async Task<string> ReadStringAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken ct)
    {
        var bytes = await ReadBytesAsync(content, maximumBytes, ct);
        var encoding = GetEncoding(content.Headers.ContentType?.CharSet);
        return encoding.GetString(bytes);
    }

    private static Encoding GetEncoding(string? characterSet)
    {
        if (!string.IsNullOrWhiteSpace(characterSet))
        {
            try
            {
                return Encoding.GetEncoding(characterSet.Trim(' ', '"', '\''));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Fall back to UTF-8 for unknown/malformed charset declarations.
            }
        }

        return Encoding.UTF8;
    }
}
