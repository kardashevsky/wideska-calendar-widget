namespace Wideska.Models;

public enum TaskStage { Backlog, ImportantUrgent, ImportantNotUrgent, UrgentNotImportant, Neither, Done }

public sealed record BoardTask(Guid Id, string Title, TaskStage Stage);

public sealed class TaskBoardSnapshot
{
    public List<BoardTask> Tasks { get; set; } = [];
    public ScreenPlacement? Placement { get; set; }
}
