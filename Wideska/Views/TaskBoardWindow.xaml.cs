using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wideska.Infrastructure;
using Wideska.Models;
using Wideska.Services;
using Wideska.ViewModels;

namespace Wideska.Views;

public partial class TaskBoardWindow : Window
{
    private const string DragFormat = "Vedeska.TaskId";
    private readonly TaskBoardViewModel model;
    private readonly bool attachToDesktop;
    private DesktopHost? host;
    private Point? dragStart;
    private Point? headerStart;
    private bool loaded;
    private bool closing;
    private bool editing;
    public TaskBoardWindow(TaskBoardViewModel model, bool attachToDesktop = true)
    {
        AppLocalization.EnsureInitialized();
        InitializeComponent();
        this.model = model;
        this.attachToDesktop = attachToDesktop;
        DataContext = model;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => { host?.Dispose(); DataContext = null; };
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loaded) return;
        loaded = true;
        await model.InitializeAsync();
        if (closing) return;
        if (attachToDesktop)
        {
            try
            {
                host = new DesktopHost(this);
                host.Attach();
                if (model.Placement is { Width: > 0, Height: > 0 } saved) host.SetBounds(saved);
                else
                {
                    var area = host.WorkArea();
                    host.SetBounds(new(area.X + 20, area.Y + 20, Math.Min(area.Width, 1080), Math.Min(area.Height, 620)));
                }
                host.RecoverToScreen();
            }
            catch (Exception ex) { host?.Dispose(); host = null; MessageBox.Show(ex.Message, "Vedeska Widget", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
    private void CapturePlacement()
    {
        if (host?.IsAttached == true) model.Placement = host.GetBounds();
    }
    public void RefreshLanguage()
    {
        foreach (var column in model.Columns) column.RefreshLabels();
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        Hide();
        CapturePlacement();
        await model.SaveAsync();
    }
    public async Task<bool> SaveAndCloseAsync()
    {
        CapturePlacement();
        await model.SaveAsync();
        if (!string.IsNullOrEmpty(model.Error)) { Show(); return false; }
        closing = true;
        Close();
        return true;
    }
    private async void OnRetry(object sender, RoutedEventArgs e)
    {
        if (!model.IsLoaded) await model.InitializeAsync();
        else await model.SaveAsync();
    }
    private async void OnAddTask(object sender, RoutedEventArgs e)
    {
        if (!model.IsLoaded || editing) return;
        var title = EditTitle("");
        if (title is not null) await model.AddAsync(title);
    }
    private string? EditTitle(string initial)
    {
        editing = true;
        try
        {
            // A normal dialog accepts keyboard focus while the desktop widget remains nonactivating.
            var input = new TextBox { Text = initial, MaxLength = 1000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var save = new Button { Content = AppLocalization.Text("Save task"), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
            var content = new StackPanel { Margin = new(20) };
            content.Children.Add(new TextBlock { Text = AppLocalization.Text("Task title"), Margin = new(0, 0, 0, 8) });
            content.Children.Add(input); content.Children.Add(save);
            var dialog = new Window { Title = AppLocalization.Text("Task title"), Content = content, Width = 440, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false, Background = (Brush)FindResource("Surface"), Icon = Icon };
            save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; };
            dialog.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.DialogResult = false; };
            dialog.Loaded += (_, _) => { input.Focus(); input.CaretIndex = input.Text.Length; };
            return dialog.ShowDialog() == true ? input.Text : null;
        }
        finally { editing = false; }
    }
    private void OnTaskMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindButton(e.OriginalSource as DependencyObject) is not null) return;
        dragStart = e.GetPosition(this);
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: BoardTask task })
        {
            dragStart = null;
            ShowTaskMenu((FrameworkElement)sender, task);
            e.Handled = true;
        }
    }
    private static Button? FindButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button button) return button;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : null;
        }
        return null;
    }
    private void OnTaskMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { dragStart = null; return; }
        if (dragStart is not { } start || sender is not FrameworkElement { DataContext: BoardTask task } card) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragStart = null;
        DragDrop.DoDragDrop(card, new DataObject(DragFormat, task.Id.ToString()), DragDropEffects.Move);
    }
    private bool TryTask(DragEventArgs e, out Guid id)
    {
        id = Guid.Empty;
        if (!e.Data.GetDataPresent(DragFormat) || !Guid.TryParse(e.Data.GetData(DragFormat) as string, out var parsed)) return false;
        id = parsed;
        return model.Columns.Any(c => c.Tasks.Any(t => t.Id == parsed));
    }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryTask(e, out _) ? DragDropEffects.Move : DragDropEffects.None;
        ((Border)sender).Opacity = e.Effects == DragDropEffects.Move ? .75 : 1;
        e.Handled = true;
    }
    private void OnDragLeave(object sender, DragEventArgs e) => ((Border)sender).Opacity = 1;
    private async void OnDrop(object sender, DragEventArgs e)
    {
        ((Border)sender).Opacity = 1;
        if (sender is FrameworkElement { DataContext: TaskColumn column } && TryTask(e, out var id))
        {
            e.Handled = true;
            await model.MoveAsync(id, column.Stage);
        }
    }
    private void OnTaskMenu(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BoardTask task } element) ShowTaskMenu(element, task);
        e.Handled = true;
    }
    private void OnTaskContextMenu(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: BoardTask task } element) ShowTaskMenu(element, task);
    }
    private void ShowTaskMenu(FrameworkElement element, BoardTask task)
    {
        var menu = new ContextMenu { PlacementTarget = element, Placement = PlacementMode.Bottom };
        menu.SetResourceReference(StyleProperty, "TrayContextMenu");
        var edit = new MenuItem { Header = AppLocalization.Text("Edit task") };
        edit.SetResourceReference(StyleProperty, "TrayMenuItem");
        edit.Click += async (_, _) => { var title = EditTitle(task.Title); if (title is not null) await model.RenameAsync(task.Id, title); };
        menu.Items.Add(edit); menu.Items.Add(new Separator());
        foreach (var column in model.Columns)
        {
            var item = new MenuItem { Header = column.Title, IsEnabled = column.Stage != task.Stage };
            item.SetResourceReference(StyleProperty, "TrayMenuItem");
            item.Click += async (_, _) => await model.MoveAsync(task.Id, column.Stage);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    private void OnHeaderDown(object sender, MouseButtonEventArgs e)
    {
        if (FindButton(e.OriginalSource as DependencyObject) is not null) return;
        if (host?.IsAttached != true) { DragMove(); return; }
        headerStart = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
    }
    private void OnHeaderMove(object sender, MouseEventArgs e)
    {
        if (headerStart is not { } start || e.LeftButton != MouseButtonState.Pressed || host?.IsAttached != true) return;
        var point = PointToScreen(e.GetPosition(this));
        var b = host.GetBounds();
        host.SetBounds(b with { X = b.X + (int)(point.X - start.X), Y = b.Y + (int)(point.Y - start.Y) });
        headerStart = point;
    }
    private async void OnHeaderUp(object sender, MouseButtonEventArgs e)
    {
        if (headerStart is null) return;
        headerStart = null; Mouse.Capture(null);
        CapturePlacement(); await model.SaveAsync();
    }
    private async void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        CapturePlacement();
        await model.SaveAsync();
    }
    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        if (host?.IsAttached == true) host.Resize("SE", e.HorizontalChange, e.VerticalChange);
        else { Width = Math.Max(MinWidth, Width + e.HorizontalChange); Height = Math.Max(MinHeight, Height + e.VerticalChange); }
    }
}
