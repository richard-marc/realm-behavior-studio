using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;
using TKR.WorldServer.logic;

namespace Studio.Raple;

public record ConstructorBinding(ConstructorInfo? Constructor, string[] SlotNames, int[] ParameterIndices, bool[] Expanded,
    string[] Errors, bool RequiresContext = false);

// Public signature mirrors exist only in an un-emitted semantic compilation. No game/user code runs during binding.
public sealed class NativeConstructorSchema
{
    static bool NativeName(string name, Type type) => name.Replace("global::", "") is var clean && (clean == type.Name || clean == type.FullName?.Replace('+', '.'));
    static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);
    readonly Dictionary<string, Type> types;
    readonly Dictionary<Type, string> enumNames;
    readonly CSharpCompilation signatures;
    readonly string imports;
    readonly ConcurrentDictionary<string, ConstructorBinding> cache = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, (Type? Type, object? Value, bool Constant)> values = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<(string, Type), bool> conversions = new();
    public NativeConstructorSchema(Assembly engine)
    {
        types = engine.GetTypes().Where(t => !t.IsInterface && !t.ContainsGenericParameters && (typeof(IStateChildren).IsAssignableFrom(t) || t.Name == "Cooldown"))
            .GroupBy(t => t.Name).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        static IEnumerable<Type> LeafTypes(Type t) => t.IsArray || t.IsByRef ? LeafTypes(t.GetElementType()!) : t.IsGenericType ? t.GetGenericArguments().SelectMany(LeafTypes) : [t];
        var helperTypes = types.Values.SelectMany(t => t.GetConstructors()).SelectMany(c => c.GetParameters()).SelectMany(p => LeafTypes(p.ParameterType)).Where(t => !t.IsEnum && !t.IsVisible && !types.ContainsKey(t.Name)).Distinct().ToArray();
        foreach (var helper in helperTypes) types.Add(helper.Name, helper);
        enumNames = types.Values.SelectMany(t => t.GetConstructors()).SelectMany(c => c.GetParameters()).SelectMany(p => LeafTypes(p.ParameterType))
            .Where(t => t.IsEnum && !t.IsVisible).Distinct().ToDictionary(t => t, t => t.Name);
        var source = new StringBuilder("namespace Studio.NativeSchema { [System.AttributeUsage(System.AttributeTargets.Constructor)] public sealed class NativeCtorAttribute : System.Attribute { public NativeCtorAttribute(int token) {} } ");
        foreach (var (type, enumName) in enumNames)
        {
            var underlying = Enum.GetUnderlyingType(type); var keyword = underlying.Name switch { "Byte" => "byte", "SByte" => "sbyte", "Int16" => "short", "UInt16" => "ushort", "Int32" => "int", "UInt32" => "uint", "Int64" => "long", _ => "ulong" };
            source.Append("public enum ").Append(enumName).Append(" : ").Append(keyword).Append(" {")
                .Append(string.Join(",", type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => "@" + f.Name + "=" + Convert.ToString(f.GetRawConstantValue(), CultureInfo.InvariantCulture) + (underlying == typeof(ulong) ? "UL" : "")))).Append('}');
        }
        foreach (var type in types.Values)
        {
            var parent = type.IsValueType ? "" : type.BaseType != null && types.ContainsKey(type.BaseType.Name) ? " : global::Studio.NativeSchema." + type.BaseType.Name : typeof(Behavior).IsAssignableFrom(type) ? " : global::TKR.WorldServer.logic.Behavior" : typeof(Transition).IsAssignableFrom(type) ? " : global::TKR.WorldServer.logic.Transition" : typeof(IStateChildren).IsAssignableFrom(type) ? " : global::TKR.WorldServer.logic.IStateChildren" : "";
            source.Append("public ").Append(type.IsAbstract ? "abstract " : "").Append(type.IsValueType ? "struct " : "class ").Append(type.Name).Append(parent).Append(" {");
            foreach (var constructor in type.GetConstructors())
            {
                source.Append("[NativeCtor(").Append(constructor.MetadataToken).Append(")] public ").Append(type.Name).Append('(');
                source.Append(string.Join(", ", constructor.GetParameters().Select(ParameterDeclaration)));
                source.Append(')');
                if (!type.IsValueType && type.BaseType != null && types.TryGetValue(type.BaseType.Name, out var mirroredBase))
                {
                    var baseConstructor = mirroredBase.GetConstructors().OrderBy(c => c.GetParameters().Length).FirstOrDefault();
                    source.Append(" : base(").Append(baseConstructor == null ? "" : string.Join(",", baseConstructor.GetParameters().Select(p => "default(" + TypeName(p.ParameterType) + ")"))).Append(')');
                }
                else if (typeof(Transition).IsAssignableFrom(type)) source.Append(" : base()");
                source.Append(" {}");
            }
            if (typeof(Transition).IsAssignableFrom(type)) source.Append("protected override bool TickCore(global::TKR.WorldServer.core.objects.Entity host, global::TKR.WorldServer.core.worlds.TickTime time, ref object state) => false;");
            foreach (var conversion in type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name is "op_Implicit" or "op_Explicit" && m.GetParameters().Length == 1))
                source.Append("public static ").Append(conversion.Name == "op_Implicit" ? "implicit" : "explicit").Append(" operator ").Append(TypeName(conversion.ReturnType)).Append('(').Append(TypeName(conversion.GetParameters()[0].ParameterType)).Append(" value) => default;");
            source.Append('}');
        }
        source.Append('}');
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];
        var references = paths.Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)).Select(a => a.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p));
        signatures = CSharpCompilation.Create("Studio.NativeSignatureModel", [CSharpSyntaxTree.ParseText(source.ToString(), ParseOptions)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var namespaces = types.Values.SelectMany(t => t.GetConstructors()).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType.IsArray ? p.ParameterType.GetElementType()! : p.ParameterType)
            .Where(t => t.Namespace != null).Select(t => t.Namespace!).Distinct().Order();
        imports = string.Join("\n", namespaces.Select(n => "using " + n + ";")) + "\n" + string.Join("\n", types.Keys.Concat(enumNames.Values).Select(n => "using " + n + " = Studio.NativeSchema." + n + ";"));
        // Fail loudly if mirrors cease to match the runtime's public signature types; never silently choose another overload.
        var errors = signatures.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException("Native signature schema could not be built: " + string.Join("\n", errors.Select(d => d.ToString()).Take(12)));
    }
    string ParameterDeclaration(ParameterInfo p) => (p.GetCustomAttribute<ParamArrayAttribute>() != null ? "params " : "") + TypeName(p.ParameterType) + " @" + p.Name + (p.HasDefaultValue ? " = " + DefaultLiteral(p) : "");
    string TypeName(Type type)
    {
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[]";
        if (enumNames.TryGetValue(type, out var enumName)) return "global::Studio.NativeSchema." + enumName;
        if (types.TryGetValue(type.Name, out var mirrored) && type == mirrored) return "global::Studio.NativeSchema." + type.Name;
        if (type.IsGenericType) return "global::" + type.GetGenericTypeDefinition().FullName!.Split('`')[0].Replace('+', '.') + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
        return "global::" + type.FullName!.Replace('+', '.');
    }
    string DefaultLiteral(ParameterInfo p)
    {
        var value = p.DefaultValue;
        if (value == null) return p.ParameterType.IsValueType ? "default(" + TypeName(p.ParameterType) + ")" : "null";
        if (p.ParameterType.IsEnum) return "(" + TypeName(p.ParameterType) + ")" + Convert.ToInt64(value, CultureInfo.InvariantCulture);
        return value switch { bool b => b ? "true" : "false", string s => SyntaxFactory.Literal(s).Text, float f => f.ToString("R", CultureInfo.InvariantCulture) + "F", double d => d.ToString("R", CultureInfo.InvariantCulture) + "D", decimal m => m.ToString(CultureInfo.InvariantCulture) + "M", _ => Convert.ToString(value, CultureInfo.InvariantCulture)! };
    }
    public ConstructorBinding Bind(string expression) => cache.GetOrAdd(expression, BindCore);
    public (Type? Type, object? Value, bool Constant) ValueInfo(string expression) => values.GetOrAdd(expression, code =>
    {
        var (model, syntax) = ExpressionModel(code, "object");
        var value = model.GetConstantValue(syntax);
        return (RuntimeType(model.GetTypeInfo(syntax).Type), value.HasValue ? value.Value : null, value.HasValue);
    });
    public bool CanConvert(string expression, Type expected) => conversions.GetOrAdd((expression, expected), key =>
    {
        var (model, _) = ExpressionModel(key.Item1, TypeName(key.Item2));
        return !model.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error);
    });
    (SemanticModel Model, ExpressionSyntax Expression) ExpressionModel(string expression, string target)
    {
        var rewritten = (ExpressionSyntax)new MirrorNames(types, enumNames).Visit(SyntaxFactory.ParseExpression(expression))!;
        var annotation = new SyntaxAnnotation("value");
        var fixture = CSharpSyntaxTree.ParseText(imports + "\nclass SchemaValue { void Check() { " + target + " value = PLACEHOLDER; } }", ParseOptions).GetRoot();
        var placeholder = fixture.DescendantNodes().OfType<IdentifierNameSyntax>().Single(n => n.Identifier.ValueText == "PLACEHOLDER");
        var tree = CSharpSyntaxTree.Create((CSharpSyntaxNode)fixture.ReplaceNode(placeholder, rewritten.WithAdditionalAnnotations(annotation)), ParseOptions);
        var model = signatures.AddSyntaxTrees(tree).GetSemanticModel(tree);
        return (model, (ExpressionSyntax)tree.GetRoot().GetAnnotatedNodes(annotation).Single());
    }
    Type? RuntimeType(ITypeSymbol? symbol)
    {
        if (symbol is IArrayTypeSymbol array) return RuntimeType(array.ElementType)?.MakeArrayType();
        if (symbol is not INamedTypeSymbol named) return null;
        if (named.ContainingNamespace.ToDisplayString() == "Studio.NativeSchema")
            return types.GetValueOrDefault(named.Name) ?? enumNames.FirstOrDefault(p => p.Value == named.Name).Key;
        var fullName = named.ContainingNamespace.ToDisplayString() + "." + named.MetadataName;
        var definition = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);
        if (definition == null) return null;
        if (named.TypeArguments.Length == 0) return definition;
        var arguments = named.TypeArguments.Select(RuntimeType).ToArray();
        return arguments.All(t => t != null) ? definition.MakeGenericType(arguments!) : null;
    }
    sealed class MirrorNames(Dictionary<string, Type> types, IReadOnlyDictionary<Type, string> enums) : CSharpSyntaxRewriter
    {
        TypeSyntax MirrorType(TypeSyntax syntax)
        {
            if (syntax is ArrayTypeSyntax array) return array.WithElementType(MirrorType(array.ElementType));
            if (syntax is NullableTypeSyntax nullable) return nullable.WithElementType(MirrorType(nullable.ElementType));
            var name = syntax.ToString().Split('.').Last().Replace("global::", "");
            return types.TryGetValue(name, out var type) && NativeName(syntax.ToString(), type) || enums.Any(e => e.Value == name && NativeName(syntax.ToString(), e.Key)) ? SyntaxFactory.ParseTypeName("Studio.NativeSchema." + name).WithTriviaFrom(syntax) : syntax;
        }
        public override SyntaxNode? VisitCastExpression(CastExpressionSyntax node) => ((CastExpressionSyntax)base.VisitCastExpression(node)!).WithType(MirrorType(node.Type));
        public override SyntaxNode? VisitArrayType(ArrayTypeSyntax node) => node.WithElementType(MirrorType(node.ElementType));
        public override SyntaxNode? VisitDefaultExpression(DefaultExpressionSyntax node) => node.WithType(MirrorType(node.Type));
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var name = node.Expression.ToString().Split('.').Last().Replace("global::", "");
            return enums.Any(e => e.Value == name && NativeName(node.Expression.ToString(), e.Key)) ? node.WithExpression(SyntaxFactory.ParseExpression("Studio.NativeSchema." + name)) : base.VisitMemberAccessExpression(node);
        }
        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var current = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
            var name = node.Type.ToString().Split('.').Last().Replace("global::", "");
            return types.TryGetValue(name, out var type) && NativeName(node.Type.ToString(), type) ? current.WithType(SyntaxFactory.ParseTypeName("Studio.NativeSchema." + name).WithTriviaFrom(current.Type)).WithAdditionalAnnotations(new SyntaxAnnotation("native-call", node.ToString())) : current;
        }
    }
    ConstructorBinding BindCore(string expression)
    {
        if (SyntaxFactory.ParseExpression(expression) is not ObjectCreationExpressionSyntax original || original.ContainsDiagnostics) return new(null, [], [], [], ["Invalid constructor expression."]);
        var name = original.Type.ToString().Split('.').Last().Replace("global::", "");
        if (!types.TryGetValue(name, out var runtime) || !NativeName(original.Type.ToString(), runtime)) return new(null, [], [], [], [], true);
        var rewritten = (ExpressionSyntax)new MirrorNames(types, enumNames).Visit(original)!;
        var marker = new SyntaxAnnotation("binding");
        var fixture = CSharpSyntaxTree.ParseText(imports + "\nclass SchemaCall { void Check() { object value = PLACEHOLDER; } }", ParseOptions).GetRoot();
        var placeholder = fixture.DescendantNodes().OfType<IdentifierNameSyntax>().Single(n => n.Identifier.ValueText == "PLACEHOLDER");
        var tree = CSharpSyntaxTree.Create((CSharpSyntaxNode)fixture.ReplaceNode(placeholder, rewritten.WithAdditionalAnnotations(marker)), ParseOptions);
        var compilation = signatures.AddSyntaxTrees(tree); var model = compilation.GetSemanticModel(tree);
        var call = (ObjectCreationExpressionSyntax)tree.GetRoot().GetAnnotatedNodes(marker).Single();
        var allDiagnostics = model.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        foreach (var nested in tree.GetRoot().GetAnnotatedNodes("native-call").OfType<ObjectCreationExpressionSyntax>().Where(n => n != call))
        {
            var originalExpression = nested.GetAnnotations("native-call").Single().Data!;
            var nestedOriginal = (ObjectCreationExpressionSyntax)SyntaxFactory.ParseExpression(originalExpression);
            var nestedName = nestedOriginal.Type.ToString().Split('.').Last().Replace("global::", "");
            cache.TryAdd(originalExpression, DescribeBinding(types[nestedName], nestedOriginal, nested, model, allDiagnostics));
        }
        return DescribeBinding(runtime, original, call, model, allDiagnostics);
    }
    static ConstructorBinding DescribeBinding(Type runtime, ObjectCreationExpressionSyntax original, ObjectCreationExpressionSyntax call, SemanticModel model, Diagnostic[] allDiagnostics)
    {
        var diagnostics = allDiagnostics.Where(d => call.Span.Contains(d.Location.SourceSpan)).ToArray();
        var context = diagnostics.Any(d => d.Id is "CS0103" or "CS0246" or "CS0234");
        if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol symbol)
            return new(null, [], [], [], diagnostics.Select(d => d.GetMessage()).ToArray(), context);
        var token = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "NativeCtorAttribute")?.ConstructorArguments.FirstOrDefault().Value;
        var constructor = token is int metadataToken ? runtime.GetConstructors().Single(c => c.MetadataToken == metadataToken) : null;
        if (constructor == null) return new(null, [], [], [], runtime.Name == "Cooldown" && original.ArgumentList?.Arguments.Count == 0 ? [] : ["Native constructor identity is unavailable."]);
        var parameters = constructor.GetParameters(); var arguments = original.ArgumentList!.Arguments; var mirroredArguments = call.ArgumentList!.Arguments;
        var slots = new string[arguments.Count]; var indices = new int[arguments.Count]; var expanded = new bool[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            var named = arguments[i].NameColon?.Name.Identifier.ValueText;
            var index = named == null ? Math.Min(i, parameters.Length - 1) : Array.FindIndex(parameters, p => p.Name == named);
            indices[i] = index;
            expanded[i] = parameters[index].GetCustomAttribute<ParamArrayAttribute>() != null && model.GetTypeInfo(mirroredArguments[i].Expression).ConvertedType is not IArrayTypeSymbol;
            slots[i] = parameters[index].Name! + (expanded[i] ? "[" + (i - index) + "]" : "");
        }
        return new(constructor, slots, indices, expanded, diagnostics.Select(d => d.GetMessage()).ToArray(), context);
    }
}
