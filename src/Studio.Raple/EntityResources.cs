using BossPatternSim;
using Microsoft.CodeAnalysis.CSharp;
using Studio.Core;
using TKR.Shared.resources;

namespace Studio.Raple;

public enum EntityResourceScope { Enemy, Queryable, Resolvable }
public sealed record EntityResource(string Id, string DisplayName, ushort ObjectType, string RuntimeKind, bool Queryable, int MaxHp, int Defense, string Group, string SpriteId)
{
    public string Title => Id;
    public string Summary => $"{RuntimeKind} · 0x{ObjectType:X4}" + (RuntimeKind == "Enemy" ? $" · HP {MaxHp} · defense {Defense}" : "") + (string.IsNullOrWhiteSpace(Group) ? "" : "\n" + Group);
}
public sealed record EntityParameter(string Name, string Label, EntityResourceScope Scope, bool AllowsPlayer, string? InactiveReason = null, int? RequiredProjectileIndex = null,bool NameFragment=false,string? NullTitle=null,string? NullSummary=null);
public sealed record EntityReference(bool Known, string? Id);

public static class EntityResources
{
    public static EntityResource FromDescriptor(ObjectDesc descriptor)=>Describe(descriptor.IdName,descriptor);
    static bool MatchesScope(EntityResource resource,EntityResourceScope scope)=>scope == EntityResourceScope.Resolvable || scope == EntityResourceScope.Enemy && resource.RuntimeKind == "Enemy" || scope == EntityResourceScope.Queryable && resource.Queryable;
    static bool HasProjectile(ObjectDesc desc,int? required)=>required is not {} index||desc.Projectiles.TryGetValue(index,out var projectile)&&projectile.BulletType==index;
    public static IReadOnlyList<EntityResource> Catalog(EntityParameter parameter)=>Catalog(parameter.Scope).Where(resource=>HasProjectile(SimEngine.Data.ObjectDescs[resource.ObjectType],parameter.RequiredProjectileIndex)).ToArray();
    public static bool Accepts(EntityParameter parameter,string id)=>parameter.NameFragment?Catalog(parameter).Any(resource=>resource.SpriteId==id):SimEngine.Data.IdToObjectType.TryGetValue(id,out var type)&&SimEngine.Data.ObjectDescs.TryGetValue(type,out var desc)&&MatchesScope(Describe(id,desc),parameter.Scope)&&HasProjectile(desc,parameter.RequiredProjectileIndex);
    // Resolve the ID map first: XML aliases and repeated IDs must expose the
    // descriptor the native constructor actually resolves, including its sprite.
    public static IReadOnlyList<EntityResource> Catalog(EntityResourceScope scope) => SimEngine.Data.IdToObjectType
        .Where(pair => SimEngine.Data.ObjectDescs.ContainsKey(pair.Value)).Select(pair => Describe(pair.Key, SimEngine.Data.ObjectDescs[pair.Value]))
        .Where(resource => MatchesScope(resource,scope))
        .OrderBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    static EntityResource Describe(string id, ObjectDesc desc)
    {
        // Match Entity.Resolve and World.AddToWorld's branch order. In particular,
        // Character resolves as Enemy, and Portal/Container do not enter the
        // enemy/static collision index even though their CLR base is StaticObject.
        var kind = desc.Connects ? "Connected object" : desc.Container ? "Container" : desc.Enemy || desc.Class == "Character" ? "Enemy" : desc.Class switch
        {
            "GuildHallPortal" or "ArenaPortal" or "Portal" => "Portal",
            "ClosedVaultChest" or "Merchant" or "GuildMerchant" or "ClosedVaultChestGold" or "VaultChest" or "MarketNPC" or "SkillTree" or "Forge" or "StatNPC" => "Vendor",
            "Engine" => "Engine", _ => "World object"
        };
        return new(id, desc.DisplayName, desc.ObjectType, kind, kind is "Enemy" or "Connected object" or "Engine" or "World object", desc.MaxHP, desc.Defense, desc.Group, desc.IdName);
    }
}

