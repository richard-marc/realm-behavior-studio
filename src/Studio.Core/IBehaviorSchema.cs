namespace Studio.Core;

public record ChildArgument(string Name, string ElementType, bool IsArray, bool IsParams, int Minimum = 0);

public interface IBehaviorSchema
{
    string[] ParameterNames(string kind);
    string[] ConstructorParameterNames(string expression);
    bool IsChildArgument(string expression, int argumentIndex);
    IReadOnlyList<ChildArgument> ChildArguments(string expression);
    bool AcceptsChild(string expression, string parameter, string childExpression, string? arrayExpression = null);
    string? ArrayElementType(string expression);
}
