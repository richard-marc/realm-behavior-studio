using System.Diagnostics;
using System.Reflection;
using System.Collections.Immutable;
using System.Text.Json;
using BossPatternSim;
using Studio.Core;
using TKR.Shared.resources;
using TKR.WorldServer.core.net.stats;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.structures;
using TKR.WorldServer.core.worlds.impl;
using TKR.WorldServer.logic;

namespace Studio.Raple;

public record EncounterInput(double X = 0, double Y = 0, bool Attack = false, double AimX = 64.5, double AimY = 64.5);
public record EncounterEvent(long Ms, string Kind, string Message, Guid? NodeId = null, int? ActorId = null, int? ParentId = null,NativeStateOrigin? PhaseOrigin=null,NativePacketOrigin? PacketOrigin=null,int? PacketIndex=null);
public record DamageContribution(int PlayerId,int Damage);
public record DamageCounterView(int RecordedDamage,int PresentPlayerDamage,int? ParentActorId,ImmutableArray<DamageContribution> Players);
public record WorldObjectView(int Id,string Name,string Kind,float X,float Y,int Size,int AltTexture,int TextureIndex,string Phase);
public record EntityView(int Id, string Name, float X, float Y, int Hp, int MaxHp, string Phase, HitGeometry Hitbox, EntityMotion? Motion = null, int? ParentId = null, bool SpawnedByBehavior = false,int? Size=null,string? DisplayName=null,int? Defense=null,DamageCounterView? Damage=null,int? AltTexture=null,int? TextureIndex=null,int? GlowColor=null,int? ProjectileSizeBase=null);
public record BulletView(float X, float Y, int OwnerId, int ProjectileIndex, Guid? NodeId, string TextureId, double Angle, bool Friendly);
public record ReplayInput(long Ms, EncounterInput Input, int DurationMs = 100);
public record Emission(long Ms, int OwnerId, int Index, int Count, int BulletId, float X, float Y, float Angle, float AngleIncrement, int Damage);
// Phase is observed after the native tick; it does not attribute motion to an action.
public record MovementSample(long Ms, int DurationMs, int ActorId, string Entity, bool Player, float FromX, float FromY, float ToX, float ToY, string Phase = "", Guid? PhaseNodeId = null,NativeStateOrigin? PhaseOrigin=null);
public record ReplayRecord(int Version, int Seed, string Entity, string Source, string ResourceHash, PlayerPreset Player, List<ReplayInput> Inputs);

