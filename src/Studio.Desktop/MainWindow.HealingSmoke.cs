using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void HealingSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/healing-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunHealingUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunHealingUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        const string source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioDesktopHealing=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"healing\",new HealEntity(8,healAmount:35,coolDown:100)));}}";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var node=document.AllNodes.Single(n=>n.Kind=="HealEntity");SelectNode(node.Id);
        check(healingPanel is{IsExpanded:false}&&entityPicker is{IsExpanded:false}&&projectilePicker==null,"Nearby healing details and XML chooser start quietly collapsed");
        entityPicker!.IsExpanded=true;check(entityChoices!.Items.Cast<EntityChoice>().Any(c=>c.Id==null&&c.Title=="All enemy names")&&entityChoices.Items.Cast<EntityChoice>().All(c=>c.Title!="Nearest player"),"Healing offers all-enemy null targeting without inventing a player target");
        entitySearch!.Text="Cave Pirate Brawler";entityChoices.SelectedItem=entityChoices.Items.Cast<EntityChoice>().Single(c=>c.Id=="Cave Pirate Brawler");useEntityButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(adapter.ReadHealing(document,document.Find(node.Id)) is{Target:"Cave Pirate Brawler",Amount:35},"Actual XML chooser writes the native name input and retains the healing amount");
        Undo(this,new());check(document.Source==source,"One Undo restores omitted all-enemy targeting");Redo(this,new());
        SelectNode(node.Id);healingPanel!.IsExpanded=true;var body=(StackPanel)healingPanel.Content;
        check(body.Children.OfType<Canvas>().Single().Children.OfType<Image>().Single().Source!=null&&body.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("Strictly within 8")),"Collapsed targeting diagram uses actual host art and states the strict native range");
        Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;entityPicker!.IsExpanded=true;entityChoices!.SelectedItem=entityChoices.Items.Cast<EntityChoice>().First(c=>c.Id==null);
        check(useEntityButton!.IsEnabled==false,"Healing XML controls respect the selected region lock");document.LockedNodes.Clear();SelectNode(node.Id);
        Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="35").Text="-5";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(adapter.ReadHealing(document,document.Find(node.Id)) is{Amount:-5},"Typed Apply retains native signed HP changes");Undo(this,new());
        healingPanel!.IsExpanded=true;UpdateLayout();Status.Text="Nearby healing";CaptureView(this,Path.Combine(output,"healing-inspector.png"));
        var accepted=Snapshot();var saved=Path.Combine(output,"healing.rbs");BehaviorDocument.Save(saved,accepted);ApplyProject(BehaviorDocument.Load(saved),saved);
        check(document.Source==accepted.Source&&document.Find(node.Id).Kind=="HealEntity","Healing source and identities survive project reopening");
        check(await StartSessionAsync(observeCallbacks:true)&&session is CompiledRuntime,"Desktop Play loads the original compiled native healer");running=false;session!.SetBossHpFraction(.1);var before=session.BossHp;await session.AdvanceAsync(new(),100);UpdateSession();
        check(session.BossHp==Math.Min(session.BossMaxHp,before+35)&&session.Callbacks.Any(c=>c.Behavior.EndsWith(".HealEntity")&&c.Origin?.NodeId==node.Id),"Desktop native Play changes actual HP and records the true healing dispatcher");Stop(this,new());
        SelectNode(EncounterRoot.Id);BeginNativeCreation(adapter.Primitives.Single(p=>p.Name=="HealGroup"));check(creationGroupPicker!=null&&constructorInputs["range"].Text=="8","Native healing group creation reuses the XML group chooser and range seed");
        creationGroupPicker!.IsExpanded=true;creationGroupChoices!.SelectedItem=creationGroupChoices.Items.Cast<NativeEntityGroup>().Single(g=>g.Name=="Dwarves");((StackPanel)creationGroupPicker.Content).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var created=selectedNode!;
        check(created.Kind=="HealGroup"&&adapter.ReadHealing(document,created) is{Target:"Dwarves",Amount:null}&&groupPicker is{IsExpanded:false},"Actual Create uses the chosen native group and original full-restore default");
        groupPicker!.IsExpanded=true;check(groupMembers!.Items.Count>0&&groupMembers.Items.Cast<EntityResource>().All(m=>sprites.GetObject(m.ObjectType)!=null),"Healing groups show their original XML roster and sprite art");
        Undo(this,new());check(document.AllNodes.All(n=>n.Id!=created.Id),"One Undo removes the complete new healing action");
        check(File.ReadAllText(primary)==supplied,"Healing UI, native Play and persistence preserve supplied game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
