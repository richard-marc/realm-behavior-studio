using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
public static partial class SourceDefinitionRefactor
{
    sealed record ExtractionSlice(ITypeSymbol Type,string Expression,Func<string,string> Replace,Guid? Owner,string? Binding);
    static string MethodBinding(IMethodSymbol method)=>(method.OriginalDefinition.GetDocumentationCommentId()??method.OriginalDefinition.ToDisplayString())+"|"+method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+"|"+string.Join(",",method.TypeArguments.Select(t=>t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
    static ExtractionSlice PrepareSlice(BehaviorDocument doc,IReadOnlyList<BehaviorNode> nodes,SemanticModel model)
    {
        var parent=doc.Parent(nodes[0].Id)??throw new ArgumentException("Select adjacent child expressions.");
        if(nodes.Any(n=>doc.Parent(n.Id)?.Id!=parent.Id)||!doc.CanEditChildren(parent.Id))throw new ArgumentException("Extract adjacent expressions from one native child list.");
        var children=parent.Children.ToArray();var first=Array.FindIndex(children,n=>n.Id==nodes[0].Id);
        if(!children.Skip(first).Take(nodes.Count).Select(n=>n.Id).SequenceEqual(nodes.Select(n=>n.Id)))throw new ArgumentException("Extract adjacent expressions without skipping source inputs.");
        var root=model.SyntaxTree.GetRoot();var container=root.DescendantNodes().OfType<ExpressionSyntax>().Single(e=>e.SpanStart==parent.Start&&e.Span.Length==parent.Length);
        var owner=parent.Kind=="ArgumentGroup"?doc.Parent(parent.Id):parent;
        ITypeSymbol? collectionType=null;string[]? names=null;ArgumentListSyntax? arguments=null;
        if(parent.Kind=="ArgumentGroup"&&!parent.ArgumentFromCall)
        {
            if(container is not (CollectionExpressionSyntax or ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax))throw new ArgumentException("Selections in custom Add initializers need their collection ownership before extraction.");
            collectionType=model.GetTypeInfo(container).Type??model.GetTypeInfo(container).ConvertedType;
        }
        else
        {
            if(owner==null)throw new ArgumentException("The native child slot has no source owner.");
            container=root.DescendantNodes().OfType<ExpressionSyntax>().Single(e=>e.SpanStart==owner.Start&&e.Span.Length==owner.Length);
            if(model.GetSymbolInfo(container).Symbol is not IMethodSymbol method)throw new ArgumentException("Resolve the actual native child constructor or source factory before extraction.");
            var slots=doc.SchemaAt(owner)!.ChildArguments(doc.Expression(owner));var slot=parent.Argument??slots.SingleOrDefault()??throw new ArgumentException("Select a typed native child list.");
            var parameter=method.Parameters.Single(p=>p.Name==slot.Name);if(!slot.IsCollection)throw new ArgumentException("A scalar child slot cannot contain a multi-expression helper.");
            collectionType=parameter.Type;names=doc.SchemaAt(owner)!.ConstructorParameterNames(doc.Expression(owner));
            arguments=container switch{BaseObjectCreationExpressionSyntax c=>c.ArgumentList,InvocationExpressionSyntax c=>c.ArgumentList,_=>null};
            if(arguments==null)throw new ArgumentException("The selected source call has no native arguments.");
        }
        var element=collectionType switch
        {
            IArrayTypeSymbol a=>a.ElementType,
            INamedTypeSymbol n=>n.AllInterfaces.Prepend(n).Where(i=>i.OriginalDefinition.MetadataName=="IEnumerable`1"&&i.ContainingNamespace.ToDisplayString()=="System.Collections.Generic").Select(i=>i.TypeArguments[0]).Distinct<ITypeSymbol>(SymbolEqualityComparer.Default).SingleOrDefault(),
            _=>null
        }??throw new ArgumentException("The actual child element type cannot be resolved.");
        var arrayType=model.Compilation.CreateArrayTypeSymbol(element);var elementText=element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var firstNode=nodes[0];var last=nodes[^1];var expressions=doc.Source.Substring(firstNode.Start,last.Start+last.Length-firstNode.Start);
        var helperExpression="new "+elementText+"[] { "+expressions+" }";
        var sourceContainer=container;var slotName=parent.Argument?.Name;
        var separators=sourceContainer switch
        {
            BaseObjectCreationExpressionSyntax c=>c.ArgumentList?.Arguments.GetSeparators().ToArray()??[],
            InvocationExpressionSyntax c=>c.ArgumentList.Arguments.GetSeparators().ToArray(),
            ArrayCreationExpressionSyntax c=>c.Initializer?.Expressions.GetSeparators().ToArray()??[],
            ImplicitArrayCreationExpressionSyntax c=>c.Initializer.Expressions.GetSeparators().ToArray(),
            CollectionExpressionSyntax c=>c.Elements.GetSeparators().ToArray(),_=>[]
        };
        SyntaxNode Syntax(BehaviorNode child)=>root.DescendantNodes().First(n=>n.SpanStart==child.Start&&n.Span.Length==child.Length);
        string SeparatorTrivia(BehaviorNode child)
        {
            var separator=separators.FirstOrDefault(t=>t.SpanStart>=child.Start+child.Length);
            return separator.RawKind==0?"":separator.LeadingTrivia.ToFullString()+separator.TrailingTrivia.ToFullString();
        }
        string Input(BehaviorNode child)=>Syntax(child).ToFullString()+SeparatorTrivia(child);
        string Replace(string call)
        {
            var values=new List<string>();for(var i=0;i<children.Length;i++)
            {
                if(i==first){values.Add(Syntax(firstNode).GetLeadingTrivia().ToFullString()+".."+call+Syntax(last).GetTrailingTrivia().ToFullString()+SeparatorTrivia(last));i+=nodes.Count-1;}
                else values.Add(Input(children[i]));
            }
            var collection=SyntaxFactory.ParseExpression("["+string.Join(", ",values)+"]");ExpressionSyntax replacement=collection;
            if(arguments!=null)
            {
                var slot=slotName??doc.SchemaAt(owner!)!.ChildArguments(doc.Expression(owner!)).Single().Name;
                var indices=names!.Select((n,i)=>(n,i)).Where(p=>p.n.Split('[')[0]==slot).Select(p=>p.i).ToArray();
                if(indices.Length==0||!indices.SequenceEqual(Enumerable.Range(indices[0],indices.Length)))throw new ArgumentException("Interleaved child/scalar argument evaluation needs source extraction before grouping inputs.");
                var identifier=SyntaxFacts.GetKeywordKind(slot)==SyntaxKind.None?slot:"@"+slot;
                var rewritten=arguments.Arguments.Take(indices[0]).Concat(new[]{SyntaxFactory.Argument(collection).WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(identifier)))}).Concat(arguments.Arguments.Skip(indices[^1]+1));
                var list=arguments.WithArguments(SyntaxFactory.SeparatedList(rewritten));
                replacement=sourceContainer switch{BaseObjectCreationExpressionSyntax c=>c.WithArgumentList(list),InvocationExpressionSyntax c=>c.WithArgumentList(list),_=>throw new ArgumentException("Unknown native source call.")};
            }
            return doc.Source.Remove(sourceContainer.SpanStart,sourceContainer.Span.Length).Insert(sourceContainer.SpanStart,replacement.ToString());
        }
        var ownerExpression=owner==null?null:root.DescendantNodes().OfType<ExpressionSyntax>().Single(e=>e.SpanStart==owner.Start&&e.Span.Length==owner.Length);
        var binding=ownerExpression!=null&&model.GetSymbolInfo(ownerExpression).Symbol is IMethodSymbol bound?MethodBinding(bound):null;
        return new(arrayType,helperExpression,Replace,binding==null?null:owner?.Id,binding);
    }
}
