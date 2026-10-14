using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? callbackPanel;StackPanel? callbackRows;TextBlock? callbackSummary;Button? callbackPlay;
    int shownCallbackCount=-1;long lastCallbackRefresh;IEncounterSession? shownCallbackSession;
    void AddCallbackControls(BehaviorNode node)
    {
        callbackPanel=null;callbackRows=null;callbackSummary=null;callbackPlay=null;shownCallbackCount=-1;shownCallbackSession=null;
        if(node.Kind is "ArgumentGroup" or "Opaque" or "Spread")return;
        var body=new StackPanel();callbackSummary=new(){TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,4,0,8)};body.Children.Add(callbackSummary);
        callbackPlay=new(){Content="Play with callback recording",Style=(Style)FindResource("QuietButton"),ToolTip="Starts a fresh compiled encounter and records actual native dispatch calls."};
        callbackPlay.Click+=async(_,_)=>await TryRuntime(async()=>
        {
            callbackPlay!.IsEnabled=false;
            try{if(await StartSessionAsync(observeCallbacks:true)){running=true;UpdatePlayButton();if(callbackPanel!=null)callbackPanel.IsExpanded=true;RefreshCallbackControls();}}
            finally{if(callbackPlay!=null)callbackPlay.IsEnabled=runtimeStarting==null;}
        });body.Children.Add(callbackPlay);
        callbackRows=new();body.Children.Add(new ScrollViewer{Content=callbackRows,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        callbackPanel=new(){Header="Observed callbacks",Content=body,Margin=new(0,12,0,6)};
        callbackPanel.Expanded+=(_,_)=>{shownCallbackCount=-1;lastCallbackRefresh=0;RefreshCallbackControls();};
        callbackPanel.Collapsed+=(_,_)=>{callbackRows.Children.Clear();shownCallbackCount=-1;};
        callbackSummary.Text="Record native Play to inspect actual callback order for this source region.";
        Inspector.Children.Add(callbackPanel);
    }
    void RefreshCallbackControls()
    {
        if(callbackRows==null||selectedNode==null)return;
        callbackPlay!.IsEnabled=runtimeStarting==null;
        if(callbackPanel?.IsExpanded!=true)return;
        if(running&&ReferenceEquals(shownCallbackSession,session)&&Environment.TickCount64-lastCallbackRefresh<500)return;
        if(shownCallbackCount==(session?.Callbacks.Count??0)&&ReferenceEquals(shownCallbackSession,session))return;
        shownCallbackSession=session;shownCallbackCount=session?.Callbacks.Count??0;lastCallbackRefresh=Environment.TickCount64;callbackRows.Children.Clear();
        var roots=selectedNode.Descendants().Select(n=>n.Id).ToHashSet();var acceptedHash=StudioSourceDraft.Hash(document.Source);var selected=new HashSet<long>();var rows=new List<NativeCallbackRecord>();
        foreach(var record in session?.Callbacks??[])
        {
            var own=record.Origin is{} origin&&origin.SourceHash==acceptedHash&&(origin.NodeId is{} id&&roots.Contains(id)||origin.Start>=selectedNode.Start&&origin.Length>0&&origin.Start+origin.Length<=selectedNode.Start+selectedNode.Length)&&origin.SourceFile.Equals(document.SourceFile??selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase);
            if(!own&&!(record.ParentSequence is{} parent&&selected.Contains(parent)))continue;selected.Add(record.Sequence);rows.Add(record);
        }
        callbackPanel!.Header="Observed callbacks"+(rows.Count>0?" · "+rows.Count:"");
        callbackSummary!.Text=rows.Count==0?"Record native Play to inspect this region's actual entry, tick, exit and death dispatch. Generated calls without a known construction site retain an explicit source boundary.":"Actual native calls in start order. Nested rows follow the controller that dispatched them. Showing the latest "+Math.Min(rows.Count,200)+" calls; the full transcript is retained in traces and replay.";
        foreach(var record in rows.TakeLast(200))
        {
            var label=record.SharedEvent is{} shared?"registered by "+(shared.SubscriberBehavior.Split('.','+').Last() is{Length:>0} subscriber?subscriber:"untracked source"):record.Behavior.Split('.','+').Last();
            var details=record.SharedEvent is{} binding?$"\nReceiver #{binding.ReceiverActorId} · subscriber #{binding.SubscriberActorId} · registration call #{binding.SubscriptionCallbackSequence}\n{binding.Handler}":"";
            var button=new Button{Content=new TextBlock{Text=$"{record.Ms/1000d:0.000}s · {record.Stage} · {label}",TextWrapping=TextWrapping.Wrap},Tag=record,Style=(Style)FindResource("QuietButton"),HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new(record.ParentSequence is{} parent&&selected.Contains(parent)?10:0,2,0,2),IsEnabled=record.Origin!=null,ToolTip=$"Actor #{record.ActorId} · call #{record.Sequence} · parent #{record.ParentSequence} · {record.Outcome}"+details};
            button.Click+=(_,_)=>NavigateRuntimeSource(new(record.Ms,"callback",record.Stage+" · "+record.Behavior,record.Origin?.NodeId,record.ActorId,PhaseOrigin:record.Origin));callbackRows.Children.Add(button);
        }
    }
}
