using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Core;
public sealed partial class BehaviorDocument
{
    public Guid DuplicateInitializerEntry(Guid id,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate=null)
    {
        var node=Find(id);if(node.Kind!="InitializerEntry")throw new ArgumentException("Select a bound native Add entry.");var parent=StructuralParent(node);Editable(node);EditableContainer(parent);
        if(node.Descendants().Any(n=>n.Kind=="State"))throw new ArgumentException("Entry-owned phases need source reference reconciliation before independent duplication.");
        BehaviorNode Clone(BehaviorNode current)=>current with{Id=Guid.NewGuid(),Children=current.Children.Select(Clone).ToArray()};var cloned=Clone(node);var order=parent.Children.ToList();order.Insert(order.FindIndex(n=>n.Id==id)+1,cloned);
        var entry=CSharpSyntaxTree.ParseText(Source).GetRoot().DescendantNodes().OfType<InitializerExpressionSyntax>().Single(e=>e.SpanStart==node.Start&&e.Span.Length==node.Length);
        RewriteChildren(new(){[parent.Id]=order},new(){[cloned.Id]=SyntaxFactory.Argument(entry)},validate);return cloned.Id;
    }
}
