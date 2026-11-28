using System.Collections.ObjectModel;
using Wideska.Infrastructure;
using Wideska.Models;
using Wideska.Services;

namespace Wideska.ViewModels;

public sealed class TaskColumn(TaskStage stage, string title, string hint, string color) : ObservableObject
{
    public TaskStage Stage { get; } = stage;
    public string Title => AppLocalization.Text(title);
    public string Hint => AppLocalization.Text(hint);
    public string Color { get; } = color;
    public ObservableCollection<BoardTask> Tasks { get; } = [];
    public void RefreshLabels() { Changed(nameof(Title)); Changed(nameof(Hint)); }
}

public sealed class TaskBoardViewModel(LocalStore store) : ObservableObject
{
    public const string StorageFile = "task-board.bin";
    private string error = "";
    public string Error { get => error; private set => Set(ref error, value); }
    public bool IsLoaded { get; private set; }
    public ScreenPlacement? Placement { get; set; }
    public IReadOnlyList<TaskColumn> Columns { get; } = [
        new(TaskStage.Backlog, "Backlog", "Capture tasks before prioritizing", "#97ADC4"),
        new(TaskStage.ImportantUrgent, "Important and urgent", "1–2 days", "#FF7385"),
        new(TaskStage.ImportantNotUrgent, "Important, not urgent", "3–7 days", "#65C9EF"),
        new(TaskStage.UrgentNotImportant, "Urgent, not important", "1–3 days", "#FFCA72"),
        new(TaskStage.Neither, "Neither urgent nor important", "7–31 days", "#A8B6C6"),
        new(TaskStage.Done, "Done", "Completed tasks", "#70D7AD")
    ];
    public async Task InitializeAsync()
    {
        if (IsLoaded) return;
        try
        {
            var saved = await store.ReadAsync<TaskBoardSnapshot>(StorageFile, true);
            foreach (var task in saved?.Tasks ?? [])
            {
                if (task is null || task.Id == Guid.Empty || string.IsNullOrWhiteSpace(task.Title)
                    || Columns.Any(c => c.Tasks.Any(t => t.Id == task.Id))) continue;
                var stage = Enum.IsDefined(task.Stage) ? task.Stage : TaskStage.Backlog;
                Columns.Single(c => c.Stage == stage).Tasks.Add(task with { Stage = stage });
            }
            Placement = saved?.Placement;
            IsLoaded = true;
            Error = "";
        }
        catch (Exception) { Error = AppLocalization.Text("Unable to load tasks. Reopen the widget to retry."); }
    }
    public async Task AddAsync(string title)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(title)) return;
        Columns[0].Tasks.Add(new(Guid.NewGuid(), title.Trim()[..Math.Min(title.Trim().Length, 1000)], TaskStage.Backlog));
        await SaveAsync();
    }
    public async Task MoveAsync(Guid id, TaskStage stage)
    {
        if (!IsLoaded || !Enum.IsDefined(stage)) return;
        var source = Columns.FirstOrDefault(c => c.Tasks.Any(t => t.Id == id));
        var task = source?.Tasks.FirstOrDefault(t => t.Id == id);
        if (task is null || source!.Stage == stage) return;
        source.Tasks.Remove(task);
        Columns.Single(c => c.Stage == stage).Tasks.Add(task with { Stage = stage });
        await SaveAsync();
    }
    public async Task RenameAsync(Guid id, string title)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(title)) return;
        var column = Columns.FirstOrDefault(c => c.Tasks.Any(t => t.Id == id));
        if (column is null) return;
        int index = column.Tasks.ToList().FindIndex(t => t.Id == id);
        column.Tasks[index] = column.Tasks[index] with { Title = title.Trim()[..Math.Min(title.Trim().Length, 1000)] };
        await SaveAsync();
    }
    public async Task SaveAsync()
    {
        if (!IsLoaded) return;
        try
        {
            await store.WriteAsync(StorageFile, new TaskBoardSnapshot { Tasks = Columns.SelectMany(c => c.Tasks).ToList(), Placement = Placement }, true);
            Error = "";
        }
        catch (Exception) { Error = AppLocalization.Text("Tasks could not be saved. Use Retry before exiting."); }
    }
}
