using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
public static partial class SourcePhaseRefactor
{
    public static SourcePhaseEdit CloneFactory(RapleAdapter adapter,StudioProject project,string sourceFile,Guid selected,string name)
        =>CloneDefinition(adapter,project,sourceFile,selected,name);
    public static SourcePhaseEdit CloneDefinition(RapleAdapter adapter,StudioProject project,string sourceFile,Guid selected,string name)
    {
        var token=SyntaxFactory.ParseToken(name);if(!token.IsKind(SyntaxKind.IdentifierToken)||token.ContainsDiagnostics||token.ToFullString()!=name)throw new ArgumentException("Enter a valid C# clone name.");
        var scope=PrepareStructureScope(adapter,project,sourceFile,selected);var doc=scope.Target;var node=doc.Find(selected);
        if(node.Kind is not ("Factory" or "Reference")||doc.Parent(selected) is not {} parent||!doc.CanOrganize(selected)||!doc.CanEditRegion(selected))throw new ArgumentException("Select an editable source factory or value reference in a native child list.");
        var compiler=adapter.ForProject(project).SourceCompilation(project);var tree=compiler.SyntaxTrees.Single(t=>Path.GetFullPath(t.FilePath).Equals(scope.SourceFile,StringComparison.OrdinalIgnoreCase));var model=compiler.GetSemanticModel(tree);
        var sourceExpression=tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().Single(e=>e.SpanStart==node.Start&&e.Span.Length==node.Length);
        var original=model.GetSymbolInfo(sourceExpression).Symbol??throw new ArgumentException("The selected source definition cannot be resolved.");
        if(original is not (IMethodSymbol or IFieldSymbol or IPropertySymbol)||original is IMethodSymbol method&&(method.MethodKind!=MethodKind.Ordinary||method.IsGenericMethod))throw new ArgumentException("Clone a declared nongeneric source method, field or property.");
        original=original.OriginalDefinition;
        if(original.ContainingType.GetMembers(token.ValueText).Length>0)throw new ArgumentException("That clone member already exists.");
        var declarations=new Dictionary<ISymbol,(MemberDeclarationSyntax Member,SyntaxNode Body)>(SymbolEqualityComparer.Default);var queue=new Queue<ISymbol>([original]);
        bool ChildType(ITypeSymbol type)=>type.ToDisplayString()=="TKR.WorldServer.logic.State"||type.AllInterfaces.Any(i=>i.ToDisplayString()=="TKR.WorldServer.logic.IStateChildren")||type is IArrayTypeSymbol a&&ChildType(a.ElementType)||type is INamedTypeSymbol n&&n.AllInterfaces.Prepend(n).Any(i=>i.OriginalDefinition.MetadataName=="IEnumerable`1"&&i.TypeArguments.Any(ChildType));
        while(queue.TryDequeue(out var symbol))
        {
            symbol=symbol.OriginalDefinition;if(declarations.ContainsKey(symbol))continue;
            var syntax=symbol.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax()??throw new ArgumentException("A cloned child dependency lacks one source declaration.");
            MemberDeclarationSyntax member=syntax switch{MethodDeclarationSyntax m=>m,PropertyDeclarationSyntax p=>p,VariableDeclaratorSyntax v when v.Parent?.Parent is FieldDeclarationSyntax f=>f.WithDeclaration(f.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(v))),_=>throw new ArgumentException("Local/generated child ownership needs declaration extraction before cloning.")};
            if(symbol.ContainingType.TypeParameters.Length>0||symbol.ContainingType.ContainingType!=null||symbol is IMethodSymbol {IsGenericMethod:true})throw new ArgumentException("Generic/nested definition ownership needs source cloning.");
            var path=Path.GetFullPath(syntax.SyntaxTree.FilePath);
            if(!scope.Records.TryGetValue(path,out var record))scope.Records[path]=record=new(path,syntax.SyntaxTree.GetText().ToString());
            if(!scope.Documents.TryGetValue(path,out var declaring))declaring=new BehaviorDocument(record.Source,adapter.ForProject(project),record.Identities,path);
            declaring.LockedNodes.UnionWith(record.LockedNodes);scope.Records[path]=record with{Identities=declaring.Identities};
            var definitions=declaring.Definitions.Where(d=>d.Definition!.Start>=syntax.SpanStart&&d.Definition.Start+d.Definition.Length<=syntax.Span.End).ToArray();
            if(definitions.Length!=1||!definitions[0].Definition!.StructurallyComplete||definitions[0].Root.Descendants().Any(n=>n.Kind=="Opaque"))throw new ArgumentException("A cloned definition has generated/control-flow contents that need source ownership.");
            if(!declaring.CanEditRegion(definitions[0].Root.Id))throw new InvalidOperationException("A cloned source definition is locked.");
            scope.Documents[path]=declaring;declarations.Add(symbol,(member,syntax));
            var semantic=compiler.GetSemanticModel(syntax.SyntaxTree);
            foreach(var expression in syntax.DescendantNodes().OfType<ExpressionSyntax>())
            {
                var dependency=semantic.GetSymbolInfo(expression).Symbol;
                if(expression.Parent is AssignmentExpressionSyntax assignment&&assignment.Left==expression&&assignment.Right is InitializerExpressionSyntax&&dependency is IPropertySymbol property&&property.ContainingType.ToDisplayString()=="TKR.WorldServer.logic.State"&&property.Name is "Behaviors" or "Transitions")continue;
                var type=dependency switch{IMethodSymbol m=>m.ReturnType,IFieldSymbol f=>f.Type,IPropertySymbol p=>p.Type,_=>null};
                if(type!=null&&ChildType(type)&&dependency!.DeclaringSyntaxReferences.Length>0)queue.Enqueue(dependency);
            }
        }
        var renamed=new Dictionary<ISymbol,string>(SymbolEqualityComparer.Default){[original]=token.ValueText};var memberNames=new HashSet<string>(original.ContainingType.GetMembers().Select(m=>m.Name),StringComparer.Ordinal){token.ValueText};
        foreach(var symbol in declarations.Keys.Where(s=>!SymbolEqualityComparer.Default.Equals(s,original)))
        {var prefix=symbol.Name+"Copy";var candidate=prefix;var suffix=2;while(memberNames.Contains(candidate))candidate=prefix+suffix++;memberNames.Add(candidate);renamed[symbol]=candidate;}
        var phaseMap=new Dictionary<string,string>(StringComparer.Ordinal);var taken=scope.PhaseNames;
        foreach(var declaration in declarations.Values)
        {
            var path=Path.GetFullPath(declaration.Body.SyntaxTree.FilePath);var declaring=scope.Documents[path];
            foreach(var phase in declaring.AllNodes.Where(n=>n.Kind=="State"&&n.Start>=declaration.Body.SpanStart&&n.Start+n.Length<=declaration.Body.Span.End))
            {
                var old=declaring.StateName(phase.Id);if(old==null&&phase.Parameters.Any(p=>p.Name=="name"))throw new ArgumentException("Generated phase names need source reconciliation before cloning.");
                if(old==null)continue;if(phaseMap.ContainsKey(old))throw new ArgumentException("A cloned definition closure has ambiguous phase names.");
                var prefix=old+"-copy";var candidate=prefix;var suffix=2;while(taken.Contains(candidate))candidate=prefix+"-"+suffix++;taken.Add(candidate);phaseMap[old]=candidate;
            }
        }
        var additions=new Dictionary<string,List<(TypeDeclarationSyntax Host,MemberDeclarationSyntax Member,int Order)>>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in declarations)
        {
            var syntax=pair.Value.Body;var semantic=compiler.GetSemanticModel(syntax.SyntaxTree);var path=Path.GetFullPath(syntax.SyntaxTree.FilePath);var declaring=scope.Documents[path];
            var edits=new Dictionary<int,(int Length,string Text)>();
            foreach(var simple in syntax.DescendantNodes().OfType<SimpleNameSyntax>())
                if(semantic.GetSymbolInfo(simple).Symbol is {} target&&renamed.TryGetValue(target.OriginalDefinition,out var replacement))edits[simple.SpanStart]=(simple.Span.Length,replacement);
            foreach(var child in declaring.AllNodes.Where(n=>n.Start>=syntax.SpanStart&&n.Start+n.Length<=syntax.Span.End))
            {
                if(child.Kind=="State"&&declaring.StateName(child.Id) is {} old&&phaseMap.TryGetValue(old,out var replacement))
                {var p=child.Parameters.First(p=>BehaviorDocument.LiteralString(p.Expression)==old);edits[p.Start]=(p.Length,SyntaxFactory.Literal(replacement).Text);}
                if(child.Kind is "Factory" or "Reference" or "Spread" or "ArgumentGroup")continue;
                foreach(var parameter in child.Parameters)
                {
                    var binding=declaring.SchemaAt(child)!;var addresses=scope.Callers.Select(id=>binding.TargetsPhaseOf(declaring.Expression(child),parameter.Name,id,id)).ToArray();
                    if(addresses.Any(a=>a==null))throw new ArgumentException("A cloned phase reference has unknown actor ownership.");if(!addresses.Any(a=>a==true))continue;
                    var targets=BehaviorDocument.PhaseLiterals(parameter.Expression)??throw new ArgumentException("Dynamic cloned phase destinations require source reconciliation.");
                    foreach(var value in targets)
                        if(phaseMap.TryGetValue(value.Name,out var phaseDestination))edits[parameter.Start+value.Start]=(value.Length,SyntaxFactory.Literal(phaseDestination).Text);
                        else if(!scope.UniqueTarget(value.Name))throw new ArgumentException("A cloned external phase destination must exist in every caller.");
                }
            }
            var text=syntax.ToString();foreach(var edit in edits.OrderByDescending(e=>e.Key))text=text.Remove(edit.Key-syntax.SpanStart,edit.Value.Length).Insert(edit.Key-syntax.SpanStart,edit.Value.Text);
            MemberDeclarationSyntax cloned=syntax is VariableDeclaratorSyntax?pair.Value.Member:SyntaxFactory.ParseMemberDeclaration(text)??throw new ArgumentException("The clone is not a valid member declaration.");
            var newName=SyntaxFactory.Identifier(renamed[pair.Key]);
            cloned=cloned switch{MethodDeclarationSyntax m=>m.WithIdentifier(newName),PropertyDeclarationSyntax p=>p.WithIdentifier(newName),_=>SyntaxFactory.ParseMemberDeclaration(pair.Value.Member.ToString()) is FieldDeclarationSyntax f?f.WithDeclaration(f.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(newName).WithInitializer(((VariableDeclaratorSyntax)syntax).Initializer)))):throw new ArgumentException("Unsupported clone declaration.")};
            if(cloned is FieldDeclarationSyntax)
            {
                var field=(FieldDeclarationSyntax)pair.Value.Member;
                var parsed=CSharpSyntaxTree.ParseText("class C { "+field.Declaration.Type+" "+text+"; }").GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
                cloned=field.WithDeclaration(field.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(parsed.WithIdentifier(newName))));
            }
            var host=syntax.Ancestors().OfType<TypeDeclarationSyntax>().First();if(!additions.TryGetValue(path,out var list))additions[path]=list=[];list.Add((host,cloned.WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed).NormalizeWhitespace(),syntax.SpanStart));
        }
        var beforeRecords=scope.Records.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
        foreach(var pair in additions)
        {
            var edited=scope.Records[pair.Key].Source;
            foreach(var host in pair.Value.GroupBy(a=>a.Host.CloseBraceToken.SpanStart).OrderByDescending(g=>g.Key))edited=edited.Insert(host.Key,"\n"+string.Join("\n",host.OrderBy(a=>a.Order).Select(a=>a.Member.ToFullString()))+"\n");
            scope.Records[pair.Key]=scope.Records[pair.Key] with{Source=edited};
        }
        StudioProject Snapshot(){var primary=scope.Records[Path.GetFullPath(project.SourceFile)];return project with{Source=primary.Source,Identities=primary.Identities,AdditionalSources=scope.Records.Where(p=>!p.Key.Equals(Path.GetFullPath(project.SourceFile),StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).ToList()};}
        var withDeclarations=Snapshot();var scoped=adapter.ForProject(withDeclarations);var caller=new BehaviorDocument(scope.Records[scope.SourceFile].Source,scoped,doc.Identities,scope.SourceFile);caller.LockedNodes.UnionWith(doc.LockedNodes);
        ExpressionSyntax Named(ExpressionSyntax expression)=>expression switch{IdentifierNameSyntax=>SyntaxFactory.IdentifierName(name),MemberAccessExpressionSyntax member=>member.WithName(SyntaxFactory.IdentifierName(name)),_=>throw new ArgumentException("The selected source access has unresolved dispatch.")};
        var clonedCall=(sourceExpression is InvocationExpressionSyntax call?call.WithExpression(Named(call.Expression)):Named(sourceExpression)).ToString();
        var index=parent.Children.ToList().FindIndex(n=>n.Id==selected)+1;var pasted=caller.PasteNodes(parent.Id,index,new(1,[clonedCall]));scope.Records[scope.SourceFile]=scope.Records[scope.SourceFile] with{Source=caller.Source,Identities=caller.Identities};
        var after=Snapshot();var final=adapter.ForProject(after);var changes=new List<SourcePhaseChange>();
        foreach(var pair in scope.Records.ToArray())
        {
            var previous=beforeRecords[pair.Key];var candidate=new BehaviorDocument(pair.Value.Source,final,pair.Key.Equals(scope.SourceFile,StringComparison.OrdinalIgnoreCase)?caller.Identities:previous.Identities,pair.Key);
            var errors=final.Validate(candidate,project.EntityId).Where(d=>d.IsError).ToArray();if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
            scope.Records[pair.Key]=pair.Value with{Identities=candidate.Identities};if(previous.Source!=pair.Value.Source)changes.Add(new(pair.Key,previous.Source,pair.Value.Source,previous.Identities.Count>0?previous.Identities:scope.Documents[pair.Key].Identities,[]){AfterIdentities=candidate.Identities});
        }
        return new(project,Snapshot(),changes){Selection=pasted};
    }
}
