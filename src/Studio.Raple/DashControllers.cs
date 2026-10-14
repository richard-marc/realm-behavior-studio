using System.Globalization;
using System.Reflection;
using Studio.Core;

namespace Studio.Raple;

public sealed record DashContract(double Speed, double Distance, double TargetRange, int IntervalMs, int InitialDelayMs, int TrailMs, bool FullDistance, int CooldownAfterEndMs, double DodgeChance);
public sealed record DashFlankContract(int Projectile, int CountPerSide, double SpreadDegrees, int CooldownMs, double SideDegrees, bool BossLabOnly);
public sealed partial class RapleAdapter
{
    public bool IsDashController(BehaviorDocument document, BehaviorNode node) => node.Kind is "Dash" or "DashFlankVolley" && NativeKind(document, node) == node.Kind;
    // Read constants only. Source getters/factories never execute in the editor.
    object?[]? DashArguments(BehaviorDocument document, BehaviorNode node)
    {
        var args = new List<object?>();
        foreach (var parameter in Constructor(RuntimeType(node.Kind)).GetParameters())
        {
            if (parameter.Name is "movement" or "dash") { args.Add(parameter.Name == "dash" ? Constructor(RuntimeType("Dash")).Invoke(Constructor(RuntimeType("Dash")).GetParameters().Select(p => p.DefaultValue).ToArray()) : null); continue; }
            if (!ReadConstantParameter(document, node, parameter.Name!, parameter.HasDefaultValue ? parameter.DefaultValue : null, out var value) || value == null) return null;
            try { args.Add(Convert.ChangeType(value, parameter.ParameterType, CultureInfo.InvariantCulture)); }
            catch (Exception error) when (error is InvalidCastException or FormatException or OverflowException) { return null; }
        }
        return args.ToArray();
    }
    public DashContract? ReadDash(BehaviorDocument document, BehaviorNode node)
    {
        if (!IsDashController(document, node) || node.Kind != "Dash" || DashArguments(document, node) is not { } args) return null;
        try { Constructor(RuntimeType("Dash")).Invoke(args); } catch (TargetInvocationException) { return null; }
        return new((double)args[0]!, (double)args[1]!, (double)args[2]!, (int)args[3]!, (int)args[4]!, (int)args[5]!, (bool)args[11]!, (int)args[12]!, (double)args[7]!);
    }
    public DashFlankContract? ReadDashFlank(BehaviorDocument document, BehaviorNode node)
    {
        if (!IsDashController(document, node) || node.Kind != "DashFlankVolley" || DashArguments(document, node) is not { } args) return null;
        try { Constructor(RuntimeType("DashFlankVolley")).Invoke(args); } catch (TargetInvocationException) { return null; }
        return new((int)args[1]!, (int)args[2]!, (double)args[3]!, (int)args[4]!, (double)args[5]!, (bool)args[6]!);
    }
    public static (double Left, double Right, double Increment) DashFans(DashFlankContract pattern, double heading)
    {
        if (!double.IsFinite(heading) || pattern.CountPerSide is < 1 or > 16 || !double.IsFinite(pattern.SpreadDegrees) || pattern.SpreadDegrees < 0 || !double.IsFinite(pattern.SideDegrees)) throw new ArgumentException("Choose finite native fan geometry.");
        var method = RuntimeType("DashFlankVolley").GetMethod("Fans", BindingFlags.Static | BindingFlags.NonPublic)!;
        var fans = ((double Left, double Right, double Increment))method.Invoke(null, [heading, pattern.CountPerSide, pattern.SpreadDegrees * Math.PI / 180, pattern.SideDegrees * Math.PI / 180])!;
        if (!double.IsFinite(fans.Left) || !double.IsFinite(fans.Right) || !double.IsFinite(fans.Increment)) throw new ArgumentException("Native fan geometry exceeds the finite diagram range.");
        return fans;
    }
    public IReadOnlyList<string> DashReferences(BehaviorDocument document, BehaviorNode phase) => phase.Kind != "State" ? [] : phase.Children
        .SelectMany(n => n.Kind == "ArgumentGroup" && n.Argument?.Name == "children" ? n.Children : [n])
        .Where(n => n.Kind == "Reference" && document.SchemaAt(n) is SourceBehaviorSchema scoped && scoped.StableDashReference(document.Expression(n)) &&
            document.ReferencedDefinitions(n).Any(d => d.Root.Kind == "Dash" && IsDashController(document, d.Root)))
        .Select(document.Expression).Distinct().ToArray();
    IEnumerable<DocumentDiagnostic> ValidateDashControllers(BehaviorDocument document, string? entityId)
    {
        var owners = document.Entities.Where(e => entityId == null || e.EntityId == entityId).Concat(document.Definitions.Where(d => d.Definition!.Encounters.Any(id => entityId == null || id == entityId)));
        foreach (var owner in owners) foreach (var node in owner.Root.Descendants().Where(n => IsDashController(document, n)))
        {
            if (DashArguments(document, node) is not { } args) continue;
            string? error = null;
            try { Constructor(RuntimeType(node.Kind)).Invoke(args); } catch (TargetInvocationException exception) { error = exception.GetBaseException().Message; }
            if (error != null) yield return new(node.Kind + ": " + error, node.Id);
        }
    }
}
