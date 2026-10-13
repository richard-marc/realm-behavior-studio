using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void MovementFamilySmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/movement-family-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunMovementFamilyUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunMovementFamilyUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        const string source="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioDesktopMovementFamily=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"move\")); } }";
        void Fixture(string body)=>ApplyProject(previous with{Source=source.Replace("new State(\"move\")","new State(\"move\","+body+")"),Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true,StartY=66.5}},null);
        foreach(var (kind,body,parameter) in new[]{("ReturnToSpawn","new ReturnToSpawn(1)","returnWithinRadius"),("StayBack","new StayBack(1)","distance"),("Charge","new Charge()","range")})
        {
            Fixture(body);var node=EncounterRoot.Children.Single();SelectNode(node.Id);
            check(movementEditorButton?.IsEnabled==true&&adapter.ReadMovementPattern(document,node)?.PrimaryParameter==parameter,"Native "+kind+" exposes its actual distance parameter and quiet viewport control");movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var before=document.Source;var revision=document.Revision;check(Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Range)),"Actual "+kind+" handle hit testing uses its native anchor");Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Range,2.5));
            check(document.Source==before&&document.Revision==revision,"Native "+kind+" gestures preview without repeated source mutation");Viewport.EndMovementDrag();
            check(document.Revision==revision+1&&Math.Abs(adapter.ReadMovementPattern(document,document.Find(node.Id))!.Range-2.5)<1e-6,"Native "+kind+" release commits one typed distance edit with stable identity");
            if(kind=="Charge")check(document.Find(node.Id).Parameters.Single(p=>p.Name=="range").Expression=="2.5f","Charge gestures retain the actual native float parameter type");
            Undo(this,new());check(document.Source==before&&document.Find(node.Id).Id==node.Id,"Native "+kind+" undo restores complete source and action identity");Redo(this,new());SelectNode(node.Id);movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(Viewport.Movement is {Editable:false}&&!Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Range)),"Native "+kind+" region locks protect controls and handle gestures");document.LockedNodes.Clear();SelectNode(node.Id);UpdateLayout();CaptureView(this,Path.Combine(output,kind+"-editor.png"));
        }
        Fixture("new StayBack(1,distance:8)");var retreat=EncounterRoot.Children.Single();SelectNode(retreat.Id);check(entityPicker!=null,"Named retreat targeting exposes the real XML enemy chooser and null-player mode");
        Fixture("new Charge(speed:4,range:10f,coolDown:new Cooldown(100,0))");var charge=EncounterRoot.Children.Single();var saved=Path.Combine(output,"charge.rbs");BehaviorDocument.Save(saved,Snapshot());ApplyProject(BehaviorDocument.Load(saved),saved);
        check(document.Find(charge.Id).Kind=="Charge","Movement family reopening retains native action identity");check(await StartSessionAsync(),"Play executes the accepted native charge on a diagonal player");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        var actor=session.Entities.Single();check(Math.Abs(double.Hypot(actor.X-64.5,actor.Y-64.5)-.4)<1e-5&&Math.Abs((actor.X-64.5)/(actor.Y-64.5)-3)<1e-4,"Desktop native playback retains the source charge direction and unscaled movement step");Stop(this,new());
        check(File.ReadAllText(primary)==supplied,"Movement family authoring, history and native playback leave supplied source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
