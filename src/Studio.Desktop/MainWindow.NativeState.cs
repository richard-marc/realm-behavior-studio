using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? nativeStatePanel;StackPanel? nativeStateRows;TextBlock? nativeStateSummary;Button? nativeStatePlay;
    IEncounterSession? shownNativeStateSession;long shownNativeStateMs=-1,lastNativeStateRefresh;
    IReadOnlyList<NativeStateSlot>? shownNativeStateValues;
    void AddNativeStateControls(BehaviorNode node)
    {
        nativeStatePanel=null;nativeStateRows=null;nativeStateSummary=null;nativeStatePlay=null;shownNativeStateSession=null;shownNativeStateValues=null;shownNativeStateMs=-1;
        if(node.Kind is "ArgumentGroup" or "Opaque" or "Spread")return;
        if(adapter.ReadLoop(document,node) is{} loop)
        {
            var body=new StackPanel();body.Children.Add(new TextBlock{Text=loop.Description,TextWrapping=TextWrapping.Wrap,FontSize=11});
            if(loop.Period is{} period)body.Children.Add(new TextBlock{Text=$"Period: {period} ms · {loop.Children.Count} authored child inputs",Margin=new(0,8,0,4)});
            foreach(var child in loop.Children){var button=new Button{Content=child.Label,Tag=child.Id,Style=(Style)FindResource("QuietButton"),HorizontalContentAlignment=HorizontalAlignment.Left};button.Click+=(_,_)=>SelectNode(child.Id);body.Children.Add(button);}
            body.Children.Add(new TextBlock{Text="Cycle status belongs to the native behavior object. Shared instances can share that status while each actor keeps its own stored counter. Play supplies actual dispatch.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,4)});
            Inspector.Children.Add(new Expander{Header="Native cycle",Content=body,Margin=new(0,12,0,6)});
        }
        var content=new StackPanel();nativeStateSummary=new(){Text="Play to inspect stored counters and state for this source region.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,4,0,8)};content.Children.Add(nativeStateSummary);
        nativeStatePlay=new(){Content="Play with live state",Style=(Style)FindResource("QuietButton"),ToolTip="Starts fresh compiled Play with source-linked native state and callback observation."};
        nativeStatePlay.Click+=async(_,_)=>await TryRuntime(async()=>
        {
            nativeStatePlay!.IsEnabled=false;
            try{if(await StartSessionAsync(observeCallbacks:true)){running=true;UpdatePlayButton();if(nativeStatePanel!=null)nativeStatePanel.IsExpanded=true;shownNativeStateMs=-1;RefreshNativeStateControls();}}
            finally{if(nativeStatePlay!=null)nativeStatePlay.IsEnabled=runtimeStarting==null;}
        });content.Children.Add(nativeStatePlay);
        nativeStateRows=new();content.Children.Add(new ScrollViewer{Content=nativeStateRows,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        nativeStatePanel=new(){Header="Live native state",Content=content,Margin=new(0,12,0,6)};
        nativeStatePanel.Expanded+=(_,_)=>{shownNativeStateMs=-1;lastNativeStateRefresh=0;RefreshNativeStateControls();};nativeStatePanel.Collapsed+=(_,_)=>{nativeStateRows.Children.Clear();shownNativeStateMs=-1;};Inspector.Children.Add(nativeStatePanel);
    }
    void RefreshNativeStateControls()
    {
        if(nativeStatePanel?.IsExpanded!=true||nativeStateRows==null||selectedNode==null)return;
        var now=Environment.TickCount64;if(running&&now-lastNativeStateRefresh<500)return;
        var values=session?.NativeState??[];
        if(ReferenceEquals(session,shownNativeStateSession)&&ReferenceEquals(values,shownNativeStateValues)&&session?.Ms==shownNativeStateMs)return;
        lastNativeStateRefresh=now;shownNativeStateSession=session;shownNativeStateValues=values;shownNativeStateMs=session?.Ms??-1;nativeStateRows.Children.Clear();
        var rows=values.Where(s=>s.Origin is{} origin&&origin.SourceFile.Equals(document.SourceFile??selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase)&&origin.Start>=selectedNode.Start&&origin.Start+origin.Length<=selectedNode.Start+selectedNode.Length).ToArray();
        nativeStatePanel.Header="Live native state"+(rows.Length>0?" · "+rows.Length:"");
        nativeStateSummary!.Text=rows.Length==0?"No source-linked stored values are present for this region. Use Play with live state to inspect its native execution.":"Current values for each actor. Select a row to open its source.";
        nativeStateSummary.ToolTip="Showing up to 64 source-linked owners. Direct fields are observed without running getters or custom formatting. References and array contents stay opaque; traces retain the bounded snapshot. Unknown units remain defined by source.";
        foreach(var state in rows.Take(64))
        {
            var body=new StackPanel();body.Children.Add(new TextBlock{Text=$"#{state.ActorId} · {state.Owner.Split('.','+').Last()}",TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold});
            if(state.Fields.Length==0)body.Children.Add(new TextBlock{Text=state.Owner=="TKR.WorldServer.logic.behaviors.Timed"?"Remaining: "+state.Value+" ms":state.Owner=="TKR.WorldServer.logic.behaviors.Prioritize"?"Selected child: "+state.Value:state.Value,TextWrapping=TextWrapping.Wrap,Margin=new(0,3,0,0)});
            if(state.CycleStatus!=null)body.Children.Add(new TextBlock{Text="Cycle: "+state.CycleStatus,TextWrapping=TextWrapping.Wrap,FontSize=11});
            foreach(var field in state.Fields)body.Children.Add(new TextBlock{Text=field.Name+": "+field.Value,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(8,2,0,0)});
            if(state.Truncated)body.Children.Add(new TextBlock{Text="Additional fields are outside this bounded observation.",FontSize=11,TextWrapping=TextWrapping.Wrap});
            var button=new Button{Content=body,Tag=state,Style=(Style)FindResource("QuietButton"),HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new(0,3,0,3),ToolTip=state.Meaning+"\n"+state.ValueType};
            button.Click+=(_,_)=>NavigateRuntimeSource(new(session?.Ms??0,"state",state.Owner,state.Origin?.NodeId,state.ActorId,PhaseOrigin:state.Origin));nativeStateRows.Children.Add(button);
        }
    }
}
