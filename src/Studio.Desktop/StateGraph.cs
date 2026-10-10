using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Studio.Core;

namespace Studio.Desktop;
public sealed class StateGraph : FrameworkElement
{
    BehaviorNode? root; Guid? highlight; string? active;
    readonly Dictionary<Guid, Rect> locations = [];
    public event Action<Guid>? Selected;
    public BehaviorNode? Root { get => root; set { root = value; InvalidateVisual(); } }
    public Guid? Highlight { get => highlight; set { highlight = value; InvalidateVisual(); } }
    public string? ActivePhase { get => active; set { if (active == value) return; active = value; InvalidateVisual(); } }
    public StateGraph() { MouseLeftButtonDown += (_, e) => { var point = e.GetPosition(this); foreach (var item in locations) if (item.Value.Contains(point)) { Selected?.Invoke(item.Key); break; } }; }
    protected override void OnRender(DrawingContext dc)
    {
        if (root == null) return;
        locations.Clear();
        var states = root.Descendants().Where(n => n.Kind == "State").ToArray();
        if (states.Length == 0) return;
        var row = 0;
        foreach (var s in states) { var depth = s.Path.Count(c => c == '/'); locations[s.Id] = new Rect(20 + Math.Max(0, depth - 1) * 26, 16 + row++ * 114, 230, 90); }
        Width = 330 + states.Max(s => s.Path.Count(c => c == '/')) * 26; Height = Math.Max(270, row * 114 + 20);
        var neutral = new Pen(EncounterViewport.Brush("#353941"), 1);
        foreach (var state in states)
        {
            var from = locations[state.Id];
            foreach (var child in state.Children.Where(c => c.Kind == "State"))
            {
                var to = locations[child.Id]; var x = from.Left + 10;
                dc.DrawLine(neutral, new(x, from.Bottom), new(x, to.Top + 45)); dc.DrawLine(neutral, new(x, to.Top + 45), new(to.Left, to.Top + 45));
            }
            foreach (var transition in state.Children.Where(n => n.Kind.EndsWith("Transition")))
            {
                var expression = transition.Parameters.FirstOrDefault(p => p.Name == "targetState")?.Expression;
                var name = expression == null ? null : BehaviorDocument.LiteralString(expression);
                var target = states.FirstOrDefault(n => n.Label == name); if (target == null) continue;
                var to = locations[target.Id]; var start = new Point(from.Right, from.Top + 45); var end = new Point(to.Right, to.Top + 45); var route = Math.Max(from.Right, to.Right) + 28;
                var pen = new Pen(EncounterViewport.Brush("#8A83A1"), 1);
                dc.DrawLine(pen, start, new(route, start.Y)); dc.DrawLine(pen, new(route, start.Y), new(route, end.Y)); dc.DrawLine(pen, new(route, end.Y), end);
                dc.DrawLine(pen, end, end + new Vector(6, -4)); dc.DrawLine(pen, end, end + new Vector(6, 4));
            }
        }
        row = 0;
        foreach (var state in states)
        {
            var rect = locations[state.Id]; var activeState = state.Label == active;
            var stroke = state.Id == highlight ? "#98E8CB" : activeState ? "#A99ACA" : "#3D414A";
            dc.DrawRoundedRectangle(EncounterViewport.Brush("#202226"), new Pen(EncounterViewport.Brush(stroke), 1), rect, 9, 9);
            EncounterViewport.Text(dc, row++ == 0 ? "ENCOUNTER" : $"PHASE {row - 1:00}", new(rect.Left + 14, rect.Top + 11), 10, EncounterViewport.Brush("#858B95"));
            EncounterViewport.Text(dc, state.Label == "Root" ? "Root behavior" : state.Label, new(rect.Left + 14, rect.Top + 30), 15, EncounterViewport.Brush("#E8E9EC"));
            var actions = string.Join(" · ", state.Children.Where(n => n.Kind != "State" && !n.Kind.EndsWith("Transition")).Select(n => n.Kind));
            if (actions.Length == 0) actions = state.Children.Any(n => n.Kind == "State") ? "Contains nested phases" : "No actions";
            EncounterViewport.Text(dc, actions.Length > 29 ? actions[..28] + "…" : actions, new(rect.Left + 14, rect.Top + 60), 11, EncounterViewport.Brush("#92969F"));
            if (activeState) dc.DrawEllipse(EncounterViewport.Brush("#98E8CB"), null, new(rect.Right - 17, rect.Top + 17), 3, 3);
        }
    }
}
