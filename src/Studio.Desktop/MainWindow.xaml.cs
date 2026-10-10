using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow : Window
{
    RapleAdapter adapter = null!;
    BehaviorDocument document = null!;
    IReadOnlyList<CatalogEntry> catalog = [];
    readonly Dictionary<string, BehaviorDocument> documents = new(StringComparer.OrdinalIgnoreCase);
    CatalogEntry? selectedEntity;
    BehaviorNode? selectedNode;
    EncounterSession? session;
    DocumentPatch? pendingProposal;
    readonly DispatcherTimer autosaveTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly Stopwatch clock = new();
    double accumulator;
    bool running, initialized;
    string? projectPath;
    readonly string[] arguments;
    readonly string recoveryDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RealmBehaviorStudio", "Recovery");
    public MainWindow(string[] args)
    {
        arguments = args; InitializeComponent();
        Graph.Selected += SelectNode; Viewport.Selected += SelectNode;
        autosaveTimer.Tick += (_, _) => { if (initialized && !arguments.Contains("--smoke")) Try(() => BehaviorDocument.Save(RecoveryPath(), Snapshot())); };
        PreviewKeyDown += Shortcut;
        Loaded += (_, _) => { Try(Initialize); CompositionTarget.Rendering += Advance; };
        Closing += async (_, _) => { CompositionTarget.Rendering -= Advance; SaveWorkspaceLayout(); if (initialized && !arguments.Contains("--smoke")) Try(() => BehaviorDocument.Save(RecoveryPath(), Snapshot())); aiCancellation?.Cancel(); if (codex != null) await codex.DisposeAsync(); };
    }
    string ServerPath()
    {
        var option = Array.IndexOf(arguments, "--server");
        if (option >= 0 && option + 1 < arguments.Length) return arguments[option + 1];
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                var path = Path.Combine(d.FullName, "raple-realms-7-server");
                if (Directory.Exists(Path.Combine(path, "TKR.WorldServer"))) return path;
            }
        throw new DirectoryNotFoundException("Server source was not found. Launch with --server <server source folder>.");
    }
    void Initialize()
    {
        adapter = new RapleAdapter(ServerPath()); catalog = adapter.Catalog();
        var clientOption = Array.IndexOf(arguments, "--client");
        sprites = new ClientSprites(adapter.ServerRoot, clientOption >= 0 && clientOption + 1 < arguments.Length ? arguments[clientOption + 1] : null);
        Viewport.Sprites = sprites; LibraryCount.Text = catalog.Count + " entities"; LoadWorkspaceLayout();
        weaponCatalog = GameWeapon.Catalog(); Weapons.ItemsSource = weaponCatalog;
        Weapons.SelectedItem = weaponCatalog.First(w => w.Id == selectedWeaponId);
        Primitives.ItemsSource = adapter.Primitives; Library.ItemsSource = catalog;
        initialized = true;
        try { InitializeAI(); } catch (Exception) { AccountStatus.Text = "Account storage is unavailable. Local editing, export and simulation remain available."; }
        Library.SelectedItem = catalog.First(e => e.Id == "Cave Pirate Brawler");
        Library.ScrollIntoView(Library.SelectedItem);
        // Prime resource and behavior code paths at initialization; each actual run still gets fresh behavior instances.
        var cold = new EncounterSession(adapter, document, selectedEntity!.Id, 7, new(1000, 50, 0));
        Status.Text = $"Ready · cold runtime initialization {cold.StartMs:0} ms";
        autosaveTimer.Start();
        Status.Text = $"{catalog.Count} source entities · {adapter.Primitives.Count} runtime constructors · all editing works offline";
        if (arguments.Contains("--smoke")) Dispatcher.BeginInvoke(new Action(Smoke), DispatcherPriority.ApplicationIdle);
    }
    void Try(Action action)
    {
        try { action(); } catch (Exception e) { running = false; UpdatePlayButton(); Status.Text = e.GetBaseException().Message; if (arguments.Contains("--smoke")) { Directory.CreateDirectory(".artifacts"); File.WriteAllText(".artifacts/desktop-smoke-error.txt", e.ToString()); Application.Current.Shutdown(1); } }
    }
    string RecoveryPath() => Path.Combine(recoveryDir, (selectedEntity?.ObjectType.ToString("X4") ?? "project") + ".rbs.recovery");
    StudioProject Snapshot() => new()
    {
        SourceFile = selectedEntity!.SourceFile, Source = document.Source, Identities = document.Identities,
        EntityId = selectedEntity.Id, Brief = Brief.Text, Player = ReadPreset(), Seed = int.Parse(Seed.Text), ArenaSize = int.Parse(ArenaSize.Text),
        Layout = CaptureLayout(), LockedNodes = document.LockedNodes.ToList()
    };
    PlayerPreset ReadPreset() => new(int.Parse(PlayerHp.Text), int.Parse(PlayerSpeed.Text), int.Parse(PlayerDefense.Text), Invulnerable.IsChecked == true, Spectator.IsChecked == true,
        selectedWeaponId, int.Parse(PlayerAttack.Text), int.Parse(PlayerDexterity.Text));
    void SearchChanged(object sender, TextChangedEventArgs e) { if (adapter != null) { var results = catalog.Where(e => e.Id.Contains(Search.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); Library.ItemsSource = results; LibraryCount.Text = results.Length + " entities"; } }
    void PrimitiveSearchChanged(object sender, TextChangedEventArgs e) { if (adapter != null) Primitives.ItemsSource = adapter.Primitives.Where(p => p.Name.Contains(PrimitiveSearch.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); }
    void EntitySelected(object sender, SelectionChangedEventArgs e)
    {
        if (Library.SelectedItem is not CatalogEntry entry) return;
        Try(() =>
        {
            if (selectedEntity != null && document != null && !arguments.Contains("--smoke")) BehaviorDocument.Save(RecoveryPath(), Snapshot());
            selectedEntity = entry; session = null; running = false; Viewport.Session = null;
            if (!documents.TryGetValue(entry.SourceFile, out document!))
                documents[entry.SourceFile] = document = new BehaviorDocument(File.ReadAllText(entry.SourceFile), adapter.ParameterNames);
            projectPath = null; clearedEvents = 0; UpdatePlayButton(); Events.ItemsSource = null; EventCount.Text = "0 events"; Viewport.ResetCamera(); RefreshDocument();
        });
    }
    void RefreshDocument(Guid? selection = null)
    {
        if (selectedEntity == null) return;
        EntityTitle.Text = selectedEntity.Id; SourceEditor.Text = document.Source;
        EntitySprite.Source = sprites.Get(selectedEntity.Id); Viewport.PreviewEntity = selectedEntity.Id; Viewport.InvalidateVisual();
        EntitySubtitle.Text = $"{Path.GetFileNameWithoutExtension(selectedEntity.SourceFile)}  /  revision {document.Revision}";
        var entity = document.Entities.FirstOrDefault(e => e.EntityId == selectedEntity.Id);
        BehaviorTree.Items.Clear();
        if (entity == null) { Status.Text = "Selected entity has no .Init definition in this document."; return; }
        TreeViewItem Item(BehaviorNode n)
        {
            var item = new TreeViewItem { Header = n.Label, Tag = n, IsExpanded = true };
            foreach (var child in n.Children) item.Items.Add(Item(child)); return item;
        }
        BehaviorTree.Items.Add(Item(entity.Root)); Graph.Root = entity.Root;
        ResourceInfo.Text = $"{selectedEntity.Id}\nObject type: 0x{selectedEntity.ObjectType:X4}\n\n" + adapter.ProjectileSummary(selectedEntity.Id);
        var diagnostics = adapter.Validate(document, selectedEntity.Id).ToArray();
        Status.Text = $"Revision {document.Revision} · {diagnostics.Count(d => d.IsError)} errors · {diagnostics.Count(d => !d.IsError)} compatibility notices";
        SelectNode(selection ?? entity.Root.Id);
    }
    void NodeSelected(object sender, RoutedPropertyChangedEventArgs<object> e) { if (e.NewValue is TreeViewItem { Tag: BehaviorNode n }) SelectNode(n.Id); }
    void SelectNode(Guid id)
    {
        Try(() =>
        {
            selectedNode = document.Find(id); Graph.Highlight = id; Inspector.Children.Clear();
            Inspector.Children.Add(new TextBlock { Text = selectedNode.Label, FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
            Inspector.Children.Add(new TextBlock { Text = selectedNode.Kind + " · source line " + (document.Source[..selectedNode.Start].Count(c => c == '\n') + 1), Foreground = Brushes.LightSlateGray });
            var primitive = adapter.Primitives.FirstOrDefault(p => p.Name == selectedNode.Kind);
            Inspector.Children.Add(new TextBlock { Text = primitive?.Simulatable == true || selectedNode.Kind == "State" ? "Source runtime" : "Preserved source · preview unavailable", ToolTip = "Complete source is retained. Export is checked by the original compiler. Simulation coverage is partial; complete encounter verification remains pending.", FontSize = 11, Foreground = EncounterViewport.Brush("#91A69D"), Margin = new(0, 12, 0, 10) });
            var locked = new CheckBox { Content = "Lock this region", IsChecked = document.LockedNodes.Contains(id) };
            locked.Checked += (_, _) => document.LockedNodes.Add(id); locked.Unchecked += (_, _) => document.LockedNodes.Remove(id); Inspector.Children.Add(locked);
            var inputs = new List<(Parameter Parameter, TextBox Input)>();
            foreach (var parameter in selectedNode.Parameters)
            {
                var meta = primitive?.Parameters.FirstOrDefault(p => p.Name == parameter.Name);
                Inspector.Children.Add(new TextBlock { Text = parameter.Name + (string.IsNullOrEmpty(meta?.Unit) ? "" : " · " + meta.Unit), Margin = new(0, 10, 0, 0) });
                var input = new TextBox { Text = parameter.Expression, ToolTip = meta == null ? "Original C# expression" : meta.Explanation + "\nDefault: " + meta.Default, Margin = new(0, 6, 0, 8) }; inputs.Add((parameter, input)); Inspector.Children.Add(input);
            }
            if (primitive != null)
            {
                var optional = new StackPanel();
                foreach (var meta in primitive.Parameters.Where(p => p.Default != "required" && !p.Type.EndsWith("[]") && selectedNode.Parameters.All(a => a.Name != p.Name)))
                {
                    optional.Children.Add(new TextBlock { Text = meta.Name + " · " + meta.Unit, Margin = new(0, 8, 0, 0) });
                    var input = new TextBox { ToolTip = meta.Explanation }; inputs.Add((new(meta.Name, "", 0, 0), input)); optional.Children.Add(input);
                    input.ToolTip = meta.Explanation + "\nLeave blank for runtime default: " + meta.Default;
                }
                if (optional.Children.Count > 0) Inspector.Children.Add(new Expander { Header = "More supported parameters", Content = optional, Foreground = Brushes.LightSlateGray, Margin = new(0, 12, 0, 6) });
            }
            var apply = new Button { Content = "Apply changes", Margin = new(0, 18, 0, 0) };
            apply.Click += (_, _) => Try(() =>
            {
                var operations = inputs.Where(i => i.Input.Text != i.Parameter.Expression).Select(i => new EditOperation(id, i.Parameter.Name, i.Input.Text)).ToArray();
                if (operations.Length == 0) return;
                document.Apply(new(document.Revision, "Edit " + selectedNode.Label, operations), d => adapter.Validate(d, selectedEntity!.Id)); Changed(id);
            });
            Inspector.Children.Add(apply);
            if (selectedNode.Kind == "State")
            {
                var add = new Button { Content = "+ Add child phase" };
                add.Click += (_, _) => Try(() => { document.AddChild(id, "new State(\"phase-" + (document.Revision + 1) + "\")"); Changed(id); }); Inspector.Children.Add(add);
            }
            foreach (var diagnostic in adapter.Validate(document, selectedEntity!.Id).Where(d => d.NodeId == id)) Inspector.Children.Add(new TextBlock { Text = diagnostic.Message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Goldenrod, Margin = new(0, 12, 0, 0) });
            if (selectedNode.Parameters.Any(p => p.Name == "count")) Proposal.Text = JsonSerializer.Serialize(new DocumentPatch(document.Revision, "Increase shot count", [new(id, "count", "3")]), BehaviorDocument.JsonOptions);
        });
    }
    void Changed(Guid? id = null) { running = false; session = null; Viewport.Session = null; UpdatePlayButton(); RefreshDocument(id); Status.Text += " · changes take effect on next Play"; }
    void AddPrimitive(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (selectedNode == null || Primitives.SelectedItem is not Primitive p) return;
        var expression = p.Name switch
        {
            "Shoot" => "new Shoot(radius: 8, count: 5, shootAngle: 15, projectileIndex: 0, coolDown: 1000)",
            "Wander" => "new Wander(speed: 0.3)", "Follow" => "new Follow(speed: 0.8, acquireRange: 10, range: 3)",
            "HpLessTransition" => "new HpLessTransition(threshold: 0.7, targetState: \"" + selectedNode.Label + "\")",
            "TimedTransition" => "new TimedTransition(time: 3000, targetState: \"" + selectedNode.Label + "\")",
            "State" => "new State(\"phase-" + (document.Revision + 1) + "\")",
            _ => throw new NotSupportedException("This constructor is browsable. Add it in source until a validated visual creation form is implemented.")
        };
        document.AddChild(selectedNode.Id, expression); Changed(selectedNode.Id);
    });
    void Play(object sender, RoutedEventArgs e) => Try(() => { if (session == null) StartSession(); running = true; clock.Restart(); Viewport.Focus(); UpdatePlayButton(); });
    void StartSession(string? phase = null)
    {
        session = new EncounterSession(adapter, document, selectedEntity!.Id, int.Parse(Seed.Text), ReadPreset(), int.Parse(ArenaSize.Text), phase);
        session.BreakpointKind = Breakpoint.SelectedIndex > 0 ? ((ComboBoxItem)Breakpoint.SelectedItem).Content.ToString() : null;
        Viewport.Session = session; Viewport.ShowHitboxes = Hitboxes.IsChecked == true; Viewport.ShowGrid = ArenaGrid.IsChecked == true; accumulator = 0; clearedEvents = 0;
        WorkspaceTabs.SelectedIndex = 0;
        Coverage.Text = "Local simulation · seed " + session.Seed;
        Coverage.ToolTip = session.Coverage;
        Status.Text = phase == null ? "Playing edited document revision " + document.Revision : "Forced-phase test: earlier encounter setup bypassed";
        UpdateSession();
    }
    void Pause(object sender, RoutedEventArgs e) { running = false; Status.Text = "Paused"; UpdatePlayButton(); }
    void Restart(object sender, RoutedEventArgs e) => Try(() => { StartSession(); running = true; clock.Restart(); Viewport.Focus(); UpdatePlayButton(); });
    void Stop(object sender, RoutedEventArgs e) { running = false; session = null; Viewport.Session = null; Viewport.InvalidateVisual(); Events.ItemsSource = null; EventCount.Text = "0 events"; Status.Text = "Stopped"; UpdatePlayButton(); }
    void Step(object sender, RoutedEventArgs e) => Try(() => { running = false; if (session == null) StartSession(); session!.Step(Viewport.Input()); UpdateSession(); });
    void TestPhase(object sender, RoutedEventArgs e) => Try(() => { if (selectedNode?.Kind != "State" || selectedNode.Label == "Root") throw new ArgumentException("Select a named phase first."); StartSession(selectedNode.Label); running = true; clock.Restart(); Viewport.Focus(); });
    void Advance(object? sender, EventArgs e)
    {
        if (!running || session == null) return;
        var elapsed = clock.Elapsed.TotalMilliseconds; clock.Restart();
        Try(() => AdvancePlayback(elapsed, Viewport.Input()));
    }
    void AdvancePlayback(double elapsedMs, EncounterInput input)
    {
        var speed = Playback.SelectedIndex switch { 0 => .25, 1 => .5, 3 => 2, 4 => 4, _ => 1d };
        accumulator += Math.Min(elapsedMs, 250) * speed;
        if (accumulator >= EncounterSession.SampleMs)
        {
            var duration = (int)(accumulator / EncounterSession.SampleMs) * EncounterSession.SampleMs;
            accumulator -= duration; session!.Advance(input, duration);
            if (session.PauseRequested || session.Dead) { running = false; Status.Text = session.Dead ? "Test player died. Restart to respawn." : "Breakpoint reached"; }
        }
        UpdateSession();
    }
    void UpdateSession()
    {
        Viewport.InvalidateVisual(); Graph.ActivePhase = session?.Entities.FirstOrDefault(e => e.Name == selectedEntity?.Id)?.Phase; UpdatePlayButton();
        if (session != null) { Coverage.ToolTip = session.Coverage; Coverage.Text = session.Coverage.Contains("Unsupported") ? "Unsupported motion · see console" : "Client hit geometry · 5 ms samples"; }
        TimerInfo.Text = session?.InspectTimers(); FilterEvents(this, null!);
    }
    record EventRow(EncounterEvent Event) { public string Time => $"{Event.Ms / 1000d:0.0}s"; public override string ToString() => $"{Time}  {Event.Kind}  {Event.Message}"; }
    void FilterEvents(object sender, TextChangedEventArgs e)
    {
        if (session == null || Events == null) return;
        if (ReferenceEquals(lastEventSession, session) && lastEventCount == session.Events.Count && lastEventQuery == EventSearch.Text) return;
        var scroll = FindScrollViewer(Events); var follow = scroll == null || scroll.ScrollableHeight - scroll.VerticalOffset < 25;
        lastEventSession = session; lastEventCount = session.Events.Count; lastEventQuery = EventSearch.Text;
        var rows = session.Events.Skip(clearedEvents).Where(e => string.IsNullOrEmpty(EventSearch.Text) || e.Message.Contains(EventSearch.Text, StringComparison.OrdinalIgnoreCase) || e.Kind.Contains(EventSearch.Text, StringComparison.OrdinalIgnoreCase)).TakeLast(3000).Select(e => new EventRow(e)).ToArray();
        Events.ItemsSource = rows; EventCount.Text = rows.Length + " events";
        if (follow && rows.Length > 0) Events.ScrollIntoView(rows[^1]);
    }
    void EventSelected(object sender, SelectionChangedEventArgs e) { if (Events.SelectedItem is EventRow { Event.NodeId: { } id }) SelectNode(id); }
    void BossHpChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (session != null) Try(() => session.SetBossHpFraction(e.NewValue / 100)); }
    void ApplySource(object sender, RoutedEventArgs e) => Try(() => { document.ReplaceSource(SourceEditor.Text, d => adapter.Validate(d, selectedEntity!.Id)); Changed(); });
    void Undo(object sender, RoutedEventArgs e) => Try(() => { if (document.Undo()) Changed(); });
    void Redo(object sender, RoutedEventArgs e) => Try(() => { if (document.Redo()) Changed(); });
    void ImportSource(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new OpenFileDialog { Filter = "C# behavior files|*.cs", InitialDirectory = Path.Combine(adapter.ServerRoot, "TKR.WorldServer/logic/db") };
        if (dialog.ShowDialog() != true) return;
        var d = new BehaviorDocument(File.ReadAllText(dialog.FileName), adapter.ParameterNames); var id = d.Entities.FirstOrDefault()?.EntityId ?? throw new ArgumentException("No .Init entity definition found.");
        var known = catalog.FirstOrDefault(c => c.Id == id) ?? throw new ArgumentException("This entity has no resource definition in the supplied server.");
        selectedEntity = known with { SourceFile = dialog.FileName }; document = d; documents[dialog.FileName] = d; projectPath = null; Changed();
    });
    void SaveProject(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (projectPath == null) { var dialog = new SaveFileDialog { Filter = "Realm Studio project|*.rbs", FileName = selectedEntity!.Id + ".rbs" }; if (dialog.ShowDialog() != true) return; projectPath = dialog.FileName; }
        BehaviorDocument.Save(projectPath, Snapshot()); Status.Text = "Saved " + Path.GetFileName(projectPath);
    });
    void OpenProject(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new OpenFileDialog { Filter = "Realm Studio project or recovery|*.rbs;*.rbs.recovery", InitialDirectory = recoveryDir };
        if (dialog.ShowDialog() != true) return;
        var p = BehaviorDocument.Load(dialog.FileName);
        var d = new BehaviorDocument(p.Source, adapter.ParameterNames, p.Identities);
        var known = catalog.FirstOrDefault(c => c.Id == p.EntityId) ?? throw new ArgumentException("Project entity is missing from this server's resources.");
        selectedEntity = known with { SourceFile = p.SourceFile }; document = d; documents[p.SourceFile] = d; projectPath = dialog.FileName;
        foreach (var id in p.LockedNodes) d.LockedNodes.Add(id);
        Brief.Text = p.Brief; Seed.Text = p.Seed.ToString(); ArenaSize.Text = p.ArenaSize.ToString(); PlayerHp.Text = p.Player.Hp.ToString(); PlayerSpeed.Text = p.Player.Speed.ToString(); PlayerDefense.Text = p.Player.Defense.ToString(); Invulnerable.IsChecked = p.Player.Invulnerable; Spectator.IsChecked = p.Player.Spectator;
        PlayerAttack.Text = p.Player.Attack.ToString(); PlayerDexterity.Text = p.Player.Dexterity.ToString();
        selectedWeaponId = p.Player.WeaponId; WeaponSearch.Text = ""; Weapons.ItemsSource = weaponCatalog; Weapons.SelectedItem = weaponCatalog.FirstOrDefault(w => w.Id == selectedWeaponId);
        if (Weapons.SelectedItem == null) WeaponInfo.Text = "Missing weapon XML: " + selectedWeaponId;
        Changed(); RestoreLayout(p.Layout);
    });
    void Export(object sender, RoutedEventArgs e) => Try(() =>
    {
        var result = SourceVerification.Check(adapter.ServerRoot, selectedEntity!.SourceFile, document.Source);
        if (!result.Accepted) throw new ArgumentException("Original compiler rejected export:\n" + string.Join("\n", result.Errors.Take(8)));
        var dialog = new SaveFileDialog { Filter = "C# behavior source|*.cs", FileName = Path.GetFileName(selectedEntity.SourceFile) };
        if (dialog.ShowDialog() != true) return;
        if (Path.GetFullPath(dialog.FileName).StartsWith(adapter.ServerRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Export to a separate folder, then review before installing in the private server.");
        File.WriteAllText(dialog.FileName, document.Source); Status.Text = $"Exported C# · original server compiler accepted {result.SourceFiles} source files";
    });
    void SaveTrace(object sender, RoutedEventArgs e) => Try(() => { if (session == null) return; var dialog = new SaveFileDialog { Filter = "Encounter trace|*.json", FileName = "encounter-trace.json" }; if (dialog.ShowDialog() == true) session.ExportTrace(dialog.FileName); });
    void PreviewProposal(object sender, RoutedEventArgs e) => Try(() =>
    {
        var proposal = JsonSerializer.Deserialize<DocumentPatch>(Proposal.Text) ?? throw new ArgumentException("Invalid proposal.");
        ValidateProposal(proposal);
    });
    void ValidateProposal(DocumentPatch proposal)
    {
        pendingProposal = null;
        if (proposal.ExpectedRevision != document.Revision) throw new InvalidOperationException("This proposal is stale.");
        var candidate = new BehaviorDocument(document.Source, adapter.ParameterNames, document.Identities);
        foreach (var id in document.LockedNodes) candidate.LockedNodes.Add(id);
        candidate.Apply(proposal with { ExpectedRevision = 0 }, d => adapter.Validate(d, selectedEntity!.Id));
        pendingProposal = proposal;
        ProposalPreview.Text = proposal.Description + "\n" + string.Join("\n", proposal.Operations.Select(o => document.Find(o.NodeId).Label + "." + o.Parameter + " → " + o.Expression));
    }
    void ApplyProposal(object sender, RoutedEventArgs e) => Try(() => { if (pendingProposal == null) throw new ArgumentException("Preview a proposal first."); document.Apply(pendingProposal, d => adapter.Validate(d, selectedEntity!.Id)); pendingProposal = null; Changed(); });
    void Shortcut(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.S) { SaveProject(sender, e); e.Handled = true; }
            if (Keyboard.FocusedElement is TextBox) return;
            if (e.Key == Key.Z) { Undo(sender, e); e.Handled = true; }
            if (e.Key == Key.Y) { Redo(sender, e); e.Handled = true; }
        }
        if (e.Key == Key.F5) { Restart(sender, e); e.Handled = true; }
        if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox and not ComboBox and not ComboBoxItem and not Button and not System.Windows.Controls.Primitives.ToggleButton) { if (running) Pause(sender, e); else Play(sender, e); e.Handled = true; }
    }
    void Smoke() => Try(() =>
    {
        var shoot = document.Entities.Single(e => e.EntityId == selectedEntity!.Id).Root.Descendants().Single(n => n.Kind == "Shoot");
        document.Apply(new(document.Revision, "Desktop smoke fan", [new(shoot.Id, "count", "7")]), d => adapter.Validate(d, selectedEntity!.Id));
        RefreshDocument(shoot.Id); StartSession();
        for (var i = 0; i < 35; i++) session!.Step(new()); UpdateSession();
        var output = Path.GetFullPath(".artifacts/desktop"); Directory.CreateDirectory(output);
        BehaviorDocument.Save(Path.Combine(output, "smoke.rbs"), Snapshot()); session!.ExportTrace(Path.Combine(output, "trace.json"));
        Dispatcher.BeginInvoke(new Action(() => Try(() =>
        {
            UpdateLayout(); var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, "workspace.png")); encoder.Save(stream);
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { imported = selectedEntity!.Id, edited = "Shoot.count = 7", playerHp = session.PlayerHp, projectiles = session.Projectiles.Count, events = session.Events.Count, warmStartMs = session.StartMs }, BehaviorDocument.JsonOptions));
            RunUiSmoke(output);
        })), DispatcherPriority.ApplicationIdle);
    });
}
