using System.Reflection;
using System.Runtime.CompilerServices;
using BossPatternSim;
using Studio.Core;
using TKR.Shared;
using TKR.Shared.resources;
using TKR.WorldServer.core;
using TKR.WorldServer.core.miscfile;
using TKR.WorldServer.core.net.stats;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.objects.inventory;
using TKR.WorldServer.core.objects.player;
using TKR.Shared.database.character.inventory;
using TKR.WorldServer.core.worlds;
using TKR.WorldServer.logic;

namespace Studio.Raple;

public record WeaponEntry(string Id, string Name, string ClassId, string Summary, string? Limitation)
{
    public bool Supported => Limitation == null;
    public override string ToString() => Name;
}
public record WeaponPellet(int Id, int Index, int Damage, float Angle, float X, float Y, Item Container, ProjectileDesc Desc);
public sealed class GameWeapon
{
    readonly Player owner;
    readonly Item weapon;
    readonly ClientRandom random;
    readonly OnShootProcTracker tracker = new();
    readonly Dictionary<int, Player.ValidatedProjectile> registered = [];
    readonly Dictionary<int, Dictionary<int, Player.ValidatedProjectile>> visible;
    readonly int[] boosts = new int[8], equipment = new int[8];
    readonly ActivateBoost[] active = Enumerable.Range(0, 8).Select(_ => new ActivateBoost()).ToArray();
    readonly List<(long End, int Index, int Amount, bool NoStack)> expiry = [];
    long lastShot = -100000;
    int bulletId;
    public StatsManager Stats { get; }
    public WeaponEntry Entry { get; }
    public double Period => 1 / Stats.GetAttackFrequency() / weapon.RateOfFire;
    public long AttackStart => lastShot;
    public double AttackPeriod { get; private set; }
    public static IReadOnlyList<WeaponEntry> Catalog() => SimEngine.Data.Items.Values
        .Where(i => SimEngine.Data.SlotTypeToItemType.TryGetValue(i.SlotType, out var kind) && kind == ItemType.Weapon)
        .Select(Describe).OrderBy(e => e.Name).ToArray();
    static WeaponEntry Describe(Item item)
    {
        var cls = SimEngine.Data.Classes.Values.OrderBy(c => c.IdName == "Wizard" ? 0 : 1)
            .FirstOrDefault(c => c.SlotTypes[0] == item.SlotType)?.IdName ?? "Wizard";
        var p = item.Projectiles.FirstOrDefault();
        var reason = p == null || item.RateOfFire <= 0 ? "No firing definition" :
            item.Projectiles.Any(p => !Player.ValidatedProjectile.CanSampleBossLab(p) || p.Homing || p.Orbit) ? "Motion needs client update history" :
            item.OnShootProcs.SelectMany(p => p.Effects).Any(e => e.Activation?.Effect == ActivateEffects.PoisonGrenade) ? "Cursor grenade activation is not supported yet" :
            item.Vampiric || item.Demonized || item.Electrify || item.Mutilate || item.Insanity || item.SonicBlaster || item.MonkeyKingsWrath || item.OutOfOneMind || item.GodTouch || item.GodBless || item.Clarification || item.HolyProtection ? "Special weapon hit activation is not supported yet" :
            item.ActivateOnEquips.Any(e => e.Value != "IncrementStat") ? "Special equip activations are not supported yet" :
            item.OnShootProcs.SelectMany(p => p.Effects).Any(e => e.Activation?.UseWisMod == true) ? "Wisdom-scaled shot activation is not supported yet" : null;
        return new(item.ObjectId, item.DisplayName, cls,
            p == null ? "No projectile" : $"{p.MinDamage}–{p.MaxDamage} damage · {item.NumProjectiles} shots · {item.RateOfFire:0.##}× rate · {p.LifetimeMS * p.Speed / 10000:0.##} tile travel", reason);
    }
    public GameWeapon(Player owner, PlayerPreset preset, int seed)
    {
        this.owner = owner;
        if (!SimEngine.Data.IdToObjectType.TryGetValue(preset.WeaponId, out var type) || !SimEngine.Data.Items.TryGetValue(type, out weapon!))
            throw new ArgumentException("Weapon XML not found: " + preset.WeaponId);
        Entry = Catalog().FirstOrDefault(e => e.Id == weapon.ObjectId) ?? throw new ArgumentException("Select a weapon, not an ability or other item.");
        if (!Entry.Supported) throw new NotSupportedException(Entry.Name + ": " + Entry.Limitation);
        random = new ClientRandom((uint)Math.Max(1, seed & int.MaxValue));
        // The offline actor has no database character. Supply arrays to the ORIGINAL stat
        // managers so attack/dexterity and status formulas execute unchanged, without DB writes.
        Stats = (StatsManager)RuntimeHelpers.GetUninitializedObject(typeof(StatsManager));
        Field(Stats, "Owner", owner);
        var assembly = typeof(StatsManager).Assembly;
        var baseline = RuntimeHelpers.GetUninitializedObject(assembly.GetType("TKR.WorldServer.core.BaseStatManager")!);
        Field(baseline, "_base", new[] { preset.Hp, 0, preset.Attack, preset.Defense, preset.Speed, preset.Dexterity, 0, 0 });
        var boost = RuntimeHelpers.GetUninitializedObject(assembly.GetType("TKR.WorldServer.core.BoostStatManager")!);
        Field(boost, "_boost", boosts); Field(Stats, "Base", baseline); Field(Stats, "Boost", boost);
        owner.Stats = Stats;
        SimEngine.Set(owner, "PlayerUpdate", new PlayerUpdate(owner));
        var items = new Item[28]; items[0] = weapon;
        SimEngine.Set(owner, "Inventory", new Inventory(owner, items, new ItemData[28]));
        visible = (Dictionary<int, Dictionary<int, Player.ValidatedProjectile>>)typeof(Player).GetField("VisibleProjectiles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        visible[owner.Id] = registered;
        foreach (var pair in weapon.StatsBoost)
        {
            var index = StatsManager.GetStatIndex((StatDataType)pair.Key);
            if (index >= 0) equipment[index] += pair.Value;
        }
        Refresh(0);
        // Native Player.EnemyHit records hits. Boss Lab already bypasses XP/loot/database awards.
        var fame = (FameCounter)RuntimeHelpers.GetUninitializedObject(typeof(FameCounter));
        fame.Host = owner; SimEngine.Set(fame, "Stats", new FameStats()); SimEngine.Set(owner, "FameCounter", fame);
    }
    static void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);
    public void Register(int id, Player.ValidatedProjectile path) { visible[owner.Id] = registered; registered[id] = path; }
    public void Forget(int id) => registered.Remove(id);
    public int Hit(int id, Enemy target, ref TickTime time)
    {
        var hp = target.HP;
        owner.EnemyHit(ref time, (int)time.TotalElapsedMs, id, target.Id, false);
        return hp - target.HP;
    }
    public void Refresh(long ms)
    {
        foreach (var end in expiry.Where(e => ms >= e.End).ToArray()) { active[end.Index].Pop(end.Amount, end.NoStack); expiry.Remove(end); }
        for (var i = 0; i < 8; i++) boosts[i] = equipment[i] + active[i].GetBoost();
    }
    public IReadOnlyList<WeaponPellet> Fire(long ms, double aimX, double aimY)
    {
        var period = Period;
        if (ms - lastShot < period || owner.HasConditionEffect(ConditionEffectIndex.Stunned) ||
            owner.HasConditionEffect(ConditionEffectIndex.Paused) || owner.HasConditionEffect(ConditionEffectIndex.Petrify)) return [];
        lastShot = ms;
        AttackPeriod = period;
        var procs = tracker.NextVolley([weapon]);
        var container = weapon; var index = 0; var count = weapon.NumProjectiles; var gap = (double)weapon.ArcGap;
        foreach (var triggered in procs) foreach (var effect in triggered.Effects)
            if (effect.ProjectileId.HasValue) { container = triggered.Item; index = effect.ProjectileId.Value; count = effect.NumProjectiles ?? count; gap = effect.ArcGap ?? gap; }
        var desc = container.Projectiles[index]; var aim = Math.Atan2(aimY - owner.Y, aimX - owner.X);
        var angle = aim - gap * Math.PI / 180 * (count - 1) / 2;
        var x = (float)(owner.X + Math.Cos(aim) * .3); var y = (float)(owner.Y + Math.Sin(aim) * .3);
        var result = new List<WeaponPellet>();
        for (var i = 0; i < count; i++)
        {
            var damage = (int)(random.NextIntRange((uint)desc.MinDamage, (uint)desc.MaxDamage) * Stats.GetAttackMult());
            result.Add(new(bulletId++, index, damage, (float)(angle + i * gap * Math.PI / 180), x, y, container, desc));
            owner.FameCounter.Shoot();
        }
        // Like Player.ApplyShootEffects, apply boosts AFTER all pellets have been emitted.
        foreach (var triggered in procs) foreach (var effect in triggered.Effects)
        {
            var a = effect.Activation;
            if (a?.Effect == ActivateEffects.ConditionEffectSelf && a.ConditionEffect.HasValue)
                owner.ApplyConditionEffect(new ConditionEffect(a.ConditionEffect.Value, a.DurationMS));
            if (a?.Effect == ActivateEffects.StatBoostSelf)
            {
                var stat = StatsManager.GetStatIndex((StatDataType)a.Stats);
                if (stat >= 0) { active[stat].Push(a.Amount, false); expiry.Add((ms + a.DurationMS, stat, a.Amount, false)); }
            }
        }
        return result;
    }
}
