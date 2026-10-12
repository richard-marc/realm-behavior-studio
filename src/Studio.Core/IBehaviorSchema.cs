namespace Studio.Core;

public record ChildArgument(string Name, string ElementType, bool IsArray, bool IsParams, int Minimum = 0)
{
    public string? CollectionType { get; init; }
    public bool IsCollection => IsArray || CollectionType != null;
}
public record SourceParameter(string Name, string Type, string Default);
// Candidate source projections, not proof that native global initialization
// registered an entity. The actual loader decides range/definition precedence.
public record EncounterSource(string EntityId,int CallStart,int RootStart,int RootLength,bool Shared,string Boundary)
{
    public int OrderStart { get; init; } = -1;
}
public record SourceExpression(string Kind, string Label, bool IsState, IReadOnlyList<SourceParameter> Parameters,
    string[] ArgumentNames, IReadOnlyList<ChildArgument> Children)
{
    public bool RequiresCompiled { get; init; }
}
public record SourceDefinitionUse(int Start,int Length);
public record SourceDefinitionReference(string SourceFile,string Label,int Start,int Length);
public record SourceDefinition(string Key,string Label,int Start,int Length,ChildArgument? Collection,
    IReadOnlyList<string> Encounters,IReadOnlyList<SourceDefinitionUse> Uses,bool CompletePhaseContents,string Boundary)
{
    public bool StructurallyComplete {get;init;}
    public IReadOnlyList<string> CallerFiles {get;init;}=[];
}

public interface IBehaviorSchema
{
    IReadOnlyList<SourceDefinitionReference> ExternalDefinitions(int start,int length)=>[];
    IReadOnlyList<SourceDefinition> SourceDefinitions()=>[];
    bool AcceptsDefinitionElement(string expression,string element,bool spread=false)=>false;
    bool HasSourceContext => false;
    IBehaviorSchema ForSource(string source, string? sourceFile) => this;
    IBehaviorSchema At(int position) => this;
    SourceExpression? DescribeExpression(string expression) => null;
    IReadOnlyList<EncounterSource> AdditionalEncounters() => [];
    // null means that a phase-reference owner cannot be resolved without execution.
    bool? TargetsPhaseOf(string expression, string parameter, string ownerEntity, string targetEntity) => parameter.StartsWith("targetState", StringComparison.Ordinal) && ownerEntity == targetEntity;
    string? PhaseEntityParameter(string expression, string phaseParameter) => null;
    IEnumerable<DocumentDiagnostic> SourceDiagnostics() => [];
    string FragmentRoot(IReadOnlyList<string> expressions) => "new State(" + string.Join(",\n", expressions) + ")";
    string CollectionFragmentRoot(IReadOnlyList<string> expressions) => "new State(children: [" + string.Join(",\n", expressions) + "])";
    string ChildArray(string expression, string parameter, IReadOnlyList<string> children) => "new " + ChildArguments(expression).Single(p => p.Name == parameter).ElementType + "[] { " + string.Join(",\n", children) + " }";
    string ChildList(string expression, string parameter, IReadOnlyList<string> children) => ChildArray(expression, parameter, children);
    string[] ParameterNames(string kind);
    string[] ConstructorParameterNames(string expression);
    bool IsChildArgument(string expression, int argumentIndex);
    IReadOnlyList<ChildArgument> ChildArguments(string expression);
    bool AcceptsChild(string expression, string parameter, string childExpression, string? arrayExpression = null);
    bool AcceptsSpread(string expression, string parameter, string spreadExpression, string? collectionExpression = null) => false;
    string? ArrayElementType(string expression);
}
