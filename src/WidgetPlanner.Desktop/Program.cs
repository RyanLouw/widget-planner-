using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WidgetPlanner.Core;
using WidgetPlanner.Infrastructure;
using Forms = System.Windows.Forms;

namespace WidgetPlanner.Desktop;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var instance = new System.Threading.Mutex(true, "Local\\WidgetPlanner.Desktop", out var first);
        if (!first) { MessageBox.Show("Planner is already running. Open it from the system tray."); return; }
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetPlanner", "planner.db");
            var planner = new Planner(new SqlitePlannerStore(path));
            var panel = new PlannerWindow(planner);
            using var tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Work planner", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open planner", null, (_, _) => { panel.Show(); panel.Activate(); });
            menu.Items.Add("Exit", null, (_, _) => { tray.Visible = false; app.Shutdown(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (_, _) => { panel.Show(); panel.Activate(); };
            app.Run(panel);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Planner could not start. Existing data has not been reset.\n\n" + ex.Message, "Work planner");
        }
    }
}

public sealed class PlannerWindow : Window
{
    private readonly Planner planner;
    private readonly StackPanel body = new() { Margin = new Thickness(16) };
    private bool expanded;
    private bool workspace;
    private string tab = "Today";
    private readonly System.Windows.Threading.DispatcherTimer dayTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private DateOnly shownDay;
    private readonly TextBox organization = new() { MaxLength = 100, Padding = new Thickness(6) };
    private readonly PasswordBox pat = new() { Padding = new Thickness(6) };
    private bool syncing;
    private string syncStatus = "Azure DevOps: not connected";

    private async Task SyncAzure()
    {
        if (syncing) return;
        syncing = true;
        syncStatus = "Reading assigned Azure DevOps tickets…";
        var org = organization.Text;
        var token = pat.Password;
        Render();
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
            var items = await new AzureDevOpsClient(http).ReadAssignedAsync(org, token);
            planner.ImportAzure(items);
            syncStatus = $"Last successful read: {DateTime.Now:t} · {items.Count} assigned items checked";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { syncStatus = "Connection failed or timed out. Existing tasks are unchanged. Retry when connected."; }
        catch (Exception ex) { syncStatus = ex.Message; }
        finally { syncing = false; Render(); }
    }

