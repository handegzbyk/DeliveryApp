using Azure;
using Azure.AI.DocumentIntelligence;
using ShopDelivery.Shared;

namespace ShopDelivery.Ai;

public sealed class ReceiptExtractor(DocumentIntelligenceClient client)
{
    public async Task<ScannedReceipt> ExtractAsync(Stream image, CancellationToken ct = default)
    {
        var binary = await BinaryData.FromStreamAsync(image, ct);

        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-receipt",
            binary,                    // GA: BinaryData source directly
            cancellationToken: ct);

        var doc = operation.Value.Documents.FirstOrDefault();
        if (doc is null)
            return new ScannedReceipt(null, null, null, []);

        var merchant = GetString(doc, "MerchantName");
        var purchasedOn = GetDate(doc, "TransactionDate");
        var total = GetCurrency(doc, "Total");
        var lines = ExtractLines(doc);

        return new ScannedReceipt(merchant, purchasedOn, total, lines);
    }

    private static List<ScannedLine> ExtractLines(AnalyzedDocument doc)
    {
        var result = new List<ScannedLine>();

        if (!doc.Fields.TryGetValue("Items", out var itemsField)
            || itemsField.FieldType != DocumentFieldType.List)
        {
            return result;
        }

        foreach (var item in itemsField.ValueList)
        {
            if (item.FieldType != DocumentFieldType.Dictionary)
                continue;

            var fields = item.ValueDictionary;
            var description = fields.TryGetValue("Description", out var d) ? d.ValueString : null;
            if (string.IsNullOrWhiteSpace(description))
                continue;

            var quantity = fields.TryGetValue("Quantity", out var q) && q.ValueDouble is { } qty
                ? (int?)Math.Round(qty)
                : null;

            var price = fields.TryGetValue("TotalPrice", out var p) && p.ValueCurrency is { } c
                ? (decimal?)c.Amount
                : null;

            result.Add(new ScannedLine(description!.Trim(), quantity, price));
        }

        return result;
    }

    private static string? GetString(AnalyzedDocument doc, string key) =>
        doc.Fields.TryGetValue(key, out var f) ? f.ValueString : null;

    private static DateOnly? GetDate(AnalyzedDocument doc, string key) =>
        doc.Fields.TryGetValue(key, out var f) && f.ValueDate is { } d
            ? DateOnly.FromDateTime(d.DateTime)
            : null;

    private static decimal? GetCurrency(AnalyzedDocument doc, string key) =>
        doc.Fields.TryGetValue(key, out var f) && f.ValueCurrency is { } c
            ? (decimal)c.Amount
            : null;
}