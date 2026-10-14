using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Studio.Core;

namespace Studio.Raple;

// A semantic view of the original server, with the project source documents substituted.
// No helper or property getter is executed while describing the authoring model.
internal sealed class SourceSchemaCache(string serverRoot,IReadOnlyDictionary<string,string>? overlays=null,SourceSchemaCache? shared=null)
{
    sealed class OriginalSources
    {
        public readonly object Gate=new();
        public CSharpCompilation? Baseline;
        public string? Stamp;
    }
    readonly OriginalSources originalSources=shared?.originalSources??new();
    public (CSharpCompilation Compilation, SyntaxTree Tree) Bind(string source, string file)
    {
        lock (originalSources.Gate)
        {
            var paths = SourceVerification.SourcePaths(serverRoot);
            var references = SourceVerification.ReferencePaths(Path.Combine(serverRoot, "TKR.WorldServer"));
            var inputs = paths.Concat(references).Append(Path.Combine(serverRoot, "TKR.WorldServer/obj/project.assets.json"));
            var current = string.Join("\n", inputs.Select(p => { var info = new FileInfo(p); return p + ":" + info.Length + ":" + info.LastWriteTimeUtc.Ticks; }));
            if (originalSources.Baseline == null || current != originalSources.Stamp)
            {
                originalSources.Baseline = SourceVerification.SemanticCompilation(serverRoot, paths, references);
                originalSources.Stamp = current;
            }
            var compilation=originalSources.Baseline;
            foreach(var replacement in overlays??new Dictionary<string,string>())
            {
                var existing=compilation.SyntaxTrees.Single(t=>Path.GetFullPath(t.FilePath).Equals(replacement.Key,StringComparison.OrdinalIgnoreCase));
                compilation=compilation.ReplaceSyntaxTree(existing,CSharpSyntaxTree.ParseText(SourceText.From(replacement.Value,System.Text.Encoding.UTF8),(CSharpParseOptions)existing.Options,existing.FilePath));
            }
            var original = compilation.SyntaxTrees.SingleOrDefault(t => Path.GetFullPath(t.FilePath).Equals(Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("The source context must identify an original server file.");
            var edited = CSharpSyntaxTree.ParseText(SourceText.From(source, System.Text.Encoding.UTF8), (CSharpParseOptions)original.Options, original.FilePath);
            return (compilation.ReplaceSyntaxTree(original, edited), edited);
        }
    }
}

internal sealed partial class SourceBehaviorSchema : IBehaviorSchema
{
    sealed class Context
    {
        public readonly Lazy<(CSharpCompilation Compilation, SyntaxTree Tree)> Binding;
        public bool Prepared { get; }
        public Context(SourceSchemaCache cache, string source, string file) { Binding = new(() => cache.Bind(source, file)); }
        public Context(CSharpCompilation compiler, SyntaxTree tree) { Binding = new(() => (compiler, tree)); Prepared = true; }
        public readonly Dictionary<(int Position, string Expression), SourceExpression?> Descriptions = new();
        SemanticModel? model;
        IReadOnlyList<EncounterSource>? encounters;
        public IReadOnlyList<EncounterSource> Encounters=>encounters??=NativeRegistrations.Discover(Model).Where(r=>r.Shared).ToArray();
        public IReadOnlyList<SourceDefinition>? Definitions;
        public SemanticModel Model => model ??= Binding.Value.Compilation.GetSemanticModel(Binding.Value.Tree);
    }
    readonly RapleAdapter adapter;
    readonly Context context;
    readonly int position;
    public bool HasSourceContext => true;
    public IReadOnlyList<EncounterSource> AdditionalEncounters()=>context.Encounters;
    public string FragmentRoot(IReadOnlyList<string> expressions) => adapter.FragmentRoot(expressions);
    public string CollectionFragmentRoot(IReadOnlyList<string> expressions) => adapter.CollectionFragmentRoot(expressions);
    IMethodSymbol? SourceMethod(ExpressionSyntax expression)=>expression is InitializerExpressionSyntax||IsIndexedEntry(expression)?EntryMethod(Model,expression):Model.GetSymbolInfo(expression).Symbol as IMethodSymbol;
    public string ChildList(string expression, string parameter, IReadOnlyList<string> children)
    {
        var source = Expression(expression);
        if (source != null && SourceMethod(source) is IMethodSymbol method && method.Parameters.Single(p => p.Name == parameter).Type is { } type && type is not IArrayTypeSymbol && Collection(type))
            return "[" + string.Join(",\n", children) + "]";
        return ChildArray(expression, parameter, children);
    }
    public string ChildArray(string expression, string parameter, IReadOnlyList<string> children)
    {
        var source = Expression(expression);
        if (source != null && SourceMethod(source) is IMethodSymbol method && method.Parameters.Single(p => p.Name == parameter).Type is IArrayTypeSymbol array)
            return "new " + array.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "[] { " + string.Join(",\n", children) + " }";
        return adapter.ChildArray(expression, parameter, children);
    }
    public SourceBehaviorSchema(RapleAdapter adapter, SourceSchemaCache cache, string source, string file)
        : this(adapter, new Context(cache, source, file), -1) { }
    public SourceBehaviorSchema(RapleAdapter adapter, CSharpCompilation compiler, string file)
        : this(adapter, new Context(compiler, compiler.SyntaxTrees.Single(t => Path.GetFullPath(t.FilePath).Equals(Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))), -1) { }
    SourceBehaviorSchema(RapleAdapter adapter, Context context, int position) { this.adapter = adapter; this.context = context; this.position = position; }
    public IBehaviorSchema At(int start) => new SourceBehaviorSchema(adapter, context, start);
    public IBehaviorSchema ForSource(string source, string? file) => file == null && context.Prepared && context.Binding.Value.Tree.GetText().ToString() == source ? this : adapter.ForSource(source, file);
    SemanticModel Model => context.Model;
    internal bool TryConstant(string text, out object? value)
    {
        value = null; var expression = Expression(text); if (expression == null) return false;
        var constant = Model.GetConstantValue(expression); if (!constant.HasValue) return false;
        value = constant.Value; return true;
    }
    ExpressionSyntax? Expression(string text)
    {
        if (position < 0) return null;
        var tree = context.Binding.Value.Tree;
        var node = tree.GetRoot().FindToken(position).Parent;
        return node?.AncestorsAndSelf().OfType<ExpressionSyntax>().FirstOrDefault(e => e.SpanStart == position && e.ToString() == text);
    }
    static bool Native(ITypeSymbol? type, string name) => type?.ToDisplayString() == "TKR.WorldServer.logic." + name;
    static bool StateType(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType) if (Native(current, "State")) return true;
        return false;
    }
    static bool Child(ITypeSymbol? type) => Native(type, "IStateChildren") || type is INamedTypeSymbol named && named.AllInterfaces.Any(t => Native(t, "IStateChildren"));
    static ITypeSymbol Element(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return array.ElementType;
        if (Child(type) || type is not INamedTypeSymbol named) return type;
        var values=named.AllInterfaces.Prepend(named).Where(t=>t.OriginalDefinition.MetadataName is "IDictionary`2" or "IReadOnlyDictionary`2"&&t.ContainingNamespace.ToDisplayString()=="System.Collections.Generic").Select(t=>t.TypeArguments[1]).Where(Child).Distinct<ITypeSymbol>(SymbolEqualityComparer.Default).ToArray();
        if(values.Length==1)return values[0];
        var elements = named.AllInterfaces.Prepend(named).Where(t => t.OriginalDefinition.MetadataName == "IEnumerable`1" && t.ContainingNamespace.ToDisplayString() == "System.Collections.Generic")
            .Select(t => t.TypeArguments[0]).Distinct<ITypeSymbol>(SymbolEqualityComparer.Default).ToArray();
        return elements.Length == 1 ? elements[0] : type;
    }
    static bool Collection(ITypeSymbol type) => !SymbolEqualityComparer.Default.Equals(type, Element(type));
    static string TypeName(ITypeSymbol type) => type is IArrayTypeSymbol array ? TypeName(array.ElementType) + "[]" : type is INamedTypeSymbol { IsGenericType: true } ? type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) : type.Name;
    static bool SameType(ITypeSymbol symbol, Type runtime)
    {
        if (symbol is IArrayTypeSymbol array) return runtime.IsArray && array.Rank == runtime.GetArrayRank() && SameType(array.ElementType, runtime.GetElementType()!);
        if (symbol is not INamedTypeSymbol named || runtime.IsArray) return false;
        var definition = runtime.IsGenericType ? runtime.GetGenericTypeDefinition() : runtime;
        var symbolName = (named.ContainingType != null ? named.ContainingType.ToDisplayString() + "." : named.ContainingNamespace.IsGlobalNamespace ? "" : named.ContainingNamespace.ToDisplayString() + ".") + named.MetadataName;
        return symbolName.Replace("@", "") == definition.FullName?.Replace('+', '.') && (!runtime.IsGenericType || named.TypeArguments.Length == runtime.GetGenericArguments().Length && named.TypeArguments.Zip(runtime.GetGenericArguments()).All(p => SameType(p.First, p.Second)));
    }
    static string Default(IParameterSymbol parameter) => parameter.IsParams ? "empty array" : !parameter.HasExplicitDefaultValue ? "required" : parameter.ExplicitDefaultValue switch
    {
        null => parameter.Type.IsValueType ? "default" : "null",
        bool value => value ? "true" : "false",
        string value => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, true),
        char value => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, true),
        object value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default"
    };
    public SourceExpression? DescribeExpression(string text)
    {
        lock (context.Descriptions)
        {
            var key = (position, text);
            if (context.Descriptions.TryGetValue(key, out var cached)) return cached;
            return context.Descriptions[key] = Describe(text);
        }
    }
    SourceExpression? Describe(string text)
    {
        var expression = Expression(text); if (expression == null) return null;
        var model = Model;
        if(expression is InitializerExpressionSyntax entry&&entry.IsKind(SyntaxKind.ComplexElementInitializerExpression))return DescribeEntry(entry);
        if(expression is AssignmentExpressionSyntax indexed&&IsIndexedEntry(indexed))return DescribeIndexedEntry(indexed);
        var type = model.GetTypeInfo(expression).Type;
        if (!Child(type)) return null;
        var symbol = model.GetSymbolInfo(expression).Symbol;
        if (expression is not (InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax))
            return symbol is IFieldSymbol or IPropertySymbol or ILocalSymbol or IParameterSymbol
                ? new("Reference", symbol.Name, StateType(type), [], [], []) : null;
        if (symbol is not IMethodSymbol method) return null; // Never choose an ambiguous overload.
        var arguments = expression is InvocationExpressionSyntax call ? call.ArgumentList.Arguments : ((BaseObjectCreationExpressionSyntax)expression).ArgumentList?.Arguments??default;
        var slots = new string[arguments.Count]; var expandedIndices = new Dictionary<string, int>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var named = arguments[i].NameColon?.Name.Identifier.ValueText;
            var parameter = named == null ? method.Parameters.ElementAtOrDefault(Math.Min(i, method.Parameters.Length - 1)) : method.Parameters.FirstOrDefault(p => p.Name == named);
            if (parameter == null) return null;
            var expanded = parameter.IsParams && model.GetTypeInfo(arguments[i].Expression).ConvertedType is not IArrayTypeSymbol;
            var count = expandedIndices.GetValueOrDefault(parameter.Name); expandedIndices[parameter.Name] = count + 1;
            slots[i] = parameter.Name + (expanded ? "[" + count + "]" : "");
        }
        var children = method.Parameters.Where(p => Child(Element(p.Type)))
            .Select(p => new ChildArgument(p.Name, Element(p.Type).Name, p.Type is IArrayTypeSymbol, p.IsParams, !Collection(p.Type) && !p.IsOptional || expression is BaseObjectCreationExpressionSyntax && type!.Name == "Prioritize" ? 1 : 0)
            { CollectionType = p.Type is not IArrayTypeSymbol && Collection(p.Type) ? p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null }).ToArray();
        var standalone = RapleAdapter.Binding(text);
        var actualType = method.ContainingType.ToDisplayString().Replace("@", "");
        var nativeType = Native(type, "State") || adapter.Primitives.Any(p => p.Name == type!.Name) && RapleAdapter.NativeTypeName(type!.Name) == type.ToDisplayString().Replace("@", "");
        var kind = expression is InvocationExpressionSyntax ? "Factory" : nativeType ? type!.Name : type!.ToDisplayString();
        return new(kind,
            expression is InvocationExpressionSyntax ? method.Name + " · " + type!.Name : type!.Name,
            StateType(type), method.Parameters.Select(p => new SourceParameter(p.Name, TypeName(p.Type), Default(p))).ToArray(), slots, children)
        { RequiresCompiled = expression is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax { Initializer: not null } || standalone.RequiresContext || standalone.Constructor is not { } constructor || constructor.DeclaringType?.FullName?.Replace('+', '.') != actualType ||
            constructor.GetParameters().Length != method.Parameters.Length || !method.Parameters.Zip(constructor.GetParameters()).All(p => SameType(p.First.Type, p.Second.ParameterType)) };
    }
    public IEnumerable<DocumentDiagnostic> SourceDiagnostics() => (adapter.HasAdditionalSources ? context.Binding.Value.Compilation.GetDiagnostics() : Model.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)
        .Select(d => new DocumentDiagnostic($"{(adapter.HasAdditionalSources?Path.GetFileName(d.Location.GetLineSpan().Path)+": ":"")}Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}"));
    public string[] ParameterNames(string kind) => adapter.ParameterNames(kind);
    public string[] ConstructorParameterNames(string expression) => DescribeExpression(expression)?.ArgumentNames ?? adapter.ConstructorParameterNames(expression);
    public bool? TargetsPhaseOf(string text, string parameter, string ownerEntity, string targetEntity)
    {
        var expression = Expression(text);
        if(expression is InitializerExpressionSyntax||IsIndexedEntry(expression))return false;
        if(expression!=null&&Model.GetSymbolInfo(expression).Symbol is IMethodSymbol choice&&choice.ContainingType.ToDisplayString()==RapleAdapter.NativeTypeName("TimedRandomTransition")&&parameter.Split('[')[0]=="states")return RapleAdapter.SameEntity(ownerEntity,targetEntity);
        if (expression == null || Model.GetSymbolInfo(expression).Symbol is not IMethodSymbol method || !RapleAdapter.NativeOrderType(method.ContainingType.ToDisplayString()))
            return parameter.StartsWith("targetState", StringComparison.Ordinal) && RapleAdapter.SameEntity(ownerEntity, targetEntity);
        var death = method.ContainingType.Name == "OrderOnDeath";
        if (parameter != (death ? "state" : "targetState")) return false;
        var slot = Array.IndexOf(DescribeExpression(text)!.ArgumentNames, death ? "target" : "children");
        if (slot < 0 || expression is not ObjectCreationExpressionSyntax creation) return null;
        var constant = Model.GetConstantValue(creation.ArgumentList!.Arguments[slot].Expression);
        return constant.HasValue && constant.Value is string entity ? RapleAdapter.SameEntity(entity, targetEntity) : null;
    }
    public bool IsChildArgument(string expression, int index)
    {
        var description = DescribeExpression(expression);
        return description == null ? adapter.IsChildArgument(expression, index) : index < description.ArgumentNames.Length && description.Children.Any(p => p.Name == description.ArgumentNames[index].Split('[')[0]);
    }
    public string? PhaseEntityParameter(string text, string phaseParameter)
    {
        var expression = Expression(text);
        if (expression == null || Model.GetSymbolInfo(expression).Symbol is not IMethodSymbol method || !RapleAdapter.NativeOrderType(method.ContainingType.ToDisplayString())) return null;
        var death = method.ContainingType.Name == "OrderOnDeath";
        return phaseParameter == (death ? "state" : "targetState") ? death ? "target" : "children" : null;
    }
    public IReadOnlyList<ChildArgument> ChildArguments(string expression) => DescribeExpression(expression)?.Children ?? adapter.ChildArguments(expression);
    public string? ArrayElementType(string text)
    {
        var expression = Expression(text);
        return expression != null && (Model.GetTypeInfo(expression).Type ?? Model.GetTypeInfo(expression).ConvertedType) is IArrayTypeSymbol array ? array.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "") : adapter.ArrayElementType(text);
    }
    public bool AcceptsChild(string text, string parameter, string childExpression, string? arrayExpression = null)
    {
        var expression = Expression(text);
        if (expression == null) return adapter.AcceptsChild(text, parameter, childExpression, arrayExpression);
        if(childExpression.TrimStart().StartsWith('{'))return AcceptEntry(expression,arrayExpression,childExpression);
        if(childExpression.TrimStart().StartsWith('[')&&TryIndexedEntry(childExpression)!=null)return AcceptIndexedEntry(expression,arrayExpression,childExpression);
        var model = Model;
        if ((expression is InitializerExpressionSyntax||IsIndexedEntry(expression))&&EntryMethod(model,expression) is IMethodSymbol add)
        {
            var parameterSymbol=add.Parameters.SingleOrDefault(p=>p.Name==parameter);if(parameterSymbol==null)return false;
            var targetType=Element(parameterSymbol.Type);
            if(arrayExpression!=null&&model.GetSpeculativeTypeInfo(expression.SpanStart,SyntaxFactory.ParseExpression(arrayExpression),SpeculativeBindingOption.BindAsExpression).Type is {} entryCollectionType&&Collection(entryCollectionType))targetType=Element(entryCollectionType);
            return model.GetSpeculativeTypeInfo(expression.SpanStart,SyntaxFactory.ParseExpression(childExpression),SpeculativeBindingOption.BindAsExpression).Type is {} childType&&context.Binding.Value.Compilation.ClassifyConversion(childType,targetType).IsImplicit;
        }
        if (model.GetSymbolInfo(expression).Symbol is not IMethodSymbol method) return false;
        var slot = method.Parameters.FirstOrDefault(p => p.Name == parameter);var member=MemberElement(expression,parameter,arrayExpression);if (slot == null&&member==null) return false;
        var child = SyntaxFactory.ParseExpression(childExpression);
        var type = model.GetSpeculativeTypeInfo(expression.SpanStart, child, SpeculativeBindingOption.BindAsExpression).Type;
        ITypeSymbol target = member??Element(slot!.Type);
        if (arrayExpression != null && model.GetSpeculativeTypeInfo(expression.SpanStart, SyntaxFactory.ParseExpression(arrayExpression), SpeculativeBindingOption.BindAsExpression).Type is { } collectionType && Collection(collectionType)) target = Element(collectionType);
        return type != null && context.Binding.Value.Compilation.ClassifyConversion(type, target).IsImplicit;
    }
    public bool AcceptsSpread(string text, string parameter, string spreadExpression, string? collectionExpression = null)
    {
        var expression = Expression(text); if (expression == null) return adapter.AcceptsSpread(text, parameter, spreadExpression, collectionExpression);
        if (SourceMethod(expression) is not IMethodSymbol method || method.Parameters.FirstOrDefault(p => p.Name == parameter)?.Type is not { } target || !Collection(target)) return false;
        return Model.ClassifyConversion(expression.SpanStart, SyntaxFactory.ParseExpression("[" + spreadExpression + "]"), target).IsImplicit;
    }
}
