using System.Net;
using System.Net.Http;
using System.Text;
using WidgetPlanner.Core;
using WidgetPlanner.Infrastructure;
using Xunit;

namespace WidgetPlanner.Tests;

public sealed class AzureDevOpsTests
{
    [Fact] public async Task UsesBasicPatAndDiscoversCustomWorkflowCategories()
    {
        var handler = new StubHandler();
        using var http = new HttpClient(handler);
        var items = await new AzureDevOpsClient(http).ReadAssignedAsync("example", "test-pat");
        var item = Assert.Single(items);
        Assert.Equal("Ready to verify", item.State);
        Assert.Equal("InProgress", item.StateCategory);
        Assert.Equal("Project A", item.Project);
        Assert.Equal(2, item.AzurePriority);
        Assert.Equal(3, handler.Requests);
    }
    [Fact] public async Task AccessDenialIsActionableWithoutLeakingResponseOrToken()
    {
        using var http = new HttpClient(new StubHandler(true));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new AzureDevOpsClient(http).ReadAssignedAsync("example", "test-pat"));
        Assert.Contains("PAT expiry", error.Message);
        Assert.DoesNotContain("test-pat", error.Message);
    }
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example/path")]
    [InlineData("")]
    public void RejectsNonOrganizationInput(string input) => Assert.Throws<ArgumentException>(() => AzureDevOpsClient.ValidateOrganization(input));

    [Fact] public void RefreshPreservesLocalCompletionPriorityAndReply()
    {
        var store = new MemoryStore(); var planner = new Planner(store);
        var item = new AzureWorkItem("example", "A", 1, "Original", "Bug", "Active", "InProgress", 1, "", "https://dev.azure.com/example/A/_workitems/edit/1", DateTimeOffset.UtcNow);
        planner.ImportAzure([item]);
        var task = Assert.Single(planner.Snapshot.Tasks);
        planner.PlanToday(task.Id); planner.SetState(task.Id, PlanningState.Done); planner.RequireReply(task.Id);
        planner.ImportAzure([item with { Title = "Updated", State = "Closed", StateCategory = "Completed", AzurePriority = 4 }]);
        var updated = Assert.Single(planner.Snapshot.Tasks);
        Assert.Equal(task.Id, updated.Id);
        Assert.Equal(PlanningState.Done, updated.State);
        Assert.Equal(Priority.Normal, updated.Priority);
        Assert.Equal("Closed", updated.Azure!.State);
        Assert.Single(planner.TodayTasks); Assert.Single(planner.Replies);
        planner.ImportAzure([item with { Id = 2, StateCategory = "Completed" }]);
        Assert.Single(planner.Snapshot.Tasks);
    }
    private sealed class MemoryStore : IPlannerStore
    {
        private PlannerSnapshot snapshot = new([], []);
        public PlannerSnapshot Load() => snapshot;
        public void Save(PlannerSnapshot value) => snapshot = value;
    }
    private sealed class StubHandler(bool deny = false) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal("dev.azure.com", request.RequestUri!.Host);
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes(":test-pat")), request.Headers.Authorization.Parameter);
            if (deny) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var content = Requests switch
            {
                1 => """{"workItems":[{"id":123}]}""",
                2 => """{"value":[{"id":123,"fields":{"System.TeamProject":"Project A","System.Title":"Fix issue","System.WorkItemType":"Bug","System.State":"Ready to verify","Microsoft.VSTS.Common.Priority":2}}]}""",
                _ => """{"value":[{"name":"Ready to verify","category":"InProgress"}]}"""
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") });
        }
    }
}
