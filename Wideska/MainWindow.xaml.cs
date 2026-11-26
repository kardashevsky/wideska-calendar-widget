using System.ComponentModel;
using System.Windows;
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
    private bool quitting;
    private bool? appliedStartup;
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
        if (!enableDesktopIntegration) { ApplyAppearance(); return; }
        try
        {
            desktop = new DesktopIntegration(this);
            desktop.QuitRequested += Quit;
            await model.InitializeAsync();
            if (quitting) return;
            var settings = model.Settings;
            Width = double.IsFinite(settings.Width) ? Math.Clamp(settings.Width, MinWidth, SystemParameters.VirtualScreenWidth) : 960;
            Height = double.IsFinite(settings.Height) ? Math.Clamp(settings.Height, MinHeight, SystemParameters.VirtualScreenHeight) : 680;
            Left = double.IsFinite(settings.Left) ? Math.Clamp(settings.Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100) : 100;
            Top = double.IsFinite(settings.Top) ? Math.Clamp(settings.Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60) : 100;
            initialized = true;
            ApplyAppearance();
        }
        catch (Exception ex) { model.ReportError(ex); }
    }
    private void ApplyAppearance()
    {
        bool dark = model.DarkTheme;
        var colors = new Dictionary<string, string>
        {
            ["Surface"] = dark ? "#171C26" : "#F8FAFD", ["Panel"] = dark ? "#202735" : "#EDF1F8",
            ["Text"] = dark ? "#EDF1F8" : "#202B3E", ["Muted"] = dark ? "#9AA9BE" : "#586A82",
            ["Line"] = dark ? "#323E51" : "#D5DEEB", ["Accent"] = dark ? "#92ACFF" : "#355FBC"
        };
        foreach (var pair in colors) Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
        Opacity = model.Opacity;
        desktop?.Apply(model.Behavior);
        if (initialized && appliedStartup != model.StartWithWindows)
        {
            try { DesktopIntegration.SetStartup(model.StartWithWindows); appliedStartup = model.StartWithWindows; }
            catch (Exception ex) { model.ReportError(ex); }
        }
    }
    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null && !ReferenceEquals(source, sender))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase) return;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void OnHide(object sender, RoutedEventArgs e) { if (desktop is not null) Hide(); }
    private void OnStateChanged(object? sender, EventArgs e) { if (WindowState == WindowState.Minimized && desktop is not null) Hide(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!enableDesktopIntegration) return;
        if (!quitting) { e.Cancel = true; if (desktop is not null) Hide(); else Quit(); }
    }
    private void OnQuit(object sender, RoutedEventArgs e) => Quit();
    private void QueueGeometrySave()
    {
        if (!initialized || quitting) return;
        geometryTimer.Stop(); geometryTimer.Start();
    }
    private void CaptureGeometry()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.IsEmpty) return;
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
