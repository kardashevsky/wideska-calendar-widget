using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Threading;
using Wideska.Controls;
using Wideska.ViewModels;

internal static partial class Program
{
    private static IEnumerable<Inline> DescriptionInlines(InlineCollection inlines)
    {
        foreach (Inline item in inlines)
        {
            yield return item;
            if (item is Span span)
                foreach (var child in DescriptionInlines(span.Inlines)) yield return child;
        }
    }
    private static void TestRichDescriptions()
    {
        var description = new RichDescriptionTextBlock
        {
            ContentText = "<p>First <strong>bold</strong></p><p>Second <em>italic</em><br>Next line</p>"
                + "<ul><li>One</li><li>Two</li></ul><ol><li>Numbered</li></ol>"
                + "<p><a href='https://example.com/?a=1&amp;b=2'><b>meeting</b></a></p>"
                + "<span style='font-weight:700;font-style:italic;text-decoration:underline line-through'>Styled</span>"
                + "<script>alert('hidden script')</script><style>hidden style</style>"
        };
        string text = new TextRange(description.ContentStart, description.ContentEnd).Text.Replace("\r\n", "\n");
        Check(text.Contains("bold\n\nSecond") && text.Contains("italic\nNext line"),
            "HTML paragraphs must remain separated while br produces a single line break");
        var inlines = DescriptionInlines(description.Inlines).ToArray();
        Check(inlines.OfType<Run>().Any(r => r.Text == "bold" && r.FontWeight == FontWeights.Bold)
            && inlines.OfType<Run>().Any(r => r.Text == "italic" && r.FontStyle == FontStyles.Italic),
            "Description emphasis must survive as effective WPF font properties");
        Check(text.Contains("\u2022 One") && text.Contains("\u2022 Two") && text.Contains("1. Numbered"),
            "Unordered and ordered lists must retain markers and item boundaries");
        var link = inlines.OfType<Hyperlink>().Single();
        Check(link.NavigateUri.Query == "?a=1&b=2" && new TextRange(link.ContentStart, link.ContentEnd).Text == "meeting",
            "HTML anchors must retain their label, nested formatting and decoded destination");
        Check(inlines.OfType<Span>().Any(s => s.FontWeight == FontWeights.Bold && s.FontStyle == FontStyles.Italic && s.TextDecorations.Count == 2),
            "Google-style inline CSS must retain bold, italic, underline and strikethrough");
        Check(!text.Contains("hidden script") && !text.Contains("hidden style"), "Script and style content must never appear in descriptions");
        description.ContentText = "<a href='javascript:alert(1)'>Unsafe</a><a href='file:///C:/private'>Local</a><img src='https://example.com/tracker'>";
        Check(!DescriptionInlines(description.Inlines).OfType<Hyperlink>().Any(), "Descriptions must reject executable and local-file destinations and must not load images");
        description.ContentText = "<a href='https://example.com'><a href='https://example.org'>Nested link</a></a><b>Unclosed";
        Check(DescriptionInlines(description.Inlines).OfType<Hyperlink>().Count() == 1,
            "Malformed nested anchors must not create illegal nested WPF hyperlinks");
        description.ContentText = "Line one\n\nLine two https://example.com";
        Check(new TextRange(description.ContentStart, description.ContentEnd).Text.Contains("Line one\n\nLine two")
            && DescriptionInlines(description.Inlines).OfType<Hyperlink>().Count() == 1,
            "Plain descriptions must retain their whitespace and automatic links");
        description.ContentText = new string('x', 100000);
        Check(new TextRange(description.ContentStart, description.ContentEnd).Text.Length <= 65540,
            "Large descriptions must have a bounded rendering cost");
    }
    private static async Task TestRichEventDetailsAsync(Application app, Window window, MainViewModel model, FakeProvider provider)
    {
        double width = window.Width, height = window.Height;
        string markup = "<p><strong>Meeting goals</strong></p><p>Review the plan and agree on <em>next steps</em>.</p>"
            + "<ul><li>Review the timeline</li><li>Assign owners</li></ul><p><a href='https://example.com/meeting'>Open meeting notes</a></p>";
        provider.ProjectDescription = markup;
        await model.RefreshAsync();
        var item = model.Days.SelectMany(d => d.Events).First(e => e.EventId == "b");
        Check(item.DescriptionMarkup == markup && !item.Description.Contains("<strong>"),
            "Provider HTML must reach event details intact while tooltips keep plain text");
        model.OpenEventCommand.Execute(item);
        window.Width = 960; window.Height = 740;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var card = (Border)window.FindName("EventDetailsCard");
        Check(Math.Abs(card.ActualWidth - 640) < .5, "Event details must use a balanced 640-pixel reading width on a wide widget");
        if (savePreviews) await Task.Delay(180);
        SavePreview(window, "event-details-rich-wide", true);
        window.Width = 420; window.Height = 560;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(card.ActualWidth <= window.ActualWidth - 48 && card.ActualWidth > 340,
            "The default reading width must shrink to fit a narrow widget without clipping");
        SavePreview(window, "event-details-rich-small", true);
        model.CloseEventCommand.Execute(null);
        provider.ProjectDescription = "";
        await model.RefreshAsync();
        window.Width = width; window.Height = height;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
