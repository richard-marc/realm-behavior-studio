using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void HazardSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/hazard-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunHazardUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunHazardUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var nativeEntity=adapter.Catalog().Single(e=>e.Id=="Gordo, the Unwound");var supplied=File.ReadAllText(nativeEntity.SourceFile);
        const string source="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioHazardDesktop=()=>Behav().Init(\"Gordo, the Unwound\",new State(\"hazards\")); } }";
        ApplyProject(previous with{EntityId=nativeEntity.Id,SourceFile=nativeEntity.SourceFile,Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true}},null);SelectNode(EncounterRoot.Id);
        BeginNativeCreation(adapter.Primitives.Single(p=>p.Name=="DelayedOriginHazards"));
        check(constructorInputs["projectileIndex"].Text=="40"&&createNativeButton!.IsEnabled,"Hazard creation seeds a compatible original XML projectile and native layout");
        constructorInputs["layout"].Text="HazardLayout.Lattice";constructorInputs["intervalMs"].Text="250";constructorInputs["startupMs"].Text="150";constructorInputs["volleyLimit"].Text="2";createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var id=selectedNode!.Id;
        check(adapter.ReadHazardPattern(document,selectedNode) is{Layout:"Lattice",Projectile:40,NativeIntervalMs:300,NativeStartupMs:200},"Actual Add creates the source-bound native lattice with correct attempt timing");
        check(hazardPanel is{IsExpanded:false}&&hazardLayout?.SelectedItem is "Lattice"&&hazardOutward?.IsEnabled==true,"Hazard layout and arming details start collapsed with modern shared controls");
        check(projectilePicker!=null&&projectileChoices!.Items.Count>0&&projectileChoices.Items.Cast<ListBoxItem>().All(item=>adapter.HazardProjectiles(nativeEntity.Id).Any(p=>p.Index==((EnemyProjectile)item.Tag).Index)),"The actual projectile chooser filters to dormant accelerating XML owned by this enemy");
        check(lifecycleRows.Count==4&&adapter.Callbacks("DelayedOriginHazards").All(c=>c.DeclaringType=="BossPatternAttack"),"Desktop lifecycle lists inherited native entry, tick, exit and death callbacks");
        var before=document.Source;var revision=document.Revision;hazardLayout!.SelectedItem="Trail";
        check(document.Revision==revision+1&&adapter.ReadHazardPattern(document,document.Find(id)) is{Layout:"Trail"}&&document.Find(id).Id==id,"Actual layout selection commits one typed native enum edit with stable identity");
        check(hazardOutward?.IsEnabled==false,"Trail disables the lattice-only orientation control without rewriting its stored input");Undo(this,new());check(document.Source==before&&document.Find(id).Id==id,"One Undo restores the complete native hazard source");Redo(this,new());check(adapter.ReadHazardPattern(document,document.Find(id))!.Layout=="Trail","Redo restores the accepted layout and identity");
        SelectNode(id);hazardLayout!.SelectedItem="Lattice";SelectNode(id);hazardOutward!.IsChecked=true;hazardOutward.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        check(adapter.ReadHazardPattern(document,document.Find(id)) is{OutwardFirst:true,Layout:"Lattice"},"Actual lattice orientation control edits the native outwardFirst flag");
        var accepted=document.Source;var stale=false;try{CommitHazardParameter(id,document.Revision-1,"layout","HazardLayout.Trail");}catch(InvalidOperationException){stale=true;}check(stale&&document.Source==accepted,"Stale hazard control proposals reject atomically");
        var invalid=false;try{CommitProjectileChoice(id,document.Revision,38);}catch(ArgumentException){invalid=true;}check(invalid&&document.Source==accepted,"Incompatible XML cannot be installed through the ordinary projectile action");
        Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(hazardLayout?.IsEnabled==false&&hazardOutward?.IsEnabled==false&&parameterApplyButton?.IsEnabled==false,"Region locks protect layout, orientation and generic native inputs");document.LockedNodes.Clear();SelectNode(id);
        hazardPanel!.IsExpanded=true;WorkspaceTabs.SelectedIndex=1;UpdateLayout();CaptureView(this,Path.Combine(output,"hazard-inspector.png"));
        var saved=Path.Combine(output,"hazard.rbs");BehaviorDocument.Save(saved,Snapshot());ApplyProject(BehaviorDocument.Load(saved),saved);check(adapter.ReadHazardPattern(document,document.Find(id)) is{Layout:"Lattice",OutwardFirst:true,VolleyLimit:2},"Reopening retains native hazard identity, attempts and direction");
        check(await StartSessionAsync(),"Play executes the accepted original hazard controller");running=false;await session!.AdvanceAsync(new(),800);UpdateSession();
        check(session.Shots.Count==8&&session.Effects.Count==8&&session.Shots.GroupBy(s=>s.Ms).Select(g=>g.Key).SequenceEqual(new long[]{200,500}),"Native desktop playback emits the edited lattice at its actual quantized attempt times");
        WorkspaceTabs.SelectedIndex=0;UpdateLayout();CaptureView(this,Path.Combine(output,"native-hazard-cues.png"));check(Viewport.NativeTelegraphCount>0&&session.Effects.All(e=>e.Kind==19),"Viewport renders the actual received sanctuary arming cues");Stop(this,new());
        check(File.ReadAllText(nativeEntity.SourceFile)==supplied,"Hazard authoring, history and playback leave supplied game source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
