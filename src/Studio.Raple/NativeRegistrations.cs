using BossPatternSim;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;

// Discovery describes source registrations without invoking factories. A range
// is a shared source template; runtime registration/skip order remains native.
internal static class NativeRegistrations
{
    public static IEnumerable<ExpressionSyntax> UnresolvedRoots(SemanticModel model,IReadOnlyList<EncounterSource> known)
    {
        // Chained Init invocations share SpanStart; the member name identifies each registration.
        var starts=known.Select(r=>r.OrderStart).ToHashSet();
        foreach(var call in model.SyntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var start=call.Expression is MemberAccessExpressionSyntax member?member.Name.SpanStart:call.Expression.SpanStart;
            if(starts.Contains(start)||model.GetSymbolInfo(call).Symbol is not IMethodSymbol method||method.ContainingType.ToDisplayString()!="TKR.WorldServer.logic.BehaviorDb.ctor"||method.Name is not ("Init" or "InitMany"))continue;
            var positional=0;
            foreach(var argument in call.ArgumentList.Arguments)
            {
                var name=argument.NameColon?.Name.Identifier.ValueText;
                if(name==null){if(positional>=method.Parameters.Length)continue;name=method.Parameters[positional].Name;}
                var index=Array.FindIndex(method.Parameters.ToArray(),p=>p.Name==name);if(index<0)continue;
                if(name=="rootState")yield return argument.Expression;
                positional=index+1;
            }
        }
    }
    public static IReadOnlyList<EncounterSource> Discover(SemanticModel model)
    {
        var result=new List<EncounterSource>();
        foreach(var call in model.SyntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if(model.GetSymbolInfo(call).Symbol is not IMethodSymbol method ||
                method.ContainingType.ToDisplayString()!="TKR.WorldServer.logic.BehaviorDb.ctor" || method.Name is not ("Init" or "InitMany"))continue;
            var arguments=new Dictionary<string,ExpressionSyntax>(StringComparer.Ordinal);var positional=0;
            foreach(var argument in call.ArgumentList.Arguments)
            {
                var name=argument.NameColon?.Name.Identifier.ValueText;
                if(name==null){if(positional>=method.Parameters.Length)continue;name=method.Parameters[positional].Name;}
                var index=Array.FindIndex(method.Parameters.ToArray(),p=>p.Name==name);if(index<0)continue;
                arguments.TryAdd(name,argument.Expression);positional=index+1;
            }
            string? Constant(string name)=>arguments.TryGetValue(name,out var expression)&&model.GetConstantValue(expression) is {HasValue:true,Value:string value}?value:null;
            if(!arguments.TryGetValue("rootState",out var supplied))continue;
            var order=call.Expression is MemberAccessExpressionSyntax member?member.Name.SpanStart:call.Expression.SpanStart;
            if(method.Name=="Init")
            {
                if(Constant("id") is {} id)result.Add(new(id,call.SpanStart,supplied.SpanStart,supplied.Span.Length,false,""){OrderStart=order});
                continue;
            }
            if(Constant("objTypeMin") is not {} minimum || Constant("objTypeMax") is not {} maximum ||
                !SimEngine.Data.IdToObjectType.TryGetValue(minimum,out var first)||!SimEngine.Data.IdToObjectType.TryGetValue(maximum,out var last))continue;
            // A direct lambda body has an exact editable State expression. Other
            // delegate forms retain their source boundary instead of executing.
            var body=supplied switch {SimpleLambdaExpressionSyntax {Body:ExpressionSyntax expression}=>expression,
                ParenthesizedLambdaExpressionSyntax {Body:ExpressionSyntax expression}=>expression,_=>null};
            var root=body??supplied;
            var gap=Enumerable.Range(first,Math.Max(0,last-first+1)).FirstOrDefault(type=>!SimEngine.Data.ObjectTypeToId.ContainsKey((ushort)type),-1);
            var boundary=body==null?"Shared range delegate retained in source; its generated State requires compiled discovery.":"Shared range template; edits affect every applicable native registration. Existing native definitions take precedence.";
            if(gap>=0)boundary+=$" XML has no canonical ID for 0x{gap:X4}; native initialization fails there unless an existing definition skips that type. These are source candidates, not confirmed registrations.";
            for(var type=(int)first;type<=last;type++)
            {
                if(!SimEngine.Data.ObjectTypeToId.TryGetValue((ushort)type,out var id))continue;
                result.Add(new(id,call.SpanStart,root.SpanStart,root.Span.Length,true,boundary){OrderStart=order});
            }
        }
        return result;
    }
}
