using System.Text.Json;
using Studio.Core;
using Studio.Raple;

var adapter = new RapleAdapter(Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server")); var checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
const string entity = "Cave Pirate Brawler";
var file = adapter.Catalog().Single(e => e.Id == entity).SourceFile; var original = File.ReadAllText(file);
var otherFile = adapter.Catalog().First(e => e.SourceFile != file).SourceFile; var otherOriginal = File.ReadAllText(otherFile);
const string source = """
using TKR.WorldServer.logic.behaviors;
namespace StudioPortable {
    using Value = TKR.WorldServer.logic.behaviors.Shoot;
    internal static partial class FanKit {
        static int Next;
        static readonly Value First = Make(2);
        static Value Make(int count) => new(20,count:count+Next++,coolDown:100);
        public static TKR.WorldServer.logic.Behavior[] Build() => [First,Second,BuildPair(6)];
        private static partial TKR.WorldServer.logic.Behavior BuildPair(int count);
    }
    internal partial class Settings<T> where T : struct {
        readonly int count;
        public Settings(int count) { this.count=count; }
        ~Settings() { }
    }
}
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioPartialOrigin = () => Behav().Init("Cave Pirate Brawler",new State("origin",children:[..StudioPortable.FanKit.Build()]));
} }
""";
const string parts = """
namespace StudioPortable {
    using Value = TKR.WorldServer.logic.behaviors.Swirl;
    using SettingsAlias = StudioPortable.Settings<int>;
    using static StudioPortable.FanKit;
    internal static partial class FanKit {
        static readonly TKR.WorldServer.logic.behaviors.Shoot Second = Make(4);
        private static partial TKR.WorldServer.logic.Behavior BuildPair(int count) => new TKR.WorldServer.logic.behaviors.Timed(1000,First,new TKR.WorldServer.logic.behaviors.Shoot(20,count:new SettingsAlias(count).Count(),coolDown:100));
        public static TKR.WorldServer.logic.Behavior UnusedMovement() => new Value(targeted:false);
        public static int UnusedGetter => throw new System.Exception("Authoring must not invoke this getter");
    }
    internal partial class Settings<T> where T : struct {
        public int Count() => count;
    }
}
""";
const string target = "namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioPartialTarget=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"destination\",children:[]));}}";
var project = new StudioProject { SourceFile=file,Source=source,EntityId=entity,AdditionalSources=[new(otherFile,otherOriginal+"\n"+parts)],Player=new(Invulnerable:true),Seed=7 };
var scoped = adapter.ForProject(project); var doc = new BehaviorDocument(source,scoped,sourceFile:file); project=project with{Identities=doc.Identities};
var spread = doc.Entities.Single().Root.Descendants().Single(n=>n.Kind=="Spread");
var fragment = FragmentDependencies.Capture(adapter,project,doc,[spread.Id],doc.CaptureFragment([spread.Id],"Portable partial kit"));
Check(fragment.Version==3 && fragment.Dependencies?.Members.Count==2 && fragment.Dependencies.Members.All(m=>m.Parts.Count==1),"Partial generic and static types retain all source parts under one token per type");
Check(fragment.Dependencies!.Members.SelectMany(m=>m.Declarations).Select(p=>p.SourceSha256).Distinct().Count()==2,"Each partial section retains its actual independent source fingerprint");
Check(fragment.Dependencies.Members.SelectMany(m=>m.Declarations).Select(p=>p.Imports).Any(i=>i.Contains("behaviors.Shoot")) && fragment.Dependencies.Members.SelectMany(m=>m.Declarations).Select(p=>p.Imports).Any(i=>i.Contains("behaviors.Swirl")),"Conflicting alias names retain independent declaration import scopes");
Check(fragment.Dependencies.Members.SelectMany(m=>m.Declarations).Any(p=>p.Declaration.Contains("UnusedGetter")),"Complete partial closure capture preserves unexecuted getter source without invoking it");
var json = JsonSerializer.Serialize(fragment,BehaviorDocument.JsonOptions); var loaded=JsonSerializer.Deserialize<BehaviorFragment>(json,BehaviorDocument.JsonOptions)!; loaded.Validate();
Check(loaded.Version==3 && loaded.Dependencies!.Members.All(m=>m.Parts.Count==1) && !json.Contains("\"declarations\""),"Version 3 JSON round-trips parts without serializing derived declaration enumeration");
var artifact=Path.GetFullPath(".artifacts/partial-fragments");Directory.CreateDirectory(artifact);var fragmentFile=Path.Combine(artifact,"partial.rbsfragment");loaded.Save(fragmentFile);loaded=BehaviorFragment.Load(fragmentFile);
Check(loaded.Dependencies!.Members.All(m=>m.Declarations.Count()==2),"Portable partial files retain every declaration section through atomic saving and reopening");
Reject(()=>(loaded with{Version=2}).Validate(),"Earlier fragment versions cannot silently omit additional partial sections");
var first=loaded.Dependencies.Members[0];
BehaviorFragment ChangedPart(FragmentDependencyPart part)=>loaded with{Dependencies=loaded.Dependencies with{Members=[first with{Parts=[part]}]}};
Reject(()=>ChangedPart(first.Parts[0] with{Imports="namespace Injected {}"}).Validate(),"Part imports reject unrelated namespace declarations");
Reject(()=>ChangedPart(first.Parts[0] with{Declaration="partial class Broken {"}).Validate(),"Malformed partial source rejects before insertion");
Reject(()=>ChangedPart(first.Parts[0] with{SourceSha256=new string('z',64)}).Validate(),"Each part requires a hexadecimal source fingerprint");
Reject(()=>(loaded with{Dependencies=loaded.Dependencies with{Members=[first with{Parts=null!}]}}).Validate(),"Null partial metadata rejects without bypassing validation");
Reject(()=>(loaded with{Dependencies=loaded.Dependencies with{Members=[first with{Parts=Enumerable.Repeat(first.Parts[0],256).ToArray()}]}}).Validate(),"The declaration budget counts all partial sections rather than only top-level helper tokens");
Reject(()=>ChangedPart(first.Parts[0] with{Declaration="partial class Large { /*"+new string('x',2_000_000)+"*/ }"}).Validate(),"The source-size budget includes additional declaration bodies");
var destinationProject=project with{Source=target,Identities=[],AdditionalSources=[]};var destination=new BehaviorDocument(target,adapter,sourceFile:file);var root=destination.Entities.Single().Root;var surviving=destination.Identities.ToArray();
var imported=FragmentDependencies.ImportWithDetails(adapter,destinationProject,destination,0,root.Id,0,loaded);
Check(destination.Revision==1 && imported.Selection.Count==1 && imported.Declarations.Contains("UnusedGetter") && imported.Declarations.Contains("Count()"),"Import preview includes the complete destination partial definitions in one accepted revision");
Check(!destination.Source.Contains("StudioPortable.FanKit.Build()") && destination.Source.Contains("partial class Fragment_Settings<T>"),"Generic type and constructor references consistently use one renamed identity across every section");
Check(destination.Source.Contains("~Fragment_Settings()"),"Generic partial finalizers keep their declaring type identity after import");
Check(surviving.All(i=>destination.Identities.Any(n=>n.Id==i.Id)),"Partial dependency insertion preserves existing destination identities");
Check(imported.Declarations.Contains("using static global::StudioPortable.Fragment_FanKit") && imported.Declarations.Contains("using SettingsAlias = global::StudioPortable.Fragment_Settings<int>"),"Alias and static imports rebind captured partial types to their actual destination names");
var accepted=destination.Source;var acceptedIds=destination.Identities;
Check(destination.Undo() && destination.Source==target && destination.Redo() && destination.Source==accepted,"Undo and Redo treat all declarations and imported behaviors as one source edit");
await using(var baseline=await CompiledRuntime.StartAsync(adapter.ServerRoot,project))
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,destinationProject with{Source=accepted,Identities=acceptedIds}))
{
    native.Recording=new(native);await baseline.AdvanceAsync(new(),500);await native.AdvanceAsync(new(),500);
    Check(baseline.Shots.Count>0 && baseline.Shots.Select(s=>(s.Ms,s.Count,s.Angle,s.AngleIncrement,s.Index)).SequenceEqual(native.Shots.Select(s=>(s.Ms,s.Count,s.Angle,s.AngleIncrement,s.Index))),"Original and imported multi-file partial helpers retain complete native packet order, initialization order and shared cooldown state");
    Check(native.Shots.Any(s=>s.Count==6),"The generic partial constructor and paired partial method execute their original native helper values");
    var runProject=destinationProject with{Source=accepted,Identities=acceptedIds};var replay=EncounterReplay.Capture(runProject,null,EncounterReplay.Identify(adapter,runProject,native.Stamp.BuildId),native);
    var restored=await ReplayCursor.RestoreAsync(adapter,replay,500);using var restoredSession=restored.Session;
    Check(restored.Session.Shots.SequenceEqual(native.Shots),"Portable partial initialization and shared cooldown state survive native replay");
}
FragmentDependencies.Import(adapter,destinationProject with{Source=destination.Source,Identities=destination.Identities},destination,destination.Revision,root.Id,1,loaded);
Check(destination.Source.Contains("Fragment_FanKit2") && destination.Source.Contains("Fragment_Settings2"),"Repeated partial imports use fresh type identities across every declaration part");
var locked=new BehaviorDocument(target,adapter,sourceFile:file);locked.LockedNodes.Add(locked.Entities.Single().Root.Id);
Reject(()=>FragmentDependencies.Import(adapter,destinationProject,locked,0,locked.Entities.Single().Root.Id,0,loaded),"Destination locks reject all partial source additions atomically");Check(locked.Source==target,"Rejected partial imports leave accepted source unchanged");
const string methods = "using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private static partial Behavior PartialFan(int count);private static partial Behavior PartialFan(double count);private _ StudioPartialMethod=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"origin\",PartialFan(6),PartialFan(7d)));}}";
const string implementations = "namespace TKR.WorldServer.logic{public partial class BehaviorDb{const int PartialExtra=1;private static partial Behavior PartialFan(int count)=>new TKR.WorldServer.logic.behaviors.Shoot(20,count:count+PartialExtra);private static partial Behavior PartialFan(double count)=>new TKR.WorldServer.logic.behaviors.Shoot(20,count:(int)count+2);}}";
var methodProject=project with{Source=methods,Identities=[],AdditionalSources=[new(otherFile,otherOriginal+"\n"+implementations)]};var methodAdapter=adapter.ForProject(methodProject);var methodDoc=new BehaviorDocument(methods,methodAdapter,sourceFile:file);methodProject=methodProject with{Identities=methodDoc.Identities};var calls=methodDoc.Entities.Single().Root.Children.Select(n=>n.Id).ToArray();
var methodFragment=FragmentDependencies.Capture(adapter,methodProject,methodDoc,calls,methodDoc.CaptureFragment(calls,"Partial method overloads"));
Check(methodFragment.Version==3 && methodFragment.Dependencies!.Members.Count(m=>m.Parts.Count==1)==2,"Compiler-bound partial method overloads capture both signature and implementation under distinct tokens");
var methodTargetSource=target.Replace("children:[]", "children:[]");var methodTarget=new BehaviorDocument(methodTargetSource,adapter,sourceFile:file);
var methodInsertion=FragmentDependencies.ImportWithDetails(adapter,destinationProject,methodTarget,0,methodTarget.Entities.Single().Root.Id,0,methodFragment);
Check(methodInsertion.Declarations.Contains("Fragment_PartialFan2") && methodInsertion.Declarations.Contains("Fragment_PartialExtra"),"Both partial signatures and implementation dependencies receive noncolliding destination names");
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,destinationProject with{Source=methodTarget.Source,Identities=methodTarget.Identities}))
{await native.AdvanceAsync(new(),100);Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{7,9}),"Overloaded imported partial methods dispatch the actual native implementation selected by original argument types");}
const string phaseSource="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private static partial State PartialPhases();private _ StudioPartialPhases=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"outer\",PartialPhases(),new State(\"finish\")));}}";
const string phaseImplementation="namespace TKR.WorldServer.logic{public partial class BehaviorDb{private static partial State PartialPhases()=>new State(\"loop\",new TKR.WorldServer.logic.transitions.TimedTransition(100,\"finish\"));}}";
var phaseProject=project with{Source=phaseSource,Identities=[],AdditionalSources=[new(otherFile,otherOriginal+"\n"+phaseImplementation)]};var phaseAdapter=adapter.ForProject(phaseProject);var phaseDoc=new BehaviorDocument(phaseSource,phaseAdapter,sourceFile:file);phaseProject=phaseProject with{Identities=phaseDoc.Identities};var phaseCall=phaseDoc.Entities.Single().Root.Children.Single(n=>n.Kind=="Factory");
var phaseFragment=FragmentDependencies.Capture(adapter,phaseProject,phaseDoc,[phaseCall.Id],phaseDoc.CaptureFragment([phaseCall.Id],"Partial phases"));
Check(phaseFragment.Dependencies!.ExternalPhases.SequenceEqual(new[]{"finish"}),"Phase reference discovery visits the implementation section of a partial method");
var phaseTargetSource=target.Replace("new State(\"destination\",children:[])","new State(\"root\",new State(\"destination\"),new State(\"loop-copy\"),new State(\"done\"))");var phaseTarget=new BehaviorDocument(phaseTargetSource,adapter,sourceFile:file);var phaseParent=phaseTarget.Entities.Single().Root.Children.Single(n=>n.Label=="destination");
FragmentDependencies.Import(adapter,destinationProject with{Source=phaseTargetSource},phaseTarget,0,phaseParent.Id,0,phaseFragment,phaseTargets:new Dictionary<string,string>{{"finish","done"}});
Check(phaseTarget.Source.Contains("\"loop-copy-2\"") && !phaseTarget.Source.Contains("\"finish\""),"Partial implementations reconcile native phase-name collisions and external destinations together");
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,destinationProject with{Source=phaseTarget.Source,Identities=phaseTarget.Identities}))
{await native.AdvanceAsync(new(),400);Check(native.Entities.Single().Phase=="done","An imported partial State factory executes its renamed phase and mapped native transition");}
Check(File.ReadAllText(file)==original && File.ReadAllText(otherFile)==otherOriginal,"Partial capture and native compilation preserve both supplied source files");
Console.WriteLine($"{checks} partial fragment checks passed.");
