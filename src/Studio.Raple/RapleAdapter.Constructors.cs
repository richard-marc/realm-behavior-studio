using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;
using TKR.WorldServer.logic;

namespace Studio.Raple;
public sealed partial class RapleAdapter
{
    public static ConstructorBinding Binding(string expression) => ConstructorSchema.Value.Bind(expression);
    public string[] ConstructorParameterNames(string expression)
    {
        var binding = Binding(expression);
        if (binding.Constructor != null) return binding.SlotNames;
        var creation = SyntaxFactory.ParseExpression(expression) as ObjectCreationExpressionSyntax;
        return creation == null ? [] : ParameterNames(creation.Type.ToString().Split('.').Last());
    }
    public bool IsChildArgument(string expression, int argumentIndex)
    {
        var binding = Binding(expression);
        if (binding.Constructor == null || argumentIndex >= binding.ParameterIndices.Length)
            return SyntaxFactory.ParseExpression(expression) is ObjectCreationExpressionSyntax creation && creation.ArgumentList?.Arguments[argumentIndex].Expression is ObjectCreationExpressionSyntax child && child.Type.ToString().Split('.').Last() != "Cooldown";
        var type = binding.Constructor.GetParameters()[binding.ParameterIndices[argumentIndex]].ParameterType;
        if (type.IsArray) type = type.GetElementType()!;
        return typeof(IStateChildren).IsAssignableFrom(type);
    }
    static Type? ParameterType(ConstructorBinding binding, string name)
    {
        if (binding.Constructor == null) return null;
        var slot = Array.IndexOf(binding.SlotNames, name);
        if (slot >= 0)
        {
            var type = binding.Constructor.GetParameters()[binding.ParameterIndices[slot]].ParameterType;
            return binding.Expanded[slot] ? type.GetElementType() : type;
        }
        return binding.Constructor.GetParameters().FirstOrDefault(p => p.Name == name)?.ParameterType;
    }
    public IReadOnlyList<ChildArgument> ChildArguments(string expression) => Binding(expression).Constructor?.GetParameters()
        .Where(p => typeof(IStateChildren).IsAssignableFrom(p.ParameterType.IsArray ? p.ParameterType.GetElementType()! : p.ParameterType))
        .Select(p => new ChildArgument(p.Name!, (p.ParameterType.IsArray ? p.ParameterType.GetElementType()! : p.ParameterType).Name, p.ParameterType.IsArray, p.GetCustomAttribute<ParamArrayAttribute>() != null, !p.ParameterType.IsArray && !p.IsOptional || Binding(expression).Constructor!.DeclaringType!.Name == "Prioritize" ? 1 : 0)).ToArray() ?? [];
    static string CSharpTypeName(Type type) => string.Join(".", type.FullName!.Replace('+', '.').Split('.').Select(part => SyntaxFacts.GetKeywordKind(part) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None ? part : "@" + part));
    public string ChildArray(string expression, string parameter, IReadOnlyList<string> children)
    {
        var type = Binding(expression).Constructor?.GetParameters().Single(p => p.Name == parameter).ParameterType.GetElementType() ?? throw new ArgumentException("Select an actual native child array parameter.");
        return "new global::" + CSharpTypeName(type) + "[] { " + string.Join(",\n", children) + " }";
    }
    public string? ArrayElementType(string expression) => ConstructorSchema.Value.ValueInfo(expression).Type?.GetElementType() is { } type ? CSharpTypeName(type) : null;
    public bool AcceptsChild(string expression, string parameter, string childExpression, string? arrayExpression = null)
    {
        var p = Binding(expression).Constructor?.GetParameters().SingleOrDefault(p => p.Name == parameter);
        var element = arrayExpression == null ? null : ConstructorSchema.Value.ValueInfo(arrayExpression).Type?.GetElementType();
        var childType = ConstructorSchema.Value.ValueInfo(childExpression).Type;
        return p != null && childType != null && ConstructorSchema.Value.CanConvert("default(global::" + CSharpTypeName(childType) + ")", element ?? (p.ParameterType.IsArray ? p.ParameterType.GetElementType()! : p.ParameterType));
    }
    public bool AcceptsSpread(string expression, string parameter, string spreadExpression, string? collectionExpression = null)
    {
        var type = Binding(expression).Constructor?.GetParameters().FirstOrDefault(p => p.Name == parameter)?.ParameterType;
        return type?.IsArray == true && ConstructorSchema.Value.CanConvert("[" + spreadExpression + "]", type);
    }
    public IReadOnlyList<PrimitiveParameter> ParametersFor(BehaviorDocument document, BehaviorNode node)
    {
        if (document.SchemaAt(node)?.DescribeExpression(document.Expression(node)) is { } source)
            return source.Parameters.Where(p => source.Children.All(c => c.Name != p.Name)).Select(p => new PrimitiveParameter(p.Name, p.Type, p.Default, source.Kind == "Factory" ? "" : Unit(p.Name, source.Kind), source.Kind == "Factory" ? "Argument passed to this source helper. Its implementation defines the units and behavior." : Explain(p.Name))).ToArray();
        var binding = Binding(document.Expression(node));
        return binding.Constructor?.GetParameters().Select(p => new PrimitiveParameter(p.Name!, p.ParameterType.Name, p.HasDefaultValue ? FormatDefault(p) : p.GetCustomAttribute<ParamArrayAttribute>() != null ? "empty array" : "required", Unit(p.Name!, binding.Constructor?.DeclaringType?.Name), Explain(p.Name!))).ToArray()
            ?? Primitives.FirstOrDefault(p => p.Name == node.Kind)?.Parameters ?? [];
    }
    public PrimitiveParameter? ParameterFor(BehaviorDocument document, BehaviorNode node, Parameter parameter)
    {
        if (document.SchemaAt(node)?.DescribeExpression(document.Expression(node)) is { } source)
        {
            var p = source.Parameters.FirstOrDefault(p => p.Name == parameter.Name.Split('[')[0]);
            return p == null ? null : new(parameter.Name, p.Type.EndsWith("[]") && parameter.Name.Contains('[') ? p.Type[..^2] : p.Type, "provided", source.Kind == "Factory" ? "" : Unit(p.Name, source.Kind), source.Kind == "Factory" ? "Argument passed to this source helper. Its implementation defines the units and behavior." : Explain(p.Name));
        }
        var binding = Binding(document.Expression(node)); var type = ParameterType(binding, parameter.Name);
        var name = parameter.Name.Split('[')[0];
        return type == null ? null : new(parameter.Name, type.Name, "provided", Unit(name, binding.Constructor?.DeclaringType?.Name), Explain(name));
    }
    static object EvaluateConstructor(ObjectCreationExpressionSyntax creation, Type runtime)
    {
        var binding = Binding(creation.ToString());
        if (binding.RequiresContext) throw new NotSupportedException("This constructor requires source context; preserve/export it through the original compiler.");
        if (binding.Errors.Length > 0) throw new ArgumentException(string.Join("\n", binding.Errors));
        if (runtime.Name == "Cooldown" && binding.Constructor == null && creation.ArgumentList?.Arguments.Count == 0) return Activator.CreateInstance(runtime)!;
        var constructor = binding.Constructor ?? throw new NotSupportedException("Cannot resolve the native constructor for " + runtime.Name);
        var parameters = constructor.GetParameters(); var arguments = creation.ArgumentList!.Arguments;
        var values = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i]; var slots = Enumerable.Range(0, arguments.Count).Where(s => binding.ParameterIndices[s] == i).ToArray();
            if (parameter.GetCustomAttribute<ParamArrayAttribute>() != null && (slots.Length == 0 || binding.Expanded[slots[0]]))
            {
                var element = parameter.ParameterType.GetElementType()!; var array = Array.CreateInstance(element, slots.Length);
                for (var index = 0; index < slots.Length; index++) array.SetValue(Evaluate(arguments[slots[index]].Expression, element), index);
                values[i] = array;
            }
            else if (slots.Length > 0) values[i] = Evaluate(arguments[slots[0]].Expression, parameter.ParameterType);
            else if (parameter.HasDefaultValue) values[i] = parameter.DefaultValue ?? (parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null);
            else throw new ArgumentException("Missing constructor parameter " + parameter.Name);
        }
        return constructor.Invoke(values);
    }
}
