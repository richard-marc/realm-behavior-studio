using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Core;
public sealed partial class BehaviorDocument
{
    IReadOnlyList<Guid> PasteInitializerEntries(BehaviorNode parent,int index,BehaviorClipboard clipboard,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate)
    {
        if(schema?.HasSourceContext!=true||parent.Kind!="ArgumentGroup"||parent.ArgumentFromCall||parent.Argument?.IsMember==true)throw new ArgumentException("Paste keyed entries into an explicit source collection initializer.");
        if(clipboard.Symbols==null||clipboard.Symbols.Count>10_000||clipboard.Symbols.Any(s=>s==null||s.ExpressionIndex<0||s.ExpressionIndex>=clipboard.Expressions.Count||s.Start<0||s.Length<=0||s.Start>clipboard.Expressions[s.ExpressionIndex].Length-s.Length||string.IsNullOrEmpty(s.Signature)||s.Signature.Length>4096)||clipboard.Symbols.Sum(s=>(long)s.Signature.Length)>2_000_000)throw new ArgumentException("Invalid entry clipboard bindings.");
        var tree=CSharpSyntaxTree.ParseText(Source).GetRoot();var collection=tree.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().SingleOrDefault(e=>e.SpanStart==parent.Start&&e.Span.Length==parent.Length);
        if(collection?.Initializer==null)throw new ArgumentException("The destination has no actual Add initializer.");
        var entries=clipboard.Expressions.Select(text=>
        {
            var parsed=SyntaxFactory.ParseExpression("new EntryProbe(){"+text+"}") as ObjectCreationExpressionSyntax;
            if(parsed?.Initializer?.Expressions is not {Count:1} values||parsed.ContainsDiagnostics||values[0] is not InitializerExpressionSyntax entry||!entry.IsKind(SyntaxKind.ComplexElementInitializerExpression))throw new ArgumentException("Enter a complete native Add entry.");return (ExpressionSyntax)entry;
        }).ToArray();
        var probeSource=tree.ReplaceNode(collection,collection.WithInitializer(collection.Initializer.WithExpressions(SyntaxFactory.SeparatedList(entries)))).ToFullString();
        var probe=new BehaviorDocument(probeSource,names,schema:baseSchema,sourceFile:SourceFile);var target=probe.AllNodes.Single(n=>n.Path==parent.Path&&n.Kind=="ArgumentGroup");
        if(target.Children.Count!=entries.Length||target.Children.Any(n=>n.Kind!="InitializerEntry"||n.Descendants().Any(c=>c.Kind=="State")))throw new ArgumentException("Resolve entry-owned phases or unsupported Add contracts before clipboard import.");
        var errors=probe.SourceDiagnostics().Where(d=>d.IsError).ToArray();if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        if(clipboard.Symbols.Count>0)
        {
            var bindings=target.Children.SelectMany((n,i)=>probe.SchemaAt(n)!.ClipboardSymbols(probe.Expression(n)).Select(s=>s with{ExpressionIndex=i})).ToArray();
            if(!bindings.SequenceEqual(clipboard.Symbols))throw new ArgumentException("Copied Add entries bind differently in this destination. Keep or qualify their original source context.");
        }
        var order=parent.Children.ToList();var additions=target.Children.ToArray();order.InsertRange(index,additions);
        var inputs=additions.ToDictionary(n=>n.Id,n=>SyntaxFactory.Argument((ExpressionSyntax)CSharpSyntaxTree.ParseText(probeSource).GetRoot().DescendantNodes().OfType<InitializerExpressionSyntax>().Single(e=>e.SpanStart==n.Start&&e.Span.Length==n.Length)));
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
            if(string.IsNullOrWhiteSpace(expression)&&slot.Minimum==0)
            {
                if(container.Children.Count>0)prepared.DeleteNode(container.Children.Single().Id);
            }
            else prepared.ReplaceChild(container.Id,expression);
        }
        var errors=prepared.SourceDiagnostics().Concat(validate?.Invoke(prepared)??[]).Where(d=>d.IsError).ToArray();
        if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        ApplySourceRevision(Revision,Source,prepared.Source,prepared.Identities,baseSchema);return created;
    }
    public Guid DuplicateInitializerEntry(Guid id,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate=null)
    {
        var node=Find(id);if(node.Kind!="InitializerEntry")throw new ArgumentException("Select a bound native Add entry.");var parent=StructuralParent(node);Editable(node);EditableContainer(parent);
        if(node.Descendants().Any(n=>n.Kind=="State"))throw new ArgumentException("Entry-owned phases need source reference reconciliation before independent duplication.");
        BehaviorNode Clone(BehaviorNode current)=>current with{Id=Guid.NewGuid(),Children=current.Children.Select(Clone).ToArray()};var cloned=Clone(node);var order=parent.Children.ToList();order.Insert(order.FindIndex(n=>n.Id==id)+1,cloned);
        var entry=CSharpSyntaxTree.ParseText(Source).GetRoot().DescendantNodes().OfType<InitializerExpressionSyntax>().Single(e=>e.SpanStart==node.Start&&e.Span.Length==node.Length);
        RewriteChildren(new(){[parent.Id]=order},new(){[cloned.Id]=SyntaxFactory.Argument(entry)},validate);return cloned.Id;
    }
}
