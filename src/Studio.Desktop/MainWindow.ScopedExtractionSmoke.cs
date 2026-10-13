using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void ScopedExtractionSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/scoped-extraction-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunScopedExtractionUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunScopedExtractionUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var destination=catalog.First(e=>e.SourceFile!=primary).SourceFile;var supplied=File.ReadAllText(primary);var suppliedDestination=File.ReadAllText(destination);
        var destinationRoot=CSharpSyntaxTree.ParseText(suppliedDestination).GetCompilationUnitRoot();var destinationNamespace=destinationRoot.Members.OfType<NamespaceDeclarationSyntax>().Single();
        var scopedNamespace=SyntaxFactory.FileScopedNamespaceDeclaration(destinationNamespace.Name).WithUsings(destinationNamespace.Usings).WithExterns(destinationNamespace.Externs).WithMembers(destinationNamespace.Members);
        var scopedSource="using Fire=TKR.WorldServer.logic.behaviors.Shoot;\n"+destinationRoot.ReplaceNode(destinationNamespace,scopedNamespace).NormalizeWhitespace().ToFullString();
        const string source="""
using Fire=TKR.WorldServer.logic.behaviors.Shoot;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopScopedExtract=()=>Behav().Init("Cave Pirate Brawler",StudioDesktopScopedCaller(3));
    static State StudioDesktopScopedCaller(int count)=>new State("loop",new Fire(20,count:count,fixedAngle:0,coolDown:100));
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(destination,scopedSource)],Player=previous.Player with{Invulnerable=true}},null);
        EditDefinition(document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopScopedCaller").EntityId);var action=AuthoringRoot.Children.Single();SelectNode(action.Id);
        check(extractApplyButton?.IsEnabled==true,"An aliased native action exposes extraction into a file-scoped source");extractNameInput!.Text="StudioDesktopScopedFire";extractDestination!.SelectedItem=((SourceFileChoice[])extractDestination.ItemsSource).Single(s=>s.Path==destination);
        var before=Snapshot();extractApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var extracted=Snapshot();
        check(encounterDocument!=null&&document.SourceFile==destination&&AuthoringRoot.Id==action.Id&&AuthoringRoot.Kind=="Shoot","Actual extraction opens the native action helper with its original identity in the scoped file");
        check(document.Source.StartsWith(scopedSource,StringComparison.Ordinal)&&PrimaryDocument.Source.Contains("StudioDesktopScopedFire(ref count)")&&document.Expression(AuthoringRoot).StartsWith("new Fire("),"File-scoped extraction retains exact destination imports and actual source alias binding");
        check(workspacePhaseUndo.Count==1&&workspacePhaseUndo.Peek().Documents.Count==2,"Scoped helper extraction joins both files in one workspace transaction");
        Undo(this,new());check(encounterDocument==null&&document.Source==before.Source&&Snapshot().AdditionalSources.Single(s=>s.SourceFile==destination).Source==scopedSource,"One Undo restores the caller and the exact file-scoped destination");
        Redo(this,new());check(encounterDocument!=null&&AuthoringRoot.Id==action.Id&&Snapshot().Source==extracted.Source,"One Redo follows the aliased action into its file-scoped helper body");
        var path=Path.Combine(output,"scoped-extraction.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(encounterDocument!=null&&AuthoringRoot.Id==action.Id&&document.Source.StartsWith(scopedSource,StringComparison.Ordinal),"Save/reopen restores scoped helper focus and unchanged preceding declarations");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play compiles the accepted scoped partial type and aliased native helper");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.Shots.Any(s=>s.Count==3),"Native scoped-helper playback retains the aliased constructor and captured count");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(action.Id);UpdateLayout();CaptureView(this,Path.Combine(output,"scoped-extraction-editor.png"));
        check(File.ReadAllText(primary)==supplied&&File.ReadAllText(destination)==suppliedDestination,"Scoped source extraction, history, persistence and native playback leave supplied files unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
