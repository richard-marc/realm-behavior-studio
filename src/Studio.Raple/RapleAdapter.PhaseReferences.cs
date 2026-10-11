using BossPatternSim;
using Studio.Core;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Raple;

public sealed partial class RapleAdapter
{
    internal static bool NativeOrderType(string? fullName) => fullName is "TKR.WorldServer.logic.behaviors.Order" or "TKR.WorldServer.logic.behaviors.OrderOnce" or "TKR.WorldServer.logic.behaviors.OrderOnDeath";
    internal static bool SameEntity(string first, string second) => first == second || SimEngine.Data.IdToObjectType.TryGetValue(first, out var firstType) && SimEngine.Data.IdToObjectType.TryGetValue(second, out var secondType) && firstType == secondType;
    public bool? TargetsPhaseOf(string expression, string parameter, string ownerEntity, string targetEntity)
    {
        var binding = Binding(expression); var type = binding.Constructor?.DeclaringType;
        if (!NativeOrderType(type?.FullName)) return parameter.StartsWith("targetState", StringComparison.Ordinal) && SameEntity(ownerEntity, targetEntity);
        if (parameter != (type!.Name == "OrderOnDeath" ? "state" : "targetState")) return false;
        var slot = Array.IndexOf(binding.SlotNames, type.Name == "OrderOnDeath" ? "target" : "children");
        if (slot < 0 || SyntaxFactory.ParseExpression(expression) is not ObjectCreationExpressionSyntax creation) return null;
        var value = ConstructorSchema.Value.ValueInfo(creation.ArgumentList!.Arguments[slot].Expression.ToString());
        return value.Constant && value.Value is string entity ? SameEntity(entity, targetEntity) : null;
    }
}
