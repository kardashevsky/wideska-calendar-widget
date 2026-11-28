using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wideska.Models;
using Wideska.Services;
using Wideska.ViewModels;

internal static partial class Program
{
    private static async Task RunTaskBoardSuiteAsync(Application app)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Vedeska-tasks-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { await TestTaskBoardAsync(app, directory, true); }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task TestTaskBoardAsync(Application app, string directory, bool includeUi)
    {
        var store = new LocalStore(Path.Combine(directory, "task-board"));
        var board = new TaskBoardViewModel(store);
        await board.InitializeAsync();
        Check(board.IsLoaded && board.Columns.Count == 6 && board.Columns.All(c => c.Tasks.Count == 0), "A new board must have six empty destinations, without sample or calendar data");
        await board.AddAsync("   ");
        Check(board.Columns[0].Tasks.Count == 0, "Blank tasks must be rejected");
        await board.AddAsync("  Prepare quarterly plan  ");
        var task = board.Columns[0].Tasks.Single();
        Check(task.Title == "Prepare quarterly plan" && task.Id != Guid.Empty, "New tasks must start in backlog with trimmed text and stable identity");
        foreach (var stage in Enum.GetValues<TaskStage>())
        {
            await board.MoveAsync(task.Id, stage);
            Check(board.Columns.Sum(c => c.Tasks.Count) == 1 && board.Columns.Single(c => c.Stage == stage).Tasks.Single().Id == task.Id,
                "Moving a task must preserve identity without duplicates across every quadrant and Done");
        }
        await board.MoveAsync(task.Id, TaskStage.Done);
        await board.MoveAsync(task.Id, TaskStage.Backlog);
        await board.MoveAsync(Guid.NewGuid(), TaskStage.Done);
        await board.MoveAsync(task.Id, (TaskStage)999);
        Check(board.Columns[0].Tasks.Single().Id == task.Id, "Done must be reversible and unrelated or invalid drag payloads must not change the board");
        await board.RenameAsync(task.Id, "Revised quarterly plan");
        board.Placement = new(-200, 100, 980, 620);
        await board.SaveAsync();
        var restored = new TaskBoardViewModel(store);
        await restored.InitializeAsync();
        await restored.InitializeAsync();
        Check(restored.Columns[0].Tasks.Single() == new BoardTask(task.Id, "Revised quarterly plan", TaskStage.Backlog)
            && restored.Placement == board.Placement, "Tasks and geometry must survive restart; repeated initialization must not duplicate tasks");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "task-board", TaskBoardViewModel.StorageFile));
        Check(!System.Text.Encoding.UTF8.GetString(bytes).Contains("Revised quarterly plan"), "Personal tasks must use encrypted local storage");
        string blocked = Path.Combine(directory, "blocked-task-store");
        var failing = new TaskBoardViewModel(new LocalStore(blocked));
        await failing.InitializeAsync();
        await File.WriteAllTextAsync(blocked, "not a directory");
        await failing.AddAsync("Keep this unsaved task");
        Check(failing.Error.Length > 0 && failing.Columns[0].Tasks.Count == 1, "Write errors must remain visible without dropping the unsaved task");
        File.Delete(blocked);
        await failing.SaveAsync();
        Check(failing.Error.Length == 0, "Retry must persist pending tasks after the storage failure is resolved");
        string language = AppLocalization.Culture.TwoLetterISOLanguageName;
        try
        {
            AppLocalization.Apply("ru");
            Check(board.Columns[1].Title == "Важно и срочно" && board.Columns[1].Hint == "1–2 дня"
                && board.Columns[3].Hint == "1–3 дня" && board.Columns[4].Hint == "7–31 день",
                "Task priorities and planning horizons must have intact Russian translations");
        }
        finally { AppLocalization.Apply(language == "ru" ? "ru" : "en"); }
        if (!includeUi) return;
        await board.AddAsync("A longer task title that should wrap naturally inside a narrow backlog column");
        await board.AddAsync("Review priorities");
        await board.MoveAsync(task.Id, TaskStage.ImportantUrgent);
        if (!app.Resources.Contains("Caption")) app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Wideska;component/Resources/Styles.xaml") });
        var window = new Wideska.Views.TaskBoardWindow(board, attachToDesktop: false);
        window.Show();
        try
        {
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var columns = Descendants(window).OfType<ContentControl>().Where(c => c.Content is TaskColumn).ToList();
            Check(columns.Count == 6 && columns.All(c => c.ActualWidth > 100 && c.ActualHeight > 80), "Task board must render all six destinations with usable space");
            var dropTargets = Descendants(window).OfType<Border>().Where(b => b.Name == "DropZone" && b.AllowDrop).ToList();
            Check(dropTargets.Count == 6 && dropTargets.All(b => b.DataContext is TaskColumn), "All six task destinations must accept drag and drop");
            Check(!window.ShowInTaskbar && !window.ShowActivated, "The task board must retain widget presentation without initial activation");
            SavePreview(window, "task-board-prototype");
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(columns.All(c => c.ActualWidth > 100 && c.ActualHeight > 80), "The matrix must remain usable at minimum widget dimensions");
            SavePreview(window, "task-board-compact");
        }
        finally { await window.SaveAndCloseAsync(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
