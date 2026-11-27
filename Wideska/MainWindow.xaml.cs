using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wideska.Infrastructure;
using Wideska.Providers;
using Wideska.Services;
using Wideska.ViewModels;

namespace Wideska;
public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    private DesktopIntegration? desktop;
    private readonly bool enableDesktopIntegration;
    private bool initialized;
    private bool loadStarted;
    private bool quitting;
    private bool? appliedStartup;
    private bool? widgetDisplayed;
    private Point? dragPoint;
    private bool changingGeometryMode;
    private readonly DispatcherTimer geometryTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    public MainWindow() : this(CreateViewModel(), true) { }
    private static MainViewModel CreateViewModel()
    {
        var store = new LocalStore();
        return new MainViewModel(store, new GoogleCalendarProvider(store));
    }
    public MainWindow(MainViewModel viewModel, bool enableDesktopIntegration)
    {
        InitializeComponent();
        model = viewModel;
        this.enableDesktopIntegration = enableDesktopIntegration;
        DataContext = model;
        model.AppearanceChanged += ApplyAppearance;
        IsVisibleChanged += (_, _) => model.SetClockVisible(IsVisible);
        model.FitDesktopRequested += FitDesktop;
        MouseMove += OnMove;
        MouseLeftButtonUp += (_, _) => { dragPoint = null; Mouse.Capture(null); };
        LocationChanged += (_, _) => QueueGeometrySave();
        SizeChanged += (_, _) => QueueGeometrySave();
        geometryTimer.Tick += async (_, _) =>
        {
            geometryTimer.Stop();
            CaptureGeometry();
            try { await model.SaveAsync(); }
            catch (Exception ex) { model.ReportError(ex); }
        };
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loadStarted) return;
        loadStarted = true;
        if (!enableDesktopIntegration) { ApplyAppearance(); return; }
        try
        {
            desktop = new DesktopIntegration(this);
            desktop.QuitRequested += Quit;
            desktop.SettingsRequested += () => model.SettingsOpen = model.ShowWidget;
            desktop.SyncRequested += () => model.RefreshCommand.Execute(null);
            desktop.GeometryLockChanged += value => model.GeometryLocked = value;
            desktop.FitDesktopRequested += FitDesktop;
            desktop.Host.GeometryChanged += QueueGeometrySave;
            await model.InitializeAsync();
            if (quitting) return;
            initialized = true;
            ApplyAppearance();
            QueueGeometrySave();
        }
        catch (Exception ex) { model.ReportError(ex); }
    }
    private void ApplyAppearance()
    {
        bool dark = model.DarkTheme;
        var colors = new Dictionary<string, string>
        {
            ["Surface"] = dark ? "#FA141A1F" : "#FAF5F7FA", ["Panel"] = dark ? "#FF253039" : "#FFE7EDF2",
            ["Text"] = dark ? "#FFF1F5F9" : "#FF17212B", ["Muted"] = dark ? "#FFAFBDC9" : "#FF586B7C",
            ["Line"] = dark ? "#24FFFFFF" : "#2417212B", ["Accent"] = dark ? "#FF8DD3EF" : "#FF24698C",
            ["AccentFill"] = dark ? "#FF347E9F" : "#FF24698C",
            ["TimelineCard"] = dark ? "#ED1D2730" : "#F2FFFFFF",
            ["TimelineMuted"] = dark ? "#BBCAD5" : "#526574",
            ["TimelineAxis"] = dark ? "#8098ACBA" : "#80607586",
            ["TimelineGrid"] = dark ? "#38BBCAD5" : "#38607586",
            ["TimelineNow"] = "#F43F5E",
            ["Weekend"] = dark ? "#FDA4AF" : "#BE123C"
        };
        foreach (var pair in colors) Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
        var tint = (Color)ColorConverter.ConvertFromString(dark ? "#10181E" : "#F5F7FA");
        tint.A = (byte)Math.Round(model.BackgroundOpacity * 255);
        Application.Current.Resources["WidgetGlass"] = new SolidColorBrush(tint);
        // Only the glass brush is translucent; content and controls retain full opacity.
        Opacity = 1;
        if (desktop is not null)
        {
            if (initialized && widgetDisplayed == true && !model.ShowWidget) CaptureGeometry();
            desktop.GeometryLocked = model.GeometryLocked;
            desktop.IsDesktopFilled = model.IsDesktopFilled;
            desktop.Apply(model.ShowWidget ? Models.WindowBehavior.Desktop : Models.WindowBehavior.Normal);
        }
        if (initialized && widgetDisplayed != model.ShowWidget)
        {
            geometryTimer.Stop();
            if (widgetDisplayed == true) CaptureGeometry();
            widgetDisplayed = model.ShowWidget;
            ApplyPresentationBounds();
        }
        if (initialized && appliedStartup != model.StartWithWindows)
        {
            try { DesktopIntegration.SetStartup(model.StartWithWindows); appliedStartup = model.StartWithWindows; }
            catch (Exception ex) { model.ReportError(ex); }
        }
    }
    private void ApplyPresentationBounds()
    {
        if (model.ShowWidget)
        {
            var settings = model.Settings;
            if (desktop?.Host.IsAttached == true)
            {
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var placement = settings.DesktopPlacement ?? new Models.ScreenPlacement((int)Math.Round(settings.Left * scale), (int)Math.Round(settings.Top * scale), (int)Math.Round(settings.Width * scale), (int)Math.Round(settings.Height * scale));
                changingGeometryMode = true;
                try { desktop.Host.SetBounds(placement); if (model.IsDesktopFilled) desktop.Host.FitToDesktop(); desktop.Host.RecoverToScreen(); }
                finally { changingGeometryMode = false; }
                return;
            }
            Width = double.IsFinite(settings.Width) ? Math.Clamp(settings.Width, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth)) : 720;
            Height = double.IsFinite(settings.Height) ? Math.Clamp(settings.Height, MinHeight, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight)) : 560;
            Left = double.IsFinite(settings.Left) ? Math.Clamp(settings.Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100) : 100;
            Top = double.IsFinite(settings.Top) ? Math.Clamp(settings.Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60) : 100;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            Width = Math.Min(480, work.Width); Height = Math.Min(720, work.Height);
            Left = work.Left + (work.Width - Width) / 2; Top = work.Top + (work.Height - Height) / 2;
        }
    }
    private async void OnImportCredentials(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import Google Desktop OAuth client", Filter = "Google OAuth JSON (*.json)|*.json", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try { await model.ImportCredentialsAsync(dialog.FileName); }
        catch (Exception ex) { model.ReportError(ex); }
    }
    private void OnOpenGoogleCloud(object sender, RoutedEventArgs e) => OpenLink("https://console.cloud.google.com/apis/library/calendar-json.googleapis.com");
    private void OnOpenGoogleGuide(object sender, RoutedEventArgs e) => OpenLink("https://developers.google.com/workspace/calendar/api/quickstart/python#authorize_credentials_for_a_desktop_application");
    private void OpenLink(string address)
    {
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch (Exception ex) { model.ReportError(ex); }
    }
    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        if (model.GeometryLocked) return;
        string edge = (sender as FrameworkElement)?.Tag as string ?? "SE";
        if (desktop?.Host.IsAttached == true) { desktop.Host.Resize(edge, e.HorizontalChange, e.VerticalChange); return; }
        double width = Math.Max(MinWidth, ActualWidth + (edge.Contains('W') ? -e.HorizontalChange : edge.Contains('E') ? e.HorizontalChange : 0));
        double height = Math.Max(MinHeight, ActualHeight + (edge.Contains('N') ? -e.VerticalChange : edge.Contains('S') ? e.VerticalChange : 0));
        if (edge.Contains('W')) Left += ActualWidth - width;
        if (edge.Contains('N')) Top += ActualHeight - height;
        Width = width; Height = height;
        if (e.HorizontalChange != 0 || e.VerticalChange != 0) model.IsDesktopFilled = false;
    }
    private void FitDesktop()
    {
        if (!model.CanEditGeometry) return;
        changingGeometryMode = true;
        try
        {
            if (desktop?.Host.IsAttached == true)
            {
                if (model.IsDesktopFilled)
                {
                    var area = desktop.Host.WorkArea();
                    var restore = model.Settings.RestorePlacement ?? new Models.ScreenPlacement(area.X + 40, area.Y + 40, Math.Min(720, area.Width), Math.Min(560, area.Height));
                    model.IsDesktopFilled = false; desktop.Host.SetBounds(restore); desktop.Host.RecoverToScreen();
                }
                else
                {
                    model.Settings.RestorePlacement = desktop.Host.GetBounds();
                    model.IsDesktopFilled = true; desktop.Host.FitToDesktop();
                }
            }
            else if (model.IsDesktopFilled)
            {
                model.IsDesktopFilled = false;
                Left = model.Settings.Left; Top = model.Settings.Top; Width = model.Settings.Width; Height = model.Settings.Height;
            }
            else
            {
                model.Settings.Left = Left; model.Settings.Top = Top; model.Settings.Width = ActualWidth; model.Settings.Height = ActualHeight;
                var area = SystemParameters.WorkArea;
                model.IsDesktopFilled = true; Left = area.Left; Top = area.Top; Width = area.Width; Height = area.Height;
            }
        }
        finally { changingGeometryMode = false; }
    }
    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (model.IsEventOpen || (model.ShowWidget && !model.CanMoveWidget)) return;
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null && !ReferenceEquals(source, sender))
        {
            if (source is ButtonBase or System.Windows.Controls.ComboBox or System.Windows.Controls.TextBox) return;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (desktop?.Host.IsAttached == true)
        {
            dragPoint = PointToScreen(e.GetPosition(this)); Mouse.Capture(this); e.Handled = true;
        }
        else DragMove();
    }
    private void OnMove(object sender, MouseEventArgs e)
    {
        if (dragPoint is not { } previous || e.LeftButton != MouseButtonState.Pressed || !model.CanMoveWidget || model.IsEventOpen || desktop?.Host.IsAttached != true) return;
        var current = PointToScreen(e.GetPosition(this));
        var b = desktop.Host.GetBounds();
        desktop.Host.SetBounds(b with { X = b.X + (int)Math.Round(current.X - previous.X), Y = b.Y + (int)Math.Round(current.Y - previous.Y) });
        dragPoint = current;
    }
    private void OnStateChanged(object? sender, EventArgs e) { if (WindowState == WindowState.Minimized && desktop is not null) Hide(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!enableDesktopIntegration) return;
        if (!quitting) { e.Cancel = true; if (desktop is not null) Hide(); else Quit(); }
    }
    private void QueueGeometrySave()
    {
        if (!initialized || quitting || !model.ShowWidget) return;
        if (!changingGeometryMode && desktop?.Host.IsAttached == true)
        {
            var bounds = desktop.Host.GetBounds(); var area = desktop.Host.WorkArea();
            model.IsDesktopFilled = Math.Abs(bounds.X - area.X) <= 1 && Math.Abs(bounds.Y - area.Y) <= 1 && Math.Abs(bounds.Width - area.Width) <= 1 && Math.Abs(bounds.Height - area.Height) <= 1;
        }
        geometryTimer.Stop(); geometryTimer.Start();
    }
    private void CaptureGeometry()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.IsEmpty || widgetDisplayed != true) return;
        if (desktop?.Host.IsAttached == true) { model.Settings.DesktopPlacement = desktop.Host.GetBounds(); return; }
        model.Settings.Left = bounds.Left; model.Settings.Top = bounds.Top; model.Settings.Width = bounds.Width; model.Settings.Height = bounds.Height;
    }
    private async void Quit()
    {
        if (quitting) return;
        quitting = true;
        geometryTimer.Stop();
        IsEnabled = false;
        CaptureGeometry();
        try { await model.ShutdownAsync(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Unable to save Wideska settings", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { desktop?.Dispose(); model.Dispose(); Application.Current.Shutdown(); }
    }
}
