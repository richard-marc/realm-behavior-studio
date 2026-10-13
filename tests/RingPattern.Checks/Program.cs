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
BehaviorDocument Doc(string body)=>new("class Fixture { void Build() { db.Init(\""+entity+"\",new State(\"ring\","+body+")); } }",adapter);
const string spiral="new GordoSpiral(0,4,20,15,100,delay:0,angle:30)";
const string gap="new GapRing(0,slots:12,missing:2,gapAngle:0,rotation:20,interval:100,delay:200,warnFirstVolley:true,alternateSides:true)";
var doc=Doc(gap);var node=doc.Entities.Single().Root.Children.Single();var pattern=adapter.ReadRingPattern(doc,node)!;
Check(new[]{"GordoSpiral","GapRing","GordoRotation"}.All(k=>adapter.Primitives.Single(p=>p.Name==k) is {Simulatable:true,Category:"Attacks"}),"All three original ring/rotation types have native constructor-backed authoring");
Check(pattern is {Count:10,Slots:12,Missing:2,Spacing:30,Angle:180,WarnFirstVolley:true}&&adapter.ProjectileParameter(doc,node)=="projectile"&&adapter.HasKnownProjectileOwner(doc,entity,node),"Gap metadata retains actual slots, omitted divisions, centered heading and native projectile ownership");
var spiralDoc=Doc(spiral);Check(adapter.ParametersFor(doc,node).Single(p=>p.Name=="interval").Unit=="ms"&&adapter.ParametersFor(spiralDoc,spiralDoc.Entities.Single().Root.Children.Single()).Single(p=>p.Name=="spacing").Unit=="degrees","Actual ring units and reflected parameters are available");
var before=doc.Source;var ids=doc.Identities;doc.Apply(new(doc.Revision,"Edit gap",[new(node.Id,"slots","16"),new(node.Id,"missing","3")]),d=>adapter.Validate(d,entity));
Check(adapter.ReadRingPattern(doc,doc.Find(node.Id)) is {Count:13,Spacing:22.5}&&ids.All(i=>doc.Identities.Any(n=>n.Id==i.Id)),"Atomic ring edits preserve actual divisions, emitted count and every source identity");
Check(doc.Undo()&&doc.Source==before&&doc.Redo()&&doc.Find(node.Id).Id==node.Id,"Ring source history restores paired edits and stable identities");doc.Undo();doc.LockedNodes.Add(node.Id);
Reject(()=>doc.Apply(new(doc.Revision,"Locked ring",[new(node.Id,"missing","3")]),d=>adapter.Validate(d,entity)),"A ring region lock protects pattern and timing parameters");doc.LockedNodes.Clear();
foreach(var (name,value) in new[]{("slots","3"),("slots","65"),("missing","0"),("missing","11"),("interval","99"),("delay","-1"),("delay","0"),("randomStartStep","7"),("randomStartStep","361"),("rotation","double.NaN"),("projectile","999")})
    Reject(()=>doc.Apply(new(doc.Revision,"Reject ring input",[new(node.Id,name,value)]),d=>adapter.Validate(d,entity)),"Native ring constraint rejects "+name+"="+value);
