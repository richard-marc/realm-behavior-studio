using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;

internal sealed partial class SourceBehaviorSchema
{
    sealed record DefinitionBody(ExpressionSyntax Expression,bool Complete);
    sealed class DefinitionBuilder(string key,string label,DefinitionBody body,ChildArgument? collection,ISymbol symbol)
    {
        public readonly string Key=key,Label=label;
        public readonly DefinitionBody Body=body;
        public readonly ChildArgument? Collection=collection;
        public readonly ISymbol Symbol=symbol.OriginalDefinition;
        public readonly HashSet<string> ExternalFiles=new(StringComparer.OrdinalIgnoreCase);
        public bool CompleteContexts=true;
        public bool ForeignContexts;
        public readonly HashSet<string> CallerFiles=new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Encounters=new(StringComparer.Ordinal);
        public readonly HashSet<SourceDefinitionUse> Uses=[];
        public SourceDefinition Finish()=>new(Key,Label,Body.Expression.SpanStart,Body.Expression.Span.Length,Collection,
            Encounters.Order(StringComparer.Ordinal).ToArray(),Uses.OrderBy(u=>u.Start).ToArray(),Encounters.Count>0&&Body.Complete&&CompleteContexts&&!ForeignContexts&&ExternalFiles.Count==0,
            (Body.Complete?"Edits change this source definition for every caller. Inputs remain source values; this view does not execute or substitute them.":"Editable source branch. Other statements or generated contents remain in Source; complete phase-reference reconciliation requires that context.")+
            (ExternalFiles.Count==0?"":" Known references also occur in "+string.Join(", ",ExternalFiles.Order(StringComparer.OrdinalIgnoreCase).Select(Path.GetFileName))+"; phase-name changes need those source documents."))
            {StructurallyComplete=Body.Complete&&CompleteContexts,CallerFiles=CallerFiles.Order(StringComparer.OrdinalIgnoreCase).ToArray()};
    }
    public IReadOnlyList<SourceDefinition> SourceDefinitions()=>context.Definitions??=DiscoverDefinitions();
    public IReadOnlyList<SourceDefinitionReference> ExternalDefinitions(int start,int length)
    {
        var model=Model;
        return model.SyntaxTree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>()
            .Where(e=>e.SpanStart>=start&&e.Span.End<=start+length&&e is InvocationExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax)
            .Select(e=>model.GetSymbolInfo(e).Symbol).Where(s=>s!=null)
            .Where(s=>s switch{IMethodSymbol m=>Child(Element(m.ReturnType)),IFieldSymbol f=>Child(Element(f.Type)),IPropertySymbol p=>Child(Element(p.Type)),_=>false})
            .SelectMany(s=>s!.DeclaringSyntaxReferences.Select(r=>r.GetSyntax()).Where(d=>d.SyntaxTree!=model.SyntaxTree)
                .SelectMany(d=>DefinitionBodies(d).SelectMany(ExpandBranches).Select(b=>new SourceDefinitionReference(d.SyntaxTree.FilePath,s.Name,b.Expression.SpanStart,b.Expression.Span.Length))))
            .Distinct().ToArray();
    }
    IReadOnlyList<SourceDefinition> DiscoverDefinitions()
    {
        var model=Model;var tree=model.SyntaxTree;var syntax=tree.GetRoot();
        var expressions=syntax.DescendantNodes().OfType<ExpressionSyntax>().GroupBy(e=>(e.SpanStart,e.Span.Length)).ToDictionary(g=>g.Key,g=>g.First());
        var queue=new Queue<(ExpressionSyntax Expression,string Entity,bool Complete,string Origin,bool Foreign)>();
        var compilation=context.Binding.Value.Compilation;
        var seen=new HashSet<(SyntaxTree Tree,int Start,int Length,string Entity,bool Complete,string Origin,bool Foreign)>();var definitions=new Dictionary<string,DefinitionBuilder>(StringComparer.Ordinal);
        static bool IsScope(SyntaxNode node)=>node is MethodDeclarationSyntax or LocalFunctionStatementSyntax or PropertyDeclarationSyntax or AnonymousFunctionExpressionSyntax||node is VariableDeclaratorSyntax&&node.Parent?.Parent is FieldDeclarationSyntax;
        static SyntaxNode? ScopeOf(SyntaxNode node)=>node.Ancestors().FirstOrDefault(IsScope);
        IEnumerable<SyntaxTree> CallerTrees()
        {
            var files=new HashSet<SyntaxTree>{tree};
            var known=new HashSet<ISymbol>(definitions.Values.Select(d=>d.Symbol),SymbolEqualityComparer.Default);
            var recent=known.ToArray();
            while(recent.Length!=0)
            {
                var next=new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                foreach(var other in compilation.SyntaxTrees)
                {
                    var text=other.GetText().ToString();var candidates=recent.Where(s=>text.Contains(s.Name,StringComparison.Ordinal)).ToArray();if(candidates.Length==0)continue;
                    var otherModel=compilation.GetSemanticModel(other);
                    foreach(var reference in other.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().Where(e=>e is InvocationExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax))
                    {
                        if(!candidates.Any(s=>s.Name==ReferenceName(reference))||otherModel.GetSymbolInfo(reference).Symbol is not {} symbol||!candidates.Any(s=>SymbolEqualityComparer.Default.Equals(s,symbol.OriginalDefinition)))continue;
                        files.Add(other);
                        var scope=ScopeOf(reference);var owner=scope==null?null:otherModel.GetDeclaredSymbol(scope);
                        var type=owner switch{IMethodSymbol m=>m.ReturnType,IPropertySymbol p=>p.Type,IFieldSymbol f=>f.Type,ILocalSymbol l=>l.Type,_=>null};
                        if(owner!=null&&type!=null&&Child(Element(type))&&known.Add(owner.OriginalDefinition))next.Add(owner.OriginalDefinition);
                    }
                }
                recent=next.ToArray();
            }
            return files;
        }
        string ScopeKey(SyntaxNode declaration)
        {
            var scope=ScopeOf(declaration);if(scope==null)return "<file>";
            if(scope is AnonymousFunctionExpressionSyntax)
            {
                var parent=ScopeOf(scope);var siblings=parent?.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>().Where(n=>ScopeOf(n)==parent).ToArray()??[];
                return ScopeKey(scope)+"/lambda:"+Array.FindIndex(siblings,n=>n.Span==scope.Span);
            }
            var name=model.GetDeclaredSymbol(scope)?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)??scope.Kind().ToString();
            return scope is LocalFunctionStatementSyntax?ScopeKey(scope)+"/function:"+name:name;
        }
        void AddDefinition(ISymbol? symbol,ExpressionSyntax? reference,string entity,bool complete,string origin="",bool foreign=false)
        {
                var type=symbol switch {IMethodSymbol method=>method.ReturnType,IFieldSymbol field=>field.Type,IPropertySymbol property=>property.Type,ILocalSymbol localValue=>localValue.Type,_=>null};
                if(type==null||!Child(Element(type)))return;
                var declared=symbol!.DeclaringSyntaxReferences.Select(r=>r.GetSyntax()).Where(n=>n.SyntaxTree==tree).ToArray();
                if(declared.Length==0)
                {
                    if(adapter.HasAdditionalSources&&origin.Length>0)
                        foreach(var body in symbol.DeclaringSyntaxReferences.Select(r=>r.GetSyntax()).SelectMany(DefinitionBodies).SelectMany(ExpandBranches))
                            queue.Enqueue((body.Expression,entity,complete&&body.Complete,origin,true));
                    return; // Traverse source ownership without copying or executing another file.
                }
                foreign|=reference!=null&&reference.SyntaxTree!=tree;
                var key="$definition/"+symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if(symbol is IMethodSymbol {MethodKind:MethodKind.LocalFunction})key="$definition/"+ScopeKey(declared[0])+"/function:"+symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if(symbol is ILocalSymbol local)
                {
                    var owner=ScopeOf(declared[0]);var declarations=owner?.DescendantNodes().OfType<VariableDeclaratorSyntax>().Where(v=>v.Identifier.ValueText==local.Name&&ScopeOf(v)==owner).ToArray()??[];
                    key="$definition/"+ScopeKey(declared[0])+"/local:"+local.Name+"/"+Array.FindIndex(declarations,v=>declared.Any(d=>d.Span==v.Span));
                }
                var bodies=declared.SelectMany(DefinitionBodies).SelectMany(ExpandBranches).ToArray();
                for(var index=0;index<bodies.Length;index++)
                {
                    var body=bodies[index];
                    if(body.Complete&&body.Expression.DescendantNodesAndSelf().OfType<ExpressionSyntax>().Any(e=>model.GetSymbolInfo(e).Symbol is IParameterSymbol p&&Child(Element(p.Type))))body=body with {Complete=false};
                    var id=key+"/body"+index;
                    ChildArgument? collection=Collection(type)?new("elements",Element(type).Name,type is IArrayTypeSymbol,false)
                        {CollectionType=type is IArrayTypeSymbol?null:type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}:null;
                    if(!definitions.TryGetValue(id,out var definition))definitions.Add(id,definition=new(id,symbol.Name+(bodies.Length==1?"":" · branch "+(index+1)),body,collection,symbol));
                    if(reference?.SyntaxTree==tree)definition.Uses.Add(new(reference.SpanStart,reference.Span.Length));
                    if(entity.Length>0)definition.Encounters.Add(entity);
                    if(origin.Length>0){definition.CompleteContexts&=complete;definition.ForeignContexts|=foreign;definition.CallerFiles.Add(origin);}
                    queue.Enqueue((body.Expression,entity,complete&&body.Complete,origin,foreign));
                }
        }
        // Catalog declared bodies independently of reachability so removing a call preserves body IDs and locks.
        foreach(var declaration in syntax.DescendantNodes().Where(n=>n is MethodDeclarationSyntax or PropertyDeclarationSyntax or VariableDeclaratorSyntax or LocalFunctionStatementSyntax))
            AddDefinition(model.GetDeclaredSymbol(declaration),null,"",true);
        // Walk only registrations that can reach these exact symbols. This preserves all
        // known source callers while avoiding a traversal of every unrelated game encounter.
        foreach(var registrationTree in adapter.HasAdditionalSources?CallerTrees():[tree])
        {
            var registrationExpressions=registrationTree==tree?expressions:registrationTree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().GroupBy(e=>(e.SpanStart,e.Span.Length)).ToDictionary(g=>g.Key,g=>g.First());
            var registrationModel=compilation.GetSemanticModel(registrationTree);var registrations=NativeRegistrations.Discover(registrationModel);
            foreach(var registration in registrations)
                if(registrationExpressions.TryGetValue((registration.RootStart,registration.RootLength),out var root))queue.Enqueue((root,registration.EntityId,root is ObjectCreationExpressionSyntax or InvocationExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax,registrationTree.FilePath,registrationTree!=tree));
            foreach(var unresolved in NativeRegistrations.UnresolvedRoots(registrationModel,registrations))queue.Enqueue((unresolved,"",false,registrationTree.FilePath,registrationTree!=tree));
        }
        while(queue.TryDequeue(out var pending))
        {
            if(!seen.Add((pending.Expression.SyntaxTree,pending.Expression.SpanStart,pending.Expression.Span.Length,pending.Entity,pending.Complete,pending.Origin,pending.Foreign)))continue;
            var referenceModel=compilation.GetSemanticModel(pending.Expression.SyntaxTree);
            foreach(var reference in pending.Expression.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
            {
                if(reference is not (InvocationExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax)||
                    reference.Parent is InvocationExpressionSyntax invocation&&invocation.Expression==reference||
                    reference.Parent is MemberAccessExpressionSyntax member&&member.Name==reference)continue;
                AddDefinition(referenceModel.GetSymbolInfo(reference).Symbol,reference,pending.Entity,pending.Complete,pending.Origin,pending.Foreign);
            }
        }
        foreach(var other in context.Binding.Value.Compilation.SyntaxTrees.Where(t=>t!=tree))
        {
            var text=other.GetText().ToString();var candidates=definitions.Values.Where(d=>text.Contains(d.Symbol.Name,StringComparison.Ordinal)).ToArray();if(candidates.Length==0)continue;
            var otherModel=context.Binding.Value.Compilation.GetSemanticModel(other);
            foreach(var reference in other.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().Where(e=>e is InvocationExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax))
                if(candidates.Any(d=>ReferenceName(reference)==d.Symbol.Name)&&otherModel.GetSymbolInfo(reference).Symbol is {} target)
                    foreach(var candidate in candidates.Where(d=>SymbolEqualityComparer.Default.Equals(d.Symbol,target.OriginalDefinition)))candidate.ExternalFiles.Add(other.FilePath);
        }
        return definitions.Values.OrderBy(d=>d.Body.Expression.SpanStart).Select(d=>d.Finish()).ToArray();
    }
    static string? ReferenceName(ExpressionSyntax expression)=>expression switch{IdentifierNameSyntax name=>name.Identifier.ValueText,GenericNameSyntax name=>name.Identifier.ValueText,MemberAccessExpressionSyntax member=>member.Name.Identifier.ValueText,MemberBindingExpressionSyntax member=>member.Name.Identifier.ValueText,InvocationExpressionSyntax call=>ReferenceName(call.Expression),_=>null};
    static IEnumerable<DefinitionBody> ExpandBranches(DefinitionBody body)
    {
        if(body.Expression is ConditionalExpressionSyntax conditional)
        {
            foreach(var branch in ExpandBranches(new(conditional.WhenTrue,false)).Concat(ExpandBranches(new(conditional.WhenFalse,false))))yield return branch;
        }
        else if(body.Expression is SwitchExpressionSyntax selection)
            foreach(var branch in selection.Arms.SelectMany(arm=>ExpandBranches(new(arm.Expression,false))))yield return branch;
        else yield return body;
    }
    static IEnumerable<DefinitionBody> DefinitionBodies(SyntaxNode declaration)
    {
        switch(declaration)
        {
            case VariableDeclaratorSyntax {Initializer:{Value:var value}}:
                yield return new(value,declaration.Parent?.Parent is FieldDeclarationSyntax);break;
            case MethodDeclarationSyntax {ExpressionBody:{Expression:var expression}}:
                yield return new(expression,true);break;
            case MethodDeclarationSyntax {Body:{ } body}:
                foreach(var result in Returns(body))yield return new(result,body.Statements.Count==1&&body.Statements[0] is ReturnStatementSyntax);break;
            case PropertyDeclarationSyntax {ExpressionBody:{Expression:var expression}}:
                yield return new(expression,true);break;
            case PropertyDeclarationSyntax {Initializer:{Value:var value}}:
                yield return new(value,true);break;
            case PropertyDeclarationSyntax {AccessorList:{ } accessors}:
                foreach(var getter in accessors.Accessors.Where(a=>a.IsKind(SyntaxKind.GetAccessorDeclaration)))
                {
                    if(getter.ExpressionBody is {} arrow)yield return new(arrow.Expression,true);
                    else if(getter.Body is {} block)foreach(var result in Returns(block))yield return new(result,block.Statements.Count==1&&block.Statements[0] is ReturnStatementSyntax);
                }
                break;
            case LocalFunctionStatementSyntax {ExpressionBody:{Expression:var expression}}:
                yield return new(expression,true);break;
            case LocalFunctionStatementSyntax {Body:{ } body}:
                foreach(var result in Returns(body))yield return new(result,body.Statements.Count==1&&body.Statements[0] is ReturnStatementSyntax);break;
        }
    }
    static IEnumerable<ExpressionSyntax> Returns(BlockSyntax body)=>body.DescendantNodes().OfType<ReturnStatementSyntax>()
        .Where(r=>r.Expression!=null&&!r.Ancestors().TakeWhile(a=>a!=body).Any(a=>a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)).Select(r=>r.Expression!);
    public bool AcceptsDefinitionElement(string text,string element,bool spread=false)
    {
        var expression=Expression(text);if(expression==null)return false;
        if(element.TrimStart().StartsWith('{'))return !spread&&AcceptEntry(expression,null,element);
        var type=Model.GetTypeInfo(expression).Type??Model.GetTypeInfo(expression).ConvertedType;if(type==null||!Collection(type)||!Child(Element(type)))return false;
        return spread?Model.ClassifyConversion(expression.SpanStart,SyntaxFactory.ParseExpression("["+element+"]"),type).IsImplicit:
            Model.GetSpeculativeTypeInfo(expression.SpanStart,SyntaxFactory.ParseExpression(element),SpeculativeBindingOption.BindAsExpression).Type is {} actual&&context.Binding.Value.Compilation.ClassifyConversion(actual,Element(type)).IsImplicit;
    }
}
