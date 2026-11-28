using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Wideska.Services;

namespace Wideska.Controls;

public sealed class RichDescriptionTextBlock : TextBlock
{
    public static readonly DependencyProperty ContentTextProperty = DependencyProperty.Register(nameof(ContentText), typeof(string),
        typeof(RichDescriptionTextBlock), new PropertyMetadata("", (d, _) => ((RichDescriptionTextBlock)d).UpdateContent()));
    public string ContentText { get => (string)GetValue(ContentTextProperty); set => SetValue(ContentTextProperty, value); }
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Tags = new("<!--.*?-->|<(?<close>/)?(?<name>[a-z][a-z0-9:-]*)(?<attributes>(?:[^>\"']|\"[^\"]*\"|'[^']*')*)>", Options, Timeout);
    private static readonly Regex Whitespace = new(@"[\t\r\n ]+", RegexOptions.NonBacktracking, Timeout);

    private void UpdateContent()
    {
        Inlines.Clear();
        string input = ContentText ?? "";
        if (input.Length > 65536) input = input[..65536] + "\u2026";
        try { RenderMarkup(input); }
        catch (RegexMatchTimeoutException)
        {
            Inlines.Clear();
            LinkTextBlock.AppendText(Inlines, EventText.ToPlainText(input));
        }
    }
    private void RenderMarkup(string input)
    {
        var tags = Tags.Matches(input).Cast<Match>().Take(2049).ToArray();
        if (tags.Length == 0) { LinkTextBlock.AppendText(Inlines, WebUtility.HtmlDecode(input)); return; }
        if (tags.Length > 2048) { LinkTextBlock.AppendText(Inlines, EventText.ToPlainText(input)); return; }
        var spans = new List<(string Name, Span Span)>();
        var lists = new Stack<(string Name, int Number)>();
        InlineCollection Current() => spans.Count == 0 ? Inlines : spans[^1].Span.Inlines;
        int trailingBreaks = 0;
        bool hasText = false;
        string? hiddenTag = null;
        void Break(int count)
        {
            if (!hasText) return;
            while (trailingBreaks < count) { Current().Add(new LineBreak()); trailingBreaks++; }
        }
        void Text(string value)
        {
            if (hiddenTag is not null) return;
            value = WebUtility.HtmlDecode(value).Replace('\u00A0', ' ');
            if (!spans.Any(s => s.Name == "pre")) value = Whitespace.Replace(value, " ");
            if (!hasText || trailingBreaks > 0) value = value.TrimStart();
            if (value.Length == 0) return;
            if (spans.Any(s => s.Span is Hyperlink)) Current().Add(new Run(value));
            else LinkTextBlock.AppendText(Current(), value);
            hasText = true; trailingBreaks = 0;
        }
        int position = 0;
        foreach (var token in tags)
        {
            Text(input[position..token.Index]);
            position = token.Index + token.Length;
            string name = token.Groups["name"].Value.ToLowerInvariant();
            bool closing = token.Groups["close"].Success;
            if (hiddenTag is not null) { if (closing && name == hiddenTag) hiddenTag = null; continue; }
            if (name is "script" or "style" or "iframe" or "object" or "svg" or "math")
            { if (!closing) hiddenTag = name; continue; }
            if (name.Length == 0) continue;
            if (closing)
            {
                int matching = spans.FindLastIndex(s => s.Name == name);
                if (matching >= 0) spans.RemoveRange(matching, spans.Count - matching);
                if (name is "p" or "blockquote" || name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6") Break(2);
                else if (name is "div" or "li" or "pre" or "ul" or "ol") Break(1);
                if (name is "ul" or "ol" && lists.Count > 0 && lists.Peek().Name == name) lists.Pop();
                continue;
            }
            if (name == "br") { Current().Add(new LineBreak()); trailingBreaks++; continue; }
            if (name is "p" or "blockquote" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6") Break(2);
            else if (name is "div" or "li" or "pre" or "ul" or "ol") Break(1);
            if (name is "ul" or "ol") { lists.Push((name, 0)); continue; }
            if (name == "li")
            {
                string marker = "\u2022 ";
                if (lists.TryPop(out var list))
                {
                    list.Number++;
                    if (list.Name == "ol") marker = $"{list.Number}. ";
                    lists.Push(list);
                }
                Text(new string(' ', Math.Max(0, lists.Count - 1) * 2) + marker);
            }
            if (spans.Count >= 32) continue;
            if (name is not ("a" or "b" or "strong" or "i" or "em" or "u" or "s" or "strike" or "del" or "span" or "p" or "div" or "li" or "pre" or "blockquote" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6")) continue;
            string attributes = token.Groups["attributes"].Value;
            Span span = new();
            if (name == "a" && !spans.Any(s => s.Span is Hyperlink))
            {
                string address = Attribute(attributes, "href");
                if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
                {
                    span = new Hyperlink { NavigateUri = uri, ToolTip = address };
                    span.SetResourceReference(ForegroundProperty, "Accent");
                }
            }
            string style = Attribute(attributes, "style");
            if (name is "b" or "strong" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
                || Regex.IsMatch(style, @"(?:^|;)\s*font-weight\s*:\s*(?:bold|[6-9]00)\b", Options, Timeout)) span.FontWeight = FontWeights.Bold;
            if (name is "i" or "em" || Regex.IsMatch(style, @"(?:^|;)\s*font-style\s*:\s*(?:italic|oblique)\b", Options, Timeout)) span.FontStyle = FontStyles.Italic;
            var decorations = new TextDecorationCollection();
            if (name == "u" || Regex.IsMatch(style, @"text-decoration(?:-line)?\s*:[^;]*\bunderline\b", Options, Timeout)) decorations.Add(System.Windows.TextDecorations.Underline[0]);
            if (name is "s" or "strike" or "del" || Regex.IsMatch(style, @"text-decoration(?:-line)?\s*:[^;]*\bline-through\b", Options, Timeout)) decorations.Add(System.Windows.TextDecorations.Strikethrough[0]);
            if (decorations.Count > 0) span.TextDecorations = decorations;
            Current().Add(span);
            if (!token.Value.EndsWith("/>", StringComparison.Ordinal)) spans.Add((name, span));
        }
        Text(input[position..]);
        TrimTrailingBreaks(Inlines);
    }
    private static void TrimTrailingBreaks(InlineCollection inlines)
    {
        while (inlines.LastInline is { } last)
        {
            if (last is LineBreak) inlines.Remove(last);
            else if (last is Span span)
            {
                TrimTrailingBreaks(span.Inlines);
                if (span.Inlines.Count == 0) inlines.Remove(last); else return;
            }
            else return;
        }
    }
    private static string Attribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, $"(?:^|\\s){name}\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", Options, Timeout);
        return WebUtility.HtmlDecode(match.Groups["value"].Value).Trim();
    }
}
