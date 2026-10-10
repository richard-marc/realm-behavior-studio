using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Studio.AI;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    ChatGptAccounts accounts = null!;
    AccountProfile? activeAccount;
    CodexConversation? codex;
    CancellationTokenSource? aiCancellation;
    string? codexToken, resumeThread, conversationRegistration;
    void InitializeAI() { accounts = new(); Accounts.ItemsSource = accounts.Profiles(); }
    async Task TryAI(Func<Task> action)
    {
        try { await action(); } catch (OperationCanceledException) { AccountStatus.Text = "Canceled. Your local document is unchanged."; }
        catch (Exception e) { AccountStatus.Text = e.GetBaseException().Message; }
    }
    async void AccountSelected(object sender, SelectionChangedEventArgs e)
    {
        if (codex != null) { await codex.DisposeAsync(); codex = null; }
        activeAccount = Accounts.SelectedItem as AccountProfile; resumeThread = null; conversationRegistration = null; codexToken = null;
        AccountStatus.Text = activeAccount == null ? "Choose an account or add a new registration." : activeAccount.PlanEnabled ? activeAccount + " · plan permission granted" : activeAccount + " · sign in to grant plan usage";
    }
    async void SignIn(object sender, RoutedEventArgs e) => await ConnectAccount(activeAccount);
    async void AddAccount(object sender, RoutedEventArgs e) => await ConnectAccount(null);
    async Task ConnectAccount(AccountProfile? existing) => await TryAI(async () =>
    {
        aiCancellation?.Cancel(); aiCancellation = new(); AccountStatus.Text = "Complete sign-in in your browser. Cancel is available here.";
        var profile = await accounts.SignInAsync(existing, aiCancellation.Token);
        Accounts.ItemsSource = accounts.Profiles(); Accounts.SelectedItem = ((IReadOnlyList<AccountProfile>)Accounts.ItemsSource).First(p => p.RegistrationId == profile.RegistrationId);
    });
    async void SignOut(object sender, RoutedEventArgs e) => await TryAI(async () =>
    {
        aiCancellation?.Cancel(); if (codex != null) { await codex.DisposeAsync(); codex = null; }
        if (activeAccount == null) return;
        var confirmed = await accounts.SignOutAsync(activeAccount, CancellationToken.None); Accounts.ItemsSource = accounts.Profiles(); activeAccount = null;
        AccountStatus.Text = confirmed ? "Signed out; renewable session revoked." : "Signed out locally. Remote revocation was not confirmed; disconnect the app in ChatGPT Settings.";
    });
    async void CancelAI(object sender, RoutedEventArgs e) => await TryAI(async () =>
    {
        aiCancellation?.Cancel();
        if (codex != null) await codex.CancelAsync(CancellationToken.None);
    });
    void ManageUsage(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://chatgpt.com/settings/usage") { UseShellExecute = true });
    async void SendChat(object sender, RoutedEventArgs e) => await TryAI(async () =>
    {
        if (activeAccount == null) throw new InvalidOperationException("Continue with ChatGPT before sending a message.");
        if (ShareContext.IsChecked != true) throw new InvalidOperationException("Choose whether to share selected project context before starting a project-aware conversation.");
        aiCancellation = new(); var token = aiCancellation.Token;
        activeAccount = await accounts.RenewAsync(activeAccount, token);
        if (codex == null || codexToken != activeAccount.AccessToken)
        {
            if (codex != null) { resumeThread = codex.ThreadId; await codex.DisposeAsync(); }
            if (conversationRegistration != activeAccount.RegistrationId) resumeThread = null;
            codex = new();
            codex.Delta += delta => Dispatcher.BeginInvoke(new Action(() => { Conversation.AppendText(delta); Conversation.ScrollToEnd(); }));
            codex.Status += status => Dispatcher.BeginInvoke(new Action(() => AccountStatus.Text = status));
            codex.ToolHandler = (tool, args) => Dispatcher.InvokeAsync(() => NativeTool(tool, args)).Task.Unwrap();
            await codex.StartAsync(CodexPath.Text, activeAccount, Model.Text, ToolCatalog(), token, resumeThread);
            codexToken = activeAccount.AccessToken; conversationRegistration = activeAccount.RegistrationId;
        }
        var prompt = ChatPrompt.Text; ChatPrompt.Clear(); Conversation.AppendText("\nYou: " + prompt + "\nCodex: ");
        await codex.SendAsync(prompt, token);
    });
    static JsonElement Schema(string text) => JsonDocument.Parse(text).RootElement.Clone();
    object[] ToolCatalog()
    {
        object Tool(string name, string description, string schema) => new { type = "function", name, description, inputSchema = Schema(schema), deferLoading = false };
        return [new { type = "namespace", name = "realm_studio", description = "Application-native encounter tools. All project data is limited to user-selected context.", tools = new[]
        {
            Tool("inspect_selection", "Inspect selected entity, state, parameters, locks, document revision, and encounter brief. No unrelated files.", "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"),
            Tool("search_primitives", "Search actual source constructors; distinguish simulation capability from import/export.", "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"],\"additionalProperties\":false}"),
            Tool("propose_patch", "Validate a revision-checked parameter patch and display a user proposal. Does not apply. Node IDs must come from inspection. Expressions are literal C# values.", "{\"type\":\"object\",\"properties\":{\"ExpectedRevision\":{\"type\":\"integer\"},\"Description\":{\"type\":\"string\"},\"Operations\":{\"type\":\"array\",\"maxItems\":64,\"items\":{\"type\":\"object\",\"properties\":{\"NodeId\":{\"type\":\"string\"},\"Parameter\":{\"type\":\"string\"},\"Expression\":{\"type\":\"string\"}},\"required\":[\"NodeId\",\"Parameter\",\"Expression\"],\"additionalProperties\":false}}},\"required\":[\"ExpectedRevision\",\"Description\",\"Operations\"],\"additionalProperties\":false}"),
            Tool("run_simulation", "Run a bounded stationary scenario (at most 20 seconds) on current document or pending proposal. Existing run is stopped. Reports seed, inputs, fidelity limitations and events. Does not prove full accuracy.", "{\"type\":\"object\",\"properties\":{\"durationMs\":{\"type\":\"integer\",\"minimum\":100,\"maximum\":20000},\"previewProposal\":{\"type\":\"boolean\"}},\"required\":[\"durationMs\",\"previewProposal\"],\"additionalProperties\":false}"),
            Tool("inspect_trace", "Inspect recent events from the user run; no inference of unobserved causes.", "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}")
        }}];
    }
    async Task<object> NativeTool(string tool, JsonElement args)
    {
        if (ShareContext.IsChecked != true) throw new InvalidOperationException("Project sharing is disabled.");
        tool = tool.Split('.').Last();
        switch (tool)
        {
            case "inspect_selection": return new { revision = document.Revision, entity = selectedEntity!.Id, selectedNode = selectedNode?.Id,
                brief = Brief.Text, nodes = document.Entities.First(e => e.EntityId == selectedEntity.Id).Root.Descendants().Select(n => new { n.Id, n.Kind, n.Label, n.Parameters, locked = document.LockedNodes.Contains(n.Id) }), coverage = "Partial runtime host; no full fidelity certification" };
            case "search_primitives": return adapter.Primitives.Where(p => p.Name.Contains(args.GetProperty("query").GetString()!, StringComparison.OrdinalIgnoreCase)).Take(30).ToArray();
            case "propose_patch":
                var proposed = JsonSerializer.Deserialize<DocumentPatch>(args.GetRawText()) ?? throw new ArgumentException("Invalid document patch.");
                if (proposed.Operations.Count > 64) throw new ArgumentException("Too many operations.");
                if (proposed.ExpectedRevision != document.Revision) throw new InvalidOperationException("The proposal is stale; inspect the document again.");
                var selectedIds = document.Entities.First(e => e.EntityId == selectedEntity!.Id).Root.Descendants().Select(n => n.Id).ToHashSet();
                if (proposed.Operations.Any(o => !selectedIds.Contains(o.NodeId))) throw new InvalidOperationException("Proposal refers to a node outside the shared entity.");
                ValidateProposal(proposed); Proposal.Text = JsonSerializer.Serialize(proposed, BehaviorDocument.JsonOptions);
                return new { status = "previewed", changes = proposed.Operations.Count, applied = false };
            case "run_simulation":
            {
                var duration = args.GetProperty("durationMs").GetInt32(); if (duration < 100 || duration > 20000 || duration % 100 != 0) throw new ArgumentException("Duration must be 100–20000 ms in 100 ms steps.");
                var candidate = new BehaviorDocument(document.Source, adapter, document.Identities, document.SourceFile);
                if (args.GetProperty("previewProposal").GetBoolean())
                {
                    if (pendingProposal == null || pendingProposal.ExpectedRevision != document.Revision) throw new InvalidOperationException("No current proposal to preview.");
                    candidate.Apply(pendingProposal with { ExpectedRevision = 0 }, d => adapter.Validate(d, selectedEntity!.Id));
                }
                running = false; session = null; Viewport.Session = null; UpdatePlayButton(); Viewport.InvalidateVisual();
                var preset = ReadPreset();
                using IEncounterSession sim = adapter.NeedsCompiledRuntime(candidate, selectedEntity!.Id)
                    ? await CompiledRuntime.StartAsync(adapter.ServerRoot, Snapshot() with { Source = candidate.Source, Identities = candidate.Identities }, cancellation: aiCancellation?.Token ?? default)
                    : new EncounterSession(adapter, candidate, selectedEntity.Id, int.Parse(Seed.Text), preset, int.Parse(ArenaSize.Text));
                for (var elapsed = 0; elapsed < duration; elapsed += 100) await sim.AdvanceAsync(new(), 100, aiCancellation?.Token ?? default);
                return new { durationMs = duration, sim.Seed, inputs = "stationary; no attacks", player = preset, sim.PlayerHp, sim.Coverage, entityLimit = 128, projectileLimit = 20000, events = sim.Events.TakeLast(120) };
            }
            case "inspect_trace": return new { seed = session?.Seed, coverage = session?.Coverage, events = session?.Events.TakeLast(120).ToArray() ?? [] };
            default: throw new NotSupportedException("Unknown application tool.");
        }
    }
}
