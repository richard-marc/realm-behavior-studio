using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Studio.Desktop;
public partial class MainWindow
{
    async Task RunRingHandleUiSmoke(string output,Guid id,Action<bool,string> check)
    {
        check(ringEditorButton?.IsEnabled==true&&Viewport.Ring==null,"Native gap rings offer an opt-in layout editor without ordinary viewport noise");ringEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(WorkspaceTabs.SelectedIndex==0&&Viewport.Ring?.NodeId==id&&session==null&&Viewport.Input()==new Studio.Raple.EncounterInput(),"The real ring layout control opens the diagram and suppresses gameplay input");
        var before=document.Source;var revision=document.Revision;Viewport.BeginRingDrag(Viewport.RingHandlePoint(RingHandle.Direction));Viewport.EndRingDrag();
        check(document.Source==before&&document.Revision==revision,"Clicking a ring handle without moving retains accepted source and history");
        check(Viewport.BeginRingDrag(Viewport.RingHandlePoint(RingHandle.Direction)),"Viewport hit testing selects the native gap-direction handle");Viewport.MoveRingDrag(Viewport.RingPoint(45,5));
        check(document.Source==before&&document.Revision==revision&&Viewport.Ring!.Pattern.Angle==225,"Pointer movement previews gap direction without committing repeated edits");Viewport.EndRingDrag();
        check(document.Revision==revision+1&&document.Find(id).Parameters.Any(p=>p.Name=="gapAngle"&&p.Expression=="45"),"Direction release commits the actual gapAngle source parameter once");
        Viewport.BeginRingDrag(Viewport.RingHandlePoint(RingHandle.Opening));Viewport.MoveRingDrag(Viewport.RingPoint(120,6));Viewport.EndRingDrag();
        check(adapter.ReadRingPattern(document,document.Find(id)) is {Missing:4,Count:8,Slots:12},"Opening gestures snap to native omitted divisions rather than inventing a continuous gap width");
        var twelveSource=document.Source;revision=document.Revision;Viewport.BeginRingDrag(Viewport.RingHandlePoint(RingHandle.Divisions));var scaleDistance=(Viewport.RingHandlePoint(RingHandle.Divisions)-Viewport.RingPoint(-45,3)).Length;var ringOrigin=Viewport.RingPoint(-45,0);var sixPoint=Viewport.RingPoint(-45,1);var radial=sixPoint-ringOrigin;radial.Normalize();sixPoint+=radial*scaleDistance;Viewport.MoveRingDrag(sixPoint);Viewport.EndRingDrag();
        check(document.Revision==revision+1&&adapter.ReadRingPattern(document,document.Find(id)) is {Slots:4,Missing:2,Count:2,Spacing:90},"Division release commits coupled integer slots and omitted divisions in one valid source edit");Undo(this,new());
        check(document.Source==twelveSource&&adapter.ReadRingPattern(document,document.Find(id)) is {Slots:12,Missing:4},"One Undo restores the complete division edit with source identity intact");
        check(await StartSessionAsync()&&Viewport.Ring==null&&!ringEditing,"Play exits ring editing and compiles the accepted viewport gesture values");running=false;await session!.AdvanceAsync(new(),300);UpdateSession();
        check(session.Shots is [{Count:8,Ms:200}]&&Math.Abs(session.Shots[0].Angle-2*Math.PI/3)<1e-6,"Native playback executes the viewport-authored 45-degree gap and four omitted divisions");Stop(this,new());
        Undo(this,new());Undo(this,new());check(document.Source==before&&document.Find(id).Id==id,"Each ring gesture has one complete source Undo and retains the selected action identity");SelectNode(id);ringEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Viewport.BeginRingDrag(Viewport.RingHandlePoint(RingHandle.Direction));Viewport.MoveRingDrag(Viewport.RingPoint(90,5));Viewport.CancelRingDrag();
        check(document.Source==before&&Viewport.Ring!.Pattern.Angle==180,"Cancelling a ring gesture restores the original base layout without source mutation");
        Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(Viewport.Ring is {Editable:false}&&!Viewport.BeginRingDrag(Viewport.RingHandlePoint(RingHandle.Direction))&&!ringEditorButton!.IsEnabled,"A region lock disables both ring layout controls and handle hit testing");document.LockedNodes.Clear();SelectNode(id);
        var rejected=false;try{CommitRingEdit(Viewport.Ring! with{Revision=document.Revision-1},new Dictionary<string,double>{{"gapAngle",90}});}catch(InvalidOperationException){rejected=true;}
        check(rejected&&document.Source==before,"Stale ring proposals reject atomically through the source revision guard");
        var geometry=new EncounterViewport();geometry.Arrange(new Rect(0,0,800,500));var camera=geometry.RingPoint(0,4);geometry.Ring=Viewport.Ring! with{Pattern=Viewport.Ring!.Pattern with{Slots=64,Missing=62,Count=2,Spacing=5.625}};geometry.FitRing();
        check(new Rect(0,0,800,500).Contains(geometry.RingHandlePoint(RingHandle.Divisions)),"Maximum native division counts fit the authoring canvas with a reachable handle");geometry.Ring=null;
        check((geometry.RingPoint(0,4)-camera).Length<.000001,"Closing a ring layout restores the original gameplay camera");
        UpdateLayout();CaptureView(this,Path.Combine(output,"gap-ring-layout.png"));ClearRingEditor();
    }
    void RunSpiralHandleUiSmoke(Guid id,Action<bool,string> check)
    {
        ringEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var before=document.Source;Viewport.BeginRingDrag(Viewport.RingHandlePoint(RingHandle.Opening));Viewport.MoveRingDrag(Viewport.RingPoint(210,6));Viewport.EndRingDrag();
        check(document.Find(id).Parameters.Any(p=>p.Name=="spacing"&&p.Expression=="60")&&adapter.ReadRingPattern(document,document.Find(id))!.Count==8,"Spiral spacing gestures retain complete native revolutions and leave shot count unchanged");Undo(this,new());check(document.Source==before,"One Undo restores the full spiral spacing gesture");
        var geometry=new EncounterViewport();geometry.Arrange(new Rect(0,0,800,500));geometry.Ring=new(id,document.Revision,adapter.ReadRingPattern(document,document.Find(id))! with{Spacing=-45},true);geometry.FitRing();geometry.BeginRingDrag(geometry.RingHandlePoint(RingHandle.Opening));geometry.MoveRingDrag(geometry.RingPoint(-210,6));
        check(Math.Abs(geometry.Ring!.Pattern.Spacing+60)<.000001,"Spiral gestures retain negative spacing across the signed angle boundary");geometry.CancelRingDrag();
        check(geometry.Ring!.Pattern.Spacing==-45&&document.Source==before,"Cancelling signed spacing retains the original preview and accepted source");
    }
}
