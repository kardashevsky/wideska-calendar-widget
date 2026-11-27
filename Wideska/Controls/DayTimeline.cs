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
    public static readonly DependencyProperty EventCommandProperty = DependencyProperty.Register(nameof(EventCommand), typeof(ICommand), typeof(DayTimeline));
    public ICommand? EventCommand { get => (ICommand?)GetValue(EventCommandProperty); set => SetValue(EventCommandProperty, value); }
    public static readonly DependencyProperty SurfaceBrushProperty = ThemeBrush(nameof(SurfaceBrush), "#ED1D2730");
    public static readonly DependencyProperty MutedBrushProperty = ThemeBrush(nameof(MutedBrush), "#BBCAD5");
    public static readonly DependencyProperty AxisBrushProperty = ThemeBrush(nameof(AxisBrush), "#8098ACBA");
    public static readonly DependencyProperty AccentBrushProperty = ThemeBrush(nameof(AccentBrush), "#8DD3EF");
    public static readonly DependencyProperty NowBrushProperty = ThemeBrush(nameof(NowBrush), "#F43F5E");
    public static readonly DependencyProperty WeekendBrushProperty = ThemeBrush(nameof(WeekendBrush), "#FDA4AF");
    public Brush WeekendBrush { get => (Brush)GetValue(WeekendBrushProperty); set => SetValue(WeekendBrushProperty, value); }
    public Brush SurfaceBrush { get => (Brush)GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }
    public Brush MutedBrush { get => (Brush)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public Brush AxisBrush { get => (Brush)GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush NowBrush { get => (Brush)GetValue(NowBrushProperty); set => SetValue(NowBrushProperty, value); }
    private static DependencyProperty ThemeBrush(string name, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze();
        return DependencyProperty.Register(name, typeof(Brush), typeof(DayTimeline), new FrameworkPropertyMetadata(brush, FrameworkPropertyMetadataOptions.AffectsRender));
    }
    public IEnumerable<DayCell>? Days { get => (IEnumerable<DayCell>?)GetValue(DaysProperty); set => SetValue(DaysProperty, value); }
    public DateTimeOffset CurrentTime { get => (DateTimeOffset)GetValue(CurrentTimeProperty); set => SetValue(CurrentTimeProperty, value); }
    public string TimeZoneId { get => (string)GetValue(TimeZoneIdProperty); set => SetValue(TimeZoneIdProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    private readonly List<(Rect Bounds, string Details, EventItem? Event)> hits = [];
    private INotifyCollectionChanged? observed;
    private string? hover;
    private readonly ToolTip eventToolTip = new();
    private bool scrollPending;
    private Rect slotArea = Rect.Empty;
    public DayTimeline()
    {
        MinHeight = MinimumTimelineHeight;
        SnapsToDevicePixels = true; UseLayoutRounding = true;
        Loaded += (_, _) => { Observe(); ScheduleScroll(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) ScheduleScroll(); else ClearHover(); };
        Unloaded += (_, _) => { ClearHover(); if (observed is not null) observed.CollectionChanged -= CollectionChanged; observed = null; };
        MouseLeave += (_, _) => { Cursor = Cursors.Arrow; ClearHover(); };
        MouseMove += (_, e) =>
        {
            Point position = e.GetPosition(this);
            Cursor = slotArea.Contains(position) ? Cursors.Hand : Cursors.Arrow;
            string? details = hits.LastOrDefault(h => h.Bounds.Contains(position)).Details;
            if (details == hover) return;
            if (details is null) { ClearHover(); return; }
            hover = details; eventToolTip.Content = details; ToolTip = eventToolTip; InvalidateVisual();
        };
    }
    private void ClearHover()
    {
        if (hover is not null) InvalidateVisual();
        eventToolTip.IsOpen = false; eventToolTip.Content = null;
        ToolTip = null; hover = null;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var item = hits.LastOrDefault(h => h.Bounds.Contains(e.GetPosition(this))).Event;
        if (item is not null && EventCommand?.CanExecute(item) == true)
        {
            ClearHover(); EventCommand.Execute(item); e.Handled = true;
        }
        base.OnMouseLeftButtonUp(e);
    }
    private static void DaysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DayTimeline)d).Observe();
    private void Observe()
    {
        if (observed is not null) observed.CollectionChanged -= CollectionChanged;
        observed = Days as INotifyCollectionChanged;
        if (observed is not null) observed.CollectionChanged += CollectionChanged;
        InvalidateVisual();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { ClearHover(); InvalidateVisual(); ScheduleScroll(); }
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
            double header = TimeStart(days);
            viewer.ScrollToVerticalOffset(Math.Max(0, header + local.TimeOfDay.TotalMinutes / 1440 * (ActualHeight - header - 12) - viewer.ViewportHeight / 3));
        });
    }
    public const double MinimumTimelineHeight = 1480;
    public const double TimeGutterWidth = 76;
    private const double DayHeaderHeight = 48;
    private const double AllDayRowHeight = 28;
    private double TimeStart(IReadOnlyList<DayCell> days) => DayHeaderHeight + Math.Min(3, days.Max(d => d.Events.Count(e => e.AllDay))) * AllDayRowHeight + 24;
    private void Text(DrawingContext dc, string text, double x, double y, double width, double size = 12, Brush? brush = null, FontWeight? weight = null, TextAlignment alignment = TextAlignment.Left)
    {
        if (width < 2) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), size,
            brush ?? Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, width), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = alignment };
        dc.DrawText(formatted, new Point(x, y));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); hits.Clear(); slotArea = Rect.Empty;
        var days = Days?.ToList() ?? [];
        if (days.Count == 0 || ActualWidth <= TimeGutterWidth) return;
        double column = (ActualWidth - TimeGutterWidth) / days.Count;
        int allDayRows = Math.Min(3, days.Max(d => d.Events.Count(e => e.AllDay)));
        double startY = TimeStart(days);
        double timelineHeight = Math.Max(24, ActualHeight - startY - 16);
        slotArea = new Rect(TimeGutterWidth, startY, ActualWidth - TimeGutterWidth, timelineHeight);
        int today = days.FindIndex(d => CalendarTime.IndicatorMinutes(d.Date, CurrentTime, TimeZoneId) is not null);
        double? nowMinutes = today < 0 ? null : CalendarTime.IndicatorMinutes(days[today].Date, CurrentTime, TimeZoneId);
        double nowY = nowMinutes is null ? -100 : startY + nowMinutes.Value / 1440 * timelineHeight;
        double axisX = TimeGutterWidth - 12;
        var gridPen = new Pen(GridBrush, 1);
        Text(dc, "TIME", 8, 17, TimeGutterWidth - 16, 9, MutedBrush, FontWeights.SemiBold);
        if (allDayRows > 0) Text(dc, "ALL DAY", 6, DayHeaderHeight + 7, TimeGutterWidth - 14, 9, MutedBrush, FontWeights.Medium);
        // One-hour slots: alternate subtle bands and align the labels to their boundaries.
        for (int hour = 0; hour < 24; hour++)
        {
            double top = startY + timelineHeight * hour / 24;
            if (hour % 2 == 0)
            {
                dc.PushOpacity(.035);
                dc.DrawRectangle(Foreground, null, new Rect(TimeGutterWidth, top, ActualWidth - TimeGutterWidth, timelineHeight / 24));
                dc.Pop();
            }
        }
        dc.DrawLine(new Pen(AxisBrush, 1.25), new Point(axisX, startY), new Point(axisX, ActualHeight - 16));
        for (int hour = 0; hour <= 24; hour++)
        {
            double y = startY + timelineHeight * hour / 24;
            dc.DrawLine(gridPen, new Point(TimeGutterWidth, y), new Point(ActualWidth, y));
            dc.DrawLine(new Pen(AxisBrush, 1), new Point(axisX - 4, y), new Point(TimeGutterWidth, y));
            if (Math.Abs(y - nowY) > 15)
                Text(dc, $"{hour:00}:00", 3, y - 8, axisX - 13, 12, MutedBrush, FontWeights.Medium, TextAlignment.Right);
        }
        for (int index = 0; index < days.Count; index++)
        {
            var day = days[index]; double x = TimeGutterWidth + index * column;
            hits.Add((new Rect(x, 0, column, startY), day.Summary, null));
            if (index == today)
            {
                dc.PushOpacity(.1); dc.DrawRoundedRectangle(AccentBrush, null, new Rect(x + 6, 4, Math.Max(1, column - 12), 34), 10, 10); dc.Pop();
                Text(dc, day.Label, x + 16, 12, column - 28, 13, day.IsWeekend ? WeekendBrush : AccentBrush, FontWeights.SemiBold);
            }
            else Text(dc, day.Label, x + 14, 12, column - 24, 13, day.IsWeekend ? WeekendBrush : Foreground, FontWeights.Medium);
            dc.DrawLine(gridPen, new Point(x, DayHeaderHeight), new Point(x, ActualHeight - 16));
            int row = 0;
            foreach (var item in day.Events.Where(e => e.AllDay).Take(allDayRows))
                Event(dc, item, new Rect(x + 8, DayHeaderHeight + AllDayRowHeight * row++, Math.Max(1, column - 16), 24), false);
            if (day.Events.Count(e => e.AllDay) > allDayRows)
                Text(dc, "More all-day events", x + 12, startY - 18, column - 24, 10, MutedBrush);
            foreach (var item in TimelineLayout.Arrange(day.Events))
            {
                double laneWidth = Math.Max(1, (column - 16) / item.LaneCount);
                double top = startY + Math.Clamp(item.Event.StartMinute, 0, 1440) / 1440 * timelineHeight;
                double bottom = startY + Math.Clamp(item.Event.EndMinute, 0, 1440) / 1440 * timelineHeight;
                if (bottom <= top) continue;
                var rect = new Rect(x + 8 + item.Lane * laneWidth, top + 2, Math.Max(1, laneWidth - 6), Math.Max(2, bottom - top - 6));
                Event(dc, item.Event, rect, true);
            }
        }
        // The live-time badge and line remain visually distinct from event-start accents.
        if (today >= 0 && nowMinutes is not null)
        {
            double x = TimeGutterWidth + today * column;
            dc.DrawRoundedRectangle(NowBrush, null, new Rect(3, nowY - 11, 54, 22), 7, 7);
            Text(dc, CalendarTime.Local(CurrentTime, TimeZoneId).ToString("HH:mm"), 6, nowY - 8, 48, 11, Brushes.White, FontWeights.SemiBold, TextAlignment.Center);
            dc.DrawLine(new Pen(NowBrush, 1.75), new Point(x + 4, nowY), new Point(x + column - 3, nowY));
            dc.DrawEllipse(NowBrush, null, new Point(x + 4, nowY), 4, 4);
        }
    }
    private void Event(DrawingContext dc, EventItem item, Rect rect, bool timed)
    {
        Brush color;
        try { color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Color)); }
        catch (FormatException) { color = AccentBrush; }
        bool highlighted = item.Details == hover;
        double radius = Math.Min(12, rect.Height / 2);
        var shape = new RectangleGeometry(rect, radius, radius);
        dc.PushClip(shape);
        dc.DrawRoundedRectangle(SurfaceBrush, null, rect, radius, radius);
        dc.PushOpacity(highlighted ? .2 : .12);
        dc.DrawRoundedRectangle(color, null, rect, radius, radius); dc.Pop();
        dc.PushOpacity(highlighted ? .9 : .5);
        dc.DrawRoundedRectangle(null, new Pen(color, highlighted ? 1.5 : 1), rect, radius, radius); dc.Pop();
        if (!timed)
        {
            dc.DrawEllipse(color, null, new Point(rect.X + 11, rect.Y + rect.Height / 2), 3, 3);
            Text(dc, item.Title, rect.X + 21, rect.Y + 4, rect.Width - 29, 11, Foreground, FontWeights.Medium);
        }
        else
        {
            // Keep the strong calendar-color accent inside the side of the card.
            if (rect.Height > 20)
                dc.DrawRoundedRectangle(color, null, new Rect(rect.X + 4, rect.Y + 9, 3, Math.Max(2, rect.Height - 18)), 1.5, 1.5);
            string start = TimeSpan.FromMinutes(Math.Clamp(item.StartMinute, 0, 1440)).ToString(@"hh\:mm");
            if (rect.Height >= 46)
            {
                string time = string.IsNullOrEmpty(item.Time) ? start : item.Time;
                Text(dc, time, rect.X + 14, rect.Y + 8, rect.Width - 24, 10, MutedBrush, FontWeights.Medium);
                Text(dc, item.Title, rect.X + 14, rect.Y + 25, rect.Width - 24, 13, Foreground, FontWeights.SemiBold);
            }
            else if (rect.Height >= 20)
            {
                double badgeWidth = Math.Min(39, rect.Width * .4);
                Text(dc, start, rect.X + 12, rect.Y + 5, badgeWidth, 10, MutedBrush, FontWeights.Medium);
                Text(dc, item.Title, rect.X + badgeWidth + 17, rect.Y + 4, rect.Width - badgeWidth - 24, 11, Foreground, FontWeights.Medium);
            }
        }
        dc.Pop(); hits.Add((rect, item.Details, item));
    }
}
