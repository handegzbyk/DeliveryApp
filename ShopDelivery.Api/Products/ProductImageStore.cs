using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ShopDelivery.Api.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace ShopDelivery.Api.Products;

public interface IProductImageStore
{
    Task<StoredImageAsset> SaveAsync(Stream source, CancellationToken ct);
    Task<bool> WriteResponseAsync(long imageId, HttpRequest request, HttpResponse response, CancellationToken ct);
}

public sealed record StoredImageAsset(
    long Id,
    int ByteLength,
    bool Created,
    ImageAsset? PendingEntity);

public sealed class DatabaseProductImageStore(ShopDbContext db) : IProductImageStore
{
    private const int MaxSourceBytes = 10_000_000;
    private const int MaxDimension = 4_096;

    public async Task<StoredImageAsset> SaveAsync(Stream source, CancellationToken ct)
    {
        await using var input = new MemoryStream();
        await CopyWithLimitAsync(source, input, MaxSourceBytes, ct);
        var original = input.ToArray();

        var sourceFormat = Image.DetectFormat(original);
        using var image = Image.Load(original);
        if (image.Width > MaxDimension || image.Height > MaxDimension)
            throw new InvalidDataException($"Image dimensions exceed {MaxDimension}x{MaxDimension}.");

        ushort? orientation = null;
        if (image.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var orientationValue) == true)
            orientation = orientationValue.Value;
        var requiresOrientation = orientation is > 1;
        image.Mutate(context => context.AutoOrient());
        image.Metadata.ExifProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.XmpProfile = null;

        await using var losslessWebp = new MemoryStream();
        await image.SaveAsync(
            losslessWebp,
            new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossless,
                Quality = 100,
                // Lossless output is identical after browser decoding. The fastest method
                // changes compression effort/ratio only, which keeps bulk imports practical.
                Method = WebpEncodingMethod.Fastest,
            },
            ct);

        var webp = losslessWebp.ToArray();
        var originalMime = MimeType(sourceFormat);
        var sanitizedOriginal = await SanitizeOriginalAsync(
            original,
            originalMime,
            image,
            requiresOrientation,
            ct);
        var keepOriginal = sanitizedOriginal is not null && sanitizedOriginal.Length <= webp.Length;
        var selected = keepOriginal ? sanitizedOriginal! : webp;
        var mimeType = keepOriginal ? originalMime! : "image/webp";
        var hash = Convert.ToHexString(SHA256.HashData(selected)).ToLowerInvariant();

        var local = db.ImageAssets.Local.FirstOrDefault(asset => asset.ContentHash == hash);
        if (local is not null)
            return new StoredImageAsset(local.Id, local.ByteLength, false, local.Id == 0 ? local : null);

        var existing = await db.ImageAssets
            .AsNoTracking()
            .Where(asset => asset.ContentHash == hash)
            .Select(asset => new { asset.Id, asset.ByteLength })
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return new StoredImageAsset(existing.Id, existing.ByteLength, false, null);

        var asset = new ImageAsset
        {
            ContentHash = hash,
            MimeType = mimeType,
            Width = image.Width,
            Height = image.Height,
            ByteLength = selected.Length,
            Data = selected,
        };
        db.ImageAssets.Add(asset);
        return new StoredImageAsset(0, asset.ByteLength, true, asset);
    }

    public async Task<bool> WriteResponseAsync(
        long imageId,
        HttpRequest request,
        HttpResponse response,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT MimeType, ContentHash, ByteLength, Data FROM ImageAssets WHERE Id = @id";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@id";
            parameter.Value = imageId;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess | CommandBehavior.SingleRow,
                ct);
            if (!await reader.ReadAsync(ct))
                return false;

            var contentHash = reader.GetString(1);
            var etag = $"\"{contentHash}\"";
            if (request.Headers.IfNoneMatch.Any(value => value == etag))
            {
                response.StatusCode = StatusCodes.Status304NotModified;
                return true;
            }

            response.ContentType = reader.GetString(0);
            response.ContentLength = reader.GetInt32(2);
            response.Headers.ETag = etag;
            response.Headers.CacheControl = "public,max-age=31536000,immutable";
            await using var stream = reader.GetStream(3);
            await stream.CopyToAsync(response.Body, ct);
            return true;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static string? MimeType(IImageFormat format) => format.DefaultMimeType switch
    {
        "image/jpeg" => "image/jpeg",
        "image/png" => "image/png",
        "image/webp" => "image/webp",
        "image/gif" => "image/gif",
        _ => null,
    };

    private static async Task<byte[]?> SanitizeOriginalAsync(
        byte[] original,
        string? mimeType,
        Image image,
        bool requiresOrientation,
        CancellationToken ct)
    {
        // Keeping the compressed JPEG scan avoids a lossy decode/re-encode cycle. Metadata
        // segments are removed byte-for-byte; if EXIF orientation must be applied, WebP is used.
        if (mimeType == "image/jpeg" && !requiresOrientation)
            return StripJpegMetadata(original);

        if (mimeType == "image/png")
        {
            await using var png = new MemoryStream();
            await image.SaveAsync(png, new PngEncoder(), ct);
            return png.ToArray();
        }

        // WebP/GIF metadata and orientation are normalized by the lossless WebP candidate.
        return null;
    }

    private static byte[]? StripJpegMetadata(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != (byte)JpegMarker.StartOfImage)
            return null;

        using var output = new MemoryStream(bytes.Length);
        output.Write(bytes, 0, 2);
        var position = 2;
        while (position + 1 < bytes.Length)
        {
            if (bytes[position] != 0xFF)
                return null;
            var markerStart = position++;
            while (position < bytes.Length && bytes[position] == 0xFF)
                position++;
            if (position >= bytes.Length)
                return null;

            var marker = bytes[position++];
            if (marker == (byte)JpegMarker.StartOfScan)
            {
                output.Write(bytes, markerStart, bytes.Length - markerStart);
                return output.ToArray();
            }
            if (marker is (byte)JpegMarker.EndOfImage or 0x01 or >= 0xD0 and <= 0xD7)
            {
                output.Write(bytes, markerStart, position - markerStart);
                if (marker == (byte)JpegMarker.EndOfImage)
                    return output.ToArray();
                continue;
            }
            if (position + 1 >= bytes.Length)
                return null;

            var segmentLength = (bytes[position] << 8) | bytes[position + 1];
            if (segmentLength < 2 || position + segmentLength > bytes.Length)
                return null;
            var isMetadata = marker is 0xE1 or 0xE2 or 0xED or 0xFE;
            if (!isMetadata)
                output.Write(bytes, markerStart, position + segmentLength - markerStart);
            position += segmentLength;
        }

        return null;
    }

    private enum JpegMarker : byte
    {
        StartOfImage = 0xD8,
        EndOfImage = 0xD9,
        StartOfScan = 0xDA,
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        int limit,
        CancellationToken ct)
    {
        var buffer = new byte[81_920];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0)
                break;
            total += read;
            if (total > limit)
                throw new InvalidDataException($"Image exceeds the {limit:N0}-byte limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}

public static class ProductImageUrls
{
    public static string For(HttpRequest request, long? imageAssetId) => imageAssetId is { } id
        ? $"{request.Scheme}://{request.Host}/api/product-images/{id}"
        : ShopDelivery.Shared.ProductImages.Generic;
}
