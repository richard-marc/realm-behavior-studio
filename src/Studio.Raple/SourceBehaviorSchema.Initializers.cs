using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
internal sealed partial class SourceBehaviorSchema
{
    static bool NativeStateMember(IPropertySymbol property)=>property.ContainingType.ToDisplayString()=="TKR.WorldServer.logic.State"&&property.Name is "Behaviors" or "Transitions";
    static ITypeSymbol? MemberType(ISymbol? symbol)=>symbol switch{IPropertySymbol p=>p.Type,IFieldSymbol f=>f.Type,_=>null};
    static bool MemberValues(ExpressionSyntax expression) => expression switch
    {
        InitializerExpressionSyntax list => (list.IsKind(SyntaxKind.CollectionInitializerExpression) || list.IsKind(SyntaxKind.ObjectInitializerExpression) && list.Expressions.Count == 0)
            && !list.Expressions.Any(e => e is AssignmentExpressionSyntax or InitializerExpressionSyntax),
        CollectionExpressionSyntax or ArrayCreationExpressionSyntax { Initializer: not null } or ImplicitArrayCreationExpressionSyntax => true,
        BaseObjectCreationExpressionSyntax { Initializer: null } => true,
        BaseObjectCreationExpressionSyntax { Initializer: { } initializer } => !initializer.Expressions.Any(e => e is AssignmentExpressionSyntax or InitializerExpressionSyntax),
        _ => false
    };
    public IReadOnlyList<SourceMemberCollection> InitializerCollections(string text)
    {
        if(Expression(text) is not BaseObjectCreationExpressionSyntax {Initializer:{} initializer})return [];
        var result=new List<SourceMemberCollection>();
        foreach(var assignment in initializer.Expressions.OfType<AssignmentExpressionSyntax>())
        {
            var symbol=Model.GetSymbolInfo(assignment.Left).Symbol;var type=MemberType(symbol);
            if(type==null||symbol is IPropertySymbol {IsIndexer:true}||!Child(Element(type))||StateType(Element(type))||symbol is IPropertySymbol p&&p.ContainingType.ToDisplayString()=="TKR.WorldServer.logic.State"&&!NativeStateMember(p))continue;
            var collection=Collection(type);
            if(collection&&!MemberValues(assignment.Right)||!collection&&assignment.Right is not (BaseObjectCreationExpressionSyntax or InvocationExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax)&&!assignment.Right.IsKind(SyntaxKind.NullLiteralExpression))continue;
            result.Add(new(assignment.Right.SpanStart,assignment.Right.Span.Length,new(symbol!.Name,Element(type).Name,type is IArrayTypeSymbol,false,0){CollectionType=collection&&type is not IArrayTypeSymbol?type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat):null,IsMember=true}));
        }
        return result;
    }
    ITypeSymbol? MemberElement(ExpressionSyntax owner,string parameter,string? listText)
    {
        if(owner is not BaseObjectCreationExpressionSyntax {Initializer:{} initializer}||listText==null)return null;
        var assignment=initializer.Expressions.OfType<AssignmentExpressionSyntax>().SingleOrDefault(a=>a.Right.ToString()==listText&&Model.GetSymbolInfo(a.Left).Symbol is{} symbol&&symbol.Name==parameter&&MemberType(symbol)!=null);
        return assignment!=null&&MemberType(Model.GetSymbolInfo(assignment.Left).Symbol) is{} type?Element(type):null;
    }
}
