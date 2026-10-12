using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Core;

public sealed record PhaseLiteral(string Name,int Start,int Length);
public enum PhaseChoiceEdit { Replace,Add,Remove,Up,Down }
public sealed partial class BehaviorDocument
{
    public static IReadOnlyList<PhaseLiteral>? PhaseLiterals(string expression)
    {
        var syntax=SyntaxFactory.ParseExpression(expression);
        IEnumerable<ExpressionSyntax>? values=syntax switch {
            LiteralExpressionSyntax {Token.Value:string}=>[syntax],
            ArrayCreationExpressionSyntax {Initializer:{} initializer}=>initializer.Expressions,
            ImplicitArrayCreationExpressionSyntax array=>array.Initializer.Expressions,
            CollectionExpressionSyntax collection when collection.Elements.All(e=>e is ExpressionElementSyntax)=>collection.Elements.Cast<ExpressionElementSyntax>().Select(e=>e.Expression),
            _=>null};
        if(values==null)return null;var items=values.ToArray();
        return items.All(e=>e is LiteralExpressionSyntax {Token.Value:string})?items.Select(e=>new PhaseLiteral((string)((LiteralExpressionSyntax)e).Token.Value!,e.SpanStart,e.Span.Length)).ToArray():null;
    }
    public IReadOnlyList<string>? PhaseChoices(Guid id)
    {
        var node=Find(id);if(node.Kind!="TimedRandomTransition")return null;
        var fields=node.Parameters.Where(p=>p.Name.Split('[')[0]=="states").ToArray();
        var values=fields.Select(p=>PhaseLiterals(p.Expression)).ToArray();
        return values.Any(v=>v==null)?null:values.SelectMany(v=>v!).Select(v=>v.Name).ToArray();
    }
    public void EditPhaseChoice(Guid id,long expectedRevision,PhaseChoiceEdit edit,int index,string? name=null,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate=null)
    {
        if(expectedRevision!=Revision)throw new InvalidOperationException("This phase choice edit is stale.");
        var node=Find(id);if(PhaseChoices(id)==null)throw new ArgumentException("Phase choices come from unresolved source. Edit them in Source.");Editable(node);
        var owner=OwningEntity(id);
        if(ReferencesEntityPhase(node,new("states","",0,0),owner)!=true)throw new ArgumentException("This is not a known native phase-choice parameter.");
        var call=SyntaxFactory.ParseExpression(Expression(node));var arguments=CallArguments(call);var slots=SchemaAt(node)?.ConstructorParameterNames(Expression(node))??names(node.Kind);
        var indices=Enumerable.Range(0,arguments.Arguments.Count).Where(i=>slots[i].Split('[')[0]=="states").ToArray();
        var whole=indices.Length==1&&slots[indices[0]]=="states";var array=whole?arguments.Arguments[indices[0]].Expression:null;
        SeparatedSyntaxList<ExpressionSyntax> original=array switch {ArrayCreationExpressionSyntax a=>a.Initializer!.Expressions,ImplicitArrayCreationExpressionSyntax a=>a.Initializer.Expressions,_=>default};
        var callValues=arguments.Arguments.ToList();var callSeparators=arguments.Arguments.GetSeparators().ToArray();
        for(var i=0;i<callSeparators.Length;i++)callValues[i]=callValues[i].WithTrailingTrivia(callValues[i].GetTrailingTrivia().AddRange(callSeparators[i].LeadingTrivia).AddRange(callSeparators[i].TrailingTrivia));
        var elements=whole?(array is CollectionExpressionSyntax collection?collection.Elements.Cast<ExpressionElementSyntax>().Select(e=>e.Expression).ToList():original.ToList()):indices.Select(i=>callValues[i].Expression.WithTriviaFrom(callValues[i])).ToList();
        var separators=whole?(array is CollectionExpressionSyntax separatorCollection?separatorCollection.Elements.GetSeparators():original.GetSeparators()).ToArray():[];
        for(var i=0;i<Math.Min(elements.Count,separators.Length);i++)elements[i]=elements[i].WithTrailingTrivia(elements[i].GetTrailingTrivia().AddRange(separators[i].LeadingTrivia).AddRange(separators[i].TrailingTrivia));
        if(edit is PhaseChoiceEdit.Add or PhaseChoiceEdit.Replace&&string.IsNullOrWhiteSpace(name))throw new ArgumentException("Choose a non-empty phase name.");
        if(edit!=PhaseChoiceEdit.Add&&(index<0||index>=elements.Count))throw new ArgumentOutOfRangeException(nameof(index));
        var literal=SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,SyntaxFactory.Literal(name??""));
        switch(edit)
        {
            case PhaseChoiceEdit.Add:elements.Add(literal);break;
            case PhaseChoiceEdit.Replace:elements[index]=literal.WithTriviaFrom(elements[index]);break;
            case PhaseChoiceEdit.Remove:elements.RemoveAt(index);break;
            case PhaseChoiceEdit.Up:case PhaseChoiceEdit.Down:
                var to=index+(edit==PhaseChoiceEdit.Up?-1:1);if(to<0||to>=elements.Count)return;(elements[index],elements[to])=(elements[to],elements[index]);break;
            default:throw new ArgumentOutOfRangeException(nameof(edit));
        }
        if(elements.Count==0)throw new ArgumentException("A native random transition needs at least one phase choice.");
        var list=SyntaxFactory.SeparatedList(elements,Enumerable.Repeat(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),elements.Count-1));
        ExpressionSyntax replacement=array switch {
            ArrayCreationExpressionSyntax a=>a.WithInitializer(a.Initializer!.WithExpressions(list)),
            ImplicitArrayCreationExpressionSyntax a=>a.WithInitializer(a.Initializer.WithExpressions(list)),
            CollectionExpressionSyntax c=>c.WithElements(SyntaxFactory.SeparatedList(elements.Select(e=>(CollectionElementSyntax)SyntaxFactory.ExpressionElement(e)),list.GetSeparators())),
            _=>SyntaxFactory.ImplicitArrayCreationExpression(SyntaxFactory.InitializerExpression(SyntaxKind.ArrayInitializerExpression,list))};
        var updated=callValues;
        if(whole)updated[indices[0]]=updated[indices[0]].WithExpression(replacement);
        else
        {
            var position=indices.FirstOrDefault(updated.Count);foreach(var i in indices.Reverse())updated.RemoveAt(i);
            updated.Insert(position,SyntaxFactory.Argument(replacement).WithNameColon(SyntaxFactory.NameColon("states")).WithLeadingTrivia(SyntaxFactory.Space));
        }
        var rewritten=call.ReplaceNode(arguments,arguments.WithArguments(SyntaxFactory.SeparatedList(updated)));
        Commit(Source.Remove(node.Start,node.Length).Insert(node.Start,rewritten.ToFullString()),Identities,validate);
    }
}
