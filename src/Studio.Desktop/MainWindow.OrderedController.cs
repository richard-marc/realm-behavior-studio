using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? orderedPanel;
    TextBlock? orderedSummary;
    readonly List<Button> orderedRows=[];
    void AddOrderedControllerControls(BehaviorNode node)
    {
        orderedPanel=null;orderedSummary=null;orderedRows.Clear();
        var children=adapter.OrderedChildren(document,node);
        if(children==null&&!adapter.IsOrderedMovement(document,node))return;
        var body=new StackPanel();
        var text=children!=null
            ? "Children run in source order. The first true result stops this tick. Entry, exit and death callbacks are not forwarded."
            : adapter.UsesOrderedDispatch(document,node)
                ? "Runs through the parent's ordered tick. Its state survives phase exit because OrderedBehavior does not forward the exit callback."
                : "This movement needs an OrderedBehavior parent. A direct phase action does not run its ordered tick.";
        orderedSummary=new(){Text=text,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,4,0,8)};body.Children.Add(orderedSummary);
        if(children!=null)
        {
            foreach(var (child,index) in children.Select((c,i)=>(c,i)))
            {
                var row=new Button{Content=$"{index+1}  ·  {child.Node.Kind}"+(child.NativeOverride?"":"  ·  blocks later children"),Tag=child.Node.Id,ToolTip=child.Description,Style=(Style)FindResource("QuietButton"),HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new(0,2,0,2)};
                row.Click+=(_,_)=>SelectNode(child.Node.Id);body.Children.Add(row);orderedRows.Add(row);
            }
            body.Children.Add(new TextBlock{Text="Select a child to edit its native fields. Organize → ↑ / ↓ changes dispatch priority.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,8,0,4)});
        }
        else
        {
            var detail=node.Kind switch
            {
                "NewFollow"=>"Duration and cooldown use seconds; zero duration follows continuously. Predictive targeting adds four player-position deltas. Movement uses the original BehaviourTickTime multiplier.",
                "NewCharge"=>"Captures the target position when a charge starts. Charging returns true through the finishing tick; cooldown and missing targets permit the next child.",
                "NewWander"=>"Always returns true, including zero speed or blocked movement, so later children do not run.",
                "NewStayAbove"=>"Requires RealmWorld elevation. In this Boss Lab prototype it returns true without moving and stops the list.",
                _=>"Uses the original target, cooldown and speed fields."
            };
            body.Children.Add(new TextBlock{Text=detail,TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,4,0,8)});
            body.Children.Add(new TextBlock{Text="Native MoveToward reports attempted displacement. Collision or paralysis can prevent actual motion while the result still stops later children.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,4,0,4)});
        }
        orderedPanel=new(){Header=children!=null?"Ordered dispatch":"Ordered movement",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(orderedPanel);
    }
}
