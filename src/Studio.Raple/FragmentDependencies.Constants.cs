using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Raple;
public static partial class FragmentDependencies
{
    static ExpressionSyntax WrapLexicalConstants(ExpressionSyntax expression,IEnumerable<ILocalSymbol> constants,ITypeSymbol result,IReadOnlyDictionary<ISymbol,string> names,string key)
    {
        string TypeName(ITypeSymbol type)=>string.Concat(type.ToDisplayParts(SymbolDisplayFormat.FullyQualifiedFormat).Select(p=>p.Symbol!=null&&names.TryGetValue(p.Symbol.OriginalDefinition,out var renamed)?renamed:p.ToString()));
        string Value(ILocalSymbol constant)
        {
            var value=constant.ConstantValue;
            var literal=value switch
            {
                null=>"null",string s=>SyntaxFactory.Literal(s).Text,char c=>SyntaxFactory.Literal(c).Text,bool b=>b?"true":"false",
                float f when float.IsNaN(f)=>"global::System.Single.NaN",float f when float.IsPositiveInfinity(f)=>"global::System.Single.PositiveInfinity",float f when float.IsNegativeInfinity(f)=>"global::System.Single.NegativeInfinity",
                double d when double.IsNaN(d)=>"global::System.Double.NaN",double d when double.IsPositiveInfinity(d)=>"global::System.Double.PositiveInfinity",double d when double.IsNegativeInfinity(d)=>"global::System.Double.NegativeInfinity",
                float f=>SyntaxFactory.Literal(f).Text,double d=>SyntaxFactory.Literal(d).Text,decimal m=>SyntaxFactory.Literal(m).Text,long l=>SyntaxFactory.Literal(l).Text,uint u=>SyntaxFactory.Literal(u).Text,ulong u=>SyntaxFactory.Literal(u).Text,
                _=>Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)!
            };
            return constant.Type.TypeKind==TypeKind.Enum?"unchecked(("+TypeName(constant.Type)+")("+literal+"))":literal;
        }
        var declarations=string.Join("\n",constants.OrderBy(c=>c.DeclaringSyntaxReferences.First().Span.Start).Select(c=>"const "+TypeName(c.Type)+" @"+c.Name+" = "+Value(c)+";"));
        var marker=expression.GetAnnotations("rbs-fragment").Where(a=>a.Data==key).ToArray();
        var wrapper=SyntaxFactory.ParseExpression("((global::System.Func<"+TypeName(result)+">)(() => {\n"+declarations+"\nreturn __rbsConstantValue;\n}))()");
        var returned=wrapper.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
        return wrapper.ReplaceNode(returned.Expression!,expression.WithoutAnnotations(marker).WithoutTrivia()).WithAdditionalAnnotations(marker).WithTriviaFrom(expression);
    }
}
