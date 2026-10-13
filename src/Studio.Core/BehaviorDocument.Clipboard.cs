using Microsoft.CodeAnalysis.CSharp;

namespace Studio.Core;
public sealed partial class BehaviorDocument
{
    void RenameCopiedPhases(BehaviorNode fragment,IReadOnlyDictionary<string,string> renames)
    {
        if(renames.Count==0)return;
        var owner=OwningEntity(fragment.Id);var nodes=fragment.Children.SelectMany(n=>n.Descendants()).ToArray();
        var edits=new List<SourceLiteralEdit>();
        foreach(var node in nodes)
        {
            if(node.Kind is "Factory" or "Reference" or "Spread"||node.Kind=="ArgumentGroup"&&node.ArgumentRequiresSource&&!node.IsMemberInitializer)
            {
                var definitions=ReferencedDefinitions(node);
                if(ExternalDefinitions(node).Count>0||definitions.Count==0||definitions.Any(d=>!d.Definition!.StructurallyComplete||d.Root.Descendants().Any(n=>n.Kind=="State")))
                    throw new ArgumentException("A copied phase contains shared/generated phase definitions. Copy or extract their ownership in Source before making an independent branch.");
                continue;
            }
            if(node.Kind=="State"&&StateName(node.Id) is {} name&&renames.TryGetValue(name,out var renamed))
            {
                var parameter=node.Parameters.First(p=>LiteralString(p.Expression)==name);
                edits.Add(new(parameter.Start,parameter.Length,SyntaxFactory.Literal(renamed).Text));
            }
            foreach(var parameter in node.Parameters)
            {
                var scope=ReferencesEntityPhase(node,parameter,owner);
                if(scope==false)continue;
                var values=PhaseLiterals(parameter.Expression);
                if(scope==null||values==null)throw new ArgumentException("A copied phase has unresolved phase ownership or destinations. Reconcile it in Source before copying.");
                foreach(var value in values)
                    if(renames.TryGetValue(value.Name,out var destination))edits.Add(new(parameter.Start+value.Start,value.Length,SyntaxFactory.Literal(destination).Text));
            }
        }
        var changed=Source;foreach(var edit in edits.Distinct().OrderByDescending(e=>e.Start))changed=changed.Remove(edit.Start,edit.Length).Insert(edit.Start,edit.Expression);
        CommitStructure(changed,[],null);
    }
}
