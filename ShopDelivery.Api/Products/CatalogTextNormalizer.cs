using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ShopDelivery.Api.Products;

public static partial class CatalogTextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var decomposed = value.Trim().ToLowerInvariant()
            .Replace("ß", "ss", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormD);
        var withoutMarks = new string(decomposed
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        var separated = LetterDigitBoundary().Replace(withoutMarks, "$1 $2");
        separated = DigitLetterBoundary().Replace(separated, "$1 $2");
        return Whitespace().Replace(NonAlphaNumeric().Replace(separated, " "), " ").Trim();
    }

    public static string? NormalizeGtin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length is 8 or 12 or 13 or 14 ? digits : null;
    }

    public static string DisplayName(string value) =>
        CultureInfo.GetCultureInfo("de-DE").TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphaNumeric();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"([\p{L}])([\p{N}])")]
    private static partial Regex LetterDigitBoundary();

    [GeneratedRegex(@"([\p{N}])([\p{L}])")]
    private static partial Regex DigitLetterBoundary();
}
