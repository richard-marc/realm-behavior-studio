using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Core;
public sealed partial class BehaviorDocument
{
    public Guid CreateInitializerEntry(Guid prototype,IReadOnlyDictionary<string,string> scalarInputs,string childExpression,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate=null)
    {
        var entry=Find(prototype);if(entry.Kind!="InitializerEntry")throw new ArgumentException("Select a bound Add entry as the creation template.");
        var parent=StructuralParent(entry);EditableContainer(parent);
        if(scalarInputs.Count!=entry.Parameters.Count||entry.Parameters.Any(p=>!scalarInputs.ContainsKey(p.Name)))throw new ArgumentException("Provide every scalar input of this Add overload.");
        if(baseSchema==null)throw new ArgumentException("Entry creation requires native source binding.");
        var prepared=new BehaviorDocument(Source,names,Identities,baseSchema,SourceFile);prepared.LockedNodes.UnionWith(LockedNodes);
        var created=prepared.DuplicateInitializerEntry(prototype);prepared.Apply(new(prepared.Revision,"Set new Add inputs",scalarInputs.Select(p=>new EditOperation(created,p.Key,p.Value)).ToArray()));
        prepared.ReplaceChild(created,childExpression);var errors=prepared.SourceDiagnostics().Concat(validate?.Invoke(prepared)??[]).Where(d=>d.IsError).ToArray();
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
