using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Studio.Desktop;

public partial class MainWindow
{
    ClientSprites sprites = null!;
    double consoleHeight = 260;
    int consoleMode, clearedEvents, lastEventCount = -1;
    string lastEventQuery = "";
    Studio.Raple.IEncounterSession? lastEventSession;
    readonly string layoutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RealmBehaviorStudio", "workspace.json");

    void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void CloseWindow(object sender, RoutedEventArgs e) => Close();
    void TogglePlay(object sender, RoutedEventArgs e) { if (running) Pause(sender, e); else Play(sender, e); UpdatePlayButton(); }
    void UpdatePlayButton() { PlayButton.Content = runtimeStarting != null ? "Preparing…" : running ? "Ⅱ  Pause" : "▶  Play";var requirement=selectedEntity==null?null:adapter.PreviewContextRequirement(document,selectedEntity.Id);PlayButton.IsEnabled=runtimeStarting==null&&requirement==null;PlayButton.ToolTip=requirement; }
    void ResetCamera(object sender, RoutedEventArgs e) => Viewport.ResetCamera();
    void BreakpointChanged(object sender, SelectionChangedEventArgs e)
    { if (session != null) session.BreakpointKind = Breakpoint.SelectedIndex > 0 ? ((ComboBoxItem)Breakpoint.SelectedItem).Content.ToString() : null; }
    void ViewportOptionsChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport == null) return;
        Viewport.ShowHitboxes = Hitboxes?.IsChecked == true; Viewport.ShowGrid = ArenaGrid?.IsChecked == true; Viewport.InvalidateVisual();
    }
    void ClearConsole(object sender, RoutedEventArgs e) { clearedEvents = session?.Events.Count ?? 0; lastEventCount = -1; FilterEvents(sender, null!); }
    void CollapseConsole(object sender, RoutedEventArgs e)
    {
        if (consoleMode == 1) { RestoreConsole(); return; }
        if (consoleMode == 2) RestoreConsole();
        consoleHeight = ConsoleRow.ActualHeight; consoleMode = 1; ConsoleRow.Height = new(48); ConsoleBody.Visibility = Visibility.Collapsed;
        CollapseConsoleButton.Content = "⌃"; ConsoleSplitter.IsEnabled = false;
    }
    void ExpandConsole(object sender, RoutedEventArgs e)
    {
        if (consoleMode == 2) { RestoreConsole(); return; }
        if (consoleMode == 0) consoleHeight = ConsoleRow.ActualHeight;
        consoleMode = 2; EditorGrid.Visibility = Visibility.Collapsed; EditorRow.MinHeight = 0; EditorRow.Height = new(0);
        ConsoleRow.Height = new(1, GridUnitType.Star); ConsoleBody.Visibility = Visibility.Visible; ConsoleSplitter.IsEnabled = false;
        ExpandConsoleButton.Content = "↙"; CollapseConsoleButton.Content = "⌄";
    }
    void RestoreConsole()
    {
        consoleMode = 0; EditorGrid.Visibility = Visibility.Visible; EditorRow.MinHeight = 280; EditorRow.Height = new(1, GridUnitType.Star);
        ConsoleRow.Height = new(Math.Clamp(consoleHeight, 140, Math.Max(140, WorkspaceGrid.ActualHeight - 290)));
        ConsoleBody.Visibility = Visibility.Visible; ConsoleSplitter.IsEnabled = true; ExpandConsoleButton.Content = "↗"; CollapseConsoleButton.Content = "⌄";
    }
    void PanelResized(object sender, DragCompletedEventArgs e) { consoleHeight = ConsoleRow.ActualHeight; if (consoleHeight < 85) CollapseConsole(sender, e); }
    Dictionary<string, double[]> CaptureLayout() => new()
    {
        ["columns"] = [LibraryColumn.ActualWidth, InspectorColumn.ActualWidth],
        ["console"] = [consoleMode == 0 ? ConsoleRow.ActualHeight : consoleHeight, consoleMode],
        ["workspace"] = [WorkspaceTabs.SelectedIndex]
    };
    void RestoreLayout(Dictionary<string, double[]> layout)
    {
        RestoreConsole();
        if (layout.TryGetValue("columns", out var columns) && columns.Length >= 2 && columns.All(double.IsFinite))
        { LibraryColumn.Width = new(Math.Clamp(columns[0], 175, 380)); InspectorColumn.Width = new(Math.Clamp(columns[1], 260, 500)); }
        if (layout.TryGetValue("console", out var console) && console.Length >= 2 && console.All(double.IsFinite))
        {
            consoleHeight = Math.Clamp(console[0], 140, Math.Max(140, WorkspaceGrid.ActualHeight - 290)); ConsoleRow.Height = new(consoleHeight);
            UpdateLayout();
            if (console[1] == 1) CollapseConsole(this, new()); else if (console[1] == 2) ExpandConsole(this, new());
        }
        if (layout.TryGetValue("workspace", out var workspace) && workspace.Length > 0 && double.IsFinite(workspace[0])) WorkspaceTabs.SelectedIndex = Math.Clamp((int)workspace[0], 0, WorkspaceTabs.Items.Count - 1);
    }
    void LoadWorkspaceLayout()
    {
        if (arguments.Contains("--smoke") || !File.Exists(layoutPath)) return;
        try { RestoreLayout(JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(layoutPath)) ?? new()); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }
    void SaveWorkspaceLayout()
    {
        if (arguments.Contains("--smoke")) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!); File.WriteAllText(layoutPath, JsonSerializer.Serialize(CaptureLayout())); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) if (FindScrollViewer(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
}

public sealed class EventToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => EncounterViewport.Brush(value?.ToString() switch
    {
        "hit" or "death" => "#E5A39D", "transition" => "#B8ACE0", "spawn" => "#98C7DF", "unsupported" => "#E1C28B", "attack" => "#A9C4AE", "player-attack" or "weapon-hit" => "#98D9BF", _ => "#8F949F"
    });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
