using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Studio.Desktop;
public partial class MainWindow
{
    Task RunSweepHandleUiSmoke(string output,Guid id,Action<bool,string> check)
    {
        check(sweepEditorButton?.IsEnabled==true&&Viewport.Sweep==null,"A known native sweep offers opt-in cone/range handles without adding ordinary viewport noise");sweepEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(WorkspaceTabs.SelectedIndex==0&&Viewport.Sweep?.NodeId==id&&session==null&&Viewport.Input()==new Studio.Raple.EncounterInput(),"The actual sweep editor opens a diagram and suppresses gameplay input");
        var before=document.Source;var revision=document.Revision;Viewport.BeginSweepDrag(Viewport.SweepHandlePoint(SweepHandle.Width));Viewport.EndSweepDrag();
        check(document.Source==before&&document.Revision==revision,"Clicking a sweep handle without moving creates no source edit or history");
        check(Viewport.BeginSweepDrag(Viewport.SweepHandlePoint(SweepHandle.Width)),"Viewport hit testing selects the actual cone-width handle");Viewport.MoveSweepDrag(Viewport.SweepPoint(45,5));
        check(document.Source==before&&document.Revision==revision&&Viewport.Sweep!.Pattern.SweepDegrees==90,"Dragging previews a native 90-degree cone without repeated source mutation");Viewport.EndSweepDrag();
        check(document.Revision==revision+1&&document.Find(id).Parameters.Any(p=>p.Name=="sweepDegrees"&&p.Expression=="90"),"Releasing the cone commits one typed degree edit with stable action identity");
        var coneSource=document.Source;revision=document.Revision;check(Viewport.BeginSweepDrag(Viewport.SweepHandlePoint(SweepHandle.Trigger)),"Trigger acquisition has an independently selectable range handle");Viewport.MoveSweepDrag(Viewport.SweepPoint(90,18));Viewport.EndSweepDrag();
        check(document.Revision==revision+1&&adapter.ReadSequentialSweep(document,document.Find(id)) is {TriggerRange:18,AimRange:18},"A larger trigger commits both native ranges atomically to retain constructor validity");
        Undo(this,new());check(document.Source==coneSource&&adapter.ReadSequentialSweep(document,document.Find(id)) is {TriggerRange:12,AimRange:16,SweepDegrees:90},"One Undo restores both coupled ranges while retaining the preceding cone edit");Redo(this,new());SelectNode(id);sweepEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(Viewport.BeginSweepDrag(Viewport.SweepHandlePoint(SweepHandle.AimRange)),"Reacquisition has its own independently selectable native range handle");Viewport.MoveSweepDrag(Viewport.SweepPoint(-90,21));Viewport.EndSweepDrag();
        check(adapter.ReadSequentialSweep(document,document.Find(id)) is {TriggerRange:18,AimRange:21},"Reacquisition release edits only its actual native tile parameter");
        before=document.Source;revision=document.Revision;Viewport.BeginSweepDrag(Viewport.SweepHandlePoint(SweepHandle.AimRange));Viewport.MoveSweepDrag(Viewport.SweepPoint(-90,5));
        check(Viewport.Sweep!.Pattern.AimRange==18&&document.Source==before,"Reacquisition preview respects the native trigger-range lower bound");Viewport.CancelSweepDrag();
        check(document.Revision==revision&&Viewport.Sweep!.Pattern.AimRange==21&&document.Source==before,"Cancelling a range gesture restores its original diagram and accepted history");
        Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(Viewport.Sweep is {Editable:false}&&!Viewport.BeginSweepDrag(Viewport.SweepHandlePoint(SweepHandle.Width))&&!sweepEditorButton!.IsEnabled,"Locking immediately disables sweep controls and handle hit testing");document.LockedNodes.Clear();SelectNode(id);
        var rejected=false;try{CommitSweepEdit(Viewport.Sweep! with{Revision=document.Revision-1},new Dictionary<string,double>{{"sweepDegrees",120}});}catch(InvalidOperationException){rejected=true;}
        check(rejected&&document.Source==before&&document.Revision==revision,"Stale sweep gestures reject atomically through the shared revision guard");
        CommitSweepEdit(Viewport.Sweep!,new Dictionary<string,double>{{"aimRange",1e200}});
        check(adapter.ReadSequentialSweep(document,document.Find(id))!.AimRange==1e200&&document.Find(id).Parameters.Single(p=>p.Name=="aimRange").Expression.Contains('E')&&!document.SourceDiagnostics().Any(d=>d.IsError),"Large range commits retain valid native double literals and accepted compiler binding");
        Undo(this,new());check(document.Source==before&&adapter.ReadSequentialSweep(document,document.Find(id))!.AimRange==21,"One Undo restores the complete source after an extreme native range edit");SelectNode(id);sweepEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var geometry=new EncounterViewport();geometry.Arrange(new Rect(0,0,800,500));var originalPoint=geometry.SweepPoint(0,4);geometry.Sweep=Viewport.Sweep! with{Pattern=Viewport.Sweep!.Pattern with{SweepDegrees=0,AimRange=1e200}};geometry.FitSweep();
        check(new Rect(0,0,800,500).Contains(geometry.SweepHandlePoint(SweepHandle.AimRange)),"Very large finite native double ranges fit the diagram with a reachable handle");
        geometry.BeginSweepDrag(geometry.SweepHandlePoint(SweepHandle.AimRange));geometry.MoveSweepDrag(geometry.SweepPoint(-90,5e199));check(double.IsFinite(geometry.Sweep!.Pattern.AimRange)&&Math.Abs(geometry.Sweep.Pattern.AimRange/5e199-1)<1e-6,"Large range gestures retain finite native values without squared-distance overflow");geometry.CancelSweepDrag();
        geometry.Sweep=geometry.Sweep with{Pattern=geometry.Sweep!.Pattern with{AimRange=1000}};geometry.FitSweep();geometry.BeginSweepDrag(geometry.SweepHandlePoint(SweepHandle.Width));geometry.MoveSweepDrag(geometry.SweepPoint(180,5));
        check(geometry.Sweep!.Pattern.SweepDegrees==360,"Cone gestures retain the complete native 0-to-360-degree span");geometry.CancelSweepDrag();geometry.Sweep=null;
        check((geometry.SweepPoint(0,4)-originalPoint).Length<.000001,"Leaving the sweep diagram restores the original gameplay camera");
        UpdateLayout();CaptureView(this,Path.Combine(output,"sweep-viewport-handles.png"));return Task.CompletedTask;
    }
}
