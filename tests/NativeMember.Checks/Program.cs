using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string entity="Cave Pirate Brawler";var file=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(file);
const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioMemberFixture=()=>Behav().Init("Cave Pirate Brawler",new State("loop",new StudioMemberInputs {
        Primary = /* scalar note */new Shoot(20,count:2,fixedAngle:0,coolDown:100),
        Secondary = new Shoot(20,count:3,fixedAngle:0,coolDown:100),
        Actions = { new Shoot(20,count:4,fixedAngle:0,coolDown:100),/* list note */new Shoot(20,count:5,fixedAngle:0,coolDown:100) },
        Array = new CycleBehavior[]{new Shoot(20,count:6,fixedAngle:0,coolDown:100)},
        Assigned = new System.Collections.Generic.List<CycleBehavior>(3)
    }));
    sealed class StudioMemberInputs : CycleBehavior {
        protected override void TickCore(TKR.WorldServer.core.objects.Entity host,TKR.WorldServer.core.worlds.TickTime time,ref object state){foreach(var b in All())b.Tick(host,time);}
        Behavior primary;
        public CycleBehavior Primary {set=>primary=new Prioritize(value,new Shoot(20,count:9,fixedAngle:0,coolDown:100));}
        public CycleBehavior Secondary;
        public System.Collections.Generic.List<CycleBehavior> Actions {get;}=new(){new Shoot(20,count:8,fixedAngle:0,coolDown:100)};
        public CycleBehavior[] Array;
        public System.Collections.Generic.List<CycleBehavior> Assigned;
        public System.Collections.Generic.IEnumerable<Behavior> All(){if(primary!=null)yield return primary;if(Secondary!=null)yield return Secondary;foreach(var b in Actions)yield return b;foreach(var b in Array)yield return b;foreach(var b in Assigned)yield return b;}
    }
} }
""";
var doc=new BehaviorDocument(source,adapter,sourceFile:file);
BehaviorNode Slot(string name)=>doc.AllNodes.Single(n=>n.Argument is{IsMember:true}&&n.Argument.Name==name);
var primary=Slot("Primary");var secondary=Slot("Secondary");var actions=Slot("Actions");var array=Slot("Array");var assigned=Slot("Assigned");var ids=doc.Identities;
Check(doc.CanEditChildren(primary.Id)&&doc.IsScalarChildSlot(primary.Id)&&doc.IsScalarChildSlot(secondary.Id),"Scalar native property and field values expose one typed child slot");
Check(doc.CanEditChildren(actions.Id)&&doc.CanEditChildren(array.Id)&&doc.CanEditChildren(assigned.Id)&&assigned.Children.Count==0,"Getter-filled collections, assigned arrays and absent collection braces retain distinct editable ownership");
Check(doc.AcceptsChild(primary.Id,"new Shoot(20)",true)&&!doc.AcceptsChild(primary.Id,"new TKR.WorldServer.logic.transitions.TimedTransition(100,\"loop\")",true),"Scalar member preflight uses actual native member type");
var replaced=doc.ReplaceChild(primary.Id,"new Shoot(20,count:7,fixedAngle:0,coolDown:100)",d=>adapter.Validate(d,entity));
Check(doc.Find(replaced).Kind=="Shoot"&&doc.Source.Contains("/* scalar note */")&&Slot("Secondary").Id==secondary.Id,"Scalar replacement retains assignment syntax, comments and neighboring identities");
Reject(()=>doc.AddChild(primary.Id,"new Shoot(20)"),"A filled scalar member rejects a second child");
var accepted=doc.Source;Reject(()=>doc.ReplaceChild(primary.Id,"new TKR.WorldServer.logic.transitions.TimedTransition(100,\"loop\")"),"Incompatible native member values reject atomically");Check(doc.Source==accepted,"Rejected member conversion preserves accepted source");
doc.DeleteNode(secondary.Children.Single().Id,d=>adapter.Validate(d,entity));Check(Slot("Secondary").Children.Count==0&&doc.Source.Contains("Secondary = null"),"Deleting a scalar field preserves explicit null assignment semantics");
doc.AddChild(secondary.Id,"new Shoot(20,count:10,fixedAngle:0,coolDown:100)",d=>adapter.Validate(d,entity));Check(Slot("Secondary").Children.Single().Id!=secondary.Children.Single().Id,"Empty scalar member refills with a fresh native child");
doc.MoveNode(actions.Children[1].Id,-1,d=>adapter.Validate(d,entity));Check(Slot("Actions").Children.First().Id==actions.Children[1].Id&&doc.Source.Contains("/* list note */"),"Getter-filled member reorder preserves source comments and child identity");
doc.DeleteNodes(Slot("Array").Children.Select(n=>n.Id),d=>adapter.Validate(d,entity));Check(Slot("Array").Children.Count==0&&doc.Source.Contains("new CycleBehavior[]{}"),"Assigned native array deletion retains the actual member assignment and array type");
doc.AddChild(array.Id,"new Shoot(20,count:11,fixedAngle:0,coolDown:100)",d=>adapter.Validate(d,entity));
doc.AddChild(assigned.Id,"new Shoot(20,count:12,fixedAngle:0,coolDown:100)",d=>adapter.Validate(d,entity));Check(doc.Expression(Slot("Assigned")).Contains("(3)")&&Slot("Assigned").Children.Count==1,"First collection member insertion preserves original constructor arguments");
var wrapper=doc.Parent(primary.Id)!;doc.LockedNodes.Add(wrapper.Id);Reject(()=>doc.ReplaceChild(primary.Id,"new Shoot(20)"),"Member wrapper locks protect scalar and collection child edits");doc.LockedNodes.Clear();
var clipboard=doc.CopyNodes([Slot("Actions").Children.Last().Id]);var pasted=doc.PasteNodes(assigned.Id,1,clipboard,d=>adapter.Validate(d,entity)).Single();Check(pasted!=Slot("Actions").Children.Last().Id&&Slot("Assigned").Children.Last().Id==pasted,"Member clipboard insertion preserves bindings and creates independent child identity");doc.Undo();
var project=new StudioProject{SourceFile=file,Source=doc.Source,EntityId=entity,Identities=doc.Identities,Player=new(Invulnerable:true)};
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,project)){await runtime.AdvanceAsync(new(),100);Check(runtime.Shots.Select(s=>s.Count).SequenceEqual(new[]{7,9,10,8,5,4,11,12}),"Native setter wrapping, getter constructor content, field assignments and edited lists execute in original order");}
var output=Path.GetFullPath(".artifacts/native-member");Directory.CreateDirectory(output);var path=Path.Combine(output,"members.rbs");BehaviorDocument.Save(path,project);var saved=BehaviorDocument.Load(path);var reopened=new BehaviorDocument(saved.Source,adapter,saved.Identities,file);Check(reopened.Identities.SequenceEqual(doc.Identities),"Member wrapper, scalar, field and collection identities survive save/reopen");
while(doc.Source!=source)if(!doc.Undo())throw new Exception("Missing native member history");Check(doc.Identities.SequenceEqual(ids),"Complete member history restores source and every original identity");
while(doc.Source!=project.Source)if(!doc.Redo())throw new Exception("Missing native member redo");Check(doc.Source==project.Source&&doc.Identities.SequenceEqual(project.Identities),"Redo restores the complete accepted native member transaction sequence");
var boundary=new BehaviorDocument(source.Replace("Assigned = new System.Collections.Generic.List<CycleBehavior>(3)","Assigned = StudioGeneratedMembers()" ).Replace("sealed class StudioMemberInputs", "static System.Collections.Generic.List<CycleBehavior> StudioGeneratedMembers()=>new();\n    sealed class StudioMemberInputs"),adapter,sourceFile:file);Check(boundary.AllNodes.Any(n=>n.Kind=="Opaque"&&boundary.Expression(n).StartsWith("Assigned")),"Generated member collection ownership retains its explicit source boundary");
var implicitArray=new BehaviorDocument(source.Replace("new CycleBehavior[]{new Shoot(20,count:6", "new[]{new Shoot(20,count:6"),adapter,sourceFile:file);var implicitSlot=implicitArray.AllNodes.Single(n=>n.Argument is{Name:"Array",IsMember:true});implicitArray.DeleteNodes(implicitSlot.Children.Select(n=>n.Id),d=>adapter.Validate(d,entity));Check(implicitArray.AllNodes.Single(n=>n.Id==implicitSlot.Id).Children.Count==0&&implicitArray.Source.Contains("global::TKR.WorldServer.logic.behaviors.Shoot"),"Empty implicit member arrays retain the original inferred native element type");
var targetList=new BehaviorDocument(source.Replace("Assigned = new System.Collections.Generic.List<CycleBehavior>(3)","Assigned = new(3)"),adapter,sourceFile:file);var targetSlot=targetList.AllNodes.Single(n=>n.Argument is{Name:"Assigned",IsMember:true});targetList.AddChild(targetSlot.Id,"new Shoot(20)",d=>adapter.Validate(d,entity));Check(targetList.Expression(targetList.Find(targetSlot.Id)).StartsWith("new(3)")&&targetList.Find(targetSlot.Id).Children.Count==1,"Target-typed member collection insertion retains original constructor syntax");
var phaseMember=new BehaviorDocument(source.Replace("Secondary = new Shoot(20,count:3,fixedAngle:0,coolDown:100)","Phase = new State(\"hidden\")").Replace("public CycleBehavior Secondary;","public CycleBehavior Secondary; public State Phase;"),adapter,sourceFile:file);Check(phaseMember.AllNodes.Any(n=>n.Kind=="Opaque"&&phaseMember.Expression(n).StartsWith("Phase")),"Phase-valued members retain a source boundary until native hierarchy ownership is reconciled");
Check(File.ReadAllText(file)==supplied,"Native member edits and execution preserve supplied game source");Console.WriteLine($"{checks} native member checks passed.");
