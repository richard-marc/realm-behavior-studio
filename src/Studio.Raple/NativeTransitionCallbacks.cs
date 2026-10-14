using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace Studio.Raple;
public sealed record NativeTransitionResult(bool Matched,int SelectedIndex,string? Target,NativeStateOrigin? TargetOrigin,string? EnteredPhase);
internal static class NativeTransitionCallbacks
{
    internal static NativeTransitionResult? FromData(string[] data)
    {
        if(data.Length<27||data[18] is not ("true" or "false"))return null;
        var target=data[21]==""?null:new NativeStateOrigin(data[21],int.Parse(data[22],CultureInfo.InvariantCulture),int.Parse(data[23],CultureInfo.InvariantCulture),data[24],Guid.TryParse(data[25],out var id)?id:null);
        return new(data[18]=="true",int.Parse(data[19],CultureInfo.InvariantCulture),data[20]==""?null:data[20],target,data[26]==""?null:data[26]);
    }
    internal const string ScopeSource="""
            public void TransitionResult(bool matched,int selected,State target,State entered) {
                if(Data==null)return;
                Data[18]=matched?"true":"false";Data[19]=selected.ToString(global::System.Globalization.CultureInfo.InvariantCulture);Data[20]=target?.Name??"";
                var origin=Find(target);Data[21]=origin?[0]??"";Data[22]=origin?[1]??"0";Data[23]=origin?[2]??"0";Data[24]=origin?[3]??"";Data[25]=origin?[4]??"";Data[26]=entered?.Name??"";
            }
""";
    internal static BlockSyntax ObserveSwitch(BlockSyntax body,string scope,string actorName)
    {
        var calls=body.DescendantNodes().OfType<IfStatementSyntax>().Where(i=>i.Condition is IdentifierNameSyntax {Identifier.ValueText:"ret"}&&i.Statement is ExpressionStatementSyntax {Expression:InvocationExpressionSyntax {Expression:MemberAccessExpressionSyntax {Name.Identifier.ValueText:"SwitchTo"}}}).ToArray();
        if(calls.Length!=1)throw new ArgumentException("The native transition dispatch changed. Result observation requires review.");
        var current=calls[0];var call=(InvocationExpressionSyntax)((ExpressionStatementSyntax)current.Statement).Expression;var actor=((MemberAccessExpressionSyntax)call.Expression).Expression.ToString();
        if(((MemberAccessExpressionSyntax)call.Expression).Expression is not IdentifierNameSyntax receiver||receiver.Identifier.ValueText!=actorName||call.ArgumentList.Arguments is not {Count:1}||call.ArgumentList.Arguments[0].Expression is not ElementAccessExpressionSyntax {Expression:IdentifierNameSyntax {Identifier.ValueText:"TargetState"},ArgumentList.Arguments:var indices}||indices.Count!=1||indices[0].Expression is not IdentifierNameSyntax {Identifier.ValueText:"SelectedState"})throw new ArgumentException("The native transition receiver/target boundary changed. Result observation requires review.");
        var target="__studioTransitionTarget";while(body.DescendantTokens().Any(t=>t.ValueText==target))target+="_";
        var replacement=SyntaxFactory.ParseStatement($"if(ret){{var {target}={call.ArgumentList.Arguments.Single().Expression};{actor}.SwitchTo({target});{scope}.TransitionResult(true,SelectedState,{target},{actor}.CurrentState);}}else{{{scope}.TransitionResult(false,SelectedState,null,null);}}").WithTriviaFrom(current);
        return body.ReplaceNode(current,replacement);
    }
}
