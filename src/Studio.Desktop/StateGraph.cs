using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Studio.Core;

namespace Studio.Desktop;
public sealed class StateGraph : FrameworkElement
{
    BehaviorNode? root; Guid? highlight; string? active;
    PhaseAnalysis analysis = new([], []);
    readonly Dictionary<Guid, Rect> locations = [], transitionRows = [];
    IReadOnlySet<Guid> selection = new HashSet<Guid>(), collapsed = new HashSet<Guid>();
    public event Action<Guid>? Selected;
    public event Action<Guid>? CollapseRequested;
    public string? DefinitionLabel {get;set;}
    public IReadOnlySet<Guid> Selection { get => selection; set { selection = value; InvalidateVisual(); } }
    public IReadOnlySet<Guid> CollapsedNodes { get => collapsed; set { collapsed = value; InvalidateVisual(); } }
    public BehaviorNode? Root { get => root; set { root = value; analysis = value == null ? new([], []) : BehaviorAnalysis.Analyze(value); locations.Clear(); transitionRows.Clear(); InvalidateVisual(); } }
    public Guid? Highlight { get => highlight; set { highlight = value; InvalidateVisual(); } }
    public string? ActivePhase { get => active; set { if (active == value) return; active = value; InvalidateVisual(); } }
    public StateGraph()
    {
        Focusable = true;
        MouseLeftButtonDown += (_, e) => { var point = e.GetPosition(this); SelectAt(point); if (e.ClickCount == 2 && !transitionRows.Values.Any(r => r.Contains(point))) foreach (var item in locations) if (item.Value.Contains(point)) { CollapseRequested?.Invoke(item.Key); break; } };
        MouseMove += (_, e) =>
        {
            var point = e.GetPosition(this); var id = transitionRows.FirstOrDefault(r => r.Value.Contains(point)).Key;
            var transition = analysis.Transitions.FirstOrDefault(t => t.Node.Id == id);
            ToolTip = transition == null ? null : $"{transition.Condition} → {transition.TargetName ?? "dynamic target"}\nPriority {transition.Priority}: first matching transition wins. Child phases are checked before parents. Timers use native 100 ms ticks.\nClick to edit; use Organize to change order.";
            Cursor = id == Guid.Empty ? Cursors.Arrow : Cursors.Hand;
        };
    }
    internal Rect? TransitionBounds(Guid id) => transitionRows.TryGetValue(id, out var rect) ? rect : null;
    internal Rect? PhaseBounds(Guid id) => locations.TryGetValue(id, out var rect) ? rect : null;
    internal void BringPhaseIntoView(Guid id) { if (locations.TryGetValue(id, out var rect)) BringIntoView(rect); }
    internal void SelectAt(Point point)
    {
        // Rows sit inside phase cards, so test the more specific targets first.
        foreach (var item in transitionRows.Concat(locations)) if (item.Value.Contains(point)) { Selected?.Invoke(item.Key); Focus(); break; }
    }
    static void Text(DrawingContext dc, string text, Point point, double size, string color, double width)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, EncounterViewport.Brush(color), 1)
        { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(formatted, point);
    }
    protected override void OnRender(DrawingContext dc)
    {
        locations.Clear(); transitionRows.Clear();
        if (root == null) return;
        var allStates = root.Descendants().Where(n => n.Kind == "State").ToArray();
        var states = allStates.Where(n => !allStates.Any(ancestor => collapsed.Contains(ancestor.Id) && n.Path.StartsWith(ancestor.Path + "/", StringComparison.Ordinal))).ToArray();
        if (states.Length == 0)
        {
            Width = 388; Height = 270;
            var rect = new Rect(20, 18, 320, 126); locations[root.Id] = rect;
            dc.DrawRoundedRectangle(EncounterViewport.Brush("#202226"), new Pen(EncounterViewport.Brush(root.Id == highlight ? "#98B4C3" : "#3D414A"), 1), rect, 9, 9);
            Text(dc, DefinitionLabel==null?"SOURCE ENCOUNTER":"SHARED SOURCE DEFINITION", new(34, 29), 10, "#858B95", 290);
            Text(dc, DefinitionLabel??root.Label, new(34, 49), 15, "#E8E9EC", 290);
            Text(dc, DefinitionLabel==null?"Phases are generated when the game loads":"Shared source · edits affect its callers", new(34, 82), 11, "#92969F", 290);
            Text(dc, DefinitionLabel==null?"Select to edit its source arguments":"Select to edit its actual source contents", new(34, 106), 11, "#91A69D", 290);
            return;
        }
        var y = 18d;
        foreach (var state in states)
        {
            var transitions = collapsed.Contains(state.Id) ? [] : analysis.Transitions.Where(t => t.OwnerId == state.Id).ToArray();
            var depth = allStates.Count(ancestor => state.Path.StartsWith(ancestor.Path + "/", StringComparison.Ordinal));
            var rect = new Rect(20 + depth * 20, y, 320, 94 + transitions.Length * 32);
            locations[state.Id] = rect;
            for (var i = 0; i < transitions.Length; i++) transitionRows[transitions[i].Node.Id] = new(rect.Left + 8, rect.Top + 86 + i * 32, rect.Width - 16, 30);
            y = rect.Bottom + 20;
        }
        Width = locations.Values.Max(r => r.Right) + 48; Height = Math.Max(270, y);
        var neutral = new Pen(EncounterViewport.Brush("#353941"), 1);
        foreach (var state in states)
        {
            var from = locations[state.Id];
            foreach (var child in state.StateChildren.Where(c => c.Kind == "State"))
            {
                if (!locations.TryGetValue(child.Id, out var to)) continue; var x = from.Left + 8;
                dc.DrawLine(neutral, new(x, from.Bottom), new(x, to.Top + 40)); dc.DrawLine(neutral, new(x, to.Top + 40), new(to.Left, to.Top + 40));
            }
        }
        // Only the selected transition is routed; all destinations remain readable in their rows.
        var selected = analysis.Transitions.FirstOrDefault(t => t.Node.Id == highlight);
        foreach(var targetId in selected?.Destinations.Where(d=>d.Id!=null).Select(d=>d.Id!.Value).Distinct()??[])
        if (locations.TryGetValue(targetId, out var targetRect) && transitionRows.TryGetValue(selected!.Node.Id, out var selectedRow))
        {
            var row = selectedRow; var from = locations[selected.OwnerId];
            var start = new Point(from.Right, row.Top + 15); var end = new Point(targetRect.Right, targetRect.Top + 40); var route = locations.Values.Max(r => r.Right) + 24;
            var pen = new Pen(EncounterViewport.Brush("#98B4C3"), 1);
            dc.DrawLine(pen, start, new(route, start.Y)); dc.DrawLine(pen, new(route, start.Y), new(route, end.Y)); dc.DrawLine(pen, new(route, end.Y), end);
            dc.DrawLine(pen, end, end + new Vector(6, -4)); dc.DrawLine(pen, end, end + new Vector(6, 4));
        }
        var initial = new HashSet<Guid>();
        if (!analysis.Diagnostics.Any(d => d.Message.StartsWith("Reachability analysis is incomplete", StringComparison.Ordinal)))
            for (BehaviorNode? entry = root; entry != null; entry = entry.StateChildren.FirstOrDefault(n => n.Kind == "State")) initial.Add(entry.Id);
        foreach (var state in states)
        {
            var rect = locations[state.Id]; var activeState = state.Label == active;
            var stroke = selection.Contains(state.Id) ? "#98E8CB" : state.Id == highlight ? "#98B4C3" : activeState ? "#A99ACA" : "#3D414A";
            dc.DrawRoundedRectangle(EncounterViewport.Brush("#202226"), new Pen(EncounterViewport.Brush(stroke), 1), rect, 9, 9);
            Text(dc, state.Id == root.Id ? "ENCOUNTER" : "PHASE", new(rect.Left + 14, rect.Top + 11), 10, "#858B95", 180);
            if (initial.Contains(state.Id)) Text(dc, "ENTRY", new(rect.Right - 52, rect.Top + 11), 10, "#91A69D", 40);
            Text(dc, state.Id == root.Id && state.Label == "Root" ? "Root behavior" : state.Label, new(rect.Left + 14, rect.Top + 30), 15, "#E8E9EC", 290);
            var actions = string.Join(" · ", state.StateChildren.Where(n => n.Kind != "State" && !n.IsTransition).Select(n => n.Kind switch { "Factory" => "Source helper", "Reference" => "Shared reference", "Spread" => "Spread input", "Opaque" => n.Label == "Source initializer" ? n.Label : "Source region", _ => n.Kind }));
            if (actions.Length == 0) actions = state.StateChildren.Any(n => n.Kind == "State") ? "Contains nested phases" : "No actions";
            if (collapsed.Contains(state.Id)) actions = $"+ {state.StateChildren.Count} items · collapsed";
            Text(dc, actions, new(rect.Left + 14, rect.Top + 61), 11, "#92969F", 290);
            foreach (var edge in analysis.Transitions.Where(t => t.OwnerId == state.Id && !collapsed.Contains(state.Id)))
            {
                var row = transitionRows[edge.Node.Id];
                dc.DrawRoundedRectangle(EncounterViewport.Brush(selection.Contains(edge.Node.Id) ? "#304738" : edge.Node.Id == highlight ? "#303F44" : "#272A2F"), null, row, 5, 5);
                Text(dc, edge.Priority.ToString(), new(row.Left + 7, row.Top + 7), 10, "#858B95", 22);
                Text(dc, edge.Condition, new(row.Left + 30, row.Top + 6), 11, "#CFD3DB", 156);
                Text(dc, "→ " + (edge.TargetName ?? "dynamic"), new(row.Left + 194, row.Top + 6), 11, edge.Destinations.Count==0||edge.Destinations.Any(d=>d.Id==null) ? "#DAAE84" : "#91A69D", 102);
            }
            if (activeState) dc.DrawEllipse(EncounterViewport.Brush("#98E8CB"), null, new(rect.Right - 10, rect.Top + 42), 3, 3);
        }
    }
}
