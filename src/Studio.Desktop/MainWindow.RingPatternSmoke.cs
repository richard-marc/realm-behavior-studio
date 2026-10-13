using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void RingPatternSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/ring-pattern-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunRingPatternUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunRingPatternUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        const string source="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioDesktopRing=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"ring\")); } }";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true}},null);SelectNode(EncounterRoot.Id);
        BeginNativeCreation(adapter.Primitives.Single(p=>p.Name=="GapRing"));check(constructorInputs["projectile"].Text=="0"&&createNativeButton!.IsEnabled,"Native gap-ring creation uses a real owned XML projectile and the original default constructor inputs");
        foreach(var pair in new Dictionary<string,string>{{"slots","12"},{"missing","2"},{"gapAngle","0"},{"rotation","20"},{"interval","100"},{"delay","200"},{"warnFirstVolley","true"},{"alternateSides","true"}})constructorInputs[pair.Key].Text=pair.Value;
        createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var ring=selectedNode!.Id;
        check(adapter.ReadRingPattern(document,selectedNode) is {Count:10,Spacing:30,WarnFirstVolley:true,AlternateSides:true},"Actual Add inserts the native ring divisions, gap, timing and warning flags");
        check(ringPatternPanel is {IsExpanded:false}&&projectilePicker!=null&&projectileChoices!.Items.Count>0,"The ring inspector exposes collapsed pattern details and actual XML projectile art");
        check(adapter.Callbacks("GapRing").All(c=>c.DeclaringType=="GordoSpiral")&&lifecycleRows.Count==2,"The inherited ring implementation retains original entry and ongoing callback provenance");
        await RunRingHandleUiSmoke(output,ring,check);
        var before=document.Source;Inspector.Children.OfType<TextBox>().Single(t=>t.ToolTip?.ToString()?.StartsWith("Contiguous omitted ring divisions")==true).Text="3";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(adapter.ReadRingPattern(document,document.Find(ring)) is {Count:9,Missing:3}&&document.Find(ring).Id==ring,"Actual parameter Apply changes the omitted divisions while retaining the native ring identity");
        Undo(this,new());check(document.Source==before&&adapter.ReadRingPattern(document,document.Find(ring))!.Count==10,"One Undo restores the complete ring input edit");Redo(this,new());check(adapter.ReadRingPattern(document,document.Find(ring))!.Count==9,"Redo restores the accepted emitted count and source identity");
        CommitProjectileChoice(ring,document.Revision,0);check(document.Find(ring).Parameters.Any(p=>p.Name=="projectile"&&p.Expression=="0")&&!document.Find(ring).Parameters.Any(p=>p.Name=="projectileIndex"),"The XML chooser edits the ring's actual projectile parameter");SelectNode(ring);
        Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(parameterApplyButton?.IsEnabled==false&&useProjectileButton?.IsEnabled==false,"A ring region lock protects both pattern inputs and XML selection");document.LockedNodes.Clear();SelectNode(ring);
        var saved=Path.Combine(output,"ring.rbs");BehaviorDocument.Save(saved,Snapshot());ApplyProject(BehaviorDocument.Load(saved),saved);
        check(document.Find(ring).Kind=="GapRing"&&adapter.ReadRingPattern(document,document.Find(ring)) is {Count:9,WarnFirstVolley:true},"Save/reopen retains ring inputs, warning semantics and exact node identity");
        check(await StartSessionAsync()&&Viewport.Ring==null&&!ringEditing,"Play loads the accepted ring and restores ordinary input after layout editing");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();UpdateLayout();CaptureView(this,Path.Combine(output,"native-ring-warning.png"));
        check(session.Effects is [{Kind:18,DurationMs:200,ObjectType:9}]&&session.Shots.Count==0&&Viewport.NativeTelegraphCount>0,"Desktop playback renders the received original ring cue before any native projectile volley");
        await session.AdvanceAsync(new(),200);UpdateSession();check(session.Shots is [{Ms:200,Count:9}]&&Math.Abs(session.Shots[0].Angle-session.Effects[0].X2)<1e-6,"The native first volley uses the edited omitted divisions and original warned-ray geometry");Stop(this,new());
        SelectNode(EncounterRoot.Id);BeginNativeCreation(adapter.Primitives.Single(p=>p.Name=="GordoSpiral"));check(createNativeButton!.IsEnabled&&constructorInputs["count"].Text=="8"&&constructorInputs["interval"].Text=="1000","Spiral creation exposes valid original required inputs with quiet native defaults");createNativeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Kind=="GordoSpiral"&&adapter.ReadRingPattern(document,selectedNode) is {Count:8,Spacing:45,Rotation:15},"The creation form inserts a real source-bound rotating spiral");RunSpiralHandleUiSmoke(selectedNode.Id,check);WorkspaceTabs.SelectedIndex=1;SelectNode(ring);ringPatternPanel!.IsExpanded=true;UpdateLayout();CaptureView(this,Path.Combine(output,"ring-pattern-editor.png"));
        check(File.ReadAllText(primary)==supplied,"Ring creation, history, native warnings and playback leave supplied game source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
