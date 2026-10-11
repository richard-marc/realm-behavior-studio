using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async Task RunOwnedEmitterUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var supplied=File.ReadAllText(selectedEntity!.SourceFile);var root=EncounterRoot;
        document=new(document.Source.Remove(root.Start,root.Length).Insert(root.Start,"new State()"),adapter,sourceFile:selectedEntity.SourceFile);Changed();
        BeginNativeCreation(adapter.Primitives.Single(p=>p.Name=="OwnedLaneEmitters"));creationEntityPicker!.IsExpanded=true;
        check(createNativeButton is {IsEnabled:true}&&creationEntityChoices!.Items.Cast<EntityChoice>().Any(c=>c.Id=="Gordo Procession Emitter")&&creationEntityChoices.Items.Cast<EntityChoice>().All(c=>c.Id!=null&&EnemyProjectiles.ForEntity(c.Id).Any(p=>p.Index==0)),"Native lane creation offers valid defaults and actual helper XML choices restricted to projectile zero");
        constructorInputs["intervalMs"].Text="200";constructorInputs["startupMs"].Text="200";createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var emitter=selectedNode!;var ids=document.Identities;var created=document.Source;
        check(emitter.Kind=="OwnedLaneEmitters"&&ownedEmitterPanel is {IsExpanded:false}&&ownedEmitterInfo!.Text.Contains("Gordo Procession Emitter")&&ownedEmitterInfo.Text.Contains("XML projectile #0")&&ownedLaneAxes?.SelectedItem is LaneAxisChoice {Name:"Horizontal"},"Lane inspector progressively discloses the actual child projectile and native axis selector");
        ownedEmitterPanel!.IsExpanded=true;ownedLaneAxes!.SelectedItem=ownedLaneAxes.Items.Cast<LaneAxisChoice>().Single(c=>c.Name=="Cross");
        check(adapter.ReadLaneAxis(document,document.Find(emitter.Id))=="Cross"&&ids.All(i=>document.Identities.Any(n=>n.Id==i.Id)),"Real lane axis dropdown commits a typed source edit while retaining node identities");
        Undo(this,new());check(document.Source==created&&selectedNode?.Id==emitter.Id&&adapter.ReadLaneAxis(document,document.Find(emitter.Id))=="Horizontal","Desktop lane axis Undo restores the exact source and surviving action selection");Redo(this,new());
        check(selectedNode?.Id==emitter.Id&&ownedLaneAxes?.SelectedItem is LaneAxisChoice {Name:"Cross"},"Desktop Redo retains the surviving action and its actual restored axis control");
        Inspector.Children.OfType<CheckBox>().Single(b=>Equals(b.Content,"Lock this region")).IsChecked=true;
        check(ownedLaneAxes is {IsEnabled:false},"Locking a lane action immediately disables its actual axis control");Inspector.Children.OfType<CheckBox>().Single(b=>Equals(b.Content,"Lock this region")).IsChecked=false;
        entityPicker!.IsExpanded=true;entitySearch!.Text="Ghost Ship";entityChoices!.SelectedItem=entityChoices.Items.Cast<EntityChoice>().Single(c=>c.Id=="Ghost Ship");useEntityButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(adapter.ReadOwnedEmitterResource(document,document.Find(emitter.Id))?.EntityId=="Ghost Ship"&&ownedEmitterInfo!.Text.Contains("Ghost Ship")&&document.Find(emitter.Id).Parameters.Any(p=>p.Name=="axes"&&p.Expression.EndsWith("Cross")),"Actual helper picker updates the child-owned XML preview without changing axes or inventing a projectile index");
        var path=Path.Combine(output,"owned-emitter.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);SelectNode(emitter.Id);ownedEmitterPanel!.IsExpanded=true;UpdateLayout();CaptureView(this,Path.Combine(output,"owned-emitter-editor.png"));
        check(adapter.ReadOwnedEmitterResource(document,document.Find(emitter.Id))?.EntityId=="Ghost Ship"&&adapter.ReadLaneAxis(document,document.Find(emitter.Id))=="Cross"&&ids.All(i=>document.Identities.Any(n=>n.Id==i.Id)),"Saved project reopens native emitter configuration, child resource binding and every identity");
        var guarded=false;try{await StartSessionAsync();}catch(InvalidOperationException error){guarded=error.Message.Contains("flat starting state");}
        check(guarded&&session==null,"Desktop Play clearly rejects the native lane controller's incompatible nested helper state before entry");
        SelectNode(emitter.Id);entityPicker!.IsExpanded=true;entitySearch!.Text="Gordo Procession Emitter";entityChoices!.SelectedItem=entityChoices.Items.Cast<EntityChoice>().Single(c=>c.Id=="Gordo Procession Emitter");useEntityButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));ownedEmitterPanel!.IsExpanded=true;UpdateLayout();CaptureView(this,Path.Combine(output,"owned-emitter-editor.png"));
        check(await StartSessionAsync(),"Desktop Play starts the actual native owned-emitter encounter");running=false;
        for(var tick=0;tick<12;tick++)((EncounterSession)session!).Step(new());UpdateSession();
        check(session!.Entities.Count==3&&session.Shots.Count>0&&session.Shots.All(s=>session.Entities.Any(e=>e.Id==s.OwnerId&&e.Name=="Gordo Procession Emitter"&&e.ParentId!=null)),"Desktop native playback exposes genuine helper actors and their own projectile emissions");
        WorkspaceTabs.SelectedIndex=0;UpdateLayout();CaptureView(this,Path.Combine(output,"owned-emitter-runtime.png"));Stop(this,new());
        check(File.ReadAllText(selectedEntity.SourceFile)==supplied,"Desktop emitter authoring and playback leave supplied game source untouched");ApplyProject(previous,null);
    }
}
