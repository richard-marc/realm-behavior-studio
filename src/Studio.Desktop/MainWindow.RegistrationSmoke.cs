using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async Task RunRegistrationUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();Search.Text="";
        var pet=catalog.Single(e=>e.Id=="Black Cat");var petSource=File.ReadAllText(pet.SourceFile);Library.SelectedItem=pet;UpdateLayout();
        check(selectedEntity==pet&&EncounterRoot.Children.Single().Kind=="PetFollow"&&SharedTemplateNotice.Visibility==Visibility.Visible,"Actual library selection opens a native InitMany member and its shared source hierarchy");
        check(!PlayButton.IsEnabled&&PlayButton.ToolTip?.ToString()?.Contains("player owner context")==true,"PetFollow desktop playback explains its actual Pet/player-owner prerequisite");
        var entries=catalog.Where(e=>e.SourceFile==previous.SourceFile).OrderBy(e=>e.ObjectType).ToArray();var pair=entries.Zip(entries.Skip(1)).First(p=>p.Second.ObjectType==p.First.ObjectType+1);
        var fixture=$$"""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioRangeFixture = () => Behav().InitMany("{{pair.First.Id}}", "{{pair.Second.Id}}",
        id => new State(new State("pattern", new TKR.WorldServer.logic.behaviors.Shoot(20, count: 2, fixedAngle: 0, coolDown: 100))));
} }
""";
        Library.SelectedItem=pair.First;document=new(fixture,adapter,sourceFile:pair.First.SourceFile);documents[pair.First.SourceFile]=document;Changed();
        var roots=document.Entities.ToArray();var identities=document.Identities;var shot=EncounterRoot.Children.Single().Children.Single();SelectNode(shot.Id);
        check(SharedTemplateNotice.Text.Contains("2 variants")&&PlayButton.IsEnabled&&EntitySprite.Source!=null,"Shared enemy template exposes its scope and actual sprite with native playback available");
        Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="2").Text="4";
        Inspector.Children.OfType<Button>().Single(b=>Equals(b.Content,"Apply changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Entities.All(e=>e.Root.Children.Single().Children.Single().Parameters.Any(p=>p.Name=="count"&&p.Expression=="4"))&&identities.All(i=>document.Identities.Any(n=>n.Id==i.Id)),"Real shared inspector Apply updates every variant once and retains independent editor identities");
        Undo(this,new());check(document.Source==fixture,"Desktop Undo restores the exact shared factory source");Redo(this,new());check(document.Source.Contains("count: 4"),"Desktop Redo reapplies one shared factory edit");
        Library.SelectedItem=pair.Second;check(ReferenceEquals(documents[pair.Second.SourceFile],document)&&EncounterRoot.Id==roots[1].Root.Id,"Switching shared library variants keeps the same edited document and the destination identity");
        var path=Path.Combine(output,"native-range.rbs");BehaviorDocument.Save(path,Snapshot());var saved=BehaviorDocument.Load(path);ApplyProject(saved,path);
        check(SharedTemplateNotice.Visibility==Visibility.Visible&&document.Entities.Count==2&&identities.All(i=>document.Identities.Any(n=>n.Id==i.Id)),"Save and reopen restore native shared registration facts and every variant identity");
        WorkspaceTabs.SelectedIndex=1;UpdateLayout();CaptureView(this,Path.Combine(output,"native-range-editor.png"));
        foreach(var entry in new[]{pair.Second,pair.First})
        {
            selectedEntity=entry;Changed();check(await StartSessionAsync()&&session is CompiledRuntime,"Shared variant desktop Play selects the original compiled registration factory for "+entry.Id);
            running=false;await ((CompiledRuntime)session!).AdvanceAsync(new(),100);UpdateSession();
            check(session.Shots.Any(s=>s.Count==4)&&session.Entities.Any(e=>e.Name==entry.Id),"Actual native range variant executes the shared edited attack for "+entry.Id);Stop(this,new());
        }
        check(File.ReadAllText(pet.SourceFile)==petSource&&File.ReadAllText(previous.SourceFile)!=fixture,"Shared authoring and native Play leave supplied source files untouched");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
