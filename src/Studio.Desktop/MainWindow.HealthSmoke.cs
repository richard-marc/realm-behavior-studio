using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async Task RunHealthUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var supplied=File.ReadAllText(selectedEntity!.SourceFile);var root=EncounterRoot;
        const string fixture="new State(new State(\"gate\",new GiveTemporaryDefense(40),new HealSelf(coolDown:300,amount:2),new DamageTakenTransition(1,\"hit\",wipeProgress:true),new EntityHpLessTransition(10,\"Cabin Boy\",.5,\"hit\")),new State(\"hit\"))";
        document=new(document.Source.Remove(root.Start,root.Length).Insert(root.Start,fixture),adapter,sourceFile:selectedEntity.SourceFile);Changed();
        var damage=document.AllNodes.Single(n=>n.Kind=="DamageTakenTransition");SelectNode(damage.Id);
        check(Inspector.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("wipeProgress does not clear"))&&BehaviorAnalysis.Analyze(EncounterRoot).Transitions.First(t=>t.Node.Id==damage.Id).Condition=="Credited damage ≥ 1","Damage gate inspector explains native cumulative credit and its non-resetting flag");
        var health=document.AllNodes.Single(n=>n.Kind=="EntityHpLessTransition");SelectNode(health.Id);entityPicker!.IsExpanded=true;entitySearch!.Text="Cave Pirate Cabin Boy";entityChoices!.SelectedItem=entityChoices.Items.Cast<EntityChoice>().Single(c=>c.Id=="Cave Pirate Cabin Boy");useEntityButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(health.Id).Parameters.Single(p=>p.Name=="entity").Expression.Contains("Cave Pirate Cabin Boy")&&adapter.EntityParameterFor(document,document.Find(health.Id)) is {NameFragment:true},"Native nearby-health picker inserts a canonical enemy name with substring semantics");Undo(this,new());
        check(document.Find(health.Id).Parameters.Single(p=>p.Name=="entity").Expression=="\"Cabin Boy\"","Nearby-health picker Undo restores the exact original fragment");
        var defend=document.AllNodes.Single(n=>n.Kind=="GiveTemporaryDefense");SelectNode(defend.Id);check(adapter.EffectiveCallbacks(document,defend).Select(c=>c.Stage).SequenceEqual(new[]{"Entry","Exit"}),"Defense inspector retains actual entry/exit lifecycle without invented ongoing behavior");
        var project=Snapshot() with{Player=new(Invulnerable:true,WeaponId:"Staff of Diabolic Secrets",Attack:0,Dexterity:0,StartX:66.5,StartY:64.5)};ApplyProject(project,null);
        check(await StartSessionAsync(),"Desktop Play starts the native health/defense encounter");running=false;session!.Step(new());UpdateSession();WorkspaceTabs.SelectedIndex=3;UpdateLayout();CaptureView(this,Path.Combine(output,"native-health-actors.png"));
        check(session.Entities.Single().Defense==40&&RuntimeActorInfo.Text.Contains("Defense: 40")&&RuntimeActorInfo.Text.Contains("Damage credited to present players: 0"),"Actual Actors panel displays live native defense and initial damage eligibility");
        for(var tick=0;tick<30&&session.Entities.Single().Phase!="hit";tick++)session.Step(new(Attack:true,AimX:64.5,AimY:64.5));UpdateSession();UpdateLayout();CaptureView(this,Path.Combine(output,"native-health-credit.png"));
        check(session.Entities.Single() is {Phase:"hit",Defense:0,Damage.PresentPlayerDamage:>0}&&RuntimeActorInfo.Text.Contains("Player #"),"Actual XML weapon damage changes phase, restores defense and appears in live contributor readouts");Stop(this,new());
        var ids=document.Identities;var path=Path.Combine(output,"native-health.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(ids.All(i=>document.Identities.Any(n=>n.Id==i.Id))&&document.AllNodes.Count(n=>n.Kind is "HealSelf" or "GiveTemporaryDefense" or "DamageTakenTransition" or "EntityHpLessTransition")==4,"Health/defense project reopens every native action and identity");
        check(File.ReadAllText(selectedEntity.SourceFile)==supplied,"Desktop health authoring leaves supplied game source untouched");ApplyProject(previous,null);
    }
}
