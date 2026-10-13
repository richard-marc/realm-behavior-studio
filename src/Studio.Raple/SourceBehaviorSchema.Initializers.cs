using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
internal sealed partial class SourceBehaviorSchema
{
    static bool NativeStateMember(IPropertySymbol property)=>property.ContainingType.ToDisplayString()=="TKR.WorldServer.logic.State"&&property.Name is "Behaviors" or "Transitions";
    public IReadOnlyList<SourceMemberCollection> InitializerCollections(string text)
    {
        if(Expression(text) is not BaseObjectCreationExpressionSyntax {Initializer:{} initializer})return [];
        var result=new List<SourceMemberCollection>();
        foreach(var assignment in initializer.Expressions.OfType<AssignmentExpressionSyntax>())
        {
            if(assignment.Right is not InitializerExpressionSyntax list||!(list.IsKind(SyntaxKind.CollectionInitializerExpression)||list.IsKind(SyntaxKind.ObjectInitializerExpression)&&list.Expressions.Count==0)||list.Expressions.Any(e=>e is InitializerExpressionSyntax or AssignmentExpressionSyntax)||Model.GetSymbolInfo(assignment.Left).Symbol is not IPropertySymbol property||!NativeStateMember(property)||!Collection(property.Type)||!Child(Element(property.Type)))continue;
            result.Add(new(list.SpanStart,list.Span.Length,new(property.Name,Element(property.Type).Name,false,false){CollectionType=property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),IsMember=true}));
        }
        return result;
    }
    ITypeSymbol? MemberElement(ExpressionSyntax owner,string parameter,string? listText)
    {
        if(owner is not BaseObjectCreationExpressionSyntax {Initializer:{} initializer}||listText==null)return null;
        var assignment=initializer.Expressions.OfType<AssignmentExpressionSyntax>().SingleOrDefault(a=>a.Right.ToString()==listText&&Model.GetSymbolInfo(a.Left).Symbol is IPropertySymbol p&&p.Name==parameter&&NativeStateMember(p));
        return assignment!=null&&Model.GetSymbolInfo(assignment.Left).Symbol is IPropertySymbol property?Element(property.Type):null;
    }
}