Check(doc.Source==before&&doc.Identities.SequenceEqual(ids),"Rejected ring inputs retain accepted source and history identities");
World World(EncounterSession session){var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;return (World)engine.GetType().GetField("World")!.GetValue(engine)!;}
Enemy Boss(EncounterSession session)=>World(session).Enemies.Values.Single();
using(var native=new EncounterSession(adapter,Doc(spiral),entity,7,new(Invulnerable:true)))
{
    native.Advance(new(),400);Check(native.Shots.Select(s=>s.Ms).SequenceEqual(new long[]{0,100,200,300})&&native.Shots.All(s=>s.Count==4),"Native spiral attempts one centered volley per tick with zero initial delay");
    Check(native.Shots.Select(s=>s.Angle*180/Math.PI).Zip(new[]{0d,15,30,45}).All(p=>Math.Abs(p.First-p.Second)<1e-4)&&native.Shots.All(s=>Math.Abs(s.AngleIncrement-Math.PI/9)<1e-6),"Native successful-volley rotation and signed spacing retain exact packet geometry");
}
using(var native=new EncounterSession(adapter,doc,entity,7,new(Invulnerable:true)))
{
    native.Advance(new(),100);var warning=native.Effects.Single();Check(warning is {Kind:18,Ms:0,DurationMs:200,ObjectType:10}&&native.Shots.Count==0,"The original first-volley cue carries native owner, marked count and full warning delay before any shot");
    var marks=TelegraphGeometry.Sample(native.Effects,100);Check(marks.Count(m=>m.Opening)==4&&marks.Any(m=>m.Opening&&Math.Abs(m.X-69.5)<1e-4&&Math.Abs(m.Y-64.5)<1e-4),"Native ring cue projection follows the supplied client's opening markers and actual gap angle");
    native.Advance(new(),300);Check(native.Shots.Select(s=>s.Ms).SequenceEqual(new long[]{200,300})&&Math.Abs(native.Shots[0].Angle-warning.X2)<1e-6&&Math.Abs(native.Shots[0].AngleIncrement-warning.Y2)<1e-6,"Native first-volley geometry matches the received original cue when drift is disabled");
    Check(Math.Abs(native.Shots[1].Angle*180/Math.PI+115)<1e-4&&native.Shots.All(s=>s.Count==10),"The next native gap volley combines per-shot rotation with its alternating-side offset");
}
using(var native=new EncounterSession(adapter,doc,entity,7,new(Invulnerable:true)))
{
    Boss(native).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Dazed,5000));native.Step(new());var warning=native.Effects.Single();Boss(native).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Dazed,0));native.Advance(new(),200);
    Check(warning.ObjectType==5&&native.Shots.Single().Count==5&&Math.Abs(native.Shots.Single().Angle-warning.X2)<1e-6,"Removing Dazed after a cue never adds unmarked first-volley rays in the native controller");
}
using(var native=new EncounterSession(adapter,Doc(spiral),entity,7,new(Invulnerable:true)))
{
    native.Step(new());Boss(native).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Stunned,300));native.Advance(new(),400);
    Check(native.Shots.Select(s=>s.Ms).SequenceEqual(new long[]{0,300,400})&&Math.Abs(native.Shots[1].Angle*180/Math.PI-15)<1e-4,"Suppressed native attempts advance the countdown without a backlog or rotation-step advance");
}
using(var native=new EncounterSession(adapter,Doc(spiral),entity,7,new(Invulnerable:true,StartX:86.5)))
{native.Advance(new(),500);Check(native.Shots.Count==0,"Native rings require a visible player strictly inside the fixed 22-tile acquisition range");native.Step(new(-1,0));var state=Boss(native).StateStorage.Single(p=>p.Key.GetType().Name=="GordoSpiral").Value;Check((int)state.GetType().GetField("Volley")!.GetValue(state)! == 1,"The first successful native attempt advances exactly one rotation step even before client visibility delivers its packet");}
using(var native=new EncounterSession(adapter,Doc(spiral.Replace("angle:30","angle:30,reverseEvery:2")),entity,7,new(Invulnerable:true)))
{native.Advance(new(),500);Check(native.Shots.Select(s=>s.Angle*180/Math.PI).Zip(new[]{0d,15,30,15,0}).All(p=>Math.Abs(p.First-p.Second)<1e-4),"Native reverseEvery uses the original triangular successful-volley sequence");}
using(var native=new EncounterSession(adapter,Doc("new GordoRotation(),"+spiral),entity,7,new(Invulnerable:true)))
{native.Advance(new(),300);var direction=Boss(native).StateStorage.Single(p=>p.Key.GetType().Name=="GordoRotation").Value;var sign=(int)direction;Check(Math.Abs(native.Shots[1].Angle*180/Math.PI-15*sign)<1e-4&&Math.Abs(native.Shots[2].Angle*180/Math.PI-30*sign)<1e-4,"Current-leaf GordoRotation supplies the actual seeded native sign");}
using(var native=new EncounterSession(adapter,Doc("new GordoRotation(),new State(\"child\","+spiral+")"),entity,7,new(Invulnerable:true)))
{native.Advance(new(),300);Check(Boss(native).StateStorage.Keys.Any(k=>k.GetType().Name=="GordoRotation")&&Math.Abs(native.Shots[1].Angle*180/Math.PI-15)<1e-4,"A parent-only rotation retains the native current-leaf fallback rather than invented inheritance");}
Emission[] seededShots;NativeEffect[] seededEffects;
using(var native=new EncounterSession(adapter,Doc(gap.Replace("alternateSides:true","alternateSides:true,randomStartStep:90")),entity,7,new(Invulnerable:true)))
{native.Advance(new(),400);seededShots=native.Shots.ToArray();seededEffects=native.Effects.ToArray();var offset=(seededEffects.Single().X2*180/Math.PI-45+720)%360;Check(Math.Abs(offset/90-Math.Round(offset/90))<1e-5,"Native starting offsets use actual seeded multiples of the configured degree step");}
using(var native=new EncounterSession(adapter,Doc(gap.Replace("alternateSides:true","alternateSides:true,randomStartStep:90")),entity,7,new(Invulnerable:true)))
{native.Advance(new(),400);Check(native.Shots.SequenceEqual(seededShots)&&native.Effects.SequenceEqual(seededEffects),"Repeated seeded native rings retain identical starting cues and complete volleys");}
using(var native=new EncounterSession(adapter,Doc(spiral.Replace("15,100","0,100").Replace("angle:30","angle:30,degreesPerSecond:10")),entity,7,new(Invulnerable:true)))
{native.Advance(new(),300);Check(native.Shots.Select(s=>s.Angle*180/Math.PI).Zip(new[]{1d,2,3}).All(p=>Math.Abs(p.First-p.Second)<1e-4),"Continuous native drift uses age accumulated before each firing attempt");}
using(var native=new EncounterSession(adapter,Doc(spiral),entity,7,new(Invulnerable:true),arena:new ArenaPreset{Actors=[new(entity,62.5,64.5)]}))
{native.Advance(new(),300);var actors=World(native).Enemies.Values.ToArray();Check(ReferenceEquals(actors[0].CurrentState.Behaviors.Single(),actors[1].CurrentState.Behaviors.Single())&&native.Shots.GroupBy(s=>s.OwnerId).All(g=>g.Select(s=>s.Angle*180/Math.PI).Zip(new[]{0d,15,30}).All(p=>Math.Abs(p.First-p.Second)<1e-4)),"Enemies sharing one native behavior definition retain independent ring rotation and cooldown state");}
using(var native=new EncounterSession(adapter,Doc(spiral+",new Shoot(20,projectileIndex:0,fixedAngle:0,coolDown:100)"),entity,7,new(Invulnerable:true)))
{native.Step(new());Check(native.Shots.Count==2&&native.Events.Where(e=>e.Kind=="attack").All(e=>e.NodeId==null),"Ring and Shoot packets sharing an index do not receive invented Shoot attribution");}
var sourceFile=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(sourceFile);var source="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioRingFixture=()=>Behav().Init(\""+entity+"\",new State(\"ring\","+gap+")); } }";var project=new StudioProject{SourceFile=sourceFile,Source=source,EntityId=entity,Seed=7,Player=new(Invulnerable:true)};
using(var native=new EncounterSession(adapter,doc,entity,7,project.Player)){native.Advance(new(),500);await using var compiled=await CompiledRuntime.StartAsync(adapter.ServerRoot,project);await compiled.AdvanceAsync(new(),500);Check(native.Shots.SequenceEqual(compiled.Shots)&&native.Effects.SequenceEqual(compiled.Effects),"Literal and compiled accepted source retain identical complete native ring packets and warning effects");}
var getterSource=source.Replace("private _","static int StudioRingUnknown { get { throw new System.Exception(\"authoring getter executed\"); } } private _").Replace("slots:12","slots:StudioRingUnknown");var unknown=new BehaviorDocument(getterSource,adapter,sourceFile:sourceFile);
Check(adapter.ReadRingPattern(unknown,unknown.Entities.Single().Root.Children.Single())==null&&!adapter.Validate(unknown,entity).Any(d=>d.IsError),"Unknown ring getters retain source input ownership without execution or invented divisions");
var output=Path.GetFullPath(".artifacts/ring-pattern");Directory.CreateDirectory(output);var saved=Path.Combine(output,"ring.rbs");BehaviorDocument.Save(saved,project);Check(BehaviorDocument.Load(saved).Source==source&&File.ReadAllText(sourceFile)==supplied,"Native ring authoring, execution and persistence leave supplied game source unchanged");Console.WriteLine($"{checks} ring pattern checks passed.");
