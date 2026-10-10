namespace Studio.Core;

public interface IBehaviorSchema
{
    string[] ParameterNames(string kind);
    string[] ConstructorParameterNames(string expression);
    bool IsChildArgument(string expression, int argumentIndex);
}
