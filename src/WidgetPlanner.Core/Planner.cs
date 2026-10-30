namespace WidgetPlanner.Core;

public enum PlanningState { ToDo, InProgress, Blocked, Done }
public enum Priority { Urgent, High, Normal, Low }
public sealed record PlannerTask(Guid Id, string Title, Priority Priority, PlanningState State,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, bool ReplyRequired, bool ReplyHandled)
{
    public AzureWorkItem? Azure { get; init; }
}
public sealed record AzureWorkItem(string Organization, string Project, int Id, string Title,
    string Type, string State, string StateCategory, int? AzurePriority, string Description,
    string Url, DateTimeOffset LastConfirmedAt);
public sealed record DayEntry(DateOnly Date, Guid TaskId, int Position, bool Unexpected);
public sealed record PlannerSnapshot(IReadOnlyList<PlannerTask> Tasks, IReadOnlyList<DayEntry> Days);
public sealed record DailyReview(IReadOnlyList<PlannerTask> Planned, IReadOnlyList<PlannerTask> Unexpected,
    IReadOnlyList<PlannerTask> Completed, IReadOnlyList<PlannerTask> Unfinished, IReadOnlyList<PlannerTask> Replies);

public interface IPlannerStore
{
    PlannerSnapshot Load();
    void Save(PlannerSnapshot snapshot);
}

public sealed class Planner(IPlannerStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public PlannerSnapshot Snapshot { get; private set; } = store.Load();
    public DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
    public IReadOnlyList<PlannerTask> TodayTasks => Snapshot.Days.Where(x => x.Date == Today)
        .OrderBy(x => x.Position).Select(x => Snapshot.Tasks.Single(t => t.Id == x.TaskId)).ToArray();
    public IReadOnlyList<PlannerTask> Replies => Snapshot.Tasks.Where(t => t.ReplyRequired && !t.ReplyHandled).ToArray();

    public PlannerTask Add(string title, Priority priority = Priority.Normal, bool today = false,
        bool unexpected = false, bool replyRequired = false)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Enter a task title.", nameof(title));
        var task = new PlannerTask(Guid.NewGuid(), title.Trim(), priority, PlanningState.ToDo,
            time.GetUtcNow(), null, replyRequired, false);
        var tasks = Snapshot.Tasks.Append(task).ToArray();
        var days = today ? Snapshot.Days.Append(new DayEntry(Today, task.Id, TodayTasks.Count, unexpected)).ToArray() : Snapshot.Days;
        Persist(new(tasks, days));
        return task;
    }

    public void PlanToday(Guid id)
    {
        Require(id);
        if (Snapshot.Days.Any(x => x.Date == Today && x.TaskId == id)) return;
        Persist(Snapshot with { Days = Snapshot.Days.Append(new DayEntry(Today, id, TodayTasks.Count, false)).ToArray() });
    }

    public void Move(Guid id, int direction)
    {
        var ordered = TodayTasks.Select(t => t.Id).ToList();
        var index = ordered.IndexOf(id);
        if (index < 0 || index + direction < 0 || index + direction >= ordered.Count) return;
        (ordered[index], ordered[index + direction]) = (ordered[index + direction], ordered[index]);
        Persist(Snapshot with { Days = Snapshot.Days.Select(d => d.Date == Today ? d with { Position = ordered.IndexOf(d.TaskId) } : d).ToArray() });
    }

    public void SetState(Guid id, PlanningState state) => Update(id, t => t with
    { State = state, CompletedAt = state == PlanningState.Done ? time.GetUtcNow() : null });
    public void HandleReply(Guid id) => Update(id, t => t with { ReplyHandled = true });
    public int ImportAzure(IReadOnlyList<AzureWorkItem> items)
    {
        var tasks = Snapshot.Tasks.ToList();
        foreach (var item in items)
        {
            var index = tasks.FindIndex(t => t.Azure is { } a && a.Id == item.Id &&
                string.Equals(a.Organization, item.Organization, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) tasks[index] = tasks[index] with { Title = item.Title, Azure = item };
            else if (item.StateCategory is not ("Completed" or "Removed"))
                tasks.Add(new PlannerTask(Guid.NewGuid(), item.Title, Priority.Normal, PlanningState.ToDo,
                    time.GetUtcNow(), null, false, false) { Azure = item });
        }
        Persist(Snapshot with { Tasks = tasks });
        return items.Count;
    }
    public void RequireReply(Guid id) => Update(id, t => t with { ReplyRequired = true, ReplyHandled = false });
    public DailyReview Review(DateOnly date)
    {
        var entries = Snapshot.Days.Where(d => d.Date == date).ToArray();
        PlannerTask[] Select(bool unexpected) => entries.Where(d => d.Unexpected == unexpected)
            .Select(d => Require(d.TaskId)).ToArray();
        var completed = Snapshot.Tasks.Where(t => t.CompletedAt is { } at &&
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, time.LocalTimeZone).DateTime) == date).ToArray();
        return new(Select(false), Select(true), completed,
            entries.Select(d => Require(d.TaskId)).Where(t => t.State != PlanningState.Done).ToArray(), Replies);
    }
    private PlannerTask Require(Guid id) => Snapshot.Tasks.Single(t => t.Id == id);
    private void Update(Guid id, Func<PlannerTask, PlannerTask> change)
    {
        Require(id);
        Persist(Snapshot with { Tasks = Snapshot.Tasks.Select(t => t.Id == id ? change(t) : t).ToArray() });
    }
    private void Persist(PlannerSnapshot next) { store.Save(next); Snapshot = next; }
}
