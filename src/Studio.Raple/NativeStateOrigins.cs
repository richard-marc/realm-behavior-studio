using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;
using TKR.WorldServer.logic;

namespace Studio.Raple;

// An executed construction site for a State or Behavior; event causality is separate.
public sealed record NativeStateOrigin(string SourceFile,int Start,int Length,string SourceHash,Guid? NodeId)
{
    public bool TryResolve(StudioProject project,out string source)
    {
        source="";
        var record=project.AdditionalSources.Prepend(new(project.SourceFile,project.Source))
            .FirstOrDefault(s=>Path.GetFullPath(s.SourceFile).Equals(SourceFile,StringComparison.OrdinalIgnoreCase));
        if(record==null||Start<0||Length<=0||Start>record.Source.Length-Length||StudioSourceDraft.Hash(record.Source)!=SourceHash)return false;
        source=record.Source;return true;
    }
}

internal static class NativeStateOrigins
{
    const string RegistryName="TKR.WorldServer.logic.StudioNativeStateOrigins";
    internal const string RegistrySource="""
namespace TKR.WorldServer.logic {
    internal static partial class StudioNativeStateOrigins {
        static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<object,string[]> Origins=new();
        public static State Track(State state,string file,int start,int length,string hash,string id) {
            Origins.Add(state,new[]{file,start.ToString(global::System.Globalization.CultureInfo.InvariantCulture),length.ToString(global::System.Globalization.CultureInfo.InvariantCulture),hash,id});
            return state;
        }
        public static string[] Find(object state) => state!=null&&Origins.TryGetValue(state,out var origin)?origin:null;
    }
}
""";
    static readonly Func<object,string[]?>? lookup=typeof(State).Assembly.GetType(RegistryName)?.GetMethod("Find")?.CreateDelegate<Func<object,string[]?>>();
    internal static NativeStateOrigin? Find(State? state)
    {
        if(state==null||lookup?.Invoke(state) is not {Length:5} data)return null;
        return new(data[0],int.Parse(data[1],System.Globalization.CultureInfo.InvariantCulture),int.Parse(data[2],System.Globalization.CultureInfo.InvariantCulture),data[3],Guid.TryParse(data[4],out var id)?id:null);
    }
    internal static CSharpCompilation Instrument(CSharpCompilation original,StudioProject project)
    {
        if(original.GetTypeByMetadataName(RegistryName)!=null)throw new ArgumentException("Source conflicts with Studio's native provenance registry.");
        var result=original;
        foreach(var record in project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities}))
        {
            var tree=original.SyntaxTrees.Single(t=>Path.GetFullPath(t.FilePath).Equals(Path.GetFullPath(record.SourceFile),StringComparison.OrdinalIgnoreCase));
            // Existing exact constructor signatures supply optional editor IDs. Ambiguous aliases/sites keep span provenance.
            var uniqueSites=tree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().GroupBy(c=>c.ToString())
                .Where(g=>g.Count()==1).Select(g=>g.Key).ToHashSet(StringComparer.Ordinal);
            var ids=record.Identities.Where(i=>i.Signature!=null&&uniqueSites.Contains(i.Signature)).GroupBy(i=>i.Signature!)
                .Where(g=>g.Select(i=>i.Id).Distinct().Count()==1).ToDictionary(g=>g.Key,g=>g.First().Id,StringComparer.Ordinal);
            var changed=new Tracker(original.GetSemanticModel(tree),Path.GetFullPath(record.SourceFile),StudioSourceDraft.Hash(record.Source),ids).Visit(tree.GetRoot())!;
            result=result.ReplaceSyntaxTree(tree,tree.WithRootAndOptions(changed,tree.Options));
        }
        return NativeCallbacks.InstrumentDispatch(result.AddSyntaxTrees(CSharpSyntaxTree.ParseText(Microsoft.CodeAnalysis.Text.SourceText.From(RegistrySource,System.Text.Encoding.UTF8),new CSharpParseOptions(LanguageVersion.CSharp12),"_Studio.NativeStateOrigins.g.cs")));
    }
    sealed class Tracker(SemanticModel model,string file,string hash,IReadOnlyDictionary<string,Guid> ids):CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)=>Track(node,(BaseObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!);
        public override SyntaxNode? VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)=>Track(node,(BaseObjectCreationExpressionSyntax)base.VisitImplicitObjectCreationExpression(node)!);
        SyntaxNode Track(BaseObjectCreationExpressionSyntax original,BaseObjectCreationExpressionSyntax changed)
        {
            if(model.GetSymbolInfo(original).Symbol is not IMethodSymbol method)return changed;
            var state=method.ContainingType.ToDisplayString()==typeof(State).FullName;
            var behavior=false;for(var type=method.ContainingType;type!=null;type=type.BaseType)if(type.ToDisplayString()==typeof(Behavior).FullName)behavior=true;
            if(!state&&!behavior)return changed;
            // Expression-tree consumers can inspect constructor syntax; retain their original trees.
            if(original.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().Any(lambda=>model.GetTypeInfo(lambda).ConvertedType is INamedTypeSymbol type&&type.OriginalDefinition.ToDisplayString()=="System.Linq.Expressions.Expression<TDelegate>"))return changed;
            // Wrapping an argument changes the text that CallerArgumentExpression observes.
            if(original.Ancestors().OfType<ExpressionSyntax>().Any(call=>call is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax&&model.GetSymbolInfo(call).Symbol is IMethodSymbol target&&target.Parameters.Any(p=>p.GetAttributes().Any(a=>a.AttributeClass?.ToDisplayString()=="System.Runtime.CompilerServices.CallerArgumentExpressionAttribute"))))return changed;
            var creation=changed is ImplicitObjectCreationExpressionSyntax implicitCreation
                ?SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),implicitCreation.ArgumentList,implicitCreation.Initializer)
                    .WithNewKeyword(SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space)):changed;
            static string Literal(string text)=>SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,SyntaxFactory.Literal(text)).ToFullString();
            var id=ids.TryGetValue(original.ToString(),out var known)?known.ToString():"";
            var expression=$"global::{RegistryName}.{(state?"Track":"TrackBehavior")}({creation.WithoutTrivia().ToFullString()},{Literal(file)},{original.SpanStart},{original.Span.Length},{Literal(hash)},{Literal(id)})";
            return SyntaxFactory.ParseExpression(expression).WithTriviaFrom(original);
        }
    }
}
