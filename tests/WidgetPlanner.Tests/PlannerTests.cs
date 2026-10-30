using WidgetPlanner.Core;
using WidgetPlanner.Infrastructure;
using Xunit;

namespace WidgetPlanner.Tests;

public sealed class PlannerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private SqlitePlannerStore Store() => new(Path.Combine(directory, "planner.db"));
    [Fact] public void CompletionAndRestartPreserveOutstandingReply()
    {
        var planner = new Planner(Store());
        var task = planner.Add("Fix issue", today: true, replyRequired: true);
        planner.SetState(task.Id, PlanningState.Done);
        var restarted = new Planner(Store());
        Assert.Equal(PlanningState.Done, Assert.Single(restarted.TodayTasks).State);
        Assert.Equal(task.Id, Assert.Single(restarted.Replies).Id);
        restarted.HandleReply(task.Id);
        Assert.Empty(new Planner(Store()).Replies);
    }
    [Fact] public void RolloverAndCarryForwardPreserveOriginalPlan()
    {
        var clock = new Clock(); var planner = new Planner(Store(), clock);
        var day = planner.Today;
        var task = planner.Add("Original", today: true);
        planner.Add("Urgent interruption", today: true, unexpected: true);
        clock.Now = clock.Now.AddDays(1);
        Assert.Empty(planner.TodayTasks);
        planner.PlanToday(task.Id); planner.PlanToday(task.Id);
        Assert.Single(planner.TodayTasks);
        Assert.Single(planner.Review(day).Planned);
        Assert.Single(planner.Review(day).Unexpected);
        Assert.Equal(2, planner.Review(day).Unfinished.Count);
    }
    [Fact] public void ReorderingPersistsAndEmptyTitlesAreRejected()
    {
        var planner = new Planner(Store());
        var a = planner.Add("A", today: true); var b = planner.Add("B", today: true);
        planner.Move(b.Id, -1);
        Assert.Equal(new[] { b.Id, a.Id }, new Planner(Store()).TodayTasks.Select(t => t.Id));
        Assert.Throws<ArgumentException>(() => planner.Add("  "));
        Assert.Equal(2, new Planner(Store()).Snapshot.Tasks.Count);
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
