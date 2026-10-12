using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void ReplacementSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/replacement-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunReplacementUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunReplacementUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=catalog.Single(e=>e.Id=="Cave Pirate Brawler").SourceFile;var original=File.ReadAllText(primary);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopReplacement=()=>Behav().Init("Cave Pirate Brawler",new State(new State("original",new Transform("Cave Pirate Cabin Boy")))).Init("Pirate Lieutenant",new State(new State("replacement",new ChangeSize(30,130),new Shoot(20,count:7,fixedAngle:0,coolDown:100))));
} }
""";
        ApplyProject(previous with{EntityId="Cave Pirate Brawler",SourceFile=primary,Source=source,Identities=[],AdditionalSources=[],Layout=[],LockedNodes=[],CollapsedNodes=[],Player=new(Invulnerable:true)},null);
        var transform=EncounterRoot.Descendants().Single(n=>n.Kind=="Transform");SelectNode(transform.Id);
        check(Inspector.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("game's entity resolver")),"Replacement inspector describes actual entity resolution and original health lifetime");
        entityPicker!.IsExpanded=true;entitySearch!.Text="Pirate Lieutenant";entityChoices!.SelectedItem=entityChoices.Items.Cast<EntityChoice>().Single(e=>e.Id=="Pirate Lieutenant");useEntityButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(transform.Id).Parameters.Single(p=>p.Name=="target").Expression=="\"Pirate Lieutenant\"","Actual replacement picker authors the canonical XML entity ID");
        Undo(this,new());check(document.Find(transform.Id).Parameters.Single(p=>p.Name=="target").Expression=="\"Cave Pirate Cabin Boy\"","Replacement picker Undo restores the original target");Redo(this,new());
        document.LockedNodes.Add(transform.Id);SelectNode(transform.Id);check(useEntityButton!.IsEnabled==false,"Replacement target controls respect source locks");document.LockedNodes.Clear();SelectNode(transform.Id);
        check(await StartSessionAsync()&&session is CompiledRuntime,"Native replacement always compiles the full edited behavior database");running=false;await session!.AdvanceAsync(new(),500);UpdateSession();
        check(session.Entities.Single() is {Name:"Pirate Lieutenant",Phase:"replacement",Size:130}&&session.Shots.Any(s=>s.Count==7),"Desktop replacement runs the edited target behavior and native attack");
        check(session.BossHp==0&&!BossHp.IsEnabled&&TimerInfo.Text.Contains("no longer active"),"Desktop original health and timer controls stop at native actor removal");
        WorkspaceTabs.SelectedIndex=3;DetailsTabs.SelectedIndex=2;UpdateLayout();CaptureView(this,Path.Combine(output,"native-replacement-actors.png"));
        check(RuntimeActorInfo.Text.Contains("Pirate Lieutenant")&&runtimeActorRows.Length==1,"Actors replaces the removed source row with the actual live target");Stop(this,new());
        var ids=document.Identities;var path=Path.Combine(output,"native-replacement.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(ids.All(i=>document.Identities.Any(n=>n.Id==i.Id))&&document.Find(transform.Id).Parameters.Single(p=>p.Name=="target").Expression=="\"Pirate Lieutenant\"","Replacement source and identities survive save/reopen");
        var portal=EntityResources.Catalog(EntityResourceScope.Resolvable).First(e=>e.RuntimeKind=="Portal"&&e.Id.Contains("Pirate Cave",StringComparison.Ordinal)).Id;
        SelectNode(transform.Id);entityPicker!.IsExpanded=true;entitySearch!.Text=portal;entityChoices!.SelectedItem=entityChoices.Items.Cast<EntityChoice>().Single(e=>e.Id==portal);useEntityButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(sprites.Get(EntityResources.Catalog(EntityResourceScope.Resolvable).Single(e=>e.Id==portal).SpriteId)!=null,"Portal replacement selection resolves real client art");
        check(await StartSessionAsync(),"Portal replacement starts its actual native resolver path");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.WorldObjects.Any(e=>e.Name==portal)&&session.Entities.Count==0&&runtimeActorRows.Any(row=>row.SpriteId==portal),"Native portals appear in viewport snapshots and Actors without enemy health");
        WorkspaceTabs.SelectedIndex=0;UpdateLayout();CaptureView(this,Path.Combine(output,"native-replacement-portal.png"));Stop(this,new());
        check(File.ReadAllText(primary)==original,"Desktop replacement authoring, playback and persistence preserve original game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
