using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void SequentialSweepSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/sequential-sweep-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunSequentialSweepUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunSequentialSweepUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        const string source="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioDesktopSweep=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"loop\")); } }";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true}},null);SelectNode(EncounterRoot.Id);
        BeginNativeCreation(adapter.Primitives.Single(p=>p.Name=="SequentialAimedSweep"));
        check(constructorChoices!.Items.Count==2&&constructorInputs["projectile"].Text=="0","Native sweep creation offers both actual constructors and an owned XML projectile seed");
        constructorInputs["triggerRange"].Text="12";constructorInputs["count"].Text="7";constructorInputs["shotsPerVolley"].Text="3";constructorInputs["intervalMs"].Text="150";constructorInputs["recoveryMs"].Text="250";createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var attack=selectedNode!.Id;
        check(adapter.ReadSequentialSweep(document,selectedNode) is {Count:7,ShotsPerVolley:3,NativeIntervalMs:200},"Actual Add creates the native partial-final-volley burst with retained source timing");
        check(sequentialSweepPanel is {IsExpanded:false}&&projectilePicker!=null&&projectileChoices!.Items.Count>0,"Burst details and actual XML projectile art remain available through collapsed controls");
        check(adapter.Callbacks(selectedNode.Kind).Select(c=>c.Stage).SequenceEqual(new[]{"Entry","Ongoing"})&&lifecycleRows.Count==2,"The sweep inspector shows actual entry and ongoing callbacks");
        TextBox CountInput()=>Inspector.Children.OfType<TextBox>().Single(t=>t.ToolTip?.ToString()?.StartsWith("Total projectiles in one activation")==true);
        var before=document.Source;CountInput().Text="8";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(adapter.ReadSequentialSweep(document,document.Find(attack))!.VolleyCounts.SequenceEqual(new[]{3,3,2}),"Actual parameter Apply changes the final volley without replacing its node identity");
        Undo(this,new());check(document.Source==before&&adapter.ReadSequentialSweep(document,document.Find(attack))!.Count==7,"One desktop Undo restores the complete native burst source");Redo(this,new());check(document.Find(attack).Kind=="SequentialAimedSweep"&&adapter.ReadSequentialSweep(document,document.Find(attack))!.Count==8,"Desktop Redo retains the sweep identity and accepted count");
        var revision=document.Revision;CommitProjectileChoice(attack,revision,0);check(document.Find(attack).Parameters.Any(p=>p.Name=="projectile"&&p.Expression=="0")&&!document.Find(attack).Parameters.Any(p=>p.Name=="projectileIndex"),"Projectile selection patches the sweep's actual parameter instead of introducing Shoot syntax");
        SelectNode(attack);Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(parameterApplyButton?.IsEnabled==false&&useProjectileButton?.IsEnabled==false,"A sweep region lock disables both parameter and XML selection edits");document.LockedNodes.Clear();SelectNode(attack);
        var saved=Path.Combine(output,"sweep.rbs");BehaviorDocument.Save(saved,Snapshot());ApplyProject(BehaviorDocument.Load(saved),saved);
        check(document.Find(attack).Kind=="SequentialAimedSweep"&&adapter.ReadSequentialSweep(document,document.Find(attack))!.Count==8,"Save/reopen retains native sweep parameters and exact identity");
        check(await StartSessionAsync(),"Play loads the accepted burst through the native executor");running=false;await session!.AdvanceAsync(new(),1000);UpdateSession();
        check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{3,3,2,3})&&session.Shots.Select(s=>s.Ms).SequenceEqual(new long[]{0,200,400,800}),"Desktop playback preserves native volley sizes, tick spacing and recovery");
        Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(attack);sequentialSweepPanel!.IsExpanded=true;UpdateLayout();CaptureView(this,Path.Combine(output,"sequential-sweep-editor.png"));
        check(File.ReadAllText(primary)==supplied,"Burst creation, XML selection, history and native playback leave supplied game source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
