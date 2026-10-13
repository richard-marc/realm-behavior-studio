using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void GroupSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/group-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunGroupUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunGroupUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        const string source="using TKR.WorldServer.logic.behaviors;using TKR.WorldServer.logic.transitions;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioGroupDesktop=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"groups\"));}}";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true}},null);SelectNode(EncounterRoot.Id);BeginNativeCreation(adapter.Primitives.Single(p=>p.Name=="SpawnGroup"));
        check(creationGroupPicker!=null&&!createNativeButton!.IsEnabled,"Native group creation requires a real group and offers its XML chooser");creationGroupPicker!.IsExpanded=true;creationGroupChoices!.SelectedItem=creationGroupChoices.Items.Cast<NativeEntityGroup>().Single(g=>g.Name=="Dwarves");var inputPickerBody=(StackPanel)creationGroupPicker.Content;inputPickerBody.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(constructorInputs["group"].Text=="\"Dwarves\""&&createNativeButton!.IsEnabled,"Actual group creation chooser inserts the exact typed XML group");constructorInputs["maxChildren"].Text="3";constructorInputs["initialSpawn"].Text="1";constructorInputs["radius"].Text="0";createNativeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var spawn=selectedNode!;
        check(groupPicker is{IsExpanded:false}&&adapter.ReadGroupReference(document,spawn) is{Known:true,Name:"Dwarves"},"Group resource details stay collapsed after actual native action creation");groupPicker!.IsExpanded=true;
        check(groupMembers!.Items.Count==3&&groupMembers.Items.Cast<EntityResource>().All(m=>sprites.GetObject(m.ObjectType)!=null),"Group roster previews every native member using its exact object-type sprite");
        var before=document.Source;var revision=document.Revision;groupChoices!.SelectedItem=groupChoices.Items.Cast<NativeEntityGroup>().Single(g=>g.Name=="Wargs");useGroupButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Revision==revision+1&&adapter.ReadGroupReference(document,document.Find(spawn.Id)).Name=="Wargs","Actual group Use changes one native resource input while retaining action identity");Undo(this,new());check(document.Source==before&&document.Find(spawn.Id).Id==spawn.Id,"Group selection Undo restores full source and identity");Redo(this,new());check(adapter.ReadGroupReference(document,document.Find(spawn.Id)).Name=="Wargs","Group selection Redo restores the accepted native group");SelectNode(spawn.Id);groupPicker!.IsExpanded=true;
        groupSearch!.Text="no such group 9023";check(groupChoices!.Items.Count==0&&!useGroupButton!.IsEnabled&&groupInfo!.Text.Contains("No matching"),"Empty group search cannot apply an old selection");groupSearch.Text="";Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;groupChoices!.SelectedItem=groupChoices.Items.Cast<NativeEntityGroup>().Single(g=>g.Name=="Dwarves");check(useGroupButton?.IsEnabled==false,"Group resource controls honor the actual source lock");document.LockedNodes.Clear();SelectNode(spawn.Id);
        var accepted=document.Source;var stale=false;try{adapter.ApplyGroupReference(document,selectedEntity!.Id,spawn.Id,document.Revision-1,"Dwarves");}catch(InvalidOperationException){stale=true;}check(stale&&document.Source==accepted,"Stale group changes reject before source mutation");
        groupPicker!.IsExpanded=true;WorkspaceTabs.SelectedIndex=1;UpdateLayout();CaptureView(this,Path.Combine(output,"group-roster.png"));var saved=Path.Combine(output,"groups.rbs");BehaviorDocument.Save(saved,Snapshot());ApplyProject(BehaviorDocument.Load(saved),saved);check(adapter.ReadGroupReference(document,document.Find(spawn.Id)).Name=="Wargs","Group resource selection and node identity persist through reopening");
        check(await StartSessionAsync(),"Play loads accepted native group spawning");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Entities.Count==4&&session.Entities.Where(e=>e.Name!="Cave Pirate Brawler").All(e=>NativeGroups.Catalog().Single(g=>g.Name=="Wargs").Members.Any(m=>m.Id==e.Name)),"Desktop spawning creates the original selected group members");Stop(this,new());
        SelectNode(EncounterRoot.Id);BeginNativeCreation(adapter.Primitives.Single(p=>p.Name=="Reproduce"));createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var reproduce=selectedNode!;entityPicker!.IsExpanded=true;
        check(entityChoices!.Items.Cast<EntityChoice>().Any(c=>c.Id==null&&c.Title=="This host's XML type")&&!entityChoices.Items.Cast<EntityChoice>().Any(c=>c.Id==null&&c.Title=="Nearest player"),"Reproduce exposes its actual null-self resource mode without inventing player targeting");
        check(File.ReadAllText(primary)==supplied,"Group UI, resource history and native execution leave supplied source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