public sealed partial class RapleAdapter
{
    public static EntityParameter? EntityParameterFor(string nativeKind) => nativeKind switch
    {
        "Spawn" => new("children", "Spawn entity", EntityResourceScope.Enemy, false),
        "Reproduce"=>new("children","Reproduce entity",EntityResourceScope.Resolvable,true,NullTitle:"This host's XML type",NullSummary:"Native null children reproduces the host's object type; it does not select a player."),
        "InstantSpawnRing" => new("childName", "Ring helper", EntityResourceScope.Enemy, false),
        "OwnedLaneEmitters" => new("childName", "Lane emitter", EntityResourceScope.Enemy, false, RequiredProjectileIndex: 0),
        "Order" or "OrderOnce" => new("children", "Ordered entity", EntityResourceScope.Queryable, false),
        "OrderOnDeath" => new("target", "Ordered entity", EntityResourceScope.Queryable, false),
        "Follow" or "Orbit" => new("target", "Target entity", EntityResourceScope.Queryable, true),
        "EntityExistsTransition" => new("target", "Required entity", EntityResourceScope.Queryable, false),
        "EntityWithinTransition"=>new("entity","Nearby XML name",EntityResourceScope.Queryable,true,NameFragment:true,NullTitle:"Any XML name",NullSummary:"Null matches any descriptor in the native enemy/static collision index, including this host; it does not select a player."),
        "EntityHpLessTransition"=>new("entity","Nearest enemy name",EntityResourceScope.Enemy,false,NameFragment:true),
        "StayBack"=>new("entity","Retreat target name",EntityResourceScope.Enemy,true,NameFragment:true),
        "EntityNotExistsTransition" => new("target", "Absent entity", EntityResourceScope.Queryable, true),
        "Transform" or "TransformOnDeath" => new("target", "Transform into", EntityResourceScope.Resolvable, false),
        "TransferDamageOnDeath"=>new("target","Damage-record recipient",EntityResourceScope.Enemy,false),
        "Protect"=>new("protectee","Protected XML type",EntityResourceScope.Queryable,false),
        _ => null
    };
    public EntityParameter? EntityParameterFor(BehaviorDocument document, BehaviorNode node)
    {
        if (node.Kind is "State" or "ArgumentGroup" or "Factory" or "Reference" or "Spread" or "Opaque") return null;
        var kind = NativeKind(document, node); var parameter = EntityParameterFor(kind ?? "");
        if (kind == "EntityNotExistsTransition" && parameter != null && ReadConstantParameter(document, node, "checkAttackTarget", false, out var attackTarget) && attackTarget is true)
            return parameter with { InactiveReason = "This transition checks its attack target. The XML target is unused while checkAttackTarget is true." };
        return parameter;
    }
    public EntityReference ReadEntityReference(BehaviorDocument document, BehaviorNode node, EntityParameter parameter) => ReadConstantParameter(document, node, parameter.Name, null, out var value) && value is null or string ? new(true, (string?)value) : new(false, null);
    public void ApplyEntityReference(BehaviorDocument document, string entityId, Guid nodeId, long revision, string parameterName, string? resourceId)
    {
        var node = document.Find(nodeId); var parameter = EntityParameterFor(document, node);
        if (parameter == null || parameter.Name != parameterName || parameter.InactiveReason != null || resourceId == null && !parameter.AllowsPlayer || resourceId != null && !EntityResources.Accepts(parameter,resourceId))
            throw new InvalidOperationException("Choose an entity accepted by this native parameter.");
        var expression = resourceId == null ? "null" : SyntaxFactory.Literal(resourceId).ToFullString();
        document.Apply(new(revision, "Choose " + parameter.Label, [new(nodeId, parameter.Name, expression)]), candidate => Validate(candidate, entityId));
    }
}
