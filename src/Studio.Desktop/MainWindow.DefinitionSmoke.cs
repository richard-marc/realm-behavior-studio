using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async Task RunDefinitionUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var supplied=File.ReadAllText(selectedEntity!.SourceFile);
        const string fixture="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDefinitionFixture = () => Behav().Init("Cave Pirate Brawler", StudioDefinitionRoot());
    static State StudioDefinitionRoot() => new State("shared phase", StudioDefinitionShot, new Timed(1000, StudioDefinitionList));
    static readonly Shoot StudioDefinitionShot = new Shoot(20, count: 2, fixedAngle: 0, coolDown: 100);
    static Behavior[] StudioDefinitionList = [new Shoot(20, count: 3, fixedAngle: 0, coolDown: 100), new Shoot(20, count: 5, fixedAngle: 0, coolDown: 100)];
} }
""";
        document=new(fixture,adapter,sourceFile:selectedEntity.SourceFile);Changed();var ids=document.Identities;
        Button EditButton(string name)=>Inspector.Children.OfType<StackPanel>().SelectMany(p=>p.Children.OfType<Button>()).Single(b=>Equals(b.Content,"Edit "+name));
        EditButton("StudioDefinitionRoot").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var body=document.Definitions.Single(d=>d.Definition!.Label=="StudioDefinitionRoot");var shot=document.Definitions.Single(d=>d.Definition!.Label=="StudioDefinitionShot");var list=document.Definitions.Single(d=>d.Definition!.Label=="StudioDefinitionList");
        check(AuthoringRoot.Id==body.Root.Id&&DefinitionBanner.Visibility==Visibility.Visible&&DefinitionTitle.Text.Contains("StudioDefinitionRoot"),"Real helper inspector opens its actual editable definition with explicit caller scope");
        check(TreeItems().Any(i=>((BehaviorNode)i.Tag).Id==body.Root.Id)&&Graph.Root?.Id==body.Root.Id&&EncounterRoot.Kind=="Factory","Definition hierarchy and graph use source contents while preserving the native caller model");
        SelectNode(body.Root.Children.Single(n=>n.Kind=="Reference").Id);EditButton("StudioDefinitionShot").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(AuthoringRoot.Id==shot.Root.Id&&selectedNode!.Kind=="Shoot","Shared reference inspector navigates transitively into the original field initializer");
        Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="2").Text="4";Inspector.Children.OfType<Button>().Single(b=>Equals(b.Content,"Apply changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(shot.Root.Id).Parameters.Any(p=>p.Name=="count"&&p.Expression=="4")&&ids.All(i=>document.Identities.Any(n=>n.Id==i.Id)),"Actual definition inspector Apply edits one source field and preserves caller/body identities");
        Undo(this,new());check(document.Source==fixture&&AuthoringRoot.Id==shot.Root.Id,"Desktop definition Undo restores exact source while retaining definition navigation");Redo(this,new());
        DefinitionBack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(AuthoringRoot.Id==EncounterRoot.Id&&DefinitionBanner.Visibility==Visibility.Collapsed,"Return to encounter restores the original caller hierarchy without copying helper contents");
        EditButton("StudioDefinitionRoot").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var timed=AuthoringRoot.Children.Single(n=>n.Kind=="Timed");SelectNode(timed.Children.Single().Id);
        EditButton("StudioDefinitionList").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var second=AuthoringRoot.Children[1];SelectNode(second.Id);upStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(list.Root.Id).Children[0].Id==second.Id&&AuthoringRoot.Kind=="ArgumentGroup","Real collection Organize edits the original shared initializer and retains child identities");
        var path=Path.Combine(output,"source-definition.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(AuthoringRoot.Id==list.Root.Id&&DefinitionBanner.Visibility==Visibility.Visible&&ids.All(i=>document.Identities.Any(n=>n.Id==i.Id)),"Saved project reopens all source definitions, identities and focused definition view");
        UpdateLayout();CaptureView(this,Path.Combine(output,"source-definition-editor.png"));
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play from the definition view runs the actual caller through the compiled source path");running=false;await ((CompiledRuntime)session!).AdvanceAsync(new(),100);UpdateSession();
        check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{4,5,3}),"Desktop native caller observes edited shared field and source collection order without reconstructed helper logic");Stop(this,new());
        check(File.ReadAllText(selectedEntity.SourceFile)==supplied,"Desktop definition authoring and Play preserve supplied game source");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
