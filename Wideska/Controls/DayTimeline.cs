using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wideska.Services;
using Wideska.Infrastructure;
using Wideska.ViewModels;

namespace Wideska.Controls;

public sealed class DayTimeline : Control
{
    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(nameof(Days), typeof(IEnumerable<DayCell>), typeof(DayTimeline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, DaysChanged));
    public static readonly DependencyProperty CurrentTimeProperty = DependencyProperty.Register(nameof(CurrentTime), typeof(DateTimeOffset), typeof(DayTimeline), new FrameworkPropertyMetadata(default(DateTimeOffset), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TimeZoneIdProperty = DependencyProperty.Register(nameof(TimeZoneId), typeof(string), typeof(DayTimeline), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(DayTimeline), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EventCommandProperty = DependencyProperty.Register(nameof(EventCommand), typeof(ICommand), typeof(DayTimeline));
    public static readonly DependencyProperty ClockTextProperty = DependencyProperty.Register(nameof(ClockText), typeof(string), typeof(DayTimeline), new PropertyMetadata("", (d, _) => ((DayTimeline)d).RenderTimeBadge()));
    public string ClockText { get => (string)GetValue(ClockTextProperty); set => SetValue(ClockTextProperty, value); }
    public ICommand? EventCommand { get => (ICommand?)GetValue(EventCommandProperty); set => SetValue(EventCommandProperty, value); }
    public static readonly DependencyProperty SurfaceBrushProperty = ThemeBrush(nameof(SurfaceBrush), "#ED1D2730");
    public static readonly DependencyProperty MutedBrushProperty = ThemeBrush(nameof(MutedBrush), "#BBCAD5");
    public static readonly DependencyProperty AxisBrushProperty = ThemeBrush(nameof(AxisBrush), "#8098ACBA");
    public static readonly DependencyProperty AccentBrushProperty = ThemeBrush(nameof(AccentBrush), "#8DD3EF");
    public static readonly DependencyProperty TodayHeaderBrushProperty = ThemeBrush(nameof(TodayHeaderBrush), "#155E75");
    public static readonly DependencyProperty PastDayShadeProperty = ThemeBrush(nameof(PastDayShade), "#38000000");
    public Brush PastDayShade { get => (Brush)GetValue(PastDayShadeProperty); set => SetValue(PastDayShadeProperty, value); }
    public Brush TodayHeaderBrush { get => (Brush)GetValue(TodayHeaderBrushProperty); set => SetValue(TodayHeaderBrushProperty, value); }
    public static readonly DependencyProperty NowBrushProperty = ThemeBrush(nameof(NowBrush), "#FF304F");
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
    private readonly List<(Rect Bounds, string Details, EventItem? Event)> headerHits = [];
    private ScrollViewer? scrollOwner;
    private ScrollContentPresenter? scrollPresenter;
    private ScrollBar? verticalScrollBar;
    private readonly TranslateTransform scrollTransform = new();
    private readonly ScrollMotion scrollMotion = new();
    private readonly System.Diagnostics.Stopwatch scrollClock = new();
    private double previousFrameSeconds;
    private TimeSpan? previousRenderingTime;
    private int scrollDirection;
    private bool committingScroll;
    private bool returningToNow;
    private double returnStartOffset;
    private double returnTargetOffset;
    private double returnDurationSeconds;
    internal bool IsScrollAnimating => scrollClock.IsRunning;
    internal bool IsReturningToNow => returningToNow && IsScrollAnimating;
    internal double DisplayedVerticalOffset => IsScrollAnimating ? scrollMotion.Position : scrollOwner?.VerticalOffset ?? 0;
    internal int LayoutPassCount { get; private set; }
    internal int DrawPassCount { get; private set; }
    private double headerHeight;
    private bool drawingHeader;
    private INotifyCollectionChanged? observed;
    private string? hover;
    private readonly ToolTip eventToolTip = new();
    private bool scrollPending;
    private string? positionedDateRange;
    private bool automaticScrollComplete;
    private readonly DrawingVisual timeBadge = new();
    private readonly DrawingVisual stickyHeader = new();
    private readonly DrawingVisual calendarBody = new();
    private static readonly FontFamily ClockFont = new("Consolas");
    private double badgeY = double.NaN;
    public event Action? ViewportChanged;
    internal int TodayNavigationDirection
    {
        get
        {
            var days = Days?.ToList() ?? [];
            int index = days.FindIndex(d => CalendarTime.IndicatorMinutes(d.Date, CurrentTime, TimeZoneId) is not null);
            if (index < 0 || days.Count == 0) return 1;
            double left = TimeGutterWidth + index * (ActualWidth - TimeGutterWidth) / days.Count;
            return left < (scrollOwner?.HorizontalOffset ?? 0) ? -1 : 1;
        }
    }
    public bool IsTodayColumnVisible
    {
        get
        {
            var days = Days?.ToList() ?? [];
            int index = days.FindIndex(d => CalendarTime.IndicatorMinutes(d.Date, CurrentTime, TimeZoneId) is not null);
            if (index < 0 || ActualWidth <= TimeGutterWidth) return false;
            double column = (ActualWidth - TimeGutterWidth) / days.Count;
            double offset = scrollOwner?.HorizontalOffset ?? 0;
            double width = scrollOwner?.ViewportWidth ?? ActualWidth;
            double left = TimeGutterWidth + index * column;
            return Math.Min(left + column, offset + width) - Math.Max(left, offset) >= Math.Min(64, column);
        }
    }
    public void RevealToday()
    {
        FinishVisualScroll(true);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (!IsVisible || scrollOwner is null) return;
            var days = Days?.ToList() ?? [];
            int index = days.FindIndex(d => CalendarTime.IndicatorMinutes(d.Date, CurrentTime, TimeZoneId) is not null);
            if (index < 0) return;
            double column = (ActualWidth - TimeGutterWidth) / days.Count;
            scrollOwner.ScrollToHorizontalOffset(Math.Clamp(index * column, 0, scrollOwner.ScrollableWidth));
        });
    }
    protected override int VisualChildrenCount => 3;
    protected override Visual GetVisualChild(int index) => index switch { 0 => calendarBody, 1 => stickyHeader, 2 => timeBadge, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    protected override Size MeasureOverride(Size availableSize) => new(double.IsFinite(availableSize.Width) ? availableSize.Width : MinWidth, MinimumTimelineHeight);
    protected override Size ArrangeOverride(Size finalSize) { LayoutPassCount++; return finalSize; }
    public DayTimeline()
    {
        AddVisualChild(timeBadge);
        AddVisualChild(stickyHeader);
        AddVisualChild(calendarBody);
        MinHeight = MinimumTimelineHeight;
        calendarBody.Transform = scrollTransform;
        timeBadge.Transform = scrollTransform;
        PreviewMouseWheel += OnTimelineWheel;
        SnapsToDevicePixels = true; UseLayoutRounding = true;
        Loaded += (_, _) => { Observe(); ConnectScrollOwner(); ScheduleScroll(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) ScheduleScroll(); else { FinishVisualScroll(true); ClearHover(); } };
        Unloaded += (_, _) => { FinishVisualScroll(false); ClearHover(); if (observed is not null) observed.CollectionChanged -= CollectionChanged; observed = null; DisconnectScrollOwner(); };
        MouseLeave += (_, _) => { Cursor = Cursors.Arrow; ClearHover(); };
        MouseMove += (_, e) =>
        {
            if (IsScrollAnimating) { Cursor = Cursors.Arrow; return; }
            var hit = ItemAt(e.GetPosition(this));
            Cursor = hit.Event is not null ? Cursors.Hand : Cursors.Arrow;
            string? details = hit.Details;
            if (details == hover) return;
            if (details is null) { ClearHover(); return; }
            hover = details; eventToolTip.Content = details; ToolTip = eventToolTip; InvalidateVisual();
        };
    }
    private void OnTimelineWheel(object sender, MouseWheelEventArgs e)
    {
        CancelIdleReturn();
        if (scrollOwner is null || scrollPresenter is null || e.Delta == 0 || !SystemParameters.ClientAreaAnimation) return;
        int direction = Math.Sign(e.Delta);
        double current = DisplayedVerticalOffset;
        double target = IsScrollAnimating && direction == scrollDirection ? scrollMotion.Target : current;
        if (!IsScrollAnimating || direction != scrollDirection) scrollMotion.Reset(current);
        scrollMotion.Retarget(Math.Clamp(target - e.Delta / (double)Mouse.MouseWheelDeltaForOneLine * 72, 0, scrollOwner.ScrollableHeight));
        scrollDirection = direction;
        ClearHover(); Cursor = Cursors.Arrow;
        if (!IsScrollAnimating)
        {
            previousFrameSeconds = 0; previousRenderingTime = null;
            scrollClock.Restart();
            CompositionTarget.Rendering += OnVisualScrollFrame;
        }
        e.Handled = true;
    }
    internal void CancelIdleReturn()
    {
        if (returningToNow) FinishVisualScroll(true);
    }
    internal void ReturnToCurrentTime()
    {
        if (scrollOwner is null || scrollPresenter is null || !IsVisible || headerHeight <= 0) return;
        var today = Days?.FirstOrDefault(d => CalendarTime.IndicatorMinutes(d.Date, CurrentTime, TimeZoneId) is not null);
        if (today is null) return;
        double minutes = CalendarTime.IndicatorMinutes(today.Date, CurrentTime, TimeZoneId)!.Value;
        double bodyHeight = ActualHeight - headerHeight - TimelineBottomPadding;
        double target = Math.Clamp(minutes / 1440 * bodyHeight - Math.Max(0, scrollOwner.ViewportHeight - headerHeight) / 3,
            0, scrollOwner.ScrollableHeight);
        double current = DisplayedVerticalOffset;
        if (Math.Abs(target - current) < 1) return;
        FinishVisualScroll(true);
        if (!SystemParameters.ClientAreaAnimation)
        {
            ((IScrollInfo)scrollPresenter).SetVerticalOffset(target);
            scrollOwner.UpdateLayout();
            return;
        }
        returningToNow = true;
        returnStartOffset = current;
        returnTargetOffset = target;
        returnDurationSeconds = Math.Clamp(Math.Abs(target - current) / 800, .8, 1.8);
        scrollMotion.Reset(current);
        previousFrameSeconds = 0; previousRenderingTime = null;
        ClearHover(); Cursor = Cursors.Arrow;
        scrollClock.Restart();
        CompositionTarget.Rendering += OnVisualScrollFrame;
    }
    private void OnVisualScrollFrame(object? sender, EventArgs e)
    {
        if (scrollOwner is null || !IsVisible) { FinishVisualScroll(true); return; }
        if (e is RenderingEventArgs frame)
        {
            if (previousRenderingTime == frame.RenderingTime) return;
            previousRenderingTime = frame.RenderingTime;
        }
        double seconds = scrollClock.Elapsed.TotalSeconds;
        bool settled;
        if (returningToNow)
        {
            double progress = Math.Clamp(seconds / returnDurationSeconds, 0, 1);
            double eased = progress * progress * progress * (progress * (progress * 6 - 15) + 10);
            scrollMotion.Reset(returnStartOffset + (returnTargetOffset - returnStartOffset) * eased);
            settled = progress >= 1;
        }
        else { scrollMotion.Advance(seconds - previousFrameSeconds); settled = scrollMotion.IsSettled; }
        previousFrameSeconds = seconds;
        // Keep layout and ScrollViewer offsets unchanged while moving retained visuals only.
        scrollTransform.Y = scrollOwner.VerticalOffset - scrollMotion.Position;
        UpdateHeaderPosition(false);
        verticalScrollBar?.SetCurrentValue(RangeBase.ValueProperty, scrollMotion.Position);
        if (settled) FinishVisualScroll(true);
    }
    private void FinishVisualScroll(bool commit)
    {
        if (!IsScrollAnimating) return;
        returningToNow = false;
        double offset = scrollMotion.IsSettled ? scrollMotion.Target : scrollMotion.Position;
        CompositionTarget.Rendering -= OnVisualScrollFrame;
        scrollClock.Reset();
        committingScroll = true;
        try
        {
            if (commit && scrollPresenter is IScrollInfo info)
            {
                info.SetVerticalOffset(offset);
                scrollOwner?.UpdateLayout();
            }
            scrollTransform.Y = 0;
            if (scrollOwner is not null) verticalScrollBar?.SetCurrentValue(RangeBase.ValueProperty, scrollOwner.VerticalOffset);
            UpdateHeaderPosition(false);
        }
        finally { committingScroll = false; }
    }
    private void OnScrollPointerInput(object sender, MouseButtonEventArgs e) => FinishVisualScroll(true);
    private void OnScrollBarInput(object sender, ScrollEventArgs e) => FinishVisualScroll(true);
    private void OnScrollKeyInput(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End) FinishVisualScroll(true);
    }
    private void ClearHover()
    {
        if (hover is not null) InvalidateVisual();
        eventToolTip.IsOpen = false; eventToolTip.Content = null;
        ToolTip = null; hover = null;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var item = ItemAt(e.GetPosition(this)).Event;
        if (item is not null && EventCommand?.CanExecute(item) == true)
        {
            ClearHover(); EventCommand.Execute(item); e.Handled = true;
        }
        base.OnMouseLeftButtonUp(e);
    }
    private (Rect Bounds, string Details, EventItem? Event) ItemAt(Point position)
    {
        position.Y += DisplayedVerticalOffset - (scrollOwner?.VerticalOffset ?? 0);
        double offset = DisplayedVerticalOffset;
        if (position.Y >= offset && position.Y < offset + headerHeight)
            return headerHits.LastOrDefault(h => h.Bounds.Contains(new Point(position.X, position.Y - offset)));
        return hits.LastOrDefault(h => h.Bounds.Contains(position));
    }
    public EventItem? EventAt(Point position) => ItemAt(position).Event;
    private void ConnectScrollOwner()
    {
        FinishVisualScroll(true);
        DisconnectScrollOwner();
        DependencyObject? parent = VisualTreeHelper.GetParent(this);
        while (parent is not null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
        scrollOwner = parent as ScrollViewer;
        if (scrollOwner is not null)
        {
            scrollPresenter = scrollOwner.Template.FindName("PART_ScrollContentPresenter", scrollOwner) as ScrollContentPresenter;
            verticalScrollBar = scrollOwner.Template.FindName("PART_VerticalScrollBar", scrollOwner) as ScrollBar;
            scrollOwner.ScrollChanged += OnScrollChanged;
            scrollOwner.AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnScrollPointerInput), true);
            scrollOwner.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnScrollKeyInput), true);
            scrollOwner.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(OnScrollBarInput), true);
        }
        UpdateHeaderPosition();
    }
    private void DisconnectScrollOwner()
    {
        if (scrollOwner is not null)
        {
            scrollOwner.ScrollChanged -= OnScrollChanged;
            scrollOwner.RemoveHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnScrollPointerInput));
            scrollOwner.RemoveHandler(PreviewKeyDownEvent, new KeyEventHandler(OnScrollKeyInput));
            scrollOwner.RemoveHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(OnScrollBarInput));
        }
        scrollOwner = null;
        scrollPresenter = null; verticalScrollBar = null;
    }
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (committingScroll) return;
        if (e.VerticalChange != 0 || e.ViewportHeightChange != 0 || e.ExtentHeightChange != 0) FinishVisualScroll(false);
        if (e.VerticalChange == 0 && e.HorizontalChange == 0 && e.ViewportHeightChange == 0 && e.ViewportWidthChange == 0) return;
        ClearHover(); UpdateHeaderPosition(e.HorizontalChange != 0 || e.ViewportWidthChange != 0);
        if (IsMouseOver) Cursor = ItemAt(Mouse.GetPosition(this)).Event is not null ? Cursors.Hand : Cursors.Arrow;
    }
    private void UpdateHeaderPosition(bool notifyViewport = true)
    {
        double offset = scrollOwner?.VerticalOffset ?? 0;
        if (stickyHeader.Transform is TranslateTransform headerTransform && !headerTransform.IsFrozen) headerTransform.Y = offset;
        else stickyHeader.Transform = new TranslateTransform(0, offset);
        // Custom visuals must stay inside the visible scroll viewport, including above the pinned header.
        double left = scrollOwner?.HorizontalOffset ?? 0;
        double width = scrollOwner?.ViewportWidth ?? ActualWidth;
        double height = scrollOwner?.ViewportHeight ?? ActualHeight;
        var viewport = new Rect(left, offset, Math.Max(0, width), Math.Max(0, height));
        if (Clip is RectangleGeometry currentClip && !currentClip.IsFrozen) currentClip.Rect = viewport;
        else Clip = new RectangleGeometry(viewport);
        double displayed = DisplayedVerticalOffset;
        var badgeClip = new Rect(0, displayed + headerHeight, ActualWidth, Math.Max(0, ActualHeight - displayed - headerHeight));
        if (timeBadge.Clip is RectangleGeometry currentBadgeClip && !currentBadgeClip.IsFrozen) currentBadgeClip.Rect = badgeClip;
        else timeBadge.Clip = new RectangleGeometry(badgeClip);
        if (calendarBody.Clip is RectangleGeometry bodyClip && !bodyClip.IsFrozen) bodyClip.Rect = badgeClip;
        else calendarBody.Clip = new RectangleGeometry(badgeClip);
        if (notifyViewport) ViewportChanged?.Invoke();
    }
    private static void DaysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var timeline = (DayTimeline)d;
        timeline.FinishVisualScroll(true);
        timeline.Observe();
    }
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
            if (!IsVisible || days.Count == 0) return;
            string dateRange = string.Join("|", days.Select(d => d.Date.Ticks));
            if (dateRange == positionedDateRange) return;
            positionedDateRange = dateRange;
            if (automaticScrollComplete) return;
            automaticScrollComplete = true;
            if (!days.Any(d => CalendarTime.IndicatorMinutes(d.Date, CurrentTime, TimeZoneId) is not null)) return;
            DependencyObject? parent = VisualTreeHelper.GetParent(this);
            while (parent is not null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
            if (parent is not ScrollViewer viewer) return;
            var local = CalendarTime.Local(CurrentTime, TimeZoneId);
            double header = TimeStart(days);
            double bodyViewport = Math.Max(0, viewer.ViewportHeight - header);
            viewer.ScrollToVerticalOffset(Math.Max(0, local.TimeOfDay.TotalMinutes / 1440 * (ActualHeight - header - TimelineBottomPadding) - bodyViewport / 3));
        });
    }
    public const double MinimumTimelineHeight = 1480;
    public const double TimeGutterWidth = 84;
    public const double TimelineBottomPadding = 0;
    private const double DayHeaderHeight = 36;
    private const double AllDayRowHeight = 24;
    private double TimeStart(IReadOnlyList<DayCell> days) => DayHeaderHeight + Math.Min(3, days.Max(d => d.Events.Count(e => e.AllDay))) * AllDayRowHeight + 16;
    private void Text(DrawingContext dc, string text, double x, double y, double width, double size = 12, Brush? brush = null, FontWeight? weight = null, TextAlignment alignment = TextAlignment.Left, FontFamily? fontFamily = null)
    {
        if (width < 2) return;
        var formatted = new FormattedText(text, AppLocalization.Culture, FlowDirection.LeftToRight,
            new Typeface(fontFamily ?? FontFamily, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), size,
            brush ?? Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, width), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = alignment };
        dc.DrawText(formatted, new Point(x, y));
    }
    private void Title(DrawingContext dc, string text, Rect bounds, double size, Brush brush, FontWeight weight)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        dc.PushClip(new RectangleGeometry(bounds));
        dc.DrawText(EventTitle.Layout(text, new Typeface(FontFamily, FontStyles.Normal, weight, FontStretches.Normal), brush,
            bounds.Width, bounds.Height, size, VisualTreeHelper.GetDpi(this).PixelsPerDip), bounds.TopLeft);
        dc.Pop();
    }
    protected override void OnRender(DrawingContext dc)
    {
        DrawPassCount++;
        base.OnRender(dc);
        // Retain a stationary input surface even where the moving layer has no ink.
        // Otherwise wheel input over empty slots falls through to native scrolling.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(new Size(ActualWidth, ActualHeight)));
        using var body = calendarBody.RenderOpen();
        DrawCalendar(body);
    }
    private void DrawCalendar(DrawingContext dc)
    {
        hits.Clear(); headerHits.Clear(); badgeY = double.NaN;
        var days = Days?.ToList() ?? [];
        if (days.Count == 0 || ActualWidth <= TimeGutterWidth) { RenderTimeBadge(); using var empty = stickyHeader.RenderOpen(); return; }
        double column = (ActualWidth - TimeGutterWidth) / days.Count;
        int allDayRows = Math.Min(3, days.Max(d => d.Events.Count(e => e.AllDay)));
        double startY = TimeStart(days);
        double timelineHeight = Math.Max(24, ActualHeight - startY - TimelineBottomPadding);
        headerHeight = startY;
        int today = days.FindIndex(d => CalendarTime.IndicatorMinutes(d.Date, CurrentTime, TimeZoneId) is not null);
        double? nowMinutes = today < 0 ? null : CalendarTime.IndicatorMinutes(days[today].Date, CurrentTime, TimeZoneId);
        double nowY = nowMinutes is null ? -100 : startY + nowMinutes.Value / 1440 * timelineHeight;
        double axisX = TimeGutterWidth;
        var dpi = VisualTreeHelper.GetDpi(this);
        double horizontalThickness = 1 / dpi.DpiScaleY;
        double verticalThickness = 1 / dpi.DpiScaleX;
        var gridPen = new Pen(GridBrush, horizontalThickness);
        var verticalGridPen = new Pen(GridBrush, verticalThickness);
        if (days.Count > 1 && today >= 0)
        {
            dc.PushOpacity(.035);
            dc.DrawRectangle(AccentBrush, null, new Rect(TimeGutterWidth + today * column, startY, column, timelineHeight));
            dc.Pop();
        }
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
        var guidelines = new GuidelineSet();
        for (int i = 0; i <= days.Count; i++)
        {
            double x = TimeGutterWidth + i * column;
            guidelines.GuidelinesX.Add(x - verticalThickness / 2);
            guidelines.GuidelinesX.Add(x + verticalThickness / 2);
        }
        for (int hour = 0; hour <= 24; hour++)
        {
            double y = startY + timelineHeight * hour / 24;
            guidelines.GuidelinesY.Add(y - horizontalThickness / 2);
            guidelines.GuidelinesY.Add(y + horizontalThickness / 2);
        }
        dc.PushGuidelineSet(guidelines);
        dc.DrawLine(new Pen(AxisBrush, verticalThickness), new Point(axisX, startY), new Point(axisX, ActualHeight - TimelineBottomPadding));
        for (int hour = 0; hour <= 24; hour++)
        {
            double y = startY + timelineHeight * hour / 24;
            dc.DrawLine(gridPen, new Point(TimeGutterWidth, y), new Point(ActualWidth, y));
            dc.DrawLine(new Pen(AxisBrush, horizontalThickness), new Point(axisX - 4, y), new Point(TimeGutterWidth, y));
            if (Math.Abs(y - nowY) > 15)
                Text(dc, $"{hour % 24:00}:00", 3, hour == 0 ? y + 4 : hour == 24 ? y - 18 : y - 8, axisX - 13, 12, MutedBrush, FontWeights.Medium, TextAlignment.Right);
        }
        for (int index = 1; index < days.Count; index++)
            dc.DrawLine(verticalGridPen, new Point(TimeGutterWidth + index * column, startY), new Point(TimeGutterWidth + index * column, ActualHeight));
        dc.Pop();
        for (int index = 0; index < days.Count; index++)
        {
            var day = days[index]; double x = TimeGutterWidth + index * column;
            foreach (var item in TimelineLayout.Arrange(day.Events))
            {
                double inset = index == 0 ? 2 : 8;
                double availableWidth = Math.Max(1, column - inset - 8);
                double top = startY + Math.Clamp(item.Event.StartMinute, 0, 1440) / 1440 * timelineHeight;
                double bottom = startY + Math.Clamp(item.Event.EndMinute, 0, 1440) / 1440 * timelineHeight;
                if (bottom <= top) continue;
                var rect = new Rect(x + inset + item.HorizontalOffset * availableWidth, top + 2, Math.Max(1, availableWidth * item.WidthFraction - 2), Math.Max(2, bottom - top - 4));
                Event(dc, item.Event, rect, true);
            }
            if (day.IsPast)
                dc.DrawRectangle(PastDayShade, null, new Rect(x, startY, column, timelineHeight));
        }
        // The live-time badge and line remain visually distinct from event-start accents.
        if (today >= 0 && nowMinutes is not null)
        {
            double x = TimeGutterWidth + today * column;
            badgeY = nowY;
            var contrastPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 12, 18, 24)), 5);
            dc.DrawLine(contrastPen, new Point(x + 4, nowY), new Point(x + column - 3, nowY));
            dc.DrawLine(new Pen(NowBrush, 3), new Point(x + 4, nowY), new Point(x + column - 3, nowY));
            dc.DrawEllipse(NowBrush, new Pen(contrastPen.Brush, 2), new Point(x + 4, nowY), 5, 5);
        }
        RenderTimeBadge();
        RenderHeader(days, column, today, allDayRows);
        UpdateHeaderPosition();
    }
    private void RenderHeader(IReadOnlyList<DayCell> days, double column, int today, int allDayRows)
    {
        using var dc = stickyHeader.RenderOpen();
        Brush background = SurfaceBrush;
        if (SurfaceBrush is SolidColorBrush solid) background = new SolidColorBrush(Color.FromRgb(solid.Color.R, solid.Color.G, solid.Color.B));
        var headerBounds = new Rect(TimeGutterWidth, 0, Math.Max(0, ActualWidth - TimeGutterWidth), headerHeight);
        var headerClip = new GeometryGroup { FillRule = FillRule.Nonzero };
        headerClip.Children.Add(new RectangleGeometry(headerBounds, 10, 10));
        headerClip.Children.Add(new RectangleGeometry(new Rect(headerBounds.X, headerHeight - 10, headerBounds.Width, 10)));
        headerClip.Freeze();
        dc.PushClip(headerClip);
        dc.DrawRectangle(background, null, headerBounds);
        drawingHeader = true;
        try
        {
            for (int index = 0; index < days.Count; index++)
            {
                var day = days[index]; double x = TimeGutterWidth + index * column;
                if (index > 0)
                {
                    dc.PushOpacity(.45);
                    dc.DrawLine(new Pen(GridBrush, 1), new Point(x, 8), new Point(x, headerHeight - 8));
                    dc.Pop();
                }
                headerHits.Add((new Rect(x, 0, column, headerHeight), day.Summary, null));
                bool isToday = index == today;
                if (isToday)
                {
                    dc.DrawRectangle(TodayHeaderBrush, null, new Rect(x, 0, column, headerHeight));
                    dc.DrawLine(new Pen(AccentBrush, 2), new Point(x, headerHeight - 1), new Point(x + column, headerHeight - 1));
                }
                Brush label = day.IsWeekend ? WeekendBrush : isToday ? AccentBrush : Foreground;
                double labelSize = isToday ? (column >= 100 ? 20 : column >= 70 ? 16 : 14) : 12;
                FontWeight labelWeight = isToday ? FontWeights.SemiBold : FontWeights.Medium;
                var fullLabel = new FormattedText(day.FullLabel, AppLocalization.Culture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily, FontStyles.Normal, labelWeight, FontStretches.Normal), labelSize, label, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                Text(dc, fullLabel.WidthIncludingTrailingWhitespace <= column - 20 ? day.FullLabel : day.Label, x + 10, isToday ? 16 - labelSize * .6 : 8, column - 20, labelSize, label, labelWeight);
                int row = 0;
                foreach (var item in day.Events.Where(e => e.AllDay).Take(allDayRows))
                    Event(dc, item, new Rect(x + (index == 0 ? 2 : 8), DayHeaderHeight + AllDayRowHeight * row++, Math.Max(1, column - (index == 0 ? 10 : 16)), 22), false);
                if (day.Events.Count(e => e.AllDay) > allDayRows) Text(dc, AppLocalization.Text("More all-day events"), x + 12, headerHeight - 14, column - 24, 9, MutedBrush);
                if (day.IsPast) dc.DrawRectangle(PastDayShade, null, new Rect(x, 0, column, headerHeight));
            }
        }
        finally { drawingHeader = false; dc.Pop(); }
        dc.DrawLine(new Pen(GridBrush, 1), new Point(TimeGutterWidth, headerHeight - .5), new Point(ActualWidth, headerHeight - .5));
    }
    private void RenderTimeBadge()
    {
        using var dc = timeBadge.RenderOpen();
        if (double.IsNaN(badgeY)) return;
        dc.DrawRoundedRectangle(NowBrush, null, new Rect(3, badgeY - 11, 66, 22), 7, 7);
        string text = string.IsNullOrEmpty(ClockText) ? CalendarTime.Local(CurrentTime, TimeZoneId).ToString("HH:mm:ss") : ClockText;
        Text(dc, text, 9, badgeY - 7, 54, 11, Brushes.White, FontWeights.SemiBold, TextAlignment.Left, ClockFont);
    }
    private void Event(DrawingContext dc, EventItem item, Rect rect, bool timed)
    {
        var fill = Wideska.Converters.EventColorConverter.CardColor(item);
        Brush color = new SolidColorBrush(fill);
        Brush textBrush = Wideska.Converters.EventColorConverter.CardTextBrush(item);
        bool highlighted = !item.IsSleepCalendar && item.Details == hover;
        double radius = timed ? Math.Min(8, rect.Height / 2) : 5;
        var shape = new RectangleGeometry(rect, radius, radius);
        dc.PushClip(shape);
        dc.DrawRoundedRectangle(color, null, rect, radius, radius);
        dc.PushOpacity(item.IsSleepCalendar ? .1 : (highlighted ? .75 : .3));
        dc.DrawRoundedRectangle(null, new Pen(textBrush, highlighted ? 1.5 : 1), rect, radius, radius); dc.Pop();
        if (!timed)
        {
            dc.DrawEllipse(textBrush, null, new Point(rect.X + 11, rect.Y + rect.Height / 2), 3, 3);
            Title(dc, item.Title, new Rect(rect.X + 21, rect.Y + 3, Math.Max(0, rect.Width - 29), Math.Max(0, rect.Height - 6)), 13, textBrush, FontWeights.Medium);
        }
        else
        {
            string start = TimeSpan.FromMinutes(Math.Clamp(item.StartMinute, 0, 1440)).ToString(@"hh\:mm");
            double width = Math.Max(0, rect.Width - 20);
            if (rect.Height >= 34 && width >= 2)
            {
                bool compact = rect.Height < 46;
                double top = compact ? 2 : 7;
                double titleHeight = compact ? FontFamily.LineSpacing * 13 : Math.Max(12, rect.Height - top - 24);
                var title = EventTitle.Layout(item.Title, new Typeface(FontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
                    textBrush, width, titleHeight, 13, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                Title(dc, item.Title, new Rect(rect.X + 10, rect.Y + top, width, titleHeight), 13, textBrush, FontWeights.Medium);
                double timeY = rect.Y + top + Math.Min(title.Height, titleHeight) + (compact ? 0 : 3);
                string time = string.IsNullOrEmpty(item.Time) ? start : item.Time;
                Text(dc, time, rect.X + 10, timeY, width, compact ? 11 : 12, textBrush, FontWeights.Medium);
            }
            else if (rect.Height >= 20)
            {
                string time = (string.IsNullOrEmpty(item.Time) ? start : item.Time).Replace(" ", "");
                double compactWidth = Math.Max(0, rect.Width - 12);
                var timeLayout = new FormattedText(time, AppLocalization.Culture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), 11,
                    textBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double timeWidth = Math.Min(compactWidth, Math.Ceiling(timeLayout.WidthIncludingTrailingWhitespace));
                Title(dc, item.Title, new Rect(rect.X + 6, rect.Y + (rect.Height - FontFamily.LineSpacing * 13) / 2,
                    Math.Max(0, compactWidth - timeWidth - 6), FontFamily.LineSpacing * 13), 13, textBrush, FontWeights.Medium);
                Text(dc, time, rect.Right - 6 - timeWidth, rect.Y + (rect.Height - timeLayout.Height) / 2,
                    timeWidth, 11, textBrush, FontWeights.Medium);
            }
        }
        dc.Pop(); (drawingHeader ? headerHits : hits).Add((rect, item.Details, item));
    }
}
