using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Wideska.Controls;

public sealed class LinkTextBlock : TextBlock
{
    public static readonly DependencyProperty ContentTextProperty = DependencyProperty.Register(nameof(ContentText), typeof(string), typeof(LinkTextBlock), new PropertyMetadata("", (d, _) => ((LinkTextBlock)d).UpdateInlines()));
    public string ContentText { get => (string)GetValue(ContentTextProperty); set => SetValue(ContentTextProperty, value); }
    private static readonly Regex Links = new(@"(?:https?://|www\.)[^\s<>""']+|mailto:[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private void UpdateInlines()
    {
        Inlines.Clear();
        string text = ContentText ?? "";
        int position = 0;
        foreach (Match match in Links.Matches(text))
        {
            Inlines.Add(new Run(text[position..match.Index]));
            string label = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
            while (label.EndsWith(')') && label.Count(c => c == ')') > label.Count(c => c == '(')) label = label[..^1];
            string address = label.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + label : label;
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            {
                var link = new Hyperlink(new Run(label)) { NavigateUri = uri, ToolTip = address };
                link.SetResourceReference(TextElement.ForegroundProperty, "Accent");
                Inlines.Add(link);
            }
            else Inlines.Add(new Run(label));
            Inlines.Add(new Run(match.Value[label.Length..]));
            position = match.Index + match.Length;
        }
        Inlines.Add(new Run(text[position..]));
    }
}
