using System.Reflection;
using Studio.Core;
using Studio.Raple;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string entity="Cave Pirate Brawler";
BehaviorDocument Doc(string body)=>new("class Fixture{void Build(){db.Init(\""+entity+"\",new State(\"healing\","+body+"));}}",adapter);
World World(EncounterSession session){var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;return(World)engine.GetType().GetField("World")!.GetValue(engine)!;}
Enemy Boss(EncounterSession session)=>World(session).Enemies.Values.First(e=>e.ObjectDesc.IdName==entity);
EncounterSession Session(string body,ArenaPreset? arena=null){var s=new EncounterSession(adapter,Doc(body),entity,7,new(Invulnerable:true),arena:arena);foreach(var enemy in World(s).Enemies.Values){enemy.MaximumHP=300;enemy.HP=100;}return s;}
foreach(var kind in new[]{"HealEntity","HealGroup"})Check(adapter.Primitives.Single(p=>p.Name==kind) is{Simulatable:true,Category:"Defense"},kind+" exposes its original constructor and defense category");
var doc=Doc("new HealEntity(8)");var node=doc.AllNodes.Single(n=>n.Kind=="HealEntity");
Check(adapter.ReadHealing(doc,node) is{Range:8,TargetKnown:true,Target:null,AmountKnown:true,Amount:null},"Omitted native name and amount retain all-enemy/full-restore semantics");
Check(adapter.EntityParameterFor(doc,node) is{Scope:EntityResourceScope.Enemy,NameFragment:true,NullTitle:"All enemy names"},"The name chooser offers actual Enemy descriptors and an explicit all-enemy null mode");
var original=doc.Source;var ids=doc.Identities;adapter.ApplyEntityReference(doc,entity,node.Id,doc.Revision,"name",entity);
Check(adapter.ReadHealing(doc,doc.Find(node.Id))!.Target==entity&&doc.Identities.Select(i=>i.Id).SequenceEqual(ids.Select(i=>i.Id)),"Native name selection keeps the action and all identities");
Check(doc.Undo()&&doc.Source==original&&doc.Redo(),"One Undo/Redo restores the complete healing target source");doc.LockedNodes.Add(node.Id);
Reject(()=>adapter.ApplyEntityReference(doc,entity,node.Id,doc.Revision,"name",null),"Healing target edits honor region locks");doc.LockedNodes.Clear();
Reject(()=>adapter.ApplyEntityReference(doc,entity,node.Id,doc.Revision-1,"name",null),"Stale healing target edits reject atomically");
var groupDoc=Doc("new HealGroup(8,\"no such group\")");var groupNode=groupDoc.AllNodes.Single(n=>n.Kind=="HealGroup");
Check(!adapter.Validate(groupDoc,entity).Any(d=>d.IsError),"An unmatched group remains a valid native no-op rather than a spawn-selection error");
adapter.ApplyGroupReference(groupDoc,entity,groupNode.Id,groupDoc.Revision,"Dwarves");
Check(adapter.ReadGroupReference(groupDoc,groupDoc.Find(groupNode.Id)) is{Known:true,Name:"Dwarves"},"Healing reuses exact native XML group authoring");
Check(adapter.Validate(Doc("new HealEntity(double.MaxValue)"),entity).Any(d=>d.IsError),"Ranges that overflow the native float reject before runtime");
Check(adapter.Validate(Doc("new HealGroup(double.NaN,\"Dwarves\")"),entity).Any(d=>d.IsError)&&adapter.Validate(Doc("new HealEntity(double.PositiveInfinity)"),entity).Any(d=>d.IsError),"Named non-finite constants cannot bypass native healing range validation");
using(var s=Session("new HealEntity(8)")){s.Step(new());Check(Boss(s).HP==300,"Null amount fully restores a matching host");Check(s.PlayerHp==1000&&s.Effects.Count(e=>e.Kind==(byte)TKR.WorldServer.networking.packets.outgoing.EffectType.Potion)==1,"Only the native enemy receives the potion effect; the player is not healed");}
using(var s=Session("new HealEntity(8,\"pIrAtE\",healAmount:35)")){Boss(s).Name="Unrelated display name";s.Step(new());Check(Boss(s).HP==135,"Case-insensitive XML name fragments include the host despite its display-name override");}
using(var s=Session("new HealEntity(8,\"missing\",healAmount:35)")){s.Step(new());Check(Boss(s).HP==100&&s.Effects.Count==0,"An unmatched name changes no HP and emits no healing effects");Check(Boss(s).StateStorage.Values.Contains(1000),"A no-target attempt still resets the normalized native cooldown");}
using(var s=Session("new HealEntity(8,healAmount:250)")){s.Step(new());Check(Boss(s).HP==300,"Signed native addition caps at maximum HP");}
using(var s=Session("new HealEntity(8,healAmount:-25)")){s.Step(new());Check(Boss(s).HP==75,"Negative amounts preserve the game's native HP subtraction");}
using(var s=Session("new HealEntity(8,healAmount:0)")){s.Step(new());Check(Boss(s).HP==100&&s.Effects.Count==0,"A zero amount retains native unchanged-HP effect suppression");}
using(var s=Session("new HealEntity(8,healAmount:10,coolDown:100)")){s.Step(new());Check(Boss(s).HP==110,"First native heal is immediately ready after entry");s.Step(new());Check(Boss(s).HP==110,"Countdown subtraction reaching zero does not heal in the same tick");s.Step(new());Check(Boss(s).HP==120,"The following ready tick performs the next heal");}
using(var s=Session("new HealEntity(8,healAmount:10,coolDown:100)")){Boss(s).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Stunned,-1));s.Step(new());Check(Boss(s).HP==100&&Boss(s).StateStorage.Values.Contains(0),"Stun holds a ready heal without consuming its cooldown");Boss(s).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Stunned,0));s.Step(new());Boss(s).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Stunned,-1));s.Step(new());Check(Boss(s).HP==110&&Boss(s).StateStorage.Values.Contains(0),"A positive healing cooldown still decreases while stunned");}
foreach(var effect in new[]{ConditionEffectIndex.Hidden,ConditionEffectIndex.Invisible,ConditionEffectIndex.Invulnerable})
using(var s=Session("new HealEntity(8,healAmount:10)")){Boss(s).ApplyConditionEffect(new ConditionEffect(effect,-1));s.Step(new());Check(Boss(s).HP==110,"Matching enemies remain healable under "+effect);}
var dwarf=NativeGroups.Catalog().Single(g=>g.Name=="Dwarves").Members.First(m=>m.RuntimeKind=="Enemy").Id;
using(var s=Session("new HealGroup(2,\"dWaRvEs\",healAmount:20)",new ArenaPreset{Actors=[new(dwarf,65.5,64.5),new(dwarf,66.5,64.5)]}))
{s.Step(new());var allies=World(s).Enemies.Values.Where(e=>e.ObjectDesc.IdName==dwarf).OrderBy(e=>e.X).ToArray();Check(allies[0].HP==120&&allies[1].HP==100&&Boss(s).HP==100,"Group healing is case-insensitive, excludes the exact radius boundary and leaves other groups alone");}
using(var s=Session("new HealGroup(8,null,healAmount:20)")){s.Step(new());Check(Boss(s).HP==100,"A null group matches no native XML group");}
foreach(var range in new[]{"0","-1"})using(var s=Session("new HealEntity("+range+",healAmount:20)")){s.Step(new());Check(Boss(s).HP==100,"Nonpositive native radius produces no eligible host: "+range);}
using(var s=Session("new HealEntity(8,healAmount:20)",new ArenaPreset{Actors=[new(dwarf,65.5,64.5)]}))
{var ally=World(s).Enemies.Values.Single(e=>e.ObjectDesc.IdName==dwarf);ally.ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Hidden,-1));s.Step(new());Check(Boss(s).HP==120&&ally.HP==120,"All-name targeting heals both the host and a distinct hidden Enemy");}
var unknownFile=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;const string unknownSource="namespace TKR.WorldServer.logic{public partial class BehaviorDb{static int? StudioHealingAmount()=>throw new System.Exception(\"getter must not run\");private _ StudioUnknownHealing=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"healing\",new behaviors.HealEntity(8,healAmount:StudioHealingAmount())));}}";var unknownAdapter=adapter.ForSource(unknownSource,unknownFile);var sourceAmount=new BehaviorDocument(unknownSource,unknownAdapter,sourceFile:unknownFile);Check(adapter.ReadHealing(sourceAmount,sourceAmount.AllNodes.Single(n=>n.Kind=="HealEntity")) is{AmountKnown:false},"Unknown amount expressions retain an explicit source boundary rather than a full-restore prediction");
var sourceFile=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(sourceFile);
const string source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioHealingFixture=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"healing\",new HealEntity(8,healAmount:20,coolDown:100),new HealGroup(8,\"dwarves\",healAmount:35,coolDown:100)));}}";
var bound=adapter.ForSource(source,sourceFile);var compiledDoc=new BehaviorDocument(source,bound,sourceFile:sourceFile);var project=new StudioProject{SourceFile=sourceFile,Source=source,EntityId=entity,Seed=7,Identities=compiledDoc.Identities,Player=new(Invulnerable:true),Arena=new ArenaPreset{Actors=[new(dwarf,65.5,64.5)]}};
await using(var plain=await CompiledRuntime.StartAsync(adapter.ServerRoot,project,captureStateOrigins:false,artifactRoot:Path.GetFullPath(".artifacts/healing/plain")))
{await plain.SetBossHpAsync(.1);await plain.AdvanceAsync(new(),300);var effects=plain.Effects.ToArray();var entities=plain.Entities.ToArray();
 await using var observed=await CompiledRuntime.StartAsync(adapter.ServerRoot,project,captureStateOrigins:true,artifactRoot:Path.GetFullPath(".artifacts/healing/observed"));observed.Recording=new(observed);await observed.SetBossHpAsync(.1);await observed.AdvanceAsync(new(),300);
 Check(entities.Select(e=>(e.Id,e.Hp,e.MaxHp,e.X,e.Y)).SequenceEqual(observed.Entities.Select(e=>(e.Id,e.Hp,e.MaxHp,e.X,e.Y)))&&effects.SequenceEqual(observed.Effects),"Compiled instrumentation preserves complete HP snapshots and native healing packets");
 Check(observed.Callbacks.Any(c=>c.Behavior.EndsWith(".HealEntity")&&c.Origin?.NodeId!=null)&&observed.Callbacks.Any(c=>c.Behavior.EndsWith(".HealGroup")&&c.Origin?.NodeId!=null),"Both original healing controllers dispatch with accepted construction provenance");
 Check(observed.Events.Where(e=>e.Kind=="effect").All(e=>e.PacketOrigin?.Source?.NodeId!=null),"Actual healing effect emissions retain their synchronous source links");
 var tape=EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project,observed.Stamp.BuildId),observed);var cursor=await ReplayCursor.RestoreAsync(adapter,tape,300);using var restored=cursor.Session;Check(restored.Entities.Select(e=>(e.Id,e.Hp,e.MaxHp)).SequenceEqual(observed.Entities.Select(e=>(e.Id,e.Hp,e.MaxHp)))&&restored.Effects.SequenceEqual(observed.Effects),"Recorded healing inputs reconstruct native HP and complete effects");
}
Check(File.ReadAllText(sourceFile)==supplied,"Healing authoring and compiled execution leave supplied game source unchanged");
Console.WriteLine($"{checks} healing checks passed.");
