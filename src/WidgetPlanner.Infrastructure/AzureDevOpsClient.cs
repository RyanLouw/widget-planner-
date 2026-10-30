using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WidgetPlanner.Core;

namespace WidgetPlanner.Infrastructure;

public sealed class AzureDevOpsClient(HttpClient http)
{
    public static string ValidateOrganization(string value)
    {
        value = value.Trim();
        if (value.Length is < 1 or > 100 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Enter only the organisation name, for example contoso, not its URL.");
        return value;
    }

    // PATs use Basic authentication with an empty username, never Bearer authentication.
    // Configure the supplied HttpClient with redirects disabled so credentials stay on dev.azure.com.
    public async Task<IReadOnlyList<AzureWorkItem>> ReadAssignedAsync(string organization, string pat,
        CancellationToken cancellationToken = default)
    {
        organization = ValidateOrganization(organization);
        if (string.IsNullOrWhiteSpace(pat)) throw new ArgumentException("Enter a Work Items (Read) PAT.");
        var root = $"https://dev.azure.com/{organization}/";
        async Task<JsonDocument> Send(string path, object? body = null)
        {
            using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, root + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + pat)));
            if (body != null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new InvalidOperationException("Azure DevOps denied access. Check PAT expiry, organisation access, and Work Items (Read) scope.");
            if ((int)response.StatusCode == 429) throw new InvalidOperationException("Azure DevOps is throttling requests. Wait a moment and retry.");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Azure DevOps returned HTTP {(int)response.StatusCode}. No imported tasks were changed. Retry after checking access and connectivity.");
            try { return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken); }
            catch (JsonException) { throw new InvalidOperationException("Azure DevOps did not return the expected API response. Check your organisation and PAT."); }
        }
        using var query = await Send("_apis/wit/wiql?api-version=7.1", new
        {
            query = "SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = @Me ORDER BY [System.Id]"
        });
        var ids = query.RootElement.GetProperty("workItems").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).ToArray();
        var result = new List<AzureWorkItem>();
        var categories = new Dictionary<(string Project, string Type), Dictionary<string, string>>();
        foreach (var chunk in ids.Chunk(200))
        {
            using var batch = await Send("_apis/wit/workitemsbatch?api-version=7.1", new
            {
                ids = chunk, fields = new[] { "System.Id", "System.TeamProject", "System.Title", "System.WorkItemType", "System.State", "Microsoft.VSTS.Common.Priority", "System.Description" },
                errorPolicy = "Fail"
            });
            foreach (var item in batch.RootElement.GetProperty("value").EnumerateArray())
            {
                var fields = item.GetProperty("fields");
                string Field(string name) => fields.TryGetProperty(name, out var value) ? value.ToString() : "";
                var project = Field("System.TeamProject"); var type = Field("System.WorkItemType");
                if (!categories.TryGetValue((project, type), out var states))
                {
                    using var definition = await Send($"{Uri.EscapeDataString(project)}/_apis/wit/workitemtypes/{Uri.EscapeDataString(type)}/states?api-version=7.1");
                    states = definition.RootElement.GetProperty("value").EnumerateArray().ToDictionary(
                        s => s.GetProperty("name").GetString()!, s => s.GetProperty("category").GetString()!);
                    categories.Add((project, type), states);
                }
                var state = Field("System.State");
                if (!states.TryGetValue(state, out var category))
                    throw new InvalidOperationException("A project workflow changed during import. Retry to refresh its state definitions.");
                var id = item.GetProperty("id").GetInt32();
                result.Add(new(organization, project, id, Field("System.Title"), type, state, category,
                    int.TryParse(Field("Microsoft.VSTS.Common.Priority"), out var priority) ? priority : null,
                    Field("System.Description"), $"{root}{Uri.EscapeDataString(project)}/_workitems/edit/{id}", DateTimeOffset.UtcNow));
            }
        }
        return result;
    }
}
