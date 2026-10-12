using System.IO;
using System.IO.Compression;
using System.Windows;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void SourceSetSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunSourceSetUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"source-set-ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));
        Application.Current.Shutdown(0);
    });
    async Task RunSourceSetUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        var second=catalog.First(e=>e.SourceFile!=primary).SourceFile;var suppliedSecond=File.ReadAllText(second);
        const string helper="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioDesktopSourceSetRoot() => new State(new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100));
} }
""";
        ApplyProject(previous with{SourceDrafts=[],SelectedSourceFile=null,Source=supplied,AdditionalSources=[],LockedNodes=[],Identities=[]},null);
        AttachSourceFile(second);
        check(SourceFiles.Items.Count==2&&displayedSourceFile==second&&SourceEditor.Text==suppliedSecond&&selectedEntity.SourceFile==primary,"Source file picker opens a dependency while retaining the selected encounter");
        check(SourceFiles.SelectedItem.ToString()==Path.GetRelativePath(Path.Combine(adapter.ServerRoot,"TKR.WorldServer"),second),"Source file labels show a short server-relative path instead of an internal record dump");
        SourceEditor.Text=suppliedSecond+helper;ApplySourceBuffer();
        check(Snapshot().AdditionalSources.Single().Source==suppliedSecond+helper,"Apply commits a shared helper file into the project snapshot");
        ShowEncounterSource();var root=document.Entities.Single(e=>e.EntityId==selectedEntity.Id).Root;
        SourceEditor.Text=document.Source.Remove(root.Start,root.Length).Insert(root.Start,"StudioDesktopSourceSetRoot()");ApplySourceBuffer();
        check(document.Entities.Single(e=>e.EntityId==selectedEntity.Id).Root.Kind=="Factory"&&!document.SourceDiagnostics().Any(d=>d.IsError),"Encounter source resolves a helper from the second edited file");
        AttachSourceFile(second);var ids=document.Identities;var scenario=scenarioRevision;
        SourceEditor.Text=SourceEditor.Text.Replace("count:3","count:7");ApplySourceBuffer();
        check(scenarioRevision>scenario&&ids.All(i=>document.Identities.Any(n=>n.Id==i.Id)),"Dependency changes invalidate playback and preserve caller node identities");
        check(UndoSourceBuffer(false)&&SourceEditor.Text.Contains("count:3")&&UndoSourceBuffer(true)&&SourceEditor.Text.Contains("count:7"),"Source-tab Undo and Redo restore the selected dependency atomically");
        var valid=Snapshot();SourceEditor.Text=SourceEditor.Text.Replace("count:7","count:unknownStudioCount");var rejected=false;
        try{ApplySourceBuffer();}catch(ArgumentException){rejected=true;}
        check(rejected&&Snapshot().AdditionalSources.Single().Source==valid.AdditionalSources.Single().Source&&SourceEditor.Text.Contains("unknownStudioCount"),"Invalid dependency text remains editable while the last valid project stays intact");
        SourceEditor.Text=valid.AdditionalSources.Single().Source;sourceDrafts.Remove(second);displayedSource=SourceEditor.Text;
        document.LockedNodes.Add(document.Entities.Single(e=>e.EntityId==selectedEntity.Id).Root.Id);SourceEditor.Text=SourceEditor.Text.Replace("count:7","count:9");rejected=false;
        try{ApplySourceBuffer();}catch(InvalidOperationException){rejected=true;}
        check(rejected&&Snapshot().AdditionalSources.Single().Source==valid.AdditionalSources.Single().Source,"Encounter locks guard shared-source edits until cross-file ownership is complete");
        document.LockedNodes.Clear();SourceEditor.Text=valid.AdditionalSources.Single().Source;sourceDrafts.Remove(second);displayedSource=SourceEditor.Text;
        SourceEditor.Text+="\n// retained draft";ShowEncounterSource();AttachSourceFile(second);
        check(SourceEditor.Text.EndsWith("// retained draft"),"Switching source files preserves unapplied editor drafts");
        SourceEditor.Text=valid.AdditionalSources.Single().Source;sourceDrafts.Remove(second);displayedSource=SourceEditor.Text;
        const string invalidSuffix="\n// recovery draft\nthis is unfinished C#";
        SourceEditor.Text+=invalidSuffix;var dependencyDraft=SourceEditor.Text;
        ShowEncounterSource();SourceEditor.Text+="\n// primary draft";var primaryDraft=SourceEditor.Text;AttachSourceFile(second);
        var withDrafts=Snapshot();var recovery=Path.Combine(output,"source-drafts.rbs.recovery");BehaviorDocument.Save(recovery,withDrafts);LoadProject(recovery);
        check(displayedSourceFile==second&&SourceEditor.Text==dependencyDraft&&Snapshot().SourceDrafts.Count==2,"Recovery reopens the selected dependency and exact unfinished drafts from both source files");
        check(SourceFileNotice.Text.Contains("Draft")&&DiscardSourceDraftButton.Visibility==Visibility.Visible&&!document.SourceDiagnostics().Any(d=>d.IsError),"Restored draft stays separate from accepted source with visible Apply/Discard controls");
        var draftRejected=false;try{ApplySourceBuffer();}catch(ArgumentException){draftRejected=true;}
        check(draftRejected&&SourceEditor.Text==dependencyDraft&&Snapshot().AdditionalSources.Single().Source==valid.AdditionalSources.Single().Source,"Applying an invalid recovered draft preserves its text and accepted native source");
        ShowEncounterSource();check(SourceEditor.Text==primaryDraft,"Recovered primary draft remains available after source switching");
        DiscardSourceDraftButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(SourceEditor.Text==document.Source&&Snapshot().SourceDrafts.Count==1&&DiscardSourceDraftButton.Visibility==Visibility.Collapsed,"Discard restores only the selected accepted source and retains the other file's draft");
        AttachSourceFile(second);SourceEditor.Text=valid.AdditionalSources.Single().Source.Replace("count:7","count:8");ApplySourceBuffer();
        check(Snapshot().SourceDrafts.Count==0&&SourceEditor.Text.Contains("count:8")&&SourceFileNotice.Text.Contains("Accepted"),"Applying a repaired recovered draft clears its pending state and commits through native validation");
        UndoSourceBuffer(false);
        SourceEditor.Text+="\n// protect draft";var protectedDraft=SourceEditor.Text;var historyBlocked=false;try{UndoSourceBuffer(true);}catch(InvalidOperationException){historyBlocked=true;}
        check(historyBlocked&&SourceEditor.Text==protectedDraft&&Snapshot().AdditionalSources.Single().Source.Contains("count:7"),"Source Redo protects an unapplied draft instead of erasing its contents");
        InstallDependencySource(valid.AdditionalSources.Single() with{Source=valid.AdditionalSources.Single().Source.Replace("count:7","count:9")});Changed();
        check(SourceEditor.Text==protectedDraft&&SourceFileNotice.Text.Contains("accepted source changed"),"A later accepted source revision retains the draft and identifies its older base");
        var stalePath=Path.Combine(output,"stale-source-draft.rbs");BehaviorDocument.Save(stalePath,Snapshot());LoadProject(stalePath);
        check(SourceEditor.Text==protectedDraft&&SourceFileNotice.Text.Contains("accepted source changed"),"Draft base provenance survives save/reopen after the accepted source changes");
        UpdateLayout();CaptureView(this,Path.Combine(output,"recovered-source-draft.png"));
        DiscardSourceDraft(this,new());
        check(SourceEditor.Text.Contains("count:9")&&Snapshot().SourceDrafts.Count==0,"Discard after a source revision restores the latest accepted text");
        InstallDependencySource(valid.AdditionalSources.Single());Changed();
        UpdateLayout();CaptureView(this,Path.Combine(output,"shared-source-editor.png"));
        check(await StartSessionAsync()&&session is CompiledRuntime,"Desktop Play compiles the complete edited source set");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.Shots.Single().Count==7,"Actual desktop native playback uses the dependency's edited seven-projectile volley");
        var zip=Path.Combine(output,"shared-source.zip");ExportSourceWorkspace(zip,Snapshot(),((CompiledRuntime)session).Stamp);
        using(var archive=ZipFile.OpenRead(zip))
        {
            check(archive.Entries.Count==3&&archive.Entries.Any(e=>e.FullName=="studio-verification.json"),"Multi-file export contains both sources and the native compiler/loader verification");
            var relative=Path.GetRelativePath(Path.Combine(adapter.ServerRoot,"TKR.WorldServer"),second).Replace('\\','/');using var reader=new StreamReader(archive.GetEntry(relative)!.Open());
            check(reader.ReadToEnd()==Snapshot().AdditionalSources.Single().Source,"Source bundle retains the exact edited helper text at its original relative path");
        }
        var path=Path.Combine(output,"shared-source.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(SourceFiles.Items.Count==2&&Snapshot().AdditionalSources.Single().Source==valid.AdditionalSources.Single().Source&&!document.SourceDiagnostics().Any(d=>d.IsError),"Saved multi-file project reopens a fully bound encounter and dependency picker");
        check(File.ReadAllText(primary)==supplied&&File.ReadAllText(second)==suppliedSecond,"Desktop source editing/export leaves both supplied game files untouched");
        documents[second]=new BehaviorDocument(suppliedSecond,adapter,sourceFile:second);
        Library.ItemsSource=catalog;Library.SelectedItem=catalog.First(e=>e.SourceFile==second);
        check(document.Source==valid.AdditionalSources.Single().Source&&Snapshot().AdditionalSources.Single().SourceFile==primary,"Selecting a previously cached encounter adopts its latest dependency edits and retains the old caller");
        Library.SelectedItem=catalog.Single(e=>e.Id==previous.EntityId);
        check(document.Source.Contains("StudioDesktopSourceSetRoot()")&&Snapshot().AdditionalSources.Single().Source.Contains("count:7"),"Switching back retains both accepted source files in the shared project");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
