using System.Net;
using System.Text.RegularExpressions;
namespace Wideska.Services;

public static class EventText
{
    public static string ToPlainText(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";
        string text = Regex.Replace(description, @"<(script|style)\b[^>]*>.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<a\b[^>]*\bhref\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)'|(?<url>[^\s>]+))[^>]*>(?<label>.*?)</a\s*>", match =>
        {
            string address = WebUtility.HtmlDecode(match.Groups["url"].Value);
            string label = Regex.Replace(match.Groups["label"].Value, @"<[^>]*>", "");
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto")) return label;
            return WebUtility.HtmlDecode(label).Trim() == address ? label : $"{label} ({WebUtility.HtmlEncode(address)})";
        }, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<br\s*/?>|</(?:p|div|li|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li\b[^>]*>", "• ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?[a-zA-Z][^>]*>", "");
        return WebUtility.HtmlDecode(text).Replace('\u00A0', ' ').Trim();
    }
}
