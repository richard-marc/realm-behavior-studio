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
    IEncounterSession? currentSession;
    CancellationTokenSource? runtimeStarting;
    bool advancingRuntime;
    IEncounterSession? session
    {
        get => currentSession;
        set { runtimeStarting?.Cancel(); if (!timelineReplacing) ClearTimeline(); if (!ReferenceEquals(currentSession, value)) currentSession?.Dispose(); currentSession = value; }
    }
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
        Graph.Selected += id => SelectAuthoringNode(id, Keyboard.Modifiers); Graph.CollapseRequested += TogglePhaseCollapse; Viewport.Selected += SelectNode;
        InitializeSelectionControls();
        InitializeCombatControls();
        InitializeTimelineControls();
        InitializeArenaControls();
        InitializeMapControls();
        Viewport.AttackEdited += ApplyAttackEdit;
        Viewport.SweepEdited += ApplySweepEdit;
        Viewport.RingEdited += ApplyRingEdit;
        Viewport.CrossCutEdited += ApplyCrossCutEdit;
        Viewport.SnareEdited += ApplySnareEdit;
        Viewport.CustomMoveEdited += ApplyCustomMoveEdit;
        Viewport.MovementEdited += ApplyMovementEdit;
        Viewport.PathEdited += ApplyPathEdit;
        autosaveTimer.Tick += (_, _) => { if (initialized && !arguments.Contains("--smoke")) {Try(() => BehaviorDocument.Save(RecoveryPath(), Snapshot()));BeginMapRecovery();} };
        PreviewKeyDown += Shortcut;
        Loaded += (_, _) => { Try(Initialize); CompositionTarget.Rendering += Advance; };
        Closing += async (_, e) => { if(e.Cancel)return; ClearCombatPreview(); ClearPathEditor(); CompositionTarget.Rendering -= Advance; session = null; SaveWorkspaceLayout(); if (initialized && !arguments.Contains("--smoke")) Try(() => BehaviorDocument.Save(RecoveryPath(), Snapshot())); aiCancellation?.Cancel(); if (codex != null) await codex.DisposeAsync(); };
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
        adapter = new RapleAdapter(ServerPath()); catalog = adapter.Catalog().Select(e=>e with{SourceFile=Path.GetFullPath(e.SourceFile)}).ToArray();
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
        RefreshFragments();
        // Prime resource and behavior code paths at initialization; each actual run still gets fresh behavior instances.
        var cold = new EncounterSession(adapter, document, selectedEntity!.Id, 7, new(1000, 50, 0));
        Status.Text = $"Ready · cold runtime initialization {cold.StartMs:0} ms";
        autosaveTimer.Start();
        Status.Text = $"{catalog.Count} source entities · {adapter.Primitives.Count} runtime constructors · all editing works offline";
        if (arguments.Contains("--smoke")) Dispatcher.BeginInvoke(new Action(()=>{if(arguments.Contains("--replacement-smoke"))ReplacementSmoke();else if(arguments.Contains("--transformation-smoke"))TransformationSmoke();else if(arguments.Contains("--reference-clone-smoke"))ReferenceCloneSmoke();else if(arguments.Contains("--definition-clone-smoke"))DefinitionCloneSmoke();else if(arguments.Contains("--custom-movement-smoke"))CustomMovementSmoke();else if(arguments.Contains("--defense-actions-smoke"))DefenseActionsSmoke();else if(arguments.Contains("--presence-smoke"))PresenceSmoke();else if(arguments.Contains("--phase-choice-smoke"))PhaseChoiceSmoke();else if(arguments.Contains("--snare-viewport-smoke"))SnareViewportSmoke();else if(arguments.Contains("--snare-smoke"))SnareSmoke();else if(arguments.Contains("--sweep-geometry-smoke"))SweepGeometrySmoke();else if(arguments.Contains("--crosscut-smoke"))CrossCutSmoke();else if(arguments.Contains("--group-smoke"))GroupSmoke();else if(arguments.Contains("--completion-smoke"))CompletionSmoke();else if(arguments.Contains("--hazard-smoke"))HazardSmoke();else if(arguments.Contains("--bounded-follow-smoke"))BoundedFollowSmoke();else if(arguments.Contains("--movement-family-smoke"))MovementFamilySmoke();else if(arguments.Contains("--definition-removal-smoke"))DefinitionRemovalSmoke();else if(arguments.Contains("--ring-pattern-smoke"))RingPatternSmoke();else if(arguments.Contains("--cycle-smoke"))CycleSmoke();else if(arguments.Contains("--viewport-editors-smoke"))ViewportEditorsSmoke();else if(arguments.Contains("--sequential-sweep-smoke"))SequentialSweepSmoke();else if(arguments.Contains("--shared-entry-smoke"))SharedEntrySmoke();else if(arguments.Contains("--collection-entry-smoke"))CollectionEntrySmoke();else if(arguments.Contains("--optional-initializer-smoke"))OptionalInitializerSmoke();else if(arguments.Contains("--keyed-initializer-smoke"))KeyedInitializerSmoke();else if(arguments.Contains("--member-initializer-smoke"))MemberInitializerSmoke();else if(arguments.Contains("--selection-extraction-smoke"))SelectionExtractionSmoke();else if(arguments.Contains("--scoped-extraction-smoke"))ScopedExtractionSmoke();else if(arguments.Contains("--collection-extraction-smoke"))CollectionExtractionSmoke();else if(arguments.Contains("--extraction-smoke"))ExtractionSmoke();else if(arguments.Contains("--workspace-copy-smoke"))WorkspaceCopySmoke();else if(arguments.Contains("--workspace-group-smoke"))WorkspaceGroupSmoke();else if(arguments.Contains("--workspace-delete-smoke"))WorkspaceDeleteSmoke();else if(arguments.Contains("--single-source-phase-smoke"))SingleSourcePhaseSmoke();else if(arguments.Contains("--workspace-phase-smoke"))WorkspacePhaseSmoke();else if(arguments.Contains("--external-definition-smoke"))ExternalDefinitionSmoke();else if(arguments.Contains("--source-set-smoke"))SourceSetSmoke();else Smoke();}), DispatcherPriority.ApplicationIdle);
    }
    void Try(Action action)
    {
        try { action(); } catch (Exception e) { running = false; UpdatePlayButton(); Status.Text = e.GetBaseException().Message; if (arguments.Contains("--smoke")) { Directory.CreateDirectory(".artifacts"); File.WriteAllText(".artifacts/desktop-smoke-error.txt", e.ToString()); Application.Current.Shutdown(1); } }
    }
    string RecoveryPath() => Path.Combine(recoveryDir, (selectedEntity?.ObjectType.ToString("X4") ?? "project") + ".rbs.recovery");
    StudioProject Snapshot() => new()
    {
        SourceFile = selectedEntity!.SourceFile, Source = PrimaryDocument.Source, Identities = PrimaryDocument.Identities,
        AdditionalSources = SourceDependencies(), SourceDrafts=SourceDraftSnapshot(), SelectedSourceFile=displayedSourceFile,
        EntityId = selectedEntity.Id, Brief = Brief.Text, Player = ReadPreset(), Seed = int.Parse(Seed.Text), ArenaSize = int.Parse(ArenaSize.Text), Arena=arenaPreset,
        Layout = CaptureLayout(), LockedNodes = PrimaryDocument.LockedNodes.ToList(), CollapsedNodes = collapsedByDocument.GetValueOrDefault(PrimaryDocument,[]).ToList()
    };
    PlayerPreset ReadPreset() => new(int.Parse(PlayerHp.Text), int.Parse(PlayerSpeed.Text), int.Parse(PlayerDefense.Text), Invulnerable.IsChecked == true, Spectator.IsChecked == true,
        selectedWeaponId, int.Parse(PlayerAttack.Text), int.Parse(PlayerDexterity.Text), double.Parse(PlayerStartX.Text, System.Globalization.CultureInfo.InvariantCulture), double.Parse(PlayerStartY.Text, System.Globalization.CultureInfo.InvariantCulture));
    void SearchChanged(object sender, TextChangedEventArgs e) { if (adapter != null) { var results = catalog.Where(e => e.Id.Contains(Search.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); Library.ItemsSource = results; LibraryCount.Text = results.Length + " entities"; } }
    void PrimitiveSearchChanged(object sender, TextChangedEventArgs e) { if (adapter != null) Primitives.ItemsSource = adapter.Primitives.Where(p => p.Name.Contains(PrimitiveSearch.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); }
    bool synchronizingLibrary;
    void SynchronizeLibrarySelection(CatalogEntry entry)
    {
        synchronizingLibrary=true;
        try
        {
            if(Library.ItemsSource is not IEnumerable<CatalogEntry> visible||!visible.Any(c=>c.Id==entry.Id))Search.Text="";
            Library.SelectedItem=entry;Library.ScrollIntoView(entry);
        }
        finally{synchronizingLibrary=false;}
    }
    void EntitySelected(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizingLibrary||Library.SelectedItem is not CatalogEntry entry) return;
        Try(() =>
        {
            if (selectedEntity != null && document != null && !arguments.Contains("--smoke")) BehaviorDocument.Save(RecoveryPath(), Snapshot());
            LeaveExternalDefinition();
            RememberSourceDraft();displayedSourceFile=null;
            if(selectedEntity!=null&&sourceDependencies.Count!=0)
                sourceDependencies[selectedEntity.SourceFile]=new(selectedEntity.SourceFile,document.Source){Identities=document.Identities,LockedNodes=document.LockedNodes.ToList(),CollapsedNodes=CollapsedNodes.ToList()};
            if(selectedEntity!=null)dependencyDocuments.Remove(selectedEntity.SourceFile);
            dependencyDocuments.Remove(entry.SourceFile);
            selectedEntity = entry; session = null; running = false; Viewport.Session = null;
            if(sourceDependencies.TryGetValue(entry.SourceFile,out var dependency))
                documents[entry.SourceFile]=document=new BehaviorDocument(dependency.Source,adapter,dependency.Identities,entry.SourceFile);
            else if (!documents.TryGetValue(entry.SourceFile, out document!))
                documents[entry.SourceFile] = document = new BehaviorDocument(File.ReadAllText(entry.SourceFile), adapter, sourceFile: entry.SourceFile);
            if(sourceDependencies.Remove(entry.SourceFile,out var activeSource))document.LockedNodes.UnionWith(activeSource.LockedNodes);
            RebindSourceWorkspace();
            projectPath = null; clearedEvents = 0; UpdatePlayButton(); Events.ItemsSource = null; EventCount.Text = "0 events"; Viewport.ResetCamera(); RefreshDocument();
        });
    }
    void RefreshDocument(Guid? selection = null)
    {
        ClearFragmentPreview();
        ClearCombatPreview();
        RefreshRuntimeActors();
        if (selectedEntity == null) return;
        EntityTitle.Text = selectedEntity.Id; RefreshSourceFiles();
        EntitySprite.Source = sprites.Get(selectedEntity.Id); Viewport.PreviewEntity = selectedEntity.Id; Viewport.InvalidateVisual();
        EntitySubtitle.Text = $"{Path.GetFileNameWithoutExtension(selectedEntity.SourceFile)}  /  revision {document.Revision}";
        var entity = PrimaryDocument.Entities.FirstOrDefault(e => e.EntityId == selectedEntity.Id);
        var definition=document.Definitions.FirstOrDefault(d=>d.EntityId==definitionViewKey);
        if(definition==null&&encounterDocument!=null){LeaveExternalDefinition();RefreshDocument(selection);return;}
        if(definition==null)definitionViewKey=null;
        DefinitionBanner.Visibility=definition==null?Visibility.Collapsed:Visibility.Visible;
        DefinitionTitle.Text=definition==null?"":$"Editing {definition.Definition!.Label} · {System.IO.Path.GetFileName(document.SourceFile)} · {definition.Definition.Encounters.Count} {(definition.Definition.Encounters.Count==1?"caller":"callers")}";
        DefinitionTitle.ToolTip=definition?.Definition?.Boundary;Graph.DefinitionLabel=definition?.Definition?.Label;
        SharedTemplateNotice.Visibility=entity?.Registration?.Shared==true?Visibility.Visible:Visibility.Collapsed;
        SharedTemplateNotice.Text=entity?.Registration?.Shared==true?$"Shared template · {document.Entities.Count(e=>e.Registration?.Shared==true&&e.Root.Start==entity.Root.Start)} variants · edits change shared source":"";
        SharedTemplateNotice.ToolTip=entity?.Registration?.Boundary;UpdatePlayButton();
        BehaviorTree.Items.Clear();
        if (entity == null) { Status.Text = "Selected entity has no source registration in this document."; return; }
        TreeViewItem Item(BehaviorNode n)
        {
            var item = new TreeViewItem { Header = n.Label, Tag = n, IsExpanded = !CollapsedNodes.Contains(n.Id) };
            if (n.Kind == "ArgumentGroup") { item.Foreground = EncounterViewport.Brush("#92969F"); item.ToolTip = n.IsMemberInitializer?"Native member initialization. Select a typed list to edit its contents.":"Native " + n.Argument!.ElementType + " argument list. Select its contents to edit, reorder or drag compatible behaviors."; }
            item.Expanded += (_, e) => { if (e.OriginalSource == item) { CollapsedNodes.Remove(n.Id); SyncCollapsedGraph(); } };
            item.Collapsed += (_, e) => { if (e.OriginalSource == item) { CollapsedNodes.Add(n.Id); SyncCollapsedGraph(); } };
            foreach (var child in n.Children) item.Items.Add(Item(child)); return item;
        }
        CollapsedNodes.IntersectWith(document.Identities.Select(i => i.Id));
        var authoringRoot=definition?.Root??entity.Root;
        BehaviorTree.Items.Add(Item(authoringRoot)); Graph.Root = authoringRoot; SyncCollapsedGraph();
        ResourceInfo.Text = $"{selectedEntity.Id}\nObject type: 0x{selectedEntity.ObjectType:X4}\n\n" + adapter.ProjectileSummary(selectedEntity.Id);
        var diagnostics = ReviewDiagnostics().ToArray();
        RefreshPhaseReview(diagnostics);
        Status.Text = $"Revision {document.Revision} · {diagnostics.Count(d => d.IsError)} errors · {diagnostics.Count(d => !d.IsError)} notices";
        SelectNode(selection is {} selected&&authoringRoot.Descendants().Any(n=>n.Id==selected)?selected:authoringRoot.Id);
    }
    void NodeSelected(object sender, RoutedPropertyChangedEventArgs<object> e) { if (e.NewValue is TreeViewItem { Tag: BehaviorNode n } && n.Id != selectedNode?.Id) SelectAuthoringNode(n.Id, Keyboard.Modifiers); }
    void SelectNode(Guid id)
    {
        if(encounterDocument!=null&&!document.AllNodes.Any(n=>n.Id==id)&&PrimaryDocument.AllNodes.Any(n=>n.Id==id))
        {LeaveExternalDefinition();RefreshDocument(id);return;}
        if (selectedNode?.Id != id) ClearFragmentPreview();
        if (selectedNode?.Id != id) ClearCombatPreview();
        Try(() =>
        {
            selectedNode = document.Find(id); Graph.Highlight = id; Inspector.Children.Clear();parameterApplyButton=null;
            RefreshFragmentDestinations();
            RefreshAttackSelection();
            RefreshSweepSelection();
            RefreshRingSelection();
            RefreshCrossCutSelection();
            RefreshSnareSelection();
            RefreshCustomMoveSelection();
            RefreshMovementSelection();
            RefreshPathSelection();
            if (!retainNodeSelection) { nodeSelection.Clear(); nodeSelection.Add(id); selectionAnchor = id; }
            RefreshSelectionVisuals();
            SelectTreeNode(id); if (WorkspaceTabs.SelectedIndex == 1) ParametersTab.IsSelected = true;
            Inspector.Children.Add(new TextBlock { Text = selectedNode.Kind switch{"InitializerEntry"=>"Collection entry","Protect"=>"Protect an entity","BackAndForth"=>"Back and forth","PatrolCircle"=>"Patrol a spawn circle","RemoveConditionalEffect"=>"Remove host condition","ScaleHP"=>"Scale maximum HP","ScaleHP2"=>"Scale HP by XML percentage","ScaleDefense"=>"Scale defense","TransferDamageOnDeath"=>"Transfer damage records on death","NoPlayerWithinTransition"=>"No visible player nearby","AnyEntityWithinTransition"=>"Any enemy or player nearby","NoEntityWithinTransition"=>"No enemy or player nearby","EntityWithinTransition"=>"Nearby XML name","EntitiesNotExistsTransition"=>"Selected entities absent","OnGroundTransition"=>"On ground","NotMovingTransition"=>"Stopped-position check","FollowDistanceCompleteTransition"=>"Bounded follow complete","LockedFollowDistanceCompleteTransition"=>"Locked follow complete",_=>selectedNode.Label}, ToolTip=selectedNode.Label, FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
            var kindLabel = selectedNode.Kind switch { "FollowDistanceCompleteTransition" or "LockedFollowDistanceCompleteTransition"=>"Completion condition", "ArgumentGroup" => "Child slot", "Factory" => "Source helper", "Reference" => "Shared reference", "Spread" => "Spread input", "Opaque" => "Source region", _ => selectedNode.Kind };
            Inspector.Children.Add(new TextBlock { Text = kindLabel + " · source line " + (document.Source[..selectedNode.Start].Count(c => c == '\n') + 1), Foreground = Brushes.LightSlateGray });
            if (selectedNode.Kind is "Factory" or "Reference" or "Spread" or "Opaque") Inspector.Children.Add(new TextBlock {
                Text = selectedNode.Kind == "Factory" ? "Edit this call's arguments here. Open its definition below to edit the shared contents. The game builds this helper when you press Play." : selectedNode.Kind == "Spread" ? "This input expands a source collection when you press Play. Move or copy it within collection lists; open its definition below when available." : selectedNode.Kind == "Opaque" ? "This source region runs when the game builds the encounter. Open Source to edit its complete expression." : document.ReferencedDefinitions(selectedNode).Count>0 ? "Open the definition below to edit this shared source value for its callers." : "This region uses a shared source value. Open Source to edit its definition.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray, Margin = new(0, 10, 0, 0) });
            if (selectedNode.Kind is "Opaque" or "Reference")
            {
                var sourceRegion = selectedNode;
                var openRegion = new Button { Content = "Open Source", Style = (Style)FindResource("QuietButton"), Margin = new(0, 8, 0, 0) };
                openRegion.Click += (_, _) => { ShowEncounterSource(); SourceEditor.Select(sourceRegion.Start, sourceRegion.Length); SourceEditor.Focus(); }; Inspector.Children.Add(openRegion);
            }
            AddDefinitionControls(selectedNode);
            var primitive = adapter.Primitives.FirstOrDefault(p => p.Name == selectedNode.Kind);
            Inspector.Children.Add(new TextBlock { Text = selectedNode.IsMemberInitializer ? "Native member initialization" : selectedNode.Kind == "ArgumentGroup" ? selectedNode.Argument!.ElementType + (selectedNode.Argument.IsCollection ? " list" : " child") : primitive?.Simulatable == true || selectedNode.Kind == "State" ? "Source runtime" : "Compiled preview", ToolTip = "Complete source is retained. Export is checked by the original compiler and loader. Simulation coverage is partial; complete encounter verification remains pending.", FontSize = 11, Foreground = EncounterViewport.Brush("#91A69D"), Margin = new(0, 12, 0, 10) });
            var locked = new CheckBox { Content = "Lock this region", IsChecked = document.LockedNodes.Contains(id) };
            locked.Checked += (_, _) => { document.LockedNodes.Add(id); UpdateOrganizeButtons(); }; locked.Unchecked += (_, _) => { document.LockedNodes.Remove(id); UpdateOrganizeButtons(); }; Inspector.Children.Add(locked);
            AddOrganizeControls(selectedNode);
            AddArgumentControls(selectedNode);
            AddAttackControls(selectedNode);
            AddMovementControls(selectedNode);
            AddCustomMoveControls(selectedNode);
            AddPathControls(selectedNode);
            AddProjectileControls(selectedNode);
            AddSequentialSweepControls(selectedNode);
            AddRingPatternControls(selectedNode);
            AddCrossCutControls(selectedNode);
            AddSweepGeometryControls(selectedNode);
            AddSnareControls(selectedNode);
            AddHazardControls(selectedNode);
            AddCompletionControls(selectedNode);
            AddEntityControls(selectedNode);
            AddGroupControls(selectedNode);
            AddOwnedEmitterControls(selectedNode);
            AddPhaseChoiceControls(selectedNode);
            AddPresenceControls(selectedNode);
            AddTransformationControls(selectedNode);
            AddDefenseControls(selectedNode);
            AddTransitionSummary(id);
            AddCycleControls(selectedNode);
            AddDefinitionRemovalControls(selectedNode);
            AddLifecycleControls(selectedNode);
            var inputs = new List<(Parameter Parameter, TextBox Input)>();
            foreach (var parameter in selectedNode.Parameters.Where(p => selectedNode.Kind != "State" && !SnareOptionVisible(p.Name) && !HasPresenceTargetControls(p.Name) && (p.Name != "orbitClockwise" || movementDirection == null) && (p.Name!="axes"||ownedLaneAxes==null||adapter.ReadLaneAxis(document,selectedNode)==null) && (p.Name.Split('[')[0]!="states"||phaseChoiceRows==null)))
            {
                var meta = adapter.ParameterFor(document, selectedNode, parameter);
                Inspector.Children.Add(new TextBlock { Text = parameter.Name + (string.IsNullOrEmpty(meta?.Unit) ? "" : " · " + meta.Unit), Margin = new(0, 10, 0, 0) });
                var input = new TextBox { Text = parameter.Expression, ToolTip = meta == null ? "Original C# expression" : meta.Explanation + "\nDefault: " + meta.Default, Margin = new(0, 6, 0, 8) }; inputs.Add((parameter, input)); Inspector.Children.Add(input);
            }
            if (primitive != null || selectedNode.Kind == "Factory")
            {
                var optional = new StackPanel();
                foreach (var meta in adapter.ParametersFor(document, selectedNode).Where(p => p.Default != "required" && !SnareOptionVisible(p.Name) && !HasPresenceTargetControls(p.Name) && !p.Type.EndsWith("[]") && (p.Name != "orbitClockwise" || movementDirection == null) && selectedNode.Parameters.All(a => a.Name != p.Name)))
                {
                    optional.Children.Add(new TextBlock { Text = meta.Name + " · " + meta.Unit, Margin = new(0, 8, 0, 0) });
                    var input = new TextBox { ToolTip = meta.Explanation }; inputs.Add((new(meta.Name, "", 0, 0), input)); optional.Children.Add(input);
                    input.ToolTip = meta.Explanation + "\nLeave blank for runtime default: " + meta.Default;
                }
                if (optional.Children.Count > 0) Inspector.Children.Add(new Expander { Header = "More supported parameters", Content = optional, Foreground = Brushes.LightSlateGray, Margin = new(0, 12, 0, 6) });
            }
            var apply = new Button { Content = "Apply changes", Margin = new(0, 18, 0, 0),IsEnabled=RegionEditable(selectedNode) };parameterApplyButton=apply;
            apply.Click += (_, _) => Try(() =>
            {
                var operations = inputs.Where(i => i.Input.Text != i.Parameter.Expression).Select(i => new EditOperation(id, i.Parameter.Name, i.Input.Text)).ToArray();
                if (operations.Length == 0) return;
                document.Apply(new(document.Revision, "Edit " + selectedNode.Label, operations), ValidateAuthoring); Changed(id);
            });
            if (selectedNode.Kind is not ("State" or "ArgumentGroup") && inputs.Count > 0) Inspector.Children.Add(apply);
            if (selectedNode.Kind == "State")
            {
                var add = new Button { Content = "+ Add child phase" };
                add.Click += (_, _) => Try(() => { document.AddChild(id, "new State(" + PhaseLiteral(NextPhaseName()) + ")", ValidateAuthoring); Changed(id); }); Inspector.Children.Add(add);
            }
            foreach (var diagnostic in ReviewDiagnostics().Where(d => d.NodeId == id)) Inspector.Children.Add(new TextBlock { Text = diagnostic.Message, TextWrapping = TextWrapping.Wrap, Foreground = EncounterViewport.Brush(diagnostic.IsError ? "#E5A195" : "#DAAE84"), Margin = new(0, 12, 0, 0) });
            if (selectedNode.Parameters.Any(p => p.Name == "count")) Proposal.Text = JsonSerializer.Serialize(new DocumentPatch(document.Revision, "Increase shot count", [new(id, "count", "3")]), BehaviorDocument.JsonOptions);
        });
    }
    void Changed(Guid? id = null) { CaptureVisualDependencyEdit();InvalidateWorkspacePhaseRedo();running = false; session = null; Viewport.Session = null; RebindSourceWorkspace(); UpdatePlayButton(); RefreshDocument(id); Status.Text += " · changes take effect on next Play"; }
    void AddPrimitive(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (selectedNode == null || Primitives.SelectedItem is not Primitive p) return;
        BeginNativeCreation(p);
    });
    async void Play(object sender, RoutedEventArgs e) => await TryRuntime(async () => { ClearViewportEditors(); if (session is CompiledRuntime { IsStopped: true }) session = null; if (session == null && !await StartSessionAsync()) return; if(replayCursor!=null)recordedPlayback=true; running = true; clock.Restart(); Viewport.Focus(); UpdatePlayButton(); RefreshTimelineControls(); });
    void StartSession(string? phase = null)
    {
        ClearViewportEditors();
        session = new EncounterSession(adapter, PrimaryDocument, selectedEntity!.Id, int.Parse(Seed.Text), ReadPreset(), int.Parse(ArenaSize.Text), phase, arena:arenaPreset);
        BindSession(phase);
    }
    void BindSession(string? phase)
    {
        BeginTimelineRecording(phase);
        session!.BreakpointKind = Breakpoint.SelectedIndex > 0 ? ((ComboBoxItem)Breakpoint.SelectedItem).Content.ToString() : null;
        Viewport.Session = session; Viewport.ShowHitboxes = Hitboxes.IsChecked == true; Viewport.ShowGrid = ArenaGrid.IsChecked == true; accumulator = 0; clearedEvents = 0;
        WorkspaceTabs.SelectedIndex = 0;
        Coverage.Text = "Local simulation · seed " + session.Seed;
        Coverage.ToolTip = session.Coverage;
        Status.Text = phase == null ? "Playing edited document revision " + document.Revision : "Forced-phase test: earlier encounter setup bypassed";
        UpdateSession();
    }
    void Pause(object sender, RoutedEventArgs e) { running = false; Status.Text = "Paused"; UpdatePlayButton(); RefreshTimeline(); }
    async void Restart(object sender, RoutedEventArgs e) => await TryRuntime(async () => { if (!await StartSessionAsync()) return; running = true; clock.Restart(); Viewport.Focus(); UpdatePlayButton(); });
    void Stop(object sender, RoutedEventArgs e) { ClearCombatPreview(); running = false; session = null; Viewport.Session = null; Viewport.InvalidateVisual(); Events.ItemsSource = null; EventCount.Text = "0 events"; Status.Text = "Stopped"; UpdatePlayButton(); RefreshRuntimeActors(); }
    async void Step(object sender, RoutedEventArgs e) => await TryRuntime(async () => { running = false; if(replayCursor!=null && timelineReplay!=null) { await replayCursor.AdvanceToAsync(Math.Min(timelineReplay.DurationMs,session!.Ms+100)); UpdateSession(); return; } if (session == null && !await StartSessionAsync()) return; var active = session!; await active.AdvanceAsync(Viewport.Input(), 100); if (ReferenceEquals(active, session)) UpdateSession(); });
    async void TestPhase(object sender, RoutedEventArgs e) => await TryRuntime(async () => { if (selectedNode?.Kind != "State" || selectedNode.Label == "Root") throw new ArgumentException("Select a named phase first."); if (!await StartSessionAsync(selectedNode.Label)) return; running = true; clock.Restart(); Viewport.Focus(); });
    async void Advance(object? sender, EventArgs e)
    {
        if (!running || session == null || advancingRuntime) return;
        var elapsed = clock.Elapsed.TotalMilliseconds; clock.Restart();
        if(recordedPlayback) { var activeReplay=session; advancingRuntime=true; try { await AdvanceRecordedTimeline(elapsed); } catch(Exception error) { if(ReferenceEquals(activeReplay,session)) { running=false; recordedPlayback=false; replayCursor=null; timelineReplacing=true; try { session=null; Viewport.Session=null; } finally { timelineReplacing=false; } Try(()=>throw error); } } finally { advancingRuntime=false; } return; }
        if (session is not CompiledRuntime) { Try(() => AdvancePlayback(elapsed, Viewport.Input())); return; }
        var active = session; var input = Viewport.Input(); advancingRuntime = true;
        try
        {
            await AdvanceCompiledPlayback(elapsed, input);
        }
        catch (Exception error) { if (ReferenceEquals(active, session)) Try(() => throw error); }
        finally { advancingRuntime = false; }
    }
    async Task AdvanceCompiledPlayback(double elapsedMs, EncounterInput input)
    {
        var active = session!; var duration = PlaybackDuration(elapsedMs);
        if (duration > 0) await active.AdvanceAsync(input, duration);
        if (ReferenceEquals(active, session)) { CheckRuntimePause(); UpdateSession(); }
    }
    int PlaybackDuration(double elapsedMs)
    {
        var speed = Playback.SelectedIndex switch { 0 => .25, 1 => .5, 3 => 2, 4 => 4, _ => 1d };
        accumulator += Math.Min(elapsedMs, 250) * speed;
        var duration = (int)(accumulator / EncounterSession.SampleMs) * EncounterSession.SampleMs;
        accumulator -= duration; return duration;
    }
    void CheckRuntimePause() { if (session!.PauseRequested || session.Dead) { running = false; Status.Text = session.Dead ? "Test player died. Restart to respawn." : "Breakpoint reached"; } }
    void AdvancePlayback(double elapsedMs, EncounterInput input)
    {
        var duration = PlaybackDuration(elapsedMs);
        if (duration > 0) { session!.Advance(input, duration); CheckRuntimePause(); }
        UpdateSession();
    }
    void UpdateSession()
    {
        Viewport.FrameRemainderMs = accumulator;
        Viewport.InvalidateVisual(); Graph.ActivePhase = session?.Entities.FirstOrDefault(e => e.Name == selectedEntity?.Id)?.Phase; UpdatePlayButton();
        if (session != null) { Coverage.ToolTip = session.Coverage; Coverage.Text = session.Coverage.Contains("Unsupported") ? "Unsupported motion · see console" : session is CompiledRuntime ? "Compiled encounter · 5 ms samples" : "Client hit geometry · 5 ms samples"; }
        TimerInfo.Text = session?.InspectTimers(); RefreshRuntimeActors(); FilterEvents(this, null!);
        RefreshTimeline();
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
    void EventSelected(object sender, SelectionChangedEventArgs e) { if (Events.SelectedItem is EventRow row) NavigateRuntimeSource(row.Event); }
    void BossHpChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (session != null) Try(() => { BranchTimeline(); session.SetBossHpFraction(e.NewValue / 100); UpdateSession(); }); }
    void ApplySource(object sender, RoutedEventArgs e) => Try(ApplySourceBuffer);
    void Undo(object sender, RoutedEventArgs e) => Try(() => { if(MapWorkspace.IsSelected)UndoMap();else if(Viewport.ArenaEditing) UndoArena(); else if(RestoreWorkspacePhase(false)) {} else if ((SourceWorkspace.IsSelected||encounterDocument!=null) && UndoSourceBuffer(false)) {} else if (document.Undo()) {ClearViewportEditors();Changed(selectedNode?.Id);} });
    void Redo(object sender, RoutedEventArgs e) => Try(() => { if(MapWorkspace.IsSelected)RedoMap();else if(Viewport.ArenaEditing) RedoArena(); else if(RestoreWorkspacePhase(true)) {} else if ((SourceWorkspace.IsSelected||encounterDocument!=null) && UndoSourceBuffer(true)) {} else if (document.Redo()) {ClearViewportEditors();Changed(selectedNode?.Id);} });
    void ImportSource(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new OpenFileDialog { Filter = "C# behavior files|*.cs", InitialDirectory = Path.Combine(adapter.ServerRoot, "TKR.WorldServer/logic/db") };
        if (dialog.ShowDialog() != true) return;
        var d = new BehaviorDocument(File.ReadAllText(dialog.FileName), adapter, sourceFile: dialog.FileName); var id = d.Entities.FirstOrDefault()?.EntityId ?? throw new ArgumentException("No .Init entity definition found.");
        var known = catalog.FirstOrDefault(c => c.Id == id) ?? throw new ArgumentException("This entity has no resource definition in the supplied server.");
        encounterDocument=null;selectedEntity = known with { SourceFile = dialog.FileName }; document = d; documents[dialog.FileName] = d; projectPath = null;SynchronizeLibrarySelection(known); Changed();
    });
    void SaveProject(object sender, RoutedEventArgs e) => Try(() =>
    {
        if(MapWorkspace.IsSelected){SaveMapFile(sender,e);return;}
        if (projectPath == null) { var dialog = new SaveFileDialog { Filter = "Realm Studio project|*.rbs", FileName = selectedEntity!.Id + ".rbs" }; if (dialog.ShowDialog() != true) return; projectPath = dialog.FileName; }
        BehaviorDocument.Save(projectPath, Snapshot()); Status.Text = "Saved " + Path.GetFileName(projectPath);
    });
    void OpenProject(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new OpenFileDialog { Filter = "Realm Studio project or recovery|*.rbs;*.rbs.recovery", InitialDirectory = recoveryDir };
        if (dialog.ShowDialog() != true) return;
        LoadProject(dialog.FileName);
    });
    void LoadProject(string path)
    {
        var p = BehaviorDocument.Load(path);
        ApplyProject(p,path);
    }
    void ApplyProject(StudioProject p, string? path)
    {
        p.ValidateSourceSet();
        p=p with{SourceFile=Path.GetFullPath(p.SourceFile),AdditionalSources=p.AdditionalSources.Select(s=>s with{SourceFile=Path.GetFullPath(s.SourceFile)}).ToList(),SourceDrafts=p.SourceDrafts.Select(s=>s with{SourceFile=Path.GetFullPath(s.SourceFile)}).ToList(),SelectedSourceFile=p.SelectedSourceFile==null?null:Path.GetFullPath(p.SelectedSourceFile)};
        var scoped=adapter.ForProject(p);
        var d = new BehaviorDocument(p.Source, scoped, p.Identities, p.SourceFile);
        var known = catalog.FirstOrDefault(c => c.Id == p.EntityId) ?? throw new ArgumentException("Project entity is missing from this server's resources.");
        NativeArena.Validate(p.Arena); arenaPreset=p.Arena; arenaUndo.Clear();arenaRedo.Clear();scenarioRevision++;ClearArenaEditor();
        selectedEntity = known with { SourceFile = p.SourceFile }; document = d; documents[p.SourceFile] = d; projectPath = path;
        LoadSourceDependencies(p,scoped);
        foreach (var id in p.LockedNodes) d.LockedNodes.Add(id);
        Brief.Text = p.Brief; Seed.Text = p.Seed.ToString(); ArenaSize.Text = p.ArenaSize.ToString(); PlayerHp.Text = p.Player.Hp.ToString(); PlayerSpeed.Text = p.Player.Speed.ToString(); PlayerDefense.Text = p.Player.Defense.ToString(); Invulnerable.IsChecked = p.Player.Invulnerable; Spectator.IsChecked = p.Player.Spectator;
        PlayerAttack.Text = p.Player.Attack.ToString(); PlayerDexterity.Text = p.Player.Dexterity.ToString();
        PlayerStartX.Text = p.Player.StartX.ToString("R", System.Globalization.CultureInfo.InvariantCulture); PlayerStartY.Text = p.Player.StartY.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        selectedWeaponId = p.Player.WeaponId; WeaponSearch.Text = ""; Weapons.ItemsSource = weaponCatalog; Weapons.SelectedItem = weaponCatalog.FirstOrDefault(w => w.Id == selectedWeaponId);
        if (Weapons.SelectedItem == null) WeaponInfo.Text = "Missing weapon XML: " + selectedWeaponId;
        CollapsedNodes.UnionWith(p.CollapsedNodes.Where(id => d.Identities.Any(n => n.Id == id)));
        SynchronizeLibrarySelection(known);Changed(); RestoreLayout(p.Layout);
        if(p.SelectedSourceFile!=null)SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path.Equals(p.SelectedSourceFile,StringComparison.OrdinalIgnoreCase));
    }
    async void Export(object sender, RoutedEventArgs e) => await TryRuntime(async () =>
    {
        var project = Snapshot(); var expected = document; var revision = document.Revision;var expectedScenario=scenarioRevision;
        Status.Text = "Checking export…";
        await using var native = await CompiledRuntime.StartAsync(adapter.ServerRoot, project,captureStateOrigins:false);
        if (document != expected || document.Revision != revision || scenarioRevision!=expectedScenario || selectedEntity?.Id != project.EntityId) throw new InvalidOperationException("The document or arena changed during export checking. Export the current revision again.");
        var dialog = new SaveFileDialog { Filter = project.AdditionalSources.Count==0 ? "C# behavior source|*.cs" : "Verified source bundle|*.zip", FileName = project.AdditionalSources.Count==0 ? Path.GetFileName(selectedEntity.SourceFile) : selectedEntity.Id+".zip" };
        if (dialog.ShowDialog() != true) return;
        if (Path.GetFullPath(dialog.FileName).StartsWith(adapter.ServerRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Export to a separate folder, then review before installing in the private server.");
        ExportSourceWorkspace(dialog.FileName,project,native.Stamp);
        Status.Text = $"Exported C# · original compiler and native loader accepted {native.Stamp.Source.SourceFiles} source files";
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
        var candidate = new BehaviorDocument(document.Source, adapter, document.Identities, document.SourceFile);
        foreach (var id in document.LockedNodes) candidate.LockedNodes.Add(id);
        candidate.Apply(proposal with { ExpectedRevision = 0 }, ValidateAuthoring);
        pendingProposal = proposal;
        ProposalPreview.Text = proposal.Description + "\n" + string.Join("\n", proposal.Operations.Select(o => document.Find(o.NodeId).Label + "." + o.Parameter + " → " + o.Expression));
    }
    void ApplyProposal(object sender, RoutedEventArgs e) => Try(() => { if (pendingProposal == null) throw new ArgumentException("Preview a proposal first."); document.Apply(pendingProposal, ValidateAuthoring); pendingProposal = null; Changed(); });
    void Shortcut(object sender, KeyEventArgs e)
    {
        if (HandleAuthoringShortcut(e)) return;
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
        document.Apply(new(document.Revision, "Desktop smoke fan", [new(shoot.Id, "count", "7")]), ValidateAuthoring);
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
