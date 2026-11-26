using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wideska.Services;
using Wideska.ViewModels;

namespace Wideska.Controls;

public sealed class DayTimeline : Control
{
    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(nameof(Days), typeof(IEnumerable<DayCell>), typeof(DayTimeline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, DaysChanged));
    public static readonly DependencyProperty CurrentTimeProperty = DependencyProperty.Register(nameof(CurrentTime), typeof(DateTimeOffset), typeof(DayTimeline), new FrameworkPropertyMetadata(default(DateTimeOffset), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TimeZoneIdProperty = DependencyProperty.Register(nameof(TimeZoneId), typeof(string), typeof(DayTimeline), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(DayTimeline), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable<DayCell>? Days { get => (IEnumerable<DayCell>?)GetValue(DaysProperty); set => SetValue(DaysProperty, value); }
    public DateTimeOffset CurrentTime { get => (DateTimeOffset)GetValue(CurrentTimeProperty); set => SetValue(CurrentTimeProperty, value); }
    public string TimeZoneId { get => (string)GetValue(TimeZoneIdProperty); set => SetValue(TimeZoneIdProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    private readonly List<(Rect Bounds, string Details)> hits = [];
    private INotifyCollectionChanged? observed;
    private string? hover;
    private bool scrollPending;
    public DayTimeline()
    {
        MinHeight = 780;
        Loaded += (_, _) => { Observe(); ScheduleScroll(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) ScheduleScroll(); };
        Unloaded += (_, _) => { if (observed is not null) observed.CollectionChanged -= CollectionChanged; observed = null; };
        MouseMove += (_, e) =>
        {
            string? details = hits.LastOrDefault(h => h.Bounds.Contains(e.GetPosition(this))).Details;
            if (details == hover) return;
            hover = details; ToolTip = details;
        };
    }
    private static void DaysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DayTimeline)d).Observe();
    private void Observe()
    {
        if (observed is not null) observed.CollectionChanged -= CollectionChanged;
        observed = Days as INotifyCollectionChanged;
        if (observed is not null) observed.CollectionChanged += CollectionChanged;
        InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { InvalidateVisual(); ScheduleScroll(); }
    private void ScheduleScroll()
    {
        if (scrollPending || !IsLoaded || !IsVisible) return;
        scrollPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            scrollPending = false;
            var days = Days?.ToList() ?? [];
            if (!IsVisible || !days.Any(d => CalendarTime.IndicatorMinutes(d.Date, CurrentTime, TimeZoneId) is not null)) return;
            DependencyObject? parent = VisualTreeHelper.GetParent(this);
            while (parent is not null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
            if (parent is not ScrollViewer viewer) return;
            var local = CalendarTime.Local(CurrentTime, TimeZoneId);
            int rows = Math.Max(1, Math.Min(3, days.Max(d => d.Events.Count(e => e.AllDay))));
            double header = 46 + rows * 20;
            viewer.ScrollToVerticalOffset(Math.Max(0, header + local.TimeOfDay.TotalMinutes / 1440 * (ActualHeight - header - 12) - viewer.ViewportHeight / 3));
        });
    }
    private void Text(DrawingContext dc, string text, double x, double y, double width, double size = 11, Brush? brush = null)
    {
        if (width < 2) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush ?? Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, width), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(formatted, new Point(x, y));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); hits.Clear();
        var days = Days?.ToList() ?? [];
        if (days.Count == 0 || ActualWidth < 60) return;
        const double gutter = 42;
        double column = (ActualWidth - gutter) / days.Count;
        int allDayRows = Math.Max(1, Math.Min(3, days.Max(d => d.Events.Count(e => e.AllDay))));
        double startY = 34 + allDayRows * 20 + 12;
        double timelineHeight = Math.Max(24, ActualHeight - startY - 12);
        var gridPen = new Pen(GridBrush, .5);
        for (int hour = 0; hour <= 24; hour++)
        {
            double y = startY + timelineHeight * hour / 24;
            dc.DrawLine(gridPen, new Point(gutter, y), new Point(ActualWidth, y));
            if (hour < 24) Text(dc, $"{hour:00}:00", 0, y + 2, gutter - 6, 10);
        }
        for (int index = 0; index < days.Count; index++)
        {
            var day = days[index]; double x = gutter + index * column;
            hits.Add((new Rect(x, 0, column, startY), day.Summary));
            dc.DrawLine(gridPen, new Point(x, 0), new Point(x, ActualHeight));
            Text(dc, day.Label, x + 7, 3, column - 12, 12);
            int row = 0;
            foreach (var item in day.Events.Where(e => e.AllDay).Take(allDayRows))
            {
                var rect = new Rect(x + 5, 30 + 20 * row++, Math.Max(1, column - 10), 18);
                Event(dc, item, rect, false);
            }
            if (day.Events.Count(e => e.AllDay) > allDayRows) Text(dc, "+ more all-day events", x + 5, startY - 12, column - 10, 9);
            foreach (var item in TimelineLayout.Arrange(day.Events))
            {
                double width = (column - 10) / item.LaneCount;
                double top = startY + Math.Clamp(item.Event.StartMinute, 0, 1440) / 1440 * timelineHeight;
                double bottom = startY + Math.Clamp(item.Event.EndMinute, 0, 1440) / 1440 * timelineHeight;
                if (bottom <= top) continue;
                Event(dc, item.Event, new Rect(x + 5 + item.Lane * width, top, Math.Max(1, width - 2), Math.Max(3, bottom - top)), true);
            }
        }
        // Draw last so the current-time marker stays above every event.
        for (int index = 0; index < days.Count; index++)
        {
            double? minutes = CalendarTime.IndicatorMinutes(days[index].Date, CurrentTime, TimeZoneId);
            if (minutes is null) continue;
            double x = gutter + index * column;
            double y = startY + minutes.Value / 1440 * timelineHeight;
            dc.DrawLine(new Pen(Brushes.Red, 1.5), new Point(x + 4, y), new Point(x + column, y));
            dc.DrawEllipse(Brushes.Red, null, new Point(x + 4, y), 4, 4);
        }
    }
    private void Event(DrawingContext dc, EventItem item, Rect rect, bool showTime)
    {
        Brush color;
        try { color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Color)); }
        catch (FormatException) { color = Brushes.MediumPurple; }
        dc.PushClip(new RectangleGeometry(rect));
        dc.PushOpacity(.32); dc.DrawRoundedRectangle(color, null, rect, 5, 5); dc.Pop();
        dc.DrawLine(new Pen(color, 2), rect.TopLeft, rect.BottomLeft);
        Text(dc, item.Title, rect.X + 5, rect.Y + 2, rect.Width - 8);
        if (showTime && rect.Height > 32) Text(dc, item.Time, rect.X + 5, rect.Y + 18, rect.Width - 8, 9);
        dc.Pop(); hits.Add((rect, item.Details));
    }
}
