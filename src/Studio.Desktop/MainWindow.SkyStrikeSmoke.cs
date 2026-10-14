using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void SkyStrikeSmoke()=>await TryRuntime(async()=>
    {
        var output=System.IO.Path.GetFullPath(".artifacts/sky-strike-desktop");System.IO.Directory.CreateDirectory(output);var checks=new List<string>();
        await RunSkyStrikeUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunSkyStrikeUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=System.IO.File.ReadAllText(file);var source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioDesktopSky=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"strike\"));}}";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=false,Defense=25,Hp=1000,StartX=70.5,StartY=64.5},Arena=new()},null);WorkspaceTabs.SelectedIndex=1;SelectNode(EncounterRoot.Id);
        BeginNativeCreation(adapter!.Primitives.Single(p=>p.Name=="SkyStrike"));check(constructorInputs["coreIndex"].Text=="-1"&&constructorInputs["aoeRadius"].Text=="1"&&constructorInputs["aoeDamage"].Text=="100","Native Add seeds a complete area-impact contract without inventing an XML projectile");constructorInputs["fuseMs"].Text="300";constructorInputs["startupMs"].Text="0";constructorInputs["intervalMs"].Text="2000";createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var node=selectedNode!;
        check(node.Kind=="SkyStrike"&&strikePanel!=null&&strikeLayout?.SelectedItem is "Targets"&&Equals(projectilePicker?.Header,"Core projectile disabled"),"Actual native creation selects the original target layout and strike inspector");strikePanel!.IsExpanded=true;var revision=document.Revision;strikeLayout!.SelectedItem="Grid";check(document.Revision==revision+1&&adapter.ReadSkyStrike(document,document.Find(node.Id))!.Layout=="Grid","Actual layout chooser commits the compiler-bound native Grid enum");Undo(this,new());check(adapter.ReadSkyStrike(document,document.Find(node.Id))!.Layout=="Targets","One editor Undo restores the original strike layout");Redo(this,new());check(adapter.ReadSkyStrike(document,document.Find(node.Id))!.Layout=="Grid","One editor Redo retains the native layout edit");
        strikeLayout!.SelectedItem="Chain";check(strikeSummary?.Text.Contains("Native area impact")==true,"The strike inspector distinguishes original area damage from XML core impact");strikeLayout!.SelectedItem="Halo";check(adapter.ReadSkyStrike(document,document.Find(node.Id))!.Layout=="Halo","The same chooser exposes the original halo layout");strikeLayout!.SelectedItem="Targets";
        document.LockedNodes.Add(node.Id);SelectNode(node.Id);check(strikeLayout?.IsEnabled==false,"Region locks disable the native layout chooser");document.LockedNodes.Clear();SelectNode(node.Id);
        var before=document.Source;try{adapter.SetSkyStrikeLayout(document,selectedEntity!.Id,node.Id,document.Revision-1,"Chain");throw new Exception("Stale strike accepted");}catch(InvalidOperationException){}check(document.Source==before,"Stale strike layout proposals leave accepted source intact");
        var path=System.IO.Path.Combine(output,"sky-strike.rbs");var ids=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(ids),"Strike source and identities survive save/reopen");
        check(await StartSessionAsync()&&session!=null,"Play starts the original native strike controller");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Effects.Any(e=>e.Kind==22&&e.DurationMs==300)&&session.PlayerHp==1000,"Native Play announces the ring fuse before applying area damage");await session.AdvanceAsync(new(),300);UpdateSession();check(session.PlayerHp==925&&session.Events.Any(e=>e.Kind=="hit"&&e.Message.Contains("native server damage")),"Native area damage reaches the visible HP and hit log through the original defense path");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(node.Id);strikePanel!.IsExpanded=true;UpdateLayout();CaptureView(this,System.IO.Path.Combine(output,"sky-strike-editor.png"));
        projectilePicker!.IsExpanded=true;check(projectileChoices!.Items.OfType<ListBoxItem>().Any(i=>i.Tag is EnemyProjectile),"Core projectile picker uses the actual owner XML entries");
        check(System.IO.File.ReadAllText(file)==supplied,"Strike controls, persistence and native Play preserve supplied game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
