using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Studio.Core;

namespace Studio.Desktop;

public partial class MainWindow
{
    // Runs inside the real WPF dispatcher, including the GridSplitter's drag handlers and popup.
    void RunUiSmoke(string output)
    {
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("UI check failed: " + label); checks.Add(label); }
        Check(Background is SolidColorBrush { Color.R: < 40 }, "Workspace inherits the dark theme");
        Check(sprites.Get("Cave Pirate Brawler") is { PixelWidth: 8 }, "Static entity uses its XML index and client sheet");
        Check(sprites.Get("Ghost Ship") is { PixelWidth: 16 }, "Animated Ghost Ship uses a complete 16px frame");
        Check(sprites.Get("Wizard", 1, true) is { PixelWidth: 8 }, "Player directional animation resolves from client sheet");
        var wizard = sprites.Get("Wizard")!;
        var ground = new Point(100, 100); var bounds = EncounterViewport.CharacterBounds(ground, 50, 1, wizard);
        var hitCenter = EncounterViewport.HitPlanePoint(ground, 50);
        Check(bounds == new Rect(80, 59, 40, 40) && hitCenter == new Point(100, 75), "Client 50px tile projection aligns the 40px character with the elevated hit plane");
        Check(bounds.Top >= hitCenter.Y - 25 && bounds.Bottom <= hitCenter.Y + 25, "Player art fits its collision boundary instead of sitting above it");
        byte[] Pixels(BitmapSource image)
        {
            var source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; source.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels;
        }
        var down = sprites.Character("Wizard", SpriteAction.Walk, 0, Math.PI / 2);
        var up = sprites.Character("Wizard", SpriteAction.Walk, 0, -Math.PI / 2);
        Check(!Pixels(down.Image!).SequenceEqual(Pixels(up.Image!)), "Up and down movement use distinct client sheet rows");
        var right = sprites.Character("Wizard", SpriteAction.Walk, 0, 0);
        var left = sprites.Character("Wizard", SpriteAction.Walk, 0, Math.PI);
        Check(ReferenceEquals(right.Image, left.Image) && !right.Flip && left.Flip, "Left movement mirrors the client right-facing frame");
        Check(!Pixels(right.Image!).SequenceEqual(Pixels(sprites.Character("Wizard", SpriteAction.Walk, .75, 0).Image!)), "Walking plays both native steps");
        var attack1 = sprites.Character("Knight", SpriteAction.Attack, .25, 0);
        var attack2 = sprites.Character("Knight", SpriteAction.Attack, .75, 0);
        Check(attack1.Image is { PixelWidth: 8 } && attack2.Image is { PixelWidth: 24 }, "Native second attack frame retains its extended weapon sprite");
        var attackBounds = EncounterViewport.CharacterBounds(ground, 50, 1, attack2.Image!);
        Check(attackBounds.X + attackBounds.Width / 2 == ground.X && attackBounds.Bottom == bounds.Bottom, "Extended attack keeps the character body anchored at the same position");
        CaptureSpriteAlignment(Path.Combine(output, "sprite-alignment.png"));
        Check(session!.Projectiles.All(p => sprites.Get(p.TextureId) != null), "Live projectiles resolve their actual textures");
        UpdateLayout(); var original = CaptureLayout(); var before = ConsoleRow.ActualHeight;
        ConsoleSplitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        ConsoleSplitter.RaiseEvent(new DragDeltaEventArgs(0, -110) { RoutedEvent = Thumb.DragDeltaEvent });
        ConsoleSplitter.RaiseEvent(new DragCompletedEventArgs(0, -110, false) { RoutedEvent = Thumb.DragCompletedEvent });
        UpdateLayout(); Check(ConsoleRow.ActualHeight > before + 80, "Dragging the real splitter enlarges the console");
        CaptureView(this, Path.Combine(output, "console-resized.png"));
        ExpandConsole(this, new()); UpdateLayout(); Check(EditorGrid.Visibility == Visibility.Collapsed && ConsoleRow.ActualHeight > 800, "Console expands to the full workspace");
        CaptureView(this, Path.Combine(output, "console-expanded.png"));
        ExpandConsole(this, new()); CollapseConsole(this, new()); UpdateLayout(); Check(ConsoleBody.Visibility == Visibility.Collapsed && ConsoleRow.ActualHeight == 48, "Console collapses to its toolbar");
        CollapseConsole(this, new()); UpdateLayout(); Check(ConsoleBody.Visibility == Visibility.Visible && ConsoleRow.ActualHeight > before + 80, "Console restores its previous size");
        var layoutProject = Path.Combine(output, "layout.rbs"); BehaviorDocument.Save(layoutProject, Snapshot());
        var saved = BehaviorDocument.Load(layoutProject); RestoreLayout(original); RestoreLayout(saved.Layout); UpdateLayout();
        Check(Math.Abs(ConsoleRow.ActualHeight - saved.Layout["console"][0]) < 1 && Math.Abs(LibraryColumn.ActualWidth - saved.Layout["columns"][0]) < 1, "Panel sizes survive project save and restore");
        RestoreLayout(original); EventSearch.Text = "hit"; Check(Events.Items.Count > 0 && Events.Items.Count == session.Events.Count(e => e.Kind.Contains("hit") || e.Message.Contains("hit", StringComparison.OrdinalIgnoreCase)), "Console filters hit events");
        EventSearch.Text = ""; var retained = session.Events.Count; ClearConsole(this, new());
        Check(Events.Items.Count == 0 && session.Events.Count == retained, "Clearing the console preserves the encounter trace");
        clearedEvents = 0; lastEventCount = -1; FilterEvents(this, null!);
        Breakpoint.SelectedIndex = 3; Check(session.BreakpointKind == "hit", "Breakpoint changes apply during a session"); Breakpoint.SelectedIndex = 0;
        PlayerTab.IsSelected = true; WeaponSearch.Text = "Shifting Arms";
        Check(Weapons.Items.Count == 1 && Weapons.Items[0] is Studio.Raple.WeaponEntry { Supported: true }, "Weapon search finds the game's cycling staff");
        Weapons.SelectedIndex = 0; PlayerAttack.Text = "60"; PlayerDexterity.Text = "75";
        var weaponProject = Path.Combine(output, "weapon-preset.rbs"); BehaviorDocument.Save(weaponProject, Snapshot());
        Check(BehaviorDocument.Load(weaponProject).Player is { WeaponId: "Staff of Shifting Arms", Attack: 60, Dexterity: 75 }, "Weapon and attack/dexterity persist in projects");
        WeaponSearch.Text = ""; Hitboxes.IsChecked = true; Restart(this, new());
        for (var i = 0; i < 5; i++) session!.Step(new(Attack: true, AimX: 64.5, AimY: 64.5)); UpdateSession(); UpdateLayout();
        Check(session!.PlayerShots.Count > 0 && sprites.Get(session.Weapon.Id) != null, "Weapon equipped in the live viewport has real shots and a client sprite");
        Check(session.Projectiles.All(p => sprites.Get(p.TextureId) != null), "Weapon projectiles resolve their XML textures");
        var motionView = new Studio.Raple.EntityView(1, "Wizard", 1, 2, 100, 100, "moving", new(.5), new(0, 0, 1, 2, 100, 100));
        Check(EncounterViewport.EntityPosition(motionView, 125) == new Point(.25, .5) && EncounterViewport.EntityPosition(motionView, 150) == new Point(.5, 1), "Enemy presentation follows the client's linear tick-position blend");
        Check(EncounterViewport.EntityPosition(motionView, 50) == new Point(0, 0) && EncounterViewport.EntityPosition(motionView, 250) == new Point(1, 2), "Enemy motion clamps to tick endpoints without extrapolating");
        Check(EncounterViewport.EntityPosition(motionView with { Motion = null }, 150) == new Point(1, 2), "New entities appear at their spawn position without blending from the origin");
        StartSession(); running = false; Playback.SelectedIndex = 2;
        var enemyFrames = new List<(double Ms, Point Rendered, Point Runtime)>();
        for (var i = 0; i < 12; i++)
        {
            AdvancePlayback(1000d / 60, new()); var enemy = session!.Entities.First(e => e.Name == selectedEntity!.Id);
            enemyFrames.Add((session.Ms + Viewport.FrameRemainderMs, EncounterViewport.EntityPosition(enemy, session.Ms + Viewport.FrameRemainderMs), new(enemy.X, enemy.Y)));
        }
        Check(enemyFrames.Select(f => f.Rendered).Distinct().Count() == 12 && enemyFrames.Select(f => f.Runtime).Distinct().Count() <= 3, "Enemy moves on all 12 simulated 60Hz frames while runtime movement stays on enemy ticks");
        var frameDeltas = enemyFrames.Zip(enemyFrames.Skip(1), (a, b) => (b.Rendered - a.Rendered).Length).ToArray();
        var tickDelta = enemyFrames.Zip(enemyFrames.Skip(1), (a, b) => (b.Runtime - a.Runtime).Length).Max();
        Check(frameDeltas.Max() < tickDelta * .3, "Tick boundaries preserve smooth enemy movement rather than full-tick jumps");
        File.WriteAllText(Path.Combine(output, "enemy-motion.json"), JsonSerializer.Serialize(enemyFrames.Select(f => new { ms = f.Ms, renderX = f.Rendered.X, renderY = f.Rendered.Y, runtimeX = f.Runtime.X, runtimeY = f.Runtime.Y }), BehaviorDocument.JsonOptions));
        Pause(this, new()); var pausedMs = session!.Ms; var pausedRemainder = Viewport.FrameRemainderMs;
        var pausedEntity = session.Entities.First(); var pausedPosition = EncounterViewport.EntityPosition(pausedEntity, pausedMs + pausedRemainder);
        for (var i = 0; i < 5; i++) { Advance(this, EventArgs.Empty); Viewport.InvalidateVisual(); UpdateLayout(); }
        Check(session.Ms == pausedMs && Viewport.FrameRemainderMs == pausedRemainder && EncounterViewport.EntityPosition(session.Entities.First(), session.Ms + Viewport.FrameRemainderMs) == pausedPosition, "Pause freezes the enemy's displayed pose and simulation time");
        StartSession(); Playback.SelectedIndex = 4; AdvancePlayback(50, new());
        var caughtUp = session!.Entities.First(e => e.Name == selectedEntity!.Id);
        Check(caughtUp.Motion is { TickMs: 100 } && EncounterViewport.EntityPosition(caughtUp, session.Ms + Viewport.FrameRemainderMs) == new Point(caughtUp.X, caughtUp.Y), "Catch-up frames retain the final enemy tick segment and reach its endpoint");
        StartSession(); Playback.SelectedIndex = 0;
        var fractionalFrames = new List<Point>();
        for (var i = 0; i < 12; i++) { AdvancePlayback(1000d / 60, new()); fractionalFrames.Add(EncounterViewport.EntityPosition(session!.Entities.First(), session.Ms + Viewport.FrameRemainderMs)); }
        Check(fractionalFrames.Skip(1).Distinct().Count() == 11, "Quarter-speed enemy motion uses fractional frame time between 5ms samples");
        StartSession();
        Check(Viewport.FrameRemainderMs == 0 && session!.Entities.All(e => e.Motion == null), "Restart clears enemy motion and fractional time from the previous run");
        StartSession(); running = false; Playback.SelectedIndex = 2;
        var positions = new HashSet<float>(); var startY = session!.PlayerY;
        for (var i = 0; i < 12; i++) { AdvancePlayback(1000d / 60, new(Y: 1)); positions.Add(session.PlayerY); }
        Check(positions.Count == 12 && session.Ms is >= 195 and <= 200 && session.PlayerY > startY, "60Hz playback moves the player on every display frame");
        Check(Math.Abs(session.PlayerFacing - Math.PI / 2) < .0001 && session.PlayerMoveSpeed > 0, "Live animation receives actual movement speed and direction");
        StartSession(); Playback.SelectedIndex = 0;
        for (var i = 0; i < 12; i++) AdvancePlayback(1000d / 60, new(Y: 1));
        Check(session!.Ms is >= 45 and <= 50, "Quarter-speed playback preserves the 5ms interaction clock");
        StartSession(); Playback.SelectedIndex = 4;
        for (var i = 0; i < 12; i++) AdvancePlayback(1000d / 60, new(Y: 1));
        Check(session!.Ms is >= 795 and <= 800, "Four-times playback preserves the independent enemy clock");
        Playback.SelectedIndex = 2;
        Restart(this, new()); session!.Step(new(Attack: true, AimX: 64.5, AimY: 64.5)); running = false; UpdateSession(); UpdateLayout();
        CaptureView(this, Path.Combine(output, "weapons-hitboxes.png"));
        WorkspaceTabs.SelectedIndex = 1; UpdateLayout();
        Dispatcher.BeginInvoke(new Action(() => Try(() =>
        {
            UpdateLayout(); CaptureView(this, Path.Combine(output, "behavior.png"));
            WorkspaceTabs.SelectedIndex = 0; Playback.IsDropDownOpen = true;
            Dispatcher.BeginInvoke(new Action(() => Try(() =>
            {
                var popup = (Popup)Playback.Template.FindName("PART_Popup", Playback);
                Check(popup.IsOpen && popup.Child is { RenderSize.Width: > 60 }, "Playback dropdown opens its custom popup");
                CaptureView((FrameworkElement)popup.Child, Path.Combine(output, "playback-dropdown.png")); Playback.IsDropDownOpen = false;
                Width = 1120; Height = 800; UpdateLayout();
                Check(Viewport.ActualWidth > 400 && ConsoleRow.ActualHeight >= 140, "Compact window keeps a usable viewport and console");
                CaptureView(this, Path.Combine(output, "compact.png")); Width = 1500; Height = 1000; UpdateLayout();
                RunStructureUiSmoke(output, Check);
                File.WriteAllText(Path.Combine(output, "ui-checks.json"), JsonSerializer.Serialize(new { checks, spriteSheets = sprites.SheetCount, loadedFrames = sprites.LoadedFrames, fallbackNames = sprites.Missing.Order().ToArray() }, BehaviorDocument.JsonOptions));
                Application.Current.Shutdown();
            })), DispatcherPriority.ApplicationIdle);
        })), DispatcherPriority.ApplicationIdle);
    }
    void RunStructureUiSmoke(string output, Action<bool, string> check)
    {
        WorkspaceTabs.SelectedIndex = 1;
        var root = document.Entities.Single(e => e.EntityId == selectedEntity!.Id).Root; var phase = root.Children.Single(n => n.Kind == "State");
        var baseline = document.Source; SelectNode(phase.Id);
        phaseNameInput!.Text = "opening"; renamePhaseButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(document.Find(phase.Id).Label == "opening" && ParametersTab.IsSelected && BehaviorTree.SelectedItem is System.Windows.Controls.TreeViewItem { Tag: BehaviorNode node } && node.Id == phase.Id, "Inspector rename updates phase graph and keeps the tree selection");
        document.AddChild(phase.Id, "new TimedTransition(time: 1000, targetState: \"opening\")", d => adapter.Validate(d, selectedEntity!.Id)); Changed(phase.Id);
        phaseNameInput!.Text = "approach"; renamePhaseButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(document.Find(phase.Id).Children.Any(n => n.Parameters.Any(p => p.Name == "targetState" && BehaviorDocument.LiteralString(p.Expression) == "approach")), "Inspector rename rewrites transition targets in the shared document");
        organizeExpander!.IsExpanded = true; duplicateStructureButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var cloneId = selectedNode!.Id;
        check(cloneId != phase.Id && selectedNode.Label == "approach-copy" && document.Find(cloneId).Children.Any(n => n.Parameters.Any(p => p.Name == "targetState" && BehaviorDocument.LiteralString(p.Expression) == "approach-copy")), "Duplicate button selects a new phase with remapped self transitions");
        organizeExpander!.IsExpanded = true; upStructureButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(document.Find(root.Id).Children.First().Id == cloneId && selectedNode!.Id == cloneId, "Move-up button changes phase entry order and retains selection");
        organizeExpander!.IsExpanded = true; UpdateLayout(); CaptureView(this, Path.Combine(output, "phase-authoring.png"));
        var lockBox = Inspector.Children.OfType<System.Windows.Controls.CheckBox>().Single(); lockBox.IsChecked = true;
        check(!duplicateStructureButton!.IsEnabled && !deleteStructureButton!.IsEnabled && !renamePhaseButton!.IsEnabled, "Region lock immediately disables structural controls");
        lockBox.IsChecked = false;
        check(duplicateStructureButton.IsEnabled && deleteStructureButton.IsEnabled, "Unlock restores structural controls without rebuilding the inspector");
        deleteStructureButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(document.Find(root.Id).Children.Count == 1 && selectedNode!.Id == root.Id, "Delete button removes the branch and selects its parent");
        Undo(this, new());
        check(document.Find(root.Id).Children.First().Id == cloneId, "Desktop undo restores the organized phase and its identity");
        Redo(this, new());
        check(document.Find(root.Id).Children.Count == 1 && duplicateStructureButton == null && deleteStructureButton == null, "Root inspector omits duplicate, reorder and delete operations");
        while (document.Source != baseline) if (!document.Undo()) throw new InvalidOperationException("Could not restore the UI test's original document.");
        Changed(root.Id);
    }
    void CaptureSpriteAlignment(string path)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(EncounterViewport.Brush("#101215"), null, new Rect(0, 0, 800, 180));
            var examples = new[] {
                ("Wizard", SpriteAction.Stand, 0d, 0d, "Stand"),
                ("Wizard", SpriteAction.Walk, .25, 0d, "Walk · right"),
                ("Wizard", SpriteAction.Walk, .75, Math.PI, "Walk · left"),
                ("Wizard", SpriteAction.Walk, .25, -Math.PI / 2, "Walk · up"),
                ("Wizard", SpriteAction.Walk, .75, Math.PI / 2, "Walk · down"),
                ("Knight", SpriteAction.Attack, .25, 0d, "Attack · 1"),
                ("Knight", SpriteAction.Attack, .75, 0d, "Attack · 2"),
                ("Knight", SpriteAction.Attack, .75, Math.PI, "Attack · left") };
            for (var i = 0; i < examples.Length; i++)
            {
                var (id, action, phase, facing, label) = examples[i]; var point = new Point(50 + i * 100, 110);
                var frame = sprites.Character(id, action, phase, facing);
                if (frame.Flip) dc.PushTransform(new ScaleTransform(-1, 1, point.X, point.Y));
                dc.DrawImage(frame.Image, EncounterViewport.CharacterBounds(point, 64, sprites.Size(id), frame.Image!));
                if (frame.Flip) dc.Pop();
                var hit = EncounterViewport.HitPlanePoint(point, 64);
                dc.DrawRectangle(null, new Pen(EncounterViewport.Brush("#98E8CB"), 1), new Rect(hit.X - 32, hit.Y - 32, 64, 64));
                dc.DrawEllipse(EncounterViewport.Brush("#98E8CB"), null, hit, 1.5, 1.5);
                EncounterViewport.Text(dc, label, new Point(point.X - 42, 140), 11, EncounterViewport.Brush("#A4AAB3"));
            }
        }
        var bitmap = new RenderTargetBitmap(800, 180, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    static void CaptureView(FrameworkElement view, string path)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(view.ActualWidth)), Math.Max(1, (int)Math.Ceiling(view.ActualHeight)), 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