// Enemy execution, resource definitions and weapon formulas are reused. Client collision is sampled.
public sealed class EncounterSession : IEncounterSession
{
    readonly SimEngine engine;
    readonly NativePacketCapture packetCapture;
    readonly Enemy boss;
    readonly BehaviorDocument document;
    readonly List<Bullet> bullets = [];
    readonly Dictionary<int, string> lastStates = [];
    readonly Dictionary<int, EntityMotion> motions = [];
    readonly Dictionary<int, (string Name, int? Parent)> liveActors = [];
    readonly Dictionary<int,(ushort Type,int Texture,int Size,int Alt,int Glow,int Projectile)> appearances=[];
    readonly PlayerPreset preset;
    readonly string entityId;
    readonly int seed;
    readonly bool compiledSource;
    readonly ConditionEffectManager playerConditions;
    readonly GameWeapon weapon;
    readonly NativeArenaObserver arenaObserver;
    public ArenaSceneView Arena=>arenaObserver.Current;
    public ArenaTileProbe ProbeArena(double x,double y)=>new(((BossLabWorld)engine.World).ContainsArenaPoint(x,y),engine.Walkable(x,y),engine.World.IsPassable(x,y,true),engine.BlocksProjectile(x,y,false),engine.BlocksProjectile(x,y,true),engine.TileSpeed(x,y));
    public const int SampleMs = 5;
    int eventCursor;
    public long Ms { get; private set; }
    public int PlayerHp { get; private set; }
    public bool Dead => PlayerHp <= 0;
    public float PlayerX => engine.Actor.X;
    public float PlayerY => engine.Actor.Y;
    bool BossActive=>engine.World.Enemies.TryGetValue(boss.Id,out var current)&&ReferenceEquals(current,boss)&&!boss.Dead;
    public int BossHp => BossActive?boss.HP:0;
    public int BossMaxHp => boss.MaximumHP;
    public int Seed => seed;
    public double StartMs { get; }
    public List<EncounterEvent> Events { get; } = [];
    public List<NativeCallbackRecord> Callbacks { get; } = [];
    public IReadOnlyList<NativeStateSlot> NativeState=>NativeRuntimeState.Capture(engine.World.Enemies.Values);
    IReadOnlyList<NativeCallbackRecord> IEncounterSession.Callbacks => Callbacks;
    internal void CaptureCallbacks()=>Callbacks.AddRange(NativeCallbacks.Drain());
    public List<ReplayInput> Inputs { get; } = [];
    public ReplayJournal? Recording { get; set; }
    public List<MovementSample> Movements { get; } = [];
    IReadOnlyList<MovementSample> IEncounterSession.Movements => Movements;
    public List<Emission> Shots { get; } = [];
    public List<NativeEffect> Effects { get; } = [];
    IReadOnlyList<NativeEffect> IEncounterSession.Effects=>Effects;
    public bool PauseRequested { get; private set; }
    public string? BreakpointKind { get; set; }
    public string PlayerClass => weapon.Entry.ClassId;
    public WeaponEntry Weapon => weapon.Entry;
    public double PlayerFacing { get; private set; }
    public double PlayerAttackAngle { get; private set; }
    public double PlayerMoveSpeed { get; private set; }
    public long PlayerAttackStart => weapon.AttackStart;
    public double PlayerAttackPeriod => weapon.AttackPeriod;
    public IReadOnlyList<int> PlayerStats => Enumerable.Range(0, 8).Select(i => weapon.Stats[i]).ToArray();
    public string Coverage { get; private set; } = "Real enemy executor · XML weapons · client hit geometry / 5 ms samples";
    public List<Emission> PlayerShots { get; } = [];
    IReadOnlyList<EncounterEvent> IEncounterSession.Events => Events;
    IReadOnlyList<Emission> IEncounterSession.Shots => Shots;
    IReadOnlyList<Emission> IEncounterSession.PlayerShots => PlayerShots;
    IReadOnlyList<ReplayInput> IEncounterSession.Inputs => Inputs;
    public IReadOnlyList<EntityView> Entities => engine.World.Enemies.Values.Where(e => !e.Dead)
        .Select(e => new EntityView(e.Id, e.ObjectDesc.IdName, e.X, e.Y, e.HP, e.MaximumHP, e.CurrentState?.Name ?? "", ClientCollision.Geometry(e.ObjectType), motions.GetValueOrDefault(e.Id), e.ParentEntity?.Id, e.SpawnedByBehavior,e.Size,e.Name,e.Defense,CounterView(e),e.AltTextureIndex,appearances.GetValueOrDefault(e.Id).Texture,e.GlowEnemy,e.ProjectileSizeBase)).ToArray();
    IEnumerable<TKR.WorldServer.core.objects.Entity> NativeObjects()=>engine.World.StaticObjects.Values.Cast<TKR.WorldServer.core.objects.Entity>().Concat(engine.World.Portals.Values).Concat(engine.World.Containers.Values).Where(e=>!e.Dead);
    readonly Dictionary<int,int> objectTextures=[];
    readonly Dictionary<int,string> liveObjectNames=[];
    public IReadOnlyList<WorldObjectView> WorldObjects=>NativeObjects().Select(e=>new WorldObjectView(e.Id,e.ObjectDesc.IdName,e.ObjectDesc.Class??"World object",e.X,e.Y,e.Size,e.AltTextureIndex,objectTextures.GetValueOrDefault(e.Id),e.CurrentState?.Name??"")).ToArray();
    static DamageCounterView CounterView(Enemy enemy)
    {
        var counter=enemy.DamageCounter;var players=counter.GetPlayerData().Select(p=>new DamageContribution(p.Item1.Id,p.Item2)).OrderBy(p=>p.PlayerId).ToImmutableArray();
        var present=0;foreach(var player in players)present=unchecked(present+player.Damage);
        return new(counter.TotalDamage,present,counter.Parent?.Host.Id,players);
    }
    public IReadOnlyList<BulletView> Projectiles => bullets.Where(b => !b.Removed).Select(b => new BulletView(b.X, b.Y, b.Owner, b.Index, b.Node, b.Desc.ObjectId, b.Path.Angle, b.Friendly)).ToArray();
    sealed class Bullet
    {
        public long Born; public int Id, Owner, Index, Damage; public bool Removed, Hit, Friendly;
        public HashSet<int> HitTargets = [];
        public float X, Y; public double Angle; public Guid? Node;
        public ProjectileDesc Desc = null!; public Player.ValidatedProjectile Path = null!;
    }
    public EncounterSession(RapleAdapter adapter, BehaviorDocument doc, string entity, int seed, PlayerPreset player, int arenaSize = 48, string? phase = null, bool originalRuntimeReference = false,ArenaPreset? arena = null)
    {
        var watch = Stopwatch.StartNew(); document = doc; entityId = entity; this.seed = seed; preset = player; compiledSource = originalRuntimeReference;
        if(adapter.PreviewContextRequirement(doc,entity) is {} requirement)throw new InvalidOperationException(requirement);
        arena??=new();NativeArena.Validate(arena);
        if (player.Hp < 1 || player.Hp > 1000000 || player.Speed < 0 || player.Speed > 500 || player.Defense < 0 || player.Defense > 10000 || player.Attack < 0 || player.Attack > 500 || player.Dexterity < 0 || player.Dexterity > 500) throw new ArgumentException("Player values are outside supported bounds.");
        if (!double.IsFinite(player.StartX) || !double.IsFinite(player.StartY) || player.StartX < 0 || player.StartY < 0 || (float)player.StartX >= 128 || (float)player.StartY >= 128) throw new ArgumentException("Player start must be inside the 128-tile prototype map.");
        // Resize with the observer at the native arena center. The offline
        // harness lacks the network state needed by a resize-triggered teleport.
        // The requested start is applied before any encounter entry callbacks.
        NativeCallbacks.Reset();
        engine = new SimEngine(new SimConfig { Boss = entity, Seed = seed, PlayerX = BossLabWorld.Center, PlayerY = BossLabWorld.Center });
        // The upstream harness copies base fields from an enemy. Replace its effect manager so
        // a boss's immunity flags never become the test player's immunity flags.
        playerConditions = new ConditionEffectManager(engine.Actor);
        typeof(Entity).GetField("ConditionEffectManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine.Actor, playerConditions);
        weapon = new GameWeapon(engine.Actor, player, seed);
        // Native Player.Damage runs with the original stats and HP. The offline actor
        // has no database character; suppress only Player.Death's DB/client teardown.
        // This private guard is used exclusively by that teardown in the supplied source.
        typeof(Player).GetField("_dead",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(engine.Actor,true);
        if(player.Invulnerable||player.Spectator)engine.Actor.ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Invulnerable,-1));
        NativeArena.Apply(engine,arena,arenaSize);
        arenaObserver=new(engine,arena.GroundId);
        NativeArena.ValidateSpawn(engine,arena.BossX,arena.BossY,"Primary enemy",true);
        foreach(var actor in arena.Actors)NativeArena.ValidateSpawn(engine,actor.X,actor.Y,actor.EntityId,true);
        if (!((BossLabWorld)engine.World).ContainsArenaPoint(player.StartX, player.StartY) || !engine.World.Map.Contains((float)player.StartX, (float)player.StartY) || !engine.World.IsPassable((float)player.StartX, (float)player.StartY, false)) throw new ArgumentException("Player start is outside the passable prototype arena.");
        // Arena resizing can relocate the debug actor. Apply the validated start
        // again before any encounter entry callback or forced-phase setup.
        engine.Actor.Move((float)player.StartX, (float)player.StartY);
        // Some constructors resolve resources through InitDb. Reuse that exact lookup context during binding.
        var init = typeof(BehaviorDb).GetField("InitDb", BindingFlags.Static | BindingFlags.NonPublic)!;
        init.SetValue(null, engine.Server.BehaviorDb);
        State root;
        try { root = originalRuntimeReference ? engine.Server.BehaviorDb.Definitions[SimEngine.Data.IdToObjectType[entity]].Item1 : adapter.BuildState(doc, entity); } finally { init.SetValue(null, null); }
        var type = SimEngine.Data.IdToObjectType[entity];
        // Keep spawned instances on the edited behavior too; unrelated minions use their production definitions.
        engine.Server.BehaviorDb.Definitions[type] = Tuple.Create(root, engine.Server.BehaviorDb.Definitions[type].Item2);
        RapleAdapter.ValidateLaneRuntime(root,engine.Server.BehaviorDb);
        boss = NativeMapFile.SpawnConfigured(engine,new(entity,arena.BossX,arena.BossY,arena.BossConfig));
        foreach(var actor in arena.Actors)NativeMapFile.SpawnConfigured(engine,actor);
        engine.FlushAdds();
        packetCapture=new(engine.Actor.Client);
        PlayerHp = weapon.Stats[0]; engine.Actor.HP = PlayerHp;
        if (phase != null)
        {
            try
            {
            engine.Tick(0); DrainShotOrigins();engine.DrainShots();
            CaptureEffects();
            if (!BossLabRuntime.TryForceState(engine.Actor, boss, phase, SimEngine.Time(0), out var error)) throw new ArgumentException(error);
            CaptureEffects();
            Add("forced-phase", $"Forced phase {phase}; previous setup bypassed.");
            }
            catch {packetCapture.Dispose();throw;}
        }
        watch.Stop(); StartMs = watch.Elapsed.TotalMilliseconds;
        Add("start", $"Seed {seed}; 100 ms enemy ticks / {SampleMs} ms client collision samples. Weapon: {weapon.Entry.Name}. Abilities, armor/ring loadouts, regeneration and full player lifecycle remain pending.");
        ObserveActorRemovals();
        if(NativeArena.Limitations(arena) is {} limitation){Coverage=limitation;Add("unsupported",limitation);}
        arenaObserver.Capture();
    }
    void Add(string kind, string message, Guid? node = null, long? at = null, int? actor = null, int? parent = null,NativeStateOrigin? phaseOrigin=null,NativePacketOrigin? packetOrigin=null,int? packetIndex=null)
    {
        if (Events.Count < 30000) Events.Add(new(at ?? Ms, kind, message, node, actor, parent,phaseOrigin,packetOrigin,packetIndex));
        if (BreakpointKind == kind) PauseRequested = true;
    }
    NativePacketOrigin?[] DrainShotOrigins()
    {
        var queue=(System.Collections.IEnumerable)typeof(Player).GetField("PendingShootAcknowlegements",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(engine.Actor)!;
        return queue.Cast<object>().Where(ack=>ack.GetType().GetField("EnemyShoot")!.GetValue(ack)!=null).Select(ack=>NativePacketOrigins.FromData(ack.GetType().GetField("StudioPacketOrigin")?.GetValue(ack) as string[])).ToArray();
    }
    void CaptureEffects()
    {
        foreach(var effect in packetCapture.Drain(Ms))
        {
            if(Effects.Count>=30000)throw new InvalidOperationException("Encounter exceeded the 30,000 native effect limit.");
            Effects.Add(effect);
            var origin=packetCapture.EffectOrigin;
            Add("effect",((TKR.WorldServer.networking.packets.outgoing.EffectType)effect.Kind)+" · "+effect.DurationMs+" ms",origin?.Source?.NodeId,actor:effect.TargetObjectId,packetOrigin:origin,packetIndex:Effects.Count-1);
        }
    }
    public IReadOnlyList<EncounterEvent> NewEvents()
    {
        var next = Events.Skip(eventCursor).ToArray(); eventCursor = Events.Count; return next;
    }
    public void SetBossHpFraction(double fraction)
    {
        if(!BossActive)throw new InvalidOperationException("The original encounter actor is no longer active. Its health override is unavailable.");
        if (!double.IsFinite(fraction) || fraction < 0 || fraction > 1) throw new ArgumentOutOfRangeException(nameof(fraction));
        boss.HP = Math.Max(1, (int)(boss.MaximumHP * fraction)); Add("override", "Boss HP forced to " + (fraction * 100).ToString("0") + "%");
        CaptureCallbacks(); Recording?.Record(this, new(Ms, "boss-hp", Fraction: fraction));
    }
    public void RemoveMinionsForTest()
    {
        foreach (var e in engine.World.Enemies.Values.Where(e => e != boss).ToArray()) e.Expunge();
        CaptureEffects();
        Add("override", "Minions removed for transition test; death callbacks bypassed.");
        CaptureCallbacks(); Recording?.Record(this, new(Ms, "remove-minions"));
    }
    public void Step(EncounterInput input) => Advance(input, SimEngine.TickMs);
    public void Advance(EncounterInput input, int durationMs)
    {
        if (durationMs <= 0 || durationMs > 1000 || durationMs % SampleMs != 0) throw new ArgumentOutOfRangeException(nameof(durationMs), "Advance in positive 5 ms samples, up to one second.");
        PauseRequested = false;
        if (Ms + durationMs > 300000) throw new InvalidOperationException("Run reached the five-minute bound. Restart to continue.");
        Inputs.Add(new(Ms, input, durationMs));
        for (var dt = 0; dt < durationMs; dt += SampleMs)
        {
            weapon.Refresh(Ms);
            PlayerMoveSpeed = 0;
            if (Ms % SimEngine.TickMs == 0) { var effectTime = SimEngine.Time(Ms); playerConditions.Update(ref effectTime); }
            if (!Dead && !preset.Spectator)
            {
                var length = Math.Sqrt(input.X * input.X + input.Y * input.Y);
                var speed = (.004 + weapon.Stats[4] / 75d * .0056) * SampleMs * engine.TileSpeed(PlayerX, PlayerY);
                if (engine.Actor.HasConditionEffect(ConditionEffectIndex.Slowed)) speed = .004 * SampleMs * engine.TileSpeed(PlayerX, PlayerY);
                else if (engine.Actor.HasConditionEffect(ConditionEffectIndex.Speedy) || engine.Actor.HasConditionEffect(ConditionEffectIndex.NinjaSpeedy)) speed *= 1.35;
                if (engine.Actor.HasConditionEffect(ConditionEffectIndex.Paralyzed) || engine.Actor.HasConditionEffect(ConditionEffectIndex.Petrify)) speed = 0;
                if (length > 0)
                {
                    var x = PlayerX + input.X / length * speed; var y = PlayerY + input.Y / length * speed;
                    if (engine.Walkable(x, y))
                    {
                        var fromX=PlayerX; var fromY=PlayerY; engine.MovePlayer(x,y); PlayerMoveSpeed=speed/SampleMs; PlayerFacing=Math.Atan2(input.Y,input.X);
                        if(fromX!=PlayerX || fromY!=PlayerY) Movements.Add(new(Ms,SampleMs,engine.Actor.Id,PlayerClass,true,fromX,fromY,PlayerX,PlayerY));
                    }
                }
                if (input.Attack)
                {
                    var volley = weapon.Fire(Ms, input.AimX, input.AimY);
                    if (volley.Count > 0) PlayerFacing = PlayerAttackAngle = Math.Atan2(input.AimY - PlayerY, input.AimX - PlayerX);
                    weapon.Refresh(Ms);
                    foreach (var shot in volley)
                    {
                        PlayerShots.Add(new(Ms, engine.Actor.Id, shot.Index, volley.Count, shot.Id, shot.X, shot.Y, shot.Angle, 0, shot.Damage));
                        var bullet = new Bullet { Born = Ms, Id = shot.Id, Owner = engine.Actor.Id, Index = shot.Index, Friendly = true,
                            Damage = shot.Damage, Desc = shot.Desc, X = shot.X, Y = shot.Y, Angle = shot.Angle,
                            Path = new Player.ValidatedProjectile((int)(Ms), false, shot.Index, shot.X, shot.Y, shot.Angle, shot.Container.ObjectType, shot.Damage, false, true) };
                        weapon.Register(shot.Id, bullet.Path); bullets.Add(bullet);
                    }
                    if (volley.Count > 0) Add("player-attack", $"{weapon.Entry.Name}: {volley.Count} × projectile #{volley[0].Index}", at: Ms);
                }
            }
            // Enemy step is fixed independently of rendering and playback speed.
            if (Ms % SimEngine.TickMs == 0)
            {
                var beforeTick = engine.World.Enemies.Values.ToDictionary(e => e.Id, e => (e.X, e.Y));
                var dying = engine.World.Enemies.Values.Where(e => e.HP == 0 && !e.Dead).ToArray();
                var previousHp=PlayerHp;engine.Actor.HP = PlayerHp;engine.Tick(Ms);
                PlayerHp=Math.Max(0,engine.Actor.HP);
                if(PlayerHp<previousHp)Add("hit",$"Player −{previousHp-PlayerHp} HP from native server damage",actor:engine.Actor.Id);
                if(previousHp>0&&Dead){SimEngine.Set(engine.Actor,"Dead",true);Add("death","Test player died from native server damage. Restart to respawn.",actor:engine.Actor.Id);}
                CaptureEffects();
                motions.Clear();
                foreach (var dead in dying.Where(e => e.Dead)) Add("death", dead.ObjectDesc.IdName + " killed by weapon; native death callbacks executed.", actor: dead.Id, parent: dead.ParentEntity?.Id);
                foreach (var e in engine.World.Enemies.Values.Where(e => !e.Dead))
                {
                    var from = beforeTick.GetValueOrDefault(e.Id, (e.X, e.Y));
                    motions[e.Id] = new(from.X, from.Y, e.X, e.Y, Ms, SimEngine.TickMs);
                    var current = e.CurrentState?.Name ?? "";
                    var origin=NativeStateOrigins.Find(e.CurrentState);
                    var phaseNode=compiledSource?origin?.NodeId:e.ObjectType==boss.ObjectType?FindState(current):null;
                    if(from.X!=e.X || from.Y!=e.Y) Movements.Add(new(Ms,SimEngine.TickMs,e.Id,e.ObjectDesc.IdName,false,from.X,from.Y,e.X,e.Y,current,phaseNode,origin));
                    if (!lastStates.TryGetValue(e.Id, out var previous)) Add("spawn", e.ObjectDesc.IdName, actor: e.Id, parent: e.ParentEntity?.Id);
                    if (current != previous) Add("transition", $"{e.ObjectDesc.IdName}: {previous ?? "spawn"} → {current}",phaseNode,actor:e.Id,parent:e.ParentEntity?.Id,phaseOrigin:origin);
                    lastStates[e.Id] = current;
                }
                var shotOrigins=DrainShotOrigins();var shotCursor=0;
                foreach (var shot in engine.DrainShots())
                {
                    Shots.Add(new(Ms, shot.OwnerId, shot.BulletType, shot.NumShots, shot.BulletId, shot.StartingPos.X, shot.StartingPos.Y, shot.Angle, shot.AngleInc, shot.Damage));
                    var callback=shotOrigins[shotCursor++];
                    var owner = engine.World.Enemies.Values.FirstOrDefault(e => e.Id == shot.OwnerId);
                    var node = callback?.Source?.NodeId??(!compiledSource && owner?.ObjectType == boss.ObjectType ? FindAttack(owner.CurrentState?.Name ?? "", shot.BulletType) : null);
                    Add("attack", $"{owner?.ObjectDesc.IdName}: {shot.NumShots} × projectile #{shot.BulletType}", node, actor: shot.OwnerId, parent: owner?.ParentEntity?.Id,phaseOrigin:NativeStateOrigins.Find(owner?.CurrentState),packetOrigin:callback,packetIndex:Shots.Count-1);
                    if(!SimEngine.Data.ObjectDescs.TryGetValue((ushort)shot.ObjectType,out var ownerDesc)||!ownerDesc.Projectiles.TryGetValue(shot.BulletType,out var desc))
                    {Coverage="Unsupported projectile XML present · run is not verified";Add("unsupported","Received projectile has no native XML descriptor.",node);continue;}
                    if (!Player.ValidatedProjectile.CanSampleBossLab(desc) || desc.Homing || desc.Orbit)
                    { Coverage = "Unsupported projectile motion present · run is not verified"; Add("unsupported", "Projectile omitted: client-history-dependent motion.", node); continue; }
                    for (var i = 0; i < shot.NumShots; i++) bullets.Add(new Bullet { Born = Ms, Id = shot.BulletId + i, Owner = shot.OwnerId,
                        Index = shot.BulletType, Damage = shot.Damage, Desc = desc, Node = node, X = shot.StartingPos.X, Y = shot.StartingPos.Y, Angle = shot.Angle + shot.AngleInc * i,
                        Path = new Player.ValidatedProjectile((int)Ms, shot.Spawned, shot.BulletType, shot.StartingPos.X, shot.StartingPos.Y, shot.Angle + shot.AngleInc * i, shot.ObjectType, shot.Damage, true, false) });
                }
                if (engine.ActiveEnemyCount > 128 || bullets.Count > 20000) throw new InvalidOperationException("Encounter exceeded the 128 entity / 20,000 projectile limit.");
            }
            foreach (var b in bullets.Where(b => !b.Removed))
            {
                var age = (int)(Ms - b.Born);
                if (age > b.Desc.LifetimeMS || b.Desc.ReturnToOrigin && age >= ProjectileReturn.Duration(b.Desc.ReturnDistance, b.Desc.Speed, b.Desc.ReturnPauseMS)) { b.Removed = true; continue; }
                var pos = ClientTrajectory.Sample(b.Path, age, b.Id, b.Desc); b.X = pos.X; b.Y = pos.Y;
                if (engine.BlocksProjectile(b.X, b.Y, b.Desc.PassesCover)) { b.Removed = true; continue; }
                if (age < b.Desc.ArmingDelayMS) continue;
                if (b.Friendly)
                {
                    var hit = ClientCollision.Select(b.X, b.Y, engine.World.Enemies.Values.Select(e => new HitTarget(e.Id, e.ObjectType, e.X, e.Y, ClientCollision.Geometry(e.ObjectType), ClientCollision.Eligible(e))), b.HitTargets);
                    if (hit != null && engine.World.Enemies.TryGetValue(hit.Id, out var target))
                    {
                        b.HitTargets.Add(target.Id); b.Removed = !b.Desc.MultiHit;
                        var time = SimEngine.Time(Ms);
                        var damage = weapon.Hit(b.Id, target, ref time);
                        Add("weapon-hit", $"{weapon.Entry.Name} → {target.ObjectDesc.IdName}: −{damage} HP", at: Ms, actor: target.Id, parent: target.ParentEntity?.Id);
                        if (target.Dead) { engine.FlushAdds(); Add("death", target.ObjectDesc.IdName + " killed by weapon; native death callbacks executed.", at: Ms, actor: target.Id, parent: target.ParentEntity?.Id); }
                    }
                }
                else if (!Dead && !preset.Spectator && !b.Hit && ClientCollision.Eligible(engine.Actor) && new HitGeometry(.5).Contains(b.X - PlayerX, b.Y - PlayerY))
                {
                    b.Hit = true; b.Removed = !b.Desc.MultiHit;
                    var damage = preset.Invulnerable ? 0 : StatsManager.DamageWithDefense(engine.Actor, b.Damage, b.Desc.ArmorPiercing, weapon.Stats[3]);
                    PlayerHp = Math.Max(0, PlayerHp - damage); Add("hit", $"Player −{damage} HP from projectile #{b.Index}", b.Node, Ms);
                    foreach (var effect in b.Desc.Effects) engine.Actor.ApplyConditionEffect(effect);
                    if (Dead) {SimEngine.Set(engine.Actor,"Dead",true);Add("death", "Test player died. Restart to respawn.");}
                }
            }
            CaptureEffects(); ObserveActorRemovals(); CaptureCallbacks(); Ms += SampleMs;
        }
        foreach (var removed in bullets.Where(b => b.Friendly && b.Removed)) weapon.Forget(removed.Id);
        bullets.RemoveAll(b => b.Removed);
        arenaObserver.Capture();
        Recording?.Record(this, new(Ms - durationMs, "advance", durationMs, input));
    }
    void ObserveActorRemovals()
    {
        foreach(var actor in NativeObjects())
        {
            if(!liveObjectNames.ContainsKey(actor.Id))Add("spawn",actor.ObjectDesc.IdName+" · "+actor.ObjectDesc.Class,actor:actor.Id);
            liveObjectNames[actor.Id]=actor.ObjectDesc.IdName;objectTextures[actor.Id]=AlternateTextures.Resolve(actor.ObjectDesc.IdName,actor.AltTextureIndex,objectTextures.GetValueOrDefault(actor.Id));
        }
        var liveObjects=NativeObjects().Select(e=>e.Id).ToHashSet();foreach(var id in objectTextures.Keys.Where(id=>!liveObjects.Contains(id)).ToArray())objectTextures.Remove(id);
        foreach(var pair in liveObjectNames.Where(pair=>!liveObjects.Contains(pair.Key)).ToArray()){Add("despawn",pair.Value+" is no longer active in the native world.",actor:pair.Key);liveObjectNames.Remove(pair.Key);}
        foreach(var actor in engine.World.Enemies.Values.Where(e=>!e.Dead))
        {
            var previous=appearances.GetValueOrDefault(actor.Id);
            var texture=AlternateTextures.Resolve(actor.ObjectDesc.IdName,actor.AltTextureIndex,previous.Type==actor.ObjectType?previous.Texture:0);
            var current=(actor.ObjectType,texture,actor.Size,actor.AltTextureIndex,actor.GlowEnemy,actor.ProjectileSizeBase);
            if(appearances.ContainsKey(actor.Id)&&current!=previous)Add("appearance",actor.ObjectDesc.IdName+": size "+actor.Size+" · texture "+actor.AltTextureIndex+" (visible "+texture+")",actor:actor.Id);
            appearances[actor.Id]=current;
        }
        foreach(var removed in appearances.Keys.Where(id=>!engine.World.Enemies.TryGetValue(id,out var actor)||actor.Dead).ToArray())appearances.Remove(removed);
        foreach (var (id, previous) in liveActors.Where(pair => !engine.World.Enemies.TryGetValue(pair.Key, out var actor) || actor.Dead).ToArray())
        {
            Add("despawn", previous.Name + " is no longer active in the native world.", actor: id, parent: previous.Parent);
            liveActors.Remove(id);
        }
        foreach (var actor in engine.World.Enemies.Values.Where(e => !e.Dead)) liveActors[actor.Id] = (actor.ObjectDesc.IdName, actor.ParentEntity?.Id);
    }
    Guid? FindState(string state)
    {
        var matches = document.Entities.First(e => e.EntityId == entityId).Root.Descendants().Where(n => n.Kind == "State" && n.Label == state).ToArray();
        return matches.Length == 1 ? matches[0].Id : null;
    }
    Guid? FindAttack(string state, int index)
    {
        var root = document.Entities.First(e => e.EntityId == entityId).Root;
        var phase = root.Descendants().FirstOrDefault(n => n.Kind == "State" && n.Label == state) ?? root;
        var matches = phase.StateChildren.Where(n => n.Kind is "Shoot" or "SequentialAimedSweep" or "GordoSpiral" or "GapRing" && (n.Parameters.FirstOrDefault(p => p.Name == (n.Kind=="Shoot"?"projectileIndex":"projectile"))?.Expression ?? "0") == index.ToString()).ToArray();
        return matches.Length == 1 && matches[0].Kind=="Shoot" ? matches[0].Id : null; // The shared packet does not identify its controller callback.
    }
    public void ExportTrace(string path) => File.WriteAllText(path, JsonSerializer.Serialize(new { seed, entityId, sourceRevision = document.Revision, tickMs = 100, sampleMs = SampleMs, durationMs = Ms, player = preset, coverage = Coverage, actors = Entities,objects=WorldObjects, events = Events, callbacks=Callbacks, nativeState=NativeState, shots = Shots, playerShots = PlayerShots, effects=Effects, inputs = Inputs }, BehaviorDocument.JsonOptions));
    public string InspectTimers() => BossActive?string.Join("\n", boss.StateStorage.Select(p => p.Key.GetType().Name + ": " + (p.Value is int n ? n + " ms / scalar" : p.Value?.GetType().Name ?? "null"))):"Original encounter actor is no longer active. Live replacements appear in Actors.";
    public void Dispose() => packetCapture.Dispose();
}