    public PlannerWindow(Planner planner)
    {
        this.planner = planner; shownDay = planner.Today;
        Title = "Work planner"; Width = 360; SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        Left = Math.Max(0, SystemParameters.WorkArea.Right - 380); Top = 40;
        Background = new SolidColorBrush(Color.FromRgb(27, 34, 43));
        Foreground = Brushes.WhiteSmoke; Opacity = .94;
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
        dayTimer.Tick += (_, _) => { if (shownDay != planner.Today) { shownDay = planner.Today; Render(); } };
        dayTimer.Start();
        Render();
    }
    private void Run(Action action)
    {
        try { action(); Render(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save change"); }
    }
    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 4, 6, 4), Padding = new Thickness(8, 4, 8, 4) };
        button.Click += (_, _) => action(); return button;
    }
    private void Label(string text, double size = 14) => body.Children.Add(new TextBlock
    { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) });
    private void Render()
    {
        body.Children.Clear();
        Label($"Today · {planner.TodayTasks.Count(t => t.State != PlanningState.Done)} left · {planner.Replies.Count} replies", 18);
        var current = planner.TodayTasks.FirstOrDefault(t => t.State == PlanningState.InProgress);
        if (current != null) Label("Working on: " + current.Title);
        body.Children.Add(Button(expanded ? "Collapse" : "Open today", () => { expanded = !expanded; Render(); }));
        if (!expanded) return;
        var controls = new WrapPanel();
        controls.Children.Add(Button(workspace ? "Compact panel" : "Workspace", () => { workspace = !workspace; Width = workspace ? 760 : 360; Render(); }));
        var pin = new CheckBox { Content = "Always on top", IsChecked = Topmost, VerticalAlignment = VerticalAlignment.Center };
        pin.Checked += (_, _) => Topmost = true; pin.Unchecked += (_, _) => Topmost = false;
        controls.Children.Add(pin); body.Children.Add(controls);
        Label("Quick capture — Enter adds to Today");
        var input = new TextBox { Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(8), MaxLength = 500 };
        var unexpected = new CheckBox { Content = "Unexpected interruption", Margin = new Thickness(0, 4, 0, 4) };
        var reply = new CheckBox { Content = "Reply required", Margin = new Thickness(0, 4, 0, 4) };
        var priority = new ComboBox { ItemsSource = Enum.GetValues<Priority>(), SelectedItem = Priority.Normal };
        void Add() => Run(() => planner.Add(input.Text, (Priority)priority.SelectedItem, true, unexpected.IsChecked == true, reply.IsChecked == true));
        input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Add(); e.Handled = true; } };
        body.Children.Add(input); body.Children.Add(priority); body.Children.Add(unexpected); body.Children.Add(reply);
        body.Children.Add(Button("Add to Today", Add));
        if (workspace)
        {
            var tabs = new WrapPanel();
            foreach (var name in new[] { "Today", "Backlog", "Replies", "Review", "Connections" })
                tabs.Children.Add(Button(name, () => { tab = name; Render(); }));
            body.Children.Add(tabs);
        }
        var active = workspace ? tab : "Today";
        Label(active, 20);
        switch (active)
        {
            case "Connections":
                Label("Microsoft 365: not connected — existing email application details are needed.");
                Label(syncStatus);
                Label("Azure DevOps organisation name (the part after dev.azure.com/)");
                body.Children.Add(organization);
                Label("Personal access token — Work Items (Read). Kept in memory for this session only.");
                body.Children.Add(pat);
                var sync = Button(syncing ? "Reading…" : "Import / refresh my tickets", async () => await SyncAzure());
                sync.IsEnabled = !syncing;
                body.Children.Add(sync);
                var clear = Button("Forget token", () => { pat.Clear(); syncStatus = "Token cleared. Imported tasks remain available."; Render(); });
                clear.IsEnabled = !syncing;
                body.Children.Add(clear);
                Label("Read-only connection: local completion never changes a ticket in Azure DevOps. Refresh is manual. Closing or exiting the app does not delete imported tasks; exiting clears the token.");
                Label("Closing the window hides it in the system tray. Use the tray menu to exit. Local data is saved after each change.");
                break;
            case "Review":
                var review = planner.Review(planner.Today);
                Label($"Planned: {review.Planned.Count} · Unexpected: {review.Unexpected.Count}\nCompleted today: {review.Completed.Count} · Unfinished today: {review.Unfinished.Count}\nOutstanding replies: {review.Replies.Count}");
                foreach (var t in review.Completed) Label("Completed · " + t.Title);
                foreach (var t in review.Unfinished) Label(t.State + " · " + t.Title);
                foreach (var t in review.Replies) Label("Reply needed · " + t.Title);
                break;
            case "Replies": foreach (var task in planner.Replies) TaskRow(task, false); break;
            case "Backlog": foreach (var task in planner.Snapshot.Tasks.Where(t => t.State != PlanningState.Done)) TaskRow(task, true); break;
            default:
                if (planner.TodayTasks.Count == 0) Label("Choose work from Backlog or capture a task above.");
                foreach (var task in planner.TodayTasks) TaskRow(task, false);
                if (planner.Replies.Count > 0) Label($"Reply needed: {planner.Replies.Count} — open Workspace → Replies");
                break;
        }
    }
    private void TaskRow(PlannerTask task, bool backlog)
    {
        Label($"{task.Priority} · {task.Title}");
        if (task.Azure is { } azure)
        {
            Label($"{azure.Project} · #{azure.Id} · {azure.Type}\nAzure: {azure.State} · Azure priority: {azure.AzurePriority?.ToString() ?? "—"}\nLast confirmed: {azure.LastConfirmedAt.LocalDateTime:g}");
            body.Children.Add(Button("Open Azure DevOps ticket", () => Run(() =>
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(azure.Url) { UseShellExecute = true }))));
            var description = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(azure.Description, "<[^>]*>", " "));
            if (description.Length > 600) description = description[..600] + "…";
            if (!string.IsNullOrWhiteSpace(description)) Label(description);
        }
        var row = new WrapPanel();
        var state = new ComboBox { ItemsSource = Enum.GetValues<PlanningState>(), SelectedItem = task.State, Margin = new Thickness(0, 4, 6, 4) };
        state.SelectionChanged += (_, _) => Run(() => planner.SetState(task.Id, (PlanningState)state.SelectedItem));
        row.Children.Add(state);
        if (backlog) row.Children.Add(Button("Plan today", () => Run(() => planner.PlanToday(task.Id))));
        else
        {
            row.Children.Add(Button("↑", () => Run(() => planner.Move(task.Id, -1))));
            row.Children.Add(Button("↓", () => Run(() => planner.Move(task.Id, 1))));
        }
        if (task.ReplyRequired && !task.ReplyHandled)
            row.Children.Add(Button("Reply handled", () => Run(() => planner.HandleReply(task.Id))));
        else row.Children.Add(Button("Require reply", () => Run(() => planner.RequireReply(task.Id))));
        body.Children.Add(row);
        if (task.ReplyRequired && !task.ReplyHandled) Label("Reply needed");
    }
}
