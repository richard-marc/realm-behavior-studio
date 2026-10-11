using Studio.Core;

namespace Studio.Raple;

// Presentation bindings prepared from the same compiler that emitted the runtime
// assembly. The worker consumes these immutable facts without recompiling C#.
public sealed class PreparedBehaviorSchema : IBehaviorSchema
{
    readonly RapleAdapter adapter;
    readonly IReadOnlyDictionary<(int Start, string Expression), SourceExpression> bindings;
    readonly int position;
    readonly IReadOnlyList<EncounterSource> registrations;
    public PreparedBehaviorSchema(RapleAdapter adapter, IEnumerable<RuntimeSourceBinding> bindings,IReadOnlyList<EncounterSource>? registrations=null)
        : this(adapter, bindings.ToDictionary(b => (b.Start, b.Expression), b => b.Description), -1,registrations??[]) { }
    PreparedBehaviorSchema(RapleAdapter adapter, IReadOnlyDictionary<(int Start, string Expression), SourceExpression> bindings, int position,IReadOnlyList<EncounterSource> registrations)
    { this.adapter = adapter; this.bindings = bindings; this.position = position;this.registrations=registrations; }
    public IReadOnlyList<EncounterSource> AdditionalEncounters()=>registrations;
    public IBehaviorSchema At(int start) => new PreparedBehaviorSchema(adapter, bindings, start,registrations);
    public SourceExpression? DescribeExpression(string expression) => bindings.GetValueOrDefault((position, expression));
    public string[] ParameterNames(string kind) => adapter.ParameterNames(kind);
    public string[] ConstructorParameterNames(string expression) => DescribeExpression(expression)?.ArgumentNames ?? adapter.ConstructorParameterNames(expression);
    public bool IsChildArgument(string expression, int index) => DescribeExpression(expression) is { } source
        ? index < source.ArgumentNames.Length && source.Children.Any(c => c.Name == source.ArgumentNames[index].Split('[')[0]) : adapter.IsChildArgument(expression, index);
    public IReadOnlyList<ChildArgument> ChildArguments(string expression) => DescribeExpression(expression)?.Children ?? adapter.ChildArguments(expression);
    public bool AcceptsChild(string expression, string parameter, string childExpression, string? arrayExpression = null) => adapter.AcceptsChild(expression, parameter, childExpression, arrayExpression);
    public string? ArrayElementType(string expression) => adapter.ArrayElementType(expression);
}
