using System.Net;
using System.Text.RegularExpressions;
namespace Wideska.Services;

public static class EventText
{
    public static string ToPlainText(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";
        string text = Regex.Replace(description, @"<(script|style)\b[^>]*>.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<br\s*/?>|</(?:p|div|li|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li\b[^>]*>", "• ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?[a-zA-Z][^>]*>", "");
        return WebUtility.HtmlDecode(text).Replace('\u00A0', ' ').Trim();
    }
}
