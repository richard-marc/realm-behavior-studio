using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Core;
public sealed partial class BehaviorDocument
{
    public IReadOnlyList<InitializerEntryOverload> EntryOverloads(Guid collectionId)
    {
        var collection=Find(collectionId);
        return collection.Kind=="ArgumentGroup"&&!collection.ArgumentFromCall&&!collection.ArgumentRequiresSource&&collection.Argument?.IsCollection==true?SchemaAt(collection)?.InitializerEntryOverloads(Expression(collection))??[]:[];
    }
    public Guid CreateCollectionEntry(Guid collectionId,string signature,IReadOnlyDictionary<string,string> scalarInputs,IReadOnlyDictionary<string,string> childInputs,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate=null)
    {
        var collection=Find(collectionId);EditableContainer(collection);
        var contract=EntryOverloads(collectionId).SingleOrDefault(o=>o.Signature==signature)??throw new ArgumentException("Choose an available native Add overload for this collection.");
        var children=contract.Metadata.Children.Select(c=>c.Name).ToHashSet();
        if(scalarInputs.Keys.Any(k=>children.Contains(k)||contract.Metadata.Parameters.All(p=>p.Name!=k))||childInputs.Keys.Any(k=>!children.Contains(k)))throw new ArgumentException("Unknown native Add input.");
        var values=new List<string>();
        foreach(var parameter in contract.Metadata.Parameters)
        {
            var text=(children.Contains(parameter.Name)?childInputs:scalarInputs).GetValueOrDefault(parameter.Name,"");
            if(string.IsNullOrWhiteSpace(text))
            {
                if(parameter.Default=="required")throw new ArgumentException("Provide the required Add input: "+parameter.Name);
                text=parameter.Default=="empty array"?contract.Seeds[parameter.Name]:parameter.Default;
            }
            values.Add(text);
        }
        var entry=contract.IsIndexer?"["+string.Join(", ",values.Take(values.Count-1))+"] = "+values[^1]:"{"+string.Join(", ",values)+"}";
        if(SchemaAt(collection)!.InitializerEntryBinding(Expression(collection),entry)!=signature)throw new ArgumentException("These inputs do not bind to the selected native Add overload.");
        return PasteNodes(collectionId,collection.Children.Count,new(1,[entry]),validate).Single();
    }
    IReadOnlyList<Guid> PasteInitializerEntries(BehaviorNode parent,int index,BehaviorClipboard clipboard,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate)
    {
        if(schema?.HasSourceContext!=true||parent.Kind!="ArgumentGroup"||parent.ArgumentFromCall||parent.Argument?.IsCollection!=true)throw new ArgumentException("Paste keyed entries into an explicit source collection initializer.");
        if(clipboard.Symbols==null||clipboard.Symbols.Count>10_000||clipboard.Symbols.Any(s=>s==null||s.ExpressionIndex<0||s.ExpressionIndex>=clipboard.Expressions.Count||s.Start<0||s.Length<=0||s.Start>clipboard.Expressions[s.ExpressionIndex].Length-s.Length||string.IsNullOrEmpty(s.Signature)||s.Signature.Length>4096)||clipboard.Symbols.Sum(s=>(long)s.Signature.Length)>2_000_000)throw new ArgumentException("Invalid entry clipboard bindings.");
        var tree=CSharpSyntaxTree.ParseText(Source).GetRoot();var collection=tree.DescendantNodes().OfType<ExpressionSyntax>().SingleOrDefault(e=>e.SpanStart==parent.Start&&e.Span.Length==parent.Length&&e is BaseObjectCreationExpressionSyntax or InitializerExpressionSyntax);
        if(collection==null)throw new ArgumentException("The destination has no actual collection constructor.");
        var entries=clipboard.Expressions.Select(text=>
        {
            var parsed=SyntaxFactory.ParseExpression("new EntryProbe(){"+text+"}") as ObjectCreationExpressionSyntax;
            if(parsed?.Initializer?.Expressions is not {Count:1} values||parsed.ContainsDiagnostics||!IsEntry(values[0]))throw new ArgumentException("Enter a complete native initializer entry.");return values[0];
        }).ToArray();
        var indexed=entries.Any(e=>e is AssignmentExpressionSyntax);if(indexed&&entries.Any(e=>e is not AssignmentExpressionSyntax))throw new ArgumentException("Keep index assignments and Add entries in their native initializer forms.");
        var initializer=Initializer(collection);var form=SyntaxFactory.InitializerExpression(indexed?SyntaxKind.ObjectInitializerExpression:SyntaxKind.CollectionInitializerExpression,initializer.OpenBraceToken,SyntaxFactory.SeparatedList(entries),initializer.CloseBraceToken);
        var replacement=collection is ObjectCreationExpressionSyntax creation?creation.WithInitializer(form):collection is ImplicitObjectCreationExpressionSyntax implicitCreation?implicitCreation.WithInitializer(form):(ExpressionSyntax)form;
        var probeSource=tree.ReplaceNode(collection,replacement).ToFullString();
        var probe=new BehaviorDocument(probeSource,names,schema:baseSchema,sourceFile:SourceFile);var target=probe.AllNodes.Single(n=>n.Path==parent.Path&&n.Kind=="ArgumentGroup");
        if(target.Children.Count!=entries.Length||target.Children.Any(n=>n.Kind!="InitializerEntry"||n.Descendants().Any(c=>c.Kind=="State")))throw new ArgumentException("Resolve entry-owned phases or unsupported Add contracts before clipboard import.");
        var errors=probe.SourceDiagnostics().Where(d=>d.IsError).ToArray();if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        if(clipboard.Symbols.Count>0)
        {
            var bindings=target.Children.SelectMany((n,i)=>probe.SchemaAt(n)!.ClipboardSymbols(probe.Expression(n)).Select(s=>s with{ExpressionIndex=i})).ToArray();
            if(!bindings.SequenceEqual(clipboard.Symbols))throw new ArgumentException("Copied Add entries bind differently in this destination. Keep or qualify their original source context.");
        }
        var order=parent.Children.ToList();var additions=target.Children.ToArray();order.InsertRange(index,additions);
        var probeTree=CSharpSyntaxTree.ParseText(probeSource).GetRoot();var inputs=additions.ToDictionary(n=>n.Id,n=>SyntaxFactory.Argument(NodeExpression(probeTree,n)));
        RewriteChildren(new(){[parent.Id]=order},inputs,validate);return additions.Select(n=>n.Id).ToArray();
    }
    public Guid CreateInitializerEntry(Guid prototype,IReadOnlyDictionary<string,string> scalarInputs,string childExpression,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate=null)
        => CreateInitializerEntry(prototype,scalarInputs,new Dictionary<string,string>{{SchemaAt(Find(prototype))!.ChildArguments(Expression(Find(prototype))).Single().Name,childExpression}},validate);
    public Guid CreateInitializerEntry(Guid prototype,IReadOnlyDictionary<string,string> scalarInputs,IReadOnlyDictionary<string,string> childInputs,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate=null)
    {
        var entry=Find(prototype);if(entry.Kind!="InitializerEntry")throw new ArgumentException("Select a bound Add entry as the creation template.");
        var parent=StructuralParent(entry);EditableContainer(parent);
        if(scalarInputs.Count!=entry.Parameters.Count||entry.Parameters.Any(p=>!scalarInputs.ContainsKey(p.Name)))throw new ArgumentException("Provide every scalar input of this Add overload.");
        if(baseSchema==null)throw new ArgumentException("Entry creation requires native source binding.");
        var prepared=new BehaviorDocument(Source,names,Identities,baseSchema,SourceFile);prepared.LockedNodes.UnionWith(LockedNodes);
        var created=prepared.DuplicateInitializerEntry(prototype);prepared.Apply(new(prepared.Revision,"Set new Add inputs",scalarInputs.Select(p=>new EditOperation(created,p.Key,p.Value)).ToArray()));
        var slots=prepared.SchemaAt(prepared.Find(created))!.ChildArguments(prepared.Expression(prepared.Find(created)));
        if(childInputs.Keys.Any(k=>slots.All(s=>s.Name!=k))||slots.Any(s=>s.Minimum>0&&!childInputs.ContainsKey(s.Name)))throw new ArgumentException("Provide every required behavior input of this Add overload.");
        foreach(var slot in slots)
        {
            var node=prepared.Find(created);var container=node.Children.FirstOrDefault(n=>n.Argument?.Name==slot.Name)??node;
            var expression=childInputs.GetValueOrDefault(slot.Name,"");
            if(slot.IsCollection)prepared.SetNewEntryCollection(created,slot.Name,expression);
            else if(string.IsNullOrWhiteSpace(expression)&&slot.Minimum==0)
            {
                if(container.Children.Count>0)prepared.DeleteNode(container.Children.Single().Id);
            }
            else prepared.ReplaceChild(container.Id,expression);
        }
        var errors=prepared.SourceDiagnostics().Concat(validate?.Invoke(prepared)??[]).Where(d=>d.IsError).ToArray();
        if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        ApplySourceRevision(Revision,Source,prepared.Source,prepared.Identities,baseSchema);return created;
    }
    void SetNewEntryCollection(Guid id,string parameter,string text)
    {
        var node=Find(id);var ownerSchema=SchemaAt(node)!;var metadata=ownerSchema.DescribeExpression(Expression(node))!;
        var container=node.Children.Single(n=>n.Argument?.Name==parameter);Editable(container);
        if(string.IsNullOrWhiteSpace(text))text=metadata.Parameters.Single(p=>p.Name==parameter).Default is "required" or "empty array"?ownerSchema.ChildList(Expression(node),parameter,[]):metadata.Parameters.Single(p=>p.Name==parameter).Default;
        var input=SyntaxFactory.ParseExpression(text);
        if(input.ContainsDiagnostics||input.FullSpan.Length!=text.Length||!input.IsKind(SyntaxKind.NullLiteralExpression)&&ListItems(input)==null)throw new ArgumentException("Provide an explicit native collection or null default; generated lists retain source ownership.");
        var tree=CSharpSyntaxTree.ParseText(Source).GetRoot();var entry=NodeExpression(tree,node);var inputs=EntryInputs(entry).ToList();var position=metadata.Parameters.ToList().FindIndex(p=>p.Name==parameter);
        while(inputs.Count<position)
        {
            var value=metadata.Parameters[inputs.Count];if(value.Default=="required")throw new ArgumentException("Cannot skip a required native Add input.");inputs.Add(SyntaxFactory.ParseExpression(value.Default));
        }
        if(container.Argument?.IsParams==true&&metadata.ArgumentNames.Any(name=>name.Split('[')[0]==parameter&&name.Contains('[')))inputs.RemoveRange(position,inputs.Count-position);
        if(inputs.Count==position)inputs.Add(input);else inputs[position]=input.WithTriviaFrom(inputs[position]);
        var changed=tree.ReplaceNode(entry,WithEntryInputs(entry,inputs)).ToFullString();
        var replaced=container.Children.SelectMany(n=>n.Descendants()).Select(n=>n.Id).ToHashSet();var probe=new BehaviorDocument(changed,names,Identities.Where(n=>!replaced.Contains(n.Id)).ToList(),baseSchema,SourceFile);
        var errors=probe.SourceDiagnostics().Where(d=>d.IsError).ToArray();if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        var actual=probe.AllNodes.SingleOrDefault(n=>n.Path==node.Path&&n.Kind=="InitializerEntry")??throw new ArgumentException("This collection expression does not preserve a supported native Add contract.");
        string? Binding(BehaviorDocument document,BehaviorNode entryNode)=>document.SchemaAt(entryNode)!.ClipboardSymbols(document.Expression(entryNode)).FirstOrDefault(s=>s.Start==0&&s.Length==entryNode.Length)?.Signature;
        if(Binding(this,node)!=Binding(probe,actual))throw new ArgumentException("This collection input changes the selected native Add overload.");
        if(actual.Children.Single(n=>n.Argument?.Name==parameter).ArgumentRequiresSource)throw new ArgumentException("This collection input requires source ownership.");
        Commit(changed,probe.Identities,null);
    }
    public Guid DuplicateInitializerEntry(Guid id,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate=null)
    {
        var node=Find(id);if(node.Kind!="InitializerEntry")throw new ArgumentException("Select a bound native Add entry.");var parent=StructuralParent(node);Editable(node);EditableContainer(parent);
        if(node.Descendants().Any(n=>n.Kind=="State"))throw new ArgumentException("Entry-owned phases need source reference reconciliation before independent duplication.");
        BehaviorNode Clone(BehaviorNode current)=>current with{Id=Guid.NewGuid(),Children=current.Children.Select(Clone).ToArray()};var cloned=Clone(node);var order=parent.Children.ToList();order.Insert(order.FindIndex(n=>n.Id==id)+1,cloned);
        var entry=NodeExpression(CSharpSyntaxTree.ParseText(Source).GetRoot(),node);
        RewriteChildren(new(){[parent.Id]=order},new(){[cloned.Id]=SyntaxFactory.Argument(entry)},validate);return cloned.Id;
    }
}
