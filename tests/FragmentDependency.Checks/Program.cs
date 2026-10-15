using System.Text.Json;
using Studio.Core;
using Studio.Raple;
var adapter = new RapleAdapter(Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server")); var checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
const string entity = "Cave Pirate Brawler"; var file = adapter.Catalog().Single(e => e.Id == entity).SourceFile; var original = File.ReadAllText(file);
const string source = """
using FanShot=TKR.WorldServer.logic.behaviors.Shoot;
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    const int StudioFragmentShots=3;
    static readonly FanShot StudioFragmentShared=new(20,count:StudioFragmentShots,coolDown:100);
    static Behavior StudioFragmentFactory(int count)=>new Timed(1000,StudioFragmentShared,new FanShot(20,count:count,coolDown:100));
    private _ StudioPortableOrigin=()=>Behav().Init("Cave Pirate Brawler",new State("origin",StudioFragmentFactory(4)));
} }
""";
const string destinationSource = "using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioPortableDestination=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"destination\"));}}";
var document = new BehaviorDocument(source, adapter, sourceFile: file); var factory = document.AllNodes.Single(n => n.Kind == "Factory");
var project = new StudioProject { SourceFile = file, Source = source, EntityId = entity, Identities = document.Identities, Player = new(Invulnerable: true), Seed = 7 };
var raw = document.CaptureFragment([factory.Id], "Portable fan", bindings: [new(factory.Id,"count","shots")]);
var fragment = FragmentDependencies.Capture(adapter, project, document, [factory.Id], raw);
Check(fragment.Version==2&&fragment.Dependencies!.Members.Count==3&&fragment.Dependencies.Members.Select(m=>m.Name).Order().SequenceEqual(new[]{"StudioFragmentFactory","StudioFragmentShared","StudioFragmentShots"}),"Capture bundles the complete method, shared field and scalar constant closure");
Check(fragment.Parameters.Single().Value.DefaultExpression=="4"&&fragment.Contents.Expressions.Single().Contains(fragment.Dependencies.Members.Single(m=>m.Name=="StudioFragmentFactory").Token),"Semantic helper renaming preserves template value spans and exact defaults");
Check(fragment.Dependencies.Members.All(m=>m.SourceSha256==StudioSourceDraft.Hash(source))&&fragment.Dependencies.Members.Any(m=>m.Declaration.Contains("global::TKR.WorldServer.logic.behaviors.Shoot")),"Portable declarations retain source fingerprints and bind constructor aliases to their original type");
var output=Path.GetFullPath(".artifacts/fragment-dependencies");Directory.CreateDirectory(output);var saved=Path.Combine(output,"portable.rbsfrag");fragment.Save(saved);fragment=BehaviorFragment.Load(saved);
Check(fragment.Dependencies.Members.Count==3&&fragment.Parameters.Single().Name=="shots","Versioned fragment JSON preserves source declarations and template metadata");
var destination=new BehaviorDocument(destinationSource,adapter,sourceFile:file);var root=destination.Entities.Single().Root;var beforeIds=destination.Identities.ToArray();var destinationProject=project with{Source=destinationSource,Identities=destination.Identities};
var ids=FragmentDependencies.Import(adapter,destinationProject,destination,0,root.Id,0,fragment,new Dictionary<string,string>{{"shots","6"}});
Check(destination.Revision==1&&ids.Count==1&&destination.Find(ids[0]).Kind=="Factory"&&destination.Source.Contains("Fragment_StudioFragmentFactory"),"Dependency import appends private declarations and the actual helper call in one revision");
Check(beforeIds.All(id=>destination.Identities.Any(n=>n.Id==id.Id))&&SourceVerification.Check(adapter.ServerRoot,file,destination.Source).Accepted,"Imports preserve destination identities and pass the complete original compiler");
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,destinationProject with{Source=destination.Source,Identities=destination.Identities}))
{await native.AdvanceAsync(new(),100);Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{3,6}),"Imported helper closure and bound template count execute as original native attacks");}
var applied=destination.Source;Check(destination.Undo()&&destination.Source==destinationSource&&destination.Redo()&&destination.Source==applied,"One Undo/Redo restores both declarations and inserted native source");
FragmentDependencies.Import(adapter,destinationProject with{Source=destination.Source,Identities=destination.Identities},destination,destination.Revision,root.Id,1,fragment);
Check(destination.Source.Contains("Fragment_StudioFragmentFactory2")&&destination.Source.Contains("Fragment_StudioFragmentShared2"),"Repeated imports allocate independent collision-free helper and shared-value names");
var stable=destination.Source;var revision=destination.Revision;
Reject(()=>FragmentDependencies.Import(adapter,destinationProject,destination,revision-1,root.Id,0,fragment),"Stale dependency imports reject before modifying source");
destination.LockedNodes.Add(root.Id);Reject(()=>FragmentDependencies.Import(adapter,destinationProject,destination,revision,root.Id,0,fragment),"Locked destination regions reject declarations and behavior insertion together");destination.LockedNodes.Clear();
Reject(()=>FragmentDependencies.Import(adapter,destinationProject,destination,revision,root.Id,0,fragment,new Dictionary<string,string>{{"shots","\"bad\""}}),"Invalid bound template types reject in the destination compiler");
Check(destination.Source==stable&&destination.Revision==revision,"Rejected dependency imports preserve source, revision and history");
const string phaseSource="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic {public partial class BehaviorDb {
    static State StudioPortablePhase()=>new State("loop",new Shoot(20,count:5,coolDown:100),new TimedTransition(200,"finish"));
    private _ StudioPhaseOrigin=()=>Behav().Init("Cave Pirate Brawler",new State(StudioPortablePhase(),new State("finish")));
}}
""";
var phaseDoc=new BehaviorDocument(phaseSource,adapter,sourceFile:file);var phaseFactory=phaseDoc.Entities.Single().Root.Children.Single(n=>n.Kind=="Factory");var phaseProject=project with{Source=phaseSource,Identities=phaseDoc.Identities};
var phaseFragment=FragmentDependencies.Capture(adapter,phaseProject,phaseDoc,[phaseFactory.Id],phaseDoc.CaptureFragment([phaseFactory.Id],"Portable phase"));
Check(phaseFragment.Dependencies!.ExternalPhases.SequenceEqual(new[]{"finish"})&&!phaseFragment.Dependencies.UnresolvedPhases,"Declared helper phases expose their actual external native transition destinations");
const string phaseDestinationSource="namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioPhaseDestination=()=>Behav().Init(\"Cave Pirate Brawler\",new State(new State(\"destination\"),new State(\"done\"),new State(\"loop-copy\")));}}";
var phaseDestination=new BehaviorDocument(phaseDestinationSource,adapter,sourceFile:file);var phaseTarget=phaseDestination.Entities.Single().Root.StateChildren.Single(n=>n.Label=="destination");var phaseDestinationProject=project with{Source=phaseDestinationSource,Identities=phaseDestination.Identities};
Reject(()=>FragmentDependencies.Import(adapter,phaseDestinationProject,phaseDestination,0,phaseTarget.Id,0,phaseFragment),"Unmapped helper-phase dependencies reject atomically");
var phaseResult=FragmentDependencies.ImportWithDetails(adapter,phaseDestinationProject,phaseDestination,0,phaseTarget.Id,0,phaseFragment,phaseTargets:new Dictionary<string,string>{{"finish","done"}});
Check(phaseResult.Declarations.Contains("loop-copy-2")&&phaseResult.Declarations.Contains("\"done\"")&&phaseDestination.Revision==1,"Helper phase import avoids destination names and rewrites the real native external transition");
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,phaseDestinationProject with{Source=phaseDestination.Source,Identities=phaseDestination.Identities},phase:"destination"))
{await native.AdvanceAsync(new(),400);Check(native.Shots.Any(s=>s.Count==5)&&native.Entities.Single().Phase=="done","Imported helper phases execute native attacks and the mapped transition in compiled Play");}
const string customSource="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic {public partial class BehaviorDb {
    sealed class StudioPortableAction:Behavior {
        readonly Behavior action;
        public StudioPortableAction(int count)=>action=new Shoot(20,count:count,coolDown:100);
        protected override void OnStateEntry(TKR.WorldServer.core.objects.Entity host,TKR.WorldServer.core.worlds.TickTime time,ref object state)=>action.OnStateEntry(host,time);
        protected override void TickCore(TKR.WorldServer.core.objects.Entity host,TKR.WorldServer.core.worlds.TickTime time,ref object state)=>action.Tick(host,time);
    }
    static Behavior StudioGenericFactory<T>(int count) where T:class =>new StudioPortableAction(count);
    private _ StudioCustomOrigin=()=>Behav().Init("Cave Pirate Brawler",new State("origin",StudioGenericFactory<object>(7)));
}}
""";
var customDoc=new BehaviorDocument(customSource,adapter,sourceFile:file);var customFactory=customDoc.Entities.Single().Root.Children.Single(n=>n.Kind=="Factory");var customProject=project with{Source=customSource,Identities=customDoc.Identities};
var customFragment=FragmentDependencies.Capture(adapter,customProject,customDoc,[customFactory.Id],customDoc.CaptureFragment([customFactory.Id],"Custom generic action"));
Check(customFragment.Dependencies!.Members.Count==2&&customFragment.Dependencies.Members.Any(m=>m.Declaration.Contains("where T : class")||m.Declaration.Contains("where T:class")),"Generic helper constraints and complete nested custom behavior types are bundled without executing them");
var customDestination=new BehaviorDocument(destinationSource,adapter,sourceFile:file);FragmentDependencies.Import(adapter,destinationProject,customDestination,0,customDestination.Entities.Single().Root.Id,0,customFragment);
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,destinationProject with{Source=customDestination.Source,Identities=customDestination.Identities}))
{await native.AdvanceAsync(new(),100);Check(native.Shots.Single().Count==7,"Imported generic helper and renamed custom constructor preserve native child dispatch");}
var foreignFile=adapter.Catalog().First(e=>e.SourceFile!=file).SourceFile;var foreignOriginal=File.ReadAllText(foreignFile);
const string foreignHelpers="namespace PortableAux {public static class ExternalFan {public static global::TKR.WorldServer.logic.Behavior Create(int count)=>new global::TKR.WorldServer.logic.behaviors.Shoot(20,count:count,coolDown:100);}public static class PortableExtensions {public static global::TKR.WorldServer.logic.Behavior AsAttack(this int count)=>ExternalFan.Create(count);}}";
const string multiSource="using PortableAux;using PortableAlias=PortableAux.ExternalFan;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioCrossFileFragment=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"origin\",PortableAlias.Create(9),(8).AsAttack()));}}";
var multiProject=project with{Source=multiSource,AdditionalSources=[new(foreignFile,foreignOriginal+"\n"+foreignHelpers)]};var multiAdapter=adapter.ForProject(multiProject);var multiDoc=new BehaviorDocument(multiSource,multiAdapter,sourceFile:file);var multiNodes=multiDoc.Entities.Single().Root.Children.Select(n=>n.Id).ToArray();
var multiFragment=FragmentDependencies.Capture(multiAdapter,multiProject,multiDoc,multiNodes,multiDoc.CaptureFragment(multiNodes,"Cross-file helpers"));
Check(multiFragment.Dependencies!.Members.Count==2&&multiFragment.Dependencies.Members.All(m=>m.Namespace=="PortableAux"&&m.Host==null),"Dependency closure carries cross-file helper types with their original namespace ownership");
Check(!multiFragment.Contents.Expressions.Last().Contains(".AsAttack()")&&multiFragment.Contents.Expressions.Last().Contains("global::PortableAux"),"Reduced extension calls retain their actual static implementation without relying on destination imports");
var multiDestination=new BehaviorDocument(destinationSource,adapter,sourceFile:file);FragmentDependencies.Import(adapter,destinationProject,multiDestination,0,multiDestination.Entities.Single().Root.Id,0,multiFragment);
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,destinationProject with{Source=multiDestination.Source,Identities=multiDestination.Identities}))
{await native.AdvanceAsync(new(),100);Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{9,8}),"Copied cross-file helpers and extension receivers execute natively without their original project overlay");}
Check(File.ReadAllText(foreignFile)==foreignOriginal,"Cross-file packaging leaves the original declaring file unchanged");
const string getterSource="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{static int PortableCount=>throw new System.Exception(\"Do not run this getter while authoring.\");static Behavior PortableGetterFan(int count)=>new Shoot(20,count:count);private _ StudioGetterFragment=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"origin\",PortableGetterFan(PortableCount)));}}";
var getterDoc=new BehaviorDocument(getterSource,adapter,sourceFile:file);var getterFactory=getterDoc.Entities.Single().Root.Children.Single();var getterProject=project with{Source=getterSource,Identities=getterDoc.Identities};
var getterFragment=FragmentDependencies.Capture(adapter,getterProject,getterDoc,[getterFactory.Id],getterDoc.CaptureFragment([getterFactory.Id],"Deferred getter",bindings:[new(getterFactory.Id,"count","shots")]));
var getterDestination=new BehaviorDocument(destinationSource,adapter,sourceFile:file);FragmentDependencies.Import(adapter,destinationProject,getterDestination,0,getterDestination.Entities.Single().Root.Id,0,getterFragment,new Dictionary<string,string>{{"shots",getterFragment.Parameters.Single().DisplayExpression!}});
Check(getterFragment.Parameters.Single().DisplayExpression=="PortableCount"&&getterDestination.Source.Contains("throw new global::System.Exception"),"Original getter defaults stay readable and their source is copied without executing them during import");
var sharedSource=source.Replace("StudioFragmentFactory(4))","StudioFragmentFactory(4),StudioFragmentFactory(5))");var sharedDoc=new BehaviorDocument(sharedSource,adapter,sourceFile:file);var sharedFactories=sharedDoc.Entities.Single().Root.Children.Select(n=>n.Id).ToArray();var sharedProject=project with{Source=sharedSource,Identities=sharedDoc.Identities};
var sharedFragment=FragmentDependencies.Capture(adapter,sharedProject,sharedDoc,sharedFactories,sharedDoc.CaptureFragment(sharedFactories,"Shared identity"));var sharedDestination=new BehaviorDocument(destinationSource,adapter,sourceFile:file);FragmentDependencies.Import(adapter,destinationProject,sharedDestination,0,sharedDestination.Entities.Single().Root.Id,0,sharedFragment);
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,destinationProject with{Source=sharedDestination.Source,Identities=sharedDestination.Identities}))
{await native.AdvanceAsync(new(),100);Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{3,4,5}),"Multiple imported calls reuse one actual shared Shoot object and its native cooldown storage");}
var orderSource=phaseSource.Replace("new Shoot(20,count:5,coolDown:100)","new Shoot(20,count:5,coolDown:100),new OrderOnDeath(8,\"Cave Pirate Brawler\",\"loop\")");var orderDoc=new BehaviorDocument(orderSource,adapter,sourceFile:file);var orderFactory=orderDoc.Entities.Single().Root.Children.Single(n=>n.Kind=="Factory");
var orderFragment=FragmentDependencies.Capture(adapter,project with{Source=orderSource,Identities=orderDoc.Identities},orderDoc,[orderFactory.Id],orderDoc.CaptureFragment([orderFactory.Id],"Native self order"));
Check(orderFragment.Dependencies!.SelfOrders,"Native self orders inside included declarations remain visible import options");
var ghostSource=phaseDestinationSource.Replace("Cave Pirate Brawler","Ghost Ship");var ghostDoc=new BehaviorDocument(ghostSource,adapter,sourceFile:file);var ghostTarget=ghostDoc.Entities.Single().Root.StateChildren.Single(n=>n.Label=="destination");var ghostProject=destinationProject with{EntityId="Ghost Ship",Source=ghostSource,Identities=ghostDoc.Identities};
var ghostImport=FragmentDependencies.ImportWithDetails(adapter,ghostProject,ghostDoc,0,ghostTarget.Id,0,orderFragment,phaseTargets:new Dictionary<string,string>{{"finish","done"}});
Check(ghostImport.Declarations.Contains("\"Ghost Ship\"")&&!ghostImport.Declarations.Contains("\"Cave Pirate Brawler\"")&&ghostImport.Declarations.Contains("loop-copy-2"),"Cross-entity imports retarget actual self-order actors and their corresponding cloned phases");
var retainedGhost=new BehaviorDocument(ghostSource,adapter,sourceFile:file);var retainedImport=FragmentDependencies.ImportWithDetails(adapter,ghostProject,retainedGhost,0,retainedGhost.Entities.Single().Root.StateChildren.Single(n=>n.Label=="destination").Id,0,orderFragment,phaseTargets:new Dictionary<string,string>{{"finish","done"}},retargetSelfOrders:false);
Check(retainedImport.Declarations.Contains("\"Cave Pirate Brawler\",\"loop\"")||retainedImport.Declarations.Contains("\"Cave Pirate Brawler\", \"loop\""),"Keeping original self orders preserves the foreign actor and original target phase together");
var draftProject=destinationProject with{SourceDrafts=[new(file,destinationSource+"\n// draft",StudioSourceDraft.Hash(destinationSource))]};var draftDoc=new BehaviorDocument(destinationSource,adapter,sourceFile:file);
Reject(()=>FragmentDependencies.Import(adapter,draftProject,draftDoc,0,draftDoc.Entities.Single().Root.Id,0,fragment),"Pending destination drafts prevent dependency imports from overwriting source intent");
Reject(()=>(fragment with{Version=1}).Validate(),"Legacy fragments cannot silently carry unrecognized dependency declarations");
Reject(()=>(fragment with{Dependencies=fragment.Dependencies! with{Members=[fragment.Dependencies!.Members[0] with{Namespace="Bad {"}]}}).Validate(),"Malformed dependency namespaces reject before source import");
Reject(()=>(fragment with{Dependencies=fragment.Dependencies! with{Members=[fragment.Dependencies!.Members[0] with{Imports="class Extra {}"}]}}).Validate(),"Dependency using scopes cannot introduce unrelated top-level declarations");
var localSource=source.Replace("StudioFragmentFactory(4)","StudioFragmentFactory(localCount)").Replace("()=>Behav()","()=>{int localCount=4;return Behav()").Replace("Factory(localCount)));","Factory(localCount)));};");
var localDoc=new BehaviorDocument(localSource,adapter,sourceFile:file);var localFactory=localDoc.Entities.Single().Root.Children.Single();
Reject(()=>FragmentDependencies.Capture(adapter,project with{Source=localSource,Identities=localDoc.Identities},localDoc,[localFactory.Id],localDoc.CaptureFragment([localFactory.Id],"Local capture")),"Lexical local captures retain an explicit ownership boundary rather than becoming independent fields");
var externalOrderSource=orderSource.Replace("new TimedTransition(200,\"finish\")", "new TimedTransition(200,\"loop\")").Replace("OrderOnDeath(8,\"Cave Pirate Brawler\",\"loop\")", "OrderOnDeath(8,\"Cave Pirate Brawler\",\"finish\")");
var externalOrderDoc=new BehaviorDocument(externalOrderSource,adapter,sourceFile:file);var externalOrderFactory=externalOrderDoc.Entities.Single().Root.Children.Single(n=>n.Kind=="Factory");var externalOrderFragment=FragmentDependencies.Capture(adapter,project with{Source=externalOrderSource,Identities=externalOrderDoc.Identities},externalOrderDoc,[externalOrderFactory.Id],externalOrderDoc.CaptureFragment([externalOrderFactory.Id],"External self order"));
Check(externalOrderFragment.KeepOriginalPhase("finish","Ghost Ship",false)&&!externalOrderFragment.KeepOriginalPhase("finish","Ghost Ship",true),"An external phase used only by retained original orders does not require a destination mapping");
var externalGhost=new BehaviorDocument(ghostSource,adapter,sourceFile:file);var externalKept=FragmentDependencies.ImportWithDetails(adapter,ghostProject,externalGhost,0,externalGhost.Entities.Single().Root.StateChildren.Single(n=>n.Label=="destination").Id,0,externalOrderFragment,retargetSelfOrders:false);
Check(externalKept.Declarations.Contains("\"finish\"")&&externalKept.Declarations.Contains("\"Cave Pirate Brawler\""),"Kept original order targets remain untouched without an irrelevant destination phase mapping");
var fieldDefinition=document.Definitions.Single(d=>d.Definition!.Label=="StudioFragmentFactory");var inside=fieldDefinition.Root.Children.Last();var definitionRaw=document.CaptureFragment([inside.Id],"Definition child",ownerEntity:entity);
Check(definitionRaw.OriginEntity==entity,"Fragments captured inside shared source definitions retain their actual selected caller identity");
var spreadSource="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{static IStateChildren[] PortableArray()=>[new Shoot(20,count:10),new Shoot(20,count:11)];private _ StudioSpreadFragment=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"origin\",children:[..PortableArray()]));}}";
var spreadDoc=new BehaviorDocument(spreadSource,adapter,sourceFile:file);var spread=spreadDoc.Entities.Single().Root.Descendants().Single(n=>n.Kind=="Spread");var spreadFragment=FragmentDependencies.Capture(adapter,project with{Source=spreadSource,Identities=spreadDoc.Identities},spreadDoc,[spread.Id],spreadDoc.CaptureFragment([spread.Id],"Portable spread"));
var spreadTargetSource=destinationSource.Replace("new State(\"destination\")","new State(\"destination\",children:[])");var spreadTarget=new BehaviorDocument(spreadTargetSource,adapter,sourceFile:file);var spreadProject=destinationProject with{Source=spreadTargetSource,Identities=spreadTarget.Identities};
FragmentDependencies.Import(adapter,spreadProject,spreadTarget,0,spreadTarget.Entities.Single().Root.Id,0,spreadFragment);
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,spreadProject with{Source=spreadTarget.Source,Identities=spreadTarget.Identities}))
{await native.AdvanceAsync(new(),100);Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{10,11}),"A portable generated child array retains flat native spread dispatch and packet order");}
const string contextSource="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{static Behavior PortableContext()=>new Shoot(20,count:InitDb==null?1:2);private _ StudioContextFragment=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"origin\",PortableContext()));}}";
var contextDoc=new BehaviorDocument(contextSource,adapter,sourceFile:file);var contextFactory=contextDoc.Entities.Single().Root.Children.Single();var contextFragment=FragmentDependencies.Capture(adapter,project with{Source=contextSource,Identities=contextDoc.Identities},contextDoc,[contextFactory.Id],contextDoc.CaptureFragment([contextFactory.Id],"Native context"));
Check(contextFragment.Dependencies!.Members.Count==1&&contextFragment.Dependencies.Members.All(m=>m.Name!="InitDb"),"Native game initialization globals remain external rather than becoming uninitialized cloned fields");
var contextDestination=new BehaviorDocument(destinationSource,adapter,sourceFile:file);FragmentDependencies.Import(adapter,destinationProject,contextDestination,0,contextDestination.Entities.Single().Root.Id,0,contextFragment);
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,destinationProject with{Source=contextDestination.Source,Identities=contextDestination.Identities}))
{await native.AdvanceAsync(new(),100);Check(native.Shots.Single().Count==2,"Copied helper code reads the original game's actual initialization context");}
var foreignPhaseSource=phaseDestinationSource.Replace("new State(\"done\"),","").Replace("new State(\"loop-copy\")));", "new State(\"loop-copy\"))).Init(\"Ghost Ship\",new State(\"done\"));");
var foreignPhaseDoc=new BehaviorDocument(foreignPhaseSource,adapter,sourceFile:file);var foreignTarget=foreignPhaseDoc.Entities.Single(e=>e.EntityId==entity).Root.StateChildren.Single(n=>n.Label=="destination");
Reject(()=>FragmentDependencies.Import(adapter,destinationProject with{Source=foreignPhaseSource,Identities=foreignPhaseDoc.Identities},foreignPhaseDoc,0,foreignTarget.Id,0,phaseFragment,phaseTargets:new Dictionary<string,string>{{"finish","done"}}),"Another actor's phase cannot satisfy this destination's helper transition mapping");
Check(File.ReadAllText(file)==original,"Portable dependency capture and native import preserve supplied game source");
Console.WriteLine($"{checks} fragment dependency checks passed.");
