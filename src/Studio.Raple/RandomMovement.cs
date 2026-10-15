using Studio.Core;

namespace Studio.Raple;

public sealed record SwirlSettings(double Speed, double Radius, double AcquireRange, bool Targeted);
public sealed record JumpOffsetSettings(int MinX, int MaxX, int MinY, int MaxY)
{
    public long Columns => Math.Max(1L, (long)MaxX - MinX);
    public long Rows => Math.Max(1L, (long)MaxY - MinY);
    public int LastX => MaxX == MinX ? MinX : MaxX - 1;
    public int LastY => MaxY == MinY ? MinY : MaxY - 1;
}

public sealed partial class RapleAdapter
{
    public bool IsRandomMovement(BehaviorDocument document, BehaviorNode node) => node.Kind is "Swirl" or "JumpToRandomOffset" && NativeKind(document, node) == node.Kind;
    public SwirlSettings? ReadSwirl(BehaviorDocument document, BehaviorNode node)
    {
        if (!IsRandomMovement(document, node) || node.Kind != "Swirl" ||
            !ReadNumericParameter(document, node, "speed", 1, out var speed) || speed == null ||
            !ReadNumericParameter(document, node, "radius", 8, out var radius) || radius == null ||
            !ReadNumericParameter(document, node, "acquireRange", 10, out var acquire) || acquire == null ||
            new[] { speed.Value, radius.Value, acquire.Value }.Any(v => !float.IsFinite((float)v)) ||
            !ReadConstantParameter(document, node, "targeted", true, out var target) || target is not bool targeted) return null;
        return new(speed.Value, radius.Value, acquire.Value, targeted);
    }
    public JumpOffsetSettings? ReadJumpOffset(BehaviorDocument document, BehaviorNode node)
    {
        if (!IsRandomMovement(document, node) || node.Kind != "JumpToRandomOffset") return null;
        var result = new List<int>();
        foreach (var name in new[] { "minX", "maxX", "minY", "maxY" })
        {
            if (!ReadNumericParameter(document, node, name, null, out var value) || value == null ||
                value < int.MinValue || value > int.MaxValue || value != Math.Truncate(value.Value)) return null;
            result.Add((int)value.Value);
        }
        return new(result[0], result[1], result[2], result[3]);
    }
    IEnumerable<DocumentDiagnostic> ValidateRandomMovement(BehaviorDocument document, string? entityId)
    {
        var owners = document.Entities.Where(e => entityId == null || e.EntityId == entityId)
            .Concat(document.Definitions.Where(d => d.Definition!.Encounters.Any(id => entityId == null || id == entityId)));
        foreach (var owner in owners) foreach (var node in owner.Root.Descendants().Where(n => IsRandomMovement(document, n)))
        {
            if (ReadJumpOffset(document, node) is { } jump && (jump.MaxX < jump.MinX || jump.MaxY < jump.MinY))
                yield return new("Jump offset minimums must not exceed maximums: native Random.Next rejects this range on entry.", node.Id);
            if (ReadSwirl(document, node) is { Radius: 0 })
                yield return new("Swirl divides by its radius during native movement. Zero can produce non-finite positions; choose a nonzero radius before Play.", node.Id, false);
        }
    }
}
