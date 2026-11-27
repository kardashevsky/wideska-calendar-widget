using System.Net;
using System.Text.RegularExpressions;
namespace Wideska.Services;

public static class EventText
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private const RegexOptions SafeOptions = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking;
    public static string ToPlainText(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";
        description = description.Length > 65536 ? description[..65536] + "…" : description;
        try { return ConvertMarkup(description); }
        catch (RegexMatchTimeoutException)
        {
            // TextBlock renders this as literal text, never as executable HTML.
            return WebUtility.HtmlDecode(description).Replace('\u00A0', ' ').Trim();
        }
    }
    private static string ConvertMarkup(string description)
    {
        string text = Regex.Replace(description, @"<script\b[^>]*>.*?</script\s*>|<style\b[^>]*>.*?</style\s*>", "", SafeOptions, MatchTimeout);
        text = Regex.Replace(text, @"<a\b[^>]*\bhref\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)'|(?<url>[^\s>]+))[^>]*>(?<label>.*?)</a\s*>", match =>
        {
            string address = WebUtility.HtmlDecode(match.Groups["url"].Value);
            string label = Regex.Replace(match.Groups["label"].Value, @"<[^>]*>", "", SafeOptions, MatchTimeout);
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto")) return label;
            return WebUtility.HtmlDecode(label).Trim() == address ? label : $"{label} ({WebUtility.HtmlEncode(address)})";
        }, SafeOptions, MatchTimeout);
        text = Regex.Replace(text, @"<br\s*/?>|</(?:p|div|li|h[1-6])\s*>", "\n", SafeOptions, MatchTimeout);
        text = Regex.Replace(text, @"<li\b[^>]*>", "• ", SafeOptions, MatchTimeout);
        text = Regex.Replace(text, @"</?[a-zA-Z][^>]*>", "", SafeOptions, MatchTimeout);
        return WebUtility.HtmlDecode(text).Replace('\u00A0', ' ').Trim();
    }
}
