using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Wideska.Controls;

public sealed class LinkTextBlock : TextBlock
{
    public static readonly DependencyProperty ContentTextProperty = DependencyProperty.Register(nameof(ContentText), typeof(string), typeof(LinkTextBlock), new PropertyMetadata("", (d, _) => ((LinkTextBlock)d).UpdateInlines()));
    public string ContentText { get => (string)GetValue(ContentTextProperty); set => SetValue(ContentTextProperty, value); }
    private static readonly Regex Links = new(@"(?:https?://|www\.)[^\s<>""']+|mailto:[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));

    private void UpdateInlines()
    {
        Inlines.Clear();
        AppendText(Inlines, ContentText ?? "");
    }
    internal static void AppendText(InlineCollection target, string text)
    {
        if (text.Length > 65536) text = text[..65536] + "…";
        int position = 0;
        Match[] matches;
        try { matches = Links.Matches(text).Cast<Match>().Take(128).ToArray(); }
        catch (RegexMatchTimeoutException) { target.Add(new Run(text)); return; }
        foreach (Match match in matches)
        {
            target.Add(new Run(text[position..match.Index]));
            string label = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
            int excessClosingBrackets = label.Count(c => c == ')') - label.Count(c => c == '(');
            while (label.EndsWith(')') && excessClosingBrackets-- > 0) label = label[..^1];
            string address = label.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + label : label;
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            {
                var link = new Hyperlink(new Run(label)) { NavigateUri = uri, ToolTip = address };
                link.SetResourceReference(TextElement.ForegroundProperty, "Accent");
                target.Add(link);
            }
            else target.Add(new Run(label));
            target.Add(new Run(match.Value[label.Length..]));
            position = match.Index + match.Length;
        }
        target.Add(new Run(text[position..]));
    }
}
