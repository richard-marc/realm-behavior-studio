using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    async Task RunPhaseChoiceUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var supplied=File.ReadAllText(selectedEntity!.SourceFile);var root=EncounterRoot;
        const string fixture="new State(new State(\"pick\",new TimedRandomTransition(0,states:[\"a\",\"b\",\"b\"])),new State(\"a\",new TimedTransition(0,\"pick\")),new State(\"b\",new TimedTransition(0,\"pick\")))";
        document=new(document.Source.Remove(root.Start,root.Length).Insert(root.Start,fixture),adapter,sourceFile:selectedEntity.SourceFile);Changed();WorkspaceTabs.SelectedIndex=1;
        var choice=document.AllNodes.Single(n=>n.Kind=="TimedRandomTransition");SelectNode(choice.Id);
        check(phaseChoicePanel is {IsExpanded:false}&&phaseChoiceRows!.Items.Count==3&&phaseChoiceRows.Items.Cast<PhaseChoiceRow>().Count(r=>r.Name=="b"&&r.Slots==2)==2,"Native random destinations appear in a quiet collapsed list with their real duplicate-slot weights");
        phaseChoicePanel!.IsExpanded=true;phaseChoiceRows!.SelectedIndex=1;phaseChoiceUp!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.PhaseChoices(choice.Id)!.SequenceEqual(new[]{"b","a","b"}),"Actual choice reorder button edits native source order");Undo(this,new());
        check(selectedNode?.Id==choice.Id&&document.PhaseChoices(choice.Id)!.SequenceEqual(new[]{"a","b","b"}),"Choice Undo preserves the selected action and exact slot weights");
        phaseChoicePanel!.IsExpanded=true;phaseChoiceTarget!.SelectedItem="a";phaseChoiceAdd!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.PhaseChoices(choice.Id)!.SequenceEqual(new[]{"a","b","b","a"})&&phaseChoiceRows!.Items.Cast<PhaseChoiceRow>().All(r=>r.Slots==2),"Actual Add button changes probabilities by adding one genuine native slot");
        phaseChoiceRows!.SelectedIndex=1;phaseChoiceTarget!.SelectedItem="a";phaseChoiceUse!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.PhaseChoices(choice.Id)!.SequenceEqual(new[]{"a","a","b","a"}),"Actual destination selector replaces one slot rather than every repeated phase");
        phaseChoiceRows!.SelectedIndex=3;phaseChoiceRemove!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.PhaseChoices(choice.Id)!.SequenceEqual(new[]{"a","a","b"}),"Actual Remove button retains the other native choices");
        Inspector.Children.OfType<CheckBox>().Single(b=>Equals(b.Content,"Lock this region")).IsChecked=true;
        check(phaseChoiceTarget is {IsEnabled:false}&&phaseChoiceAdd is {IsEnabled:false}&&phaseChoiceRemove is {IsEnabled:false},"Region locks immediately disable all choice controls");Inspector.Children.OfType<CheckBox>().Single(b=>Equals(b.Content,"Lock this region")).IsChecked=false;
        var phase=document.AllNodes.Single(n=>n.Kind=="State"&&n.Label=="a");document.RenameState(phase.Id,"alternate",d=>adapter.Validate(d,selectedEntity!.Id));Changed(choice.Id);phaseChoicePanel!.IsExpanded=true;UpdateLayout();CaptureView(this,Path.Combine(output,"native-phase-choices.png"));
        check(document.PhaseChoices(choice.Id)!.SequenceEqual(new[]{"alternate","alternate","b"})&&BehaviorAnalysis.Analyze(EncounterRoot).Transitions.Single(t=>t.Node.Id==choice.Id).Destinations.Count==2,"Phase rename updates the actual choice list and every graph destination");
        var ids=document.Identities;var path=Path.Combine(output,"native-phase-choices.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);SelectNode(choice.Id);
        check(ids.All(i=>document.Identities.Any(n=>n.Id==i.Id))&&phaseChoiceRows!.Items.Count==3,"Saved project reopens all choices and hierarchy identities");
        check(await StartSessionAsync(),"Desktop Play starts the actual native random transition");running=false;for(var tick=0;tick<10;tick++)session!.Step(new());UpdateSession();
        check(session!.Events.Any(e=>e.Kind=="transition"&&e.Message.Contains("alternate"))&&session.Entities.Single().Phase is "pick" or "alternate" or "b","Desktop playback executes native choices among the authored phases");Stop(this,new());
        check(File.ReadAllText(selectedEntity.SourceFile)==supplied,"Desktop choice authoring and playback leave supplied game source untouched");ApplyProject(previous,null);
    }
}
