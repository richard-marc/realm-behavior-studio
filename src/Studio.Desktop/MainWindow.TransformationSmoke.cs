using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void TransformationSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/transformation-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunTransformationUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    static string SpriteDigest(BitmapSource? source)
    {
        if(source==null)return "";var image=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);var bytes=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(bytes,image.PixelWidth*4,0);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
    async Task RunTransformationUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=catalog.Single(e=>e.Id=="Cave Pirate Brawler").SourceFile;var original=File.ReadAllText(primary);
        const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static readonly SizeTransform StudioDesktopTransform=new(2,growMs:200,maxDurationMs:300,shrinkMs:200,hpThreshold:null);
    private _ StudioDesktopTransformFixture=()=>Behav().Init("Cave Pirate Brawler",new State(new State("grow",StudioDesktopTransform,new SizeTransformComplete(StudioDesktopTransform,"done")),new State("done")));
} }
""";
        ApplyProject(previous with{SourceDrafts=[],SelectedSourceFile=null,EntityId="Cave Pirate Brawler",SourceFile=primary,Source=source,AdditionalSources=[],Identities=[],Layout=[],LockedNodes=[],CollapsedNodes=[],Player=new(Invulnerable:true)},null);
        var definition=document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopTransform");EditDefinition(definition.EntityId);var transform=AuthoringRoot.Id;
        check(AuthoringRoot.Kind=="SizeTransform"&&Inspector.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("native growth/shrink")),"Shared transform inspector describes original callbacks and restoration");
        Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="2").Text="3";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(document.Find(transform).Parameters.Single(p=>p.Name=="multiplier").Expression!="3")throw new InvalidOperationException("Transform Apply failed: "+Status.Text);
        check(document.Find(transform).Parameters.Single(p=>p.Name=="multiplier").Expression=="3","The actual transform Apply edits its shared native multiplier");Undo(this,new());
        check(document.Find(transform).Parameters.Single(p=>p.Name=="multiplier").Expression=="2"&&AuthoringRoot.Id==transform,"Transform Undo retains its shared definition identity and view");
        var before=document.Source;var invalid=false;try{document.Apply(new(document.Revision,"Invalid native transform",[new(transform,"multiplier","0")]),ValidateAuthoring);}catch(ArgumentException){invalid=true;}
        check(invalid&&document.Source==before,"Target-typed transform edits enforce native constructor bounds before committing source");
        document.LockedNodes.Add(transform);SelectNode(transform);check(parameterApplyButton!.IsEnabled==false,"Transform locks disable the actual parameter Apply immediately");document.LockedNodes.Clear();
        ReturnToEncounter(this,new());var completion=EncounterRoot.Descendants().Single(n=>n.Kind=="SizeTransformComplete");SelectNode(completion.Id);
        check(BehaviorAnalysis.Analyze(EncounterRoot).Transitions.Single().Node.Id==completion.Id&&Inspector.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("same SizeTransform")),"Completion is shown as a transition with the shared-instance requirement");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Shared native transform uses the original compiled encounter");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.Entities.Single() is {Size:150,GlowColor:0xFF0000,ProjectileSizeBase:100},"Live native growth exposes actual size, glow and projectile settings");
        WorkspaceTabs.SelectedIndex=0;UpdateLayout();CaptureView(this,Path.Combine(output,"native-transform-growth.png"));
        WorkspaceTabs.SelectedIndex=3;DetailsTabs.SelectedIndex=2;UpdateLayout();CaptureView(this,Path.Combine(output,"native-transform-actors.png"));
        check(RuntimeActorInfo.Text.Contains("Size: 150")&&RuntimeActorInfo.Text.Contains("Glow: FF0000"),"The real Actors panel displays transformation state");
        await session.AdvanceAsync(new(),500);UpdateSession();
        check(session.Entities.Single() is {Phase:"done",Size:100,GlowColor:0,ProjectileSizeBase:0},"Native completion enters its phase and restores the captured appearance");Stop(this,new());
        var rat=catalog.Single(e=>e.Id=="Treasure Rat");var originalRat=File.ReadAllText(rat.SourceFile);
        const string textureSource="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopTextureFixture=()=>Behav().Init("Treasure Rat",new State(new SetAltTexture(0,3,cooldown:100,loop:false)));
} }
""";
        ApplyProject(previous with{SourceDrafts=[],SelectedSourceFile=null,EntityId=rat.Id,SourceFile=rat.SourceFile,Source=textureSource,AdditionalSources=[],Identities=[],Layout=[],LockedNodes=[],CollapsedNodes=[],Player=new(Invulnerable:true)},null);
        var action=EncounterRoot.Children.Single(n=>n.Kind=="SetAltTexture");SelectNode(action.Id);
        check(textureChoices!.Items.Cast<TextureChoice>().Select(t=>t.Index).SequenceEqual(new[]{0,1})&&textureChoices.Items.Cast<TextureChoice>().All(t=>t.Image!=null),"Texture picker loads only actual XML variants with their client sprites");
        check(SpriteDigest(sprites.Get(rat.Id))!=SpriteDigest(sprites.Get(rat.Id,altTexture:1))&&sprites.Character(rat.Id,SpriteAction.Walk,.8,0,1).Image!=null,"Alternate texture frames remain separate from the original animation cache");
        textureChoices!.SelectedItem=textureChoices.Items.Cast<TextureChoice>().Single(t=>t.Index==1);useTextureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(action.Id).Parameters.Single(p=>p.Name=="minValue").Expression=="1","Real texture-picker Apply changes only the first native texture ID");
        Undo(this,new());check(document.Find(action.Id).Parameters.Single(p=>p.Name=="minValue").Expression=="0","Texture picker Undo restores the original native ID");Redo(this,new());
        document.LockedNodes.Add(action.Id);SelectNode(action.Id);check(textureChoices!.IsEnabled==false&&useTextureButton!.IsEnabled==false,"Texture locks disable both selection and application");document.LockedNodes.Clear();SelectNode(action.Id);
        var ids=document.Identities;var path=Path.Combine(output,"native-textures.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(ids.All(i=>document.Identities.Any(n=>n.Id==i.Id))&&document.Find(action.Id).Parameters.Single(p=>p.Name=="minValue").Expression=="1","Save/reopen preserves native texture settings and identities");
        check(await StartSessionAsync(),"Native texture playback starts from the accepted source");running=false;await session!.AdvanceAsync(new(),1000);UpdateSession();WorkspaceTabs.SelectedIndex=0;Viewport.ShowHitboxes=true;UpdateLayout();CaptureView(this,Path.Combine(output,"native-alternate-texture.png"));
        check(session.Entities.Single() is {AltTexture:3,TextureIndex:1}&&Viewport.Session!.Entities.Single().TextureIndex==1,"Batched desktop playback renders the last valid XML texture through undefined native IDs");Stop(this,new());
        check(File.ReadAllText(primary)==original&&File.ReadAllText(rat.SourceFile)==originalRat,"Transformation and texture authoring, persistence and playback leave both game files untouched");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
