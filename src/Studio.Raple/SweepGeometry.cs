using BossPatternSim;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;
namespace Studio.Raple;
public sealed record SweepGeometry(IReadOnlyList<int> Projectiles,int Arms,double Angle,double Rate,double InnerRadius,double BandSpacing,int LifetimeMs,int RefreshMs,int StartupMs,bool BossLabOnly)
{
    public double OuterRadius=>InnerRadius+Projectiles.Count*BandSpacing;
    public double Heading(long ageMs)=>(Angle+Rate*ageMs/1000d)*Math.PI/180;
}
public sealed record SweepGeometryLocation(string SourceFile,int Start,int Length,SweepGeometry Geometry);
public sealed partial class RapleAdapter
{
    public string? SweepGeometrySeed(string entityId)
    {
        if(!SimEngine.Data.IdToObjectType.TryGetValue(entityId,out var type))return null;
        foreach(var pair in SimEngine.Data.ObjectDescs[type].Projectiles.OrderBy(p=>p.Key))
        {
            var p=pair.Value;var width=p.RadialEndRadius-p.RadialStartRadius;if(!p.Radial||width<=0||p.RadialStartRadius<0||p.RadialEndRadius>14||p.LifetimeMS!=Math.Truncate(p.LifetimeMS)||p.LifetimeMS is <=0 or >int.MaxValue)continue;
            var geometry=new SweepGeometry([pair.Key],2,0,p.RadialAngularSpeed,p.RadialStartRadius,width,(int)p.LifetimeMS,1600,1000,false);if(SweepXmlErrors(entityId,geometry).Count!=0)continue;
            string Number(double n)=>n.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            return "new global::TKR.WorldServer.logic.behaviors.BossSweepGeometry(new[]{"+pair.Key+"},degreesPerSecond:"+Number(geometry.Rate)+",innerRadius:"+Number(geometry.InnerRadius)+",bandSpacing:"+Number(geometry.BandSpacing)+",lifetimeMs:"+geometry.LifetimeMs+")";
        }
        return null;
    }
    public bool IsSweepController(BehaviorDocument document,BehaviorNode node)=>node.Kind is "ClockSweep" or "CorridorPulse" or "OrientationEcho"&&NativeKind(document,node)==node.Kind;
    internal (BaseObjectCreationExpressionSyntax Creation,SemanticModel? Model)? GeometryCreation(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsSweepController(document,node)||node.Parameters.SingleOrDefault(p=>p.Name=="sweep") is not{} parameter)return null;
        if(document.SourceFile!=null)
        {
            var bound=sourceSchemas.Bind(document.Source,document.SourceFile);var model=bound.Compilation.GetSemanticModel(bound.Tree);var expression=bound.Tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().FirstOrDefault(e=>e.SpanStart==parameter.Start&&e.Span.Length==parameter.Length);var seen=new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            while(expression!=null)
            {
                if(expression is BaseObjectCreationExpressionSyntax creation&&model.GetSymbolInfo(creation).Symbol is IMethodSymbol constructor&&constructor.ContainingType.ToDisplayString()=="TKR.WorldServer.logic.behaviors.BossSweepGeometry")return(creation,model);
                var symbol=model.GetSymbolInfo(expression).Symbol;if(symbol is not(ILocalSymbol or IFieldSymbol)||!seen.Add(symbol)||symbol is IFieldSymbol field&&!field.IsReadOnly)return null;
                if(symbol is IFieldSymbol generic&&generic.ContainingType.IsGenericType)return null;
                if(symbol is IFieldSymbol readonlyField)
                    foreach(var part in readonlyField.ContainingType.DeclaringSyntaxReferences.Select(r=>r.GetSyntax()))
                    {var partModel=bound.Compilation.GetSemanticModel(part.SyntaxTree);if(part.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(a=>SymbolEqualityComparer.Default.Equals(partModel.GetSymbolInfo(a.Left).Symbol,symbol)))return null;}
                if(symbol.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax{Initializer:{Value:var initializer}} declaration)return null;
                if(symbol is ILocalSymbol)
                {
                    var scope=declaration.Ancestors().FirstOrDefault(n=>n is MethodDeclarationSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax);if(scope==null)return null;
                    var declarationModel=bound.Compilation.GetSemanticModel(declaration.SyntaxTree);
                    if(scope.DescendantNodes().OfType<IdentifierNameSyntax>().Any(name=>name.Identifier.ValueText==symbol.Name&&SymbolEqualityComparer.Default.Equals(declarationModel.GetSymbolInfo(name).Symbol,symbol)&&name.Ancestors().Any(parent=>parent is AssignmentExpressionSyntax assignment&&assignment.Left.Span.Contains(name.Span)||parent is ArgumentSyntax argument&&argument.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword)))return null;
                }
                model=bound.Compilation.GetSemanticModel(declaration.SyntaxTree);expression=initializer;
            }
            return null;
        }
        return SyntaxFactory.ParseExpression(parameter.Expression) is ObjectCreationExpressionSyntax standalone&&Binding(standalone.ToString()).Constructor?.DeclaringType?.FullName=="TKR.WorldServer.logic.behaviors.BossSweepGeometry"?(standalone,null):null;
    }
    static object? GeometryConstant(ExpressionSyntax expression,SemanticModel? model)
    {if(model!=null){var value=model.GetConstantValue(expression);return value.HasValue?value.Value:null;}var info=ConstructorSchema.Value.ValueInfo(expression.ToString());return info.Constant?info.Value:null;}
    internal static int[]? GeometryIndexes(ExpressionSyntax expression,SemanticModel? model,int depth=0)
    {
        if(depth>16)return null;
        if(model!=null&&expression is IdentifierNameSyntax or MemberAccessExpressionSyntax&&model.GetSymbolInfo(expression).Symbol is ILocalSymbol or IFieldSymbol)
        {
            var symbol=model.GetSymbolInfo(expression).Symbol;if(symbol is IFieldSymbol field&&(!field.IsReadOnly||field.ContainingType.IsGenericType))return null;
            if(symbol?.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax() is VariableDeclaratorSyntax{Initializer:{Value:var value}} declaration)
            {
                var declarationModel=model.Compilation.GetSemanticModel(value.SyntaxTree);
                var scope=symbol is IFieldSymbol?declaration.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault():declaration.Ancestors().FirstOrDefault(n=>n is MethodDeclarationSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax);
                if(scope==null||scope.DescendantNodes().OfType<IdentifierNameSyntax>().Any(name=>SymbolEqualityComparer.Default.Equals(declarationModel.GetSymbolInfo(name).Symbol,symbol)&&name.Ancestors().Any(parent=>parent is AssignmentExpressionSyntax assignment&&assignment.Left.Span.Contains(name.Span)||parent is ArgumentSyntax argument&&argument.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword)))return null;
                return GeometryIndexes(value,declarationModel,depth+1);
            }
        }
        IEnumerable<ExpressionSyntax>? elements=expression switch{ArrayCreationExpressionSyntax{Initializer:{} array}=>array.Expressions,ImplicitArrayCreationExpressionSyntax{Initializer:{} array}=>array.Expressions,CollectionExpressionSyntax collection when collection.Elements.All(e=>e is ExpressionElementSyntax)=>collection.Elements.Cast<ExpressionElementSyntax>().Select(e=>e.Expression),_=>null};
        if(elements!=null){var values=elements.Select(e=>GeometryConstant(e,model)).ToArray();return values.Length is >=1 and <=16&&values.All(v=>v is int index&&index is >=0 and <=255)?values.Cast<int>().ToArray():null;}
        if(model!=null&&expression is InvocationExpressionSyntax toArray&&model.GetSymbolInfo(toArray).Symbol is IMethodSymbol{ Name:"ToArray",ContainingType:{} type}&&type.ToDisplayString()=="System.Linq.Enumerable"&&toArray.Expression is MemberAccessExpressionSyntax{Expression:InvocationExpressionSyntax range}&&model.GetSymbolInfo(range).Symbol is IMethodSymbol{ Name:"Range",ContainingType:{} rangeType}&&rangeType.ToDisplayString()=="System.Linq.Enumerable"&&range.ArgumentList.Arguments.Count==2&&GeometryConstant(range.ArgumentList.Arguments[0].Expression,model) is int start&&GeometryConstant(range.ArgumentList.Arguments[1].Expression,model) is int count&&count is >=1 and <=16&&start>=0&&start+count<=256)return Enumerable.Range(start,count).ToArray();
        return null;
    }
    internal static SweepGeometry? ReadGeometry(BaseObjectCreationExpressionSyntax creation,SemanticModel? model)
    {
        string[] names=["projectileIndexes","armCount","startAngle","degreesPerSecond","innerRadius","bandSpacing","lifetimeMs","refreshMs","startupMs","bossLabOnly"];object?[] defaults=[null,2,0d,18d,.8d,1.6d,1600,1600,1000,false];var expressions=new Dictionary<string,ExpressionSyntax>();var arguments=creation.ArgumentList?.Arguments??default;
        for(var i=0;i<arguments.Count;i++)expressions[arguments[i].NameColon?.Name.Identifier.ValueText??names[i]]=arguments[i].Expression;
        if(!expressions.TryGetValue("projectileIndexes",out var indexesExpression)||GeometryIndexes(indexesExpression,model) is not{} indexes||indexes.Distinct().Count()!=indexes.Length)return null;
        var values=new object?[10];for(var i=1;i<10;i++){values[i]=expressions.TryGetValue(names[i],out var expression)?GeometryConstant(expression,model):defaults[i];if(values[i]==null)return null;}
        double Number(int i)=>Convert.ToDouble(values[i],System.Globalization.CultureInfo.InvariantCulture);
        if(values[9] is not bool only||new[]{1,6,7,8}.Any(i=>values[i] is not int)||Enumerable.Range(1,8).Any(i=>!double.IsFinite(Number(i))))return null;
        var arms=(int)values[1]!;var inner=Number(4);var spacing=Number(5);if(arms is <1 or >8||indexes.Length*arms>128||inner<0||spacing<=0||inner+indexes.Length*spacing>14||(int)values[6]!<=0||(int)values[7]!<100||(int)values[8]!<0)return null;
        return new(indexes,arms,Number(2),Number(3),inner,spacing,(int)values[6]!, (int)values[7]!, (int)values[8]!,only);
    }
    public SweepGeometryLocation? ReadSweepGeometry(BehaviorDocument document,BehaviorNode node)
    {if(GeometryCreation(document,node) is not{} found||ReadGeometry(found.Creation,found.Model) is not{} geometry)return null;return new(found.Creation.SyntaxTree.FilePath.Length==0?document.SourceFile??"":found.Creation.SyntaxTree.FilePath,found.Creation.SpanStart,found.Creation.Span.Length,geometry);}
    public IReadOnlyList<string> SweepXmlErrors(string entityId,SweepGeometry geometry,IReadOnlyList<int>? indexes=null,bool echo=false)
    {
        if(!SimEngine.Data.IdToObjectType.TryGetValue(entityId,out var type))return["Unknown native owner XML."];var owner=SimEngine.Data.ObjectDescs[type];indexes??=geometry.Projectiles;if(indexes.Count!=geometry.Projectiles.Count)return["Echo must have the same native band count."];var errors=new List<string>();
        for(var i=0;i<indexes.Count;i++)
        {
            if(!owner.Projectiles.TryGetValue(indexes[i],out var p)||p.BulletType!=indexes[i]||!p.Radial||Math.Abs(p.RadialStartRadius-(geometry.InnerRadius+i*geometry.BandSpacing))>.00001||Math.Abs(p.RadialEndRadius-(geometry.InnerRadius+(i+1)*geometry.BandSpacing))>.00001||Math.Abs(p.RadialAngularSpeed-(echo?0:geometry.Rate))>.00001||p.LifetimeMS!=geometry.LifetimeMs||p.Speed!=0||p.Converging||p.Parametric||p.Boomerang||p.ReturnToOrigin||p.Homing||p.Orbit||p.Wavy||p.Amplitude!=0)errors.Add("Native XML projectile #"+indexes[i]+" does not match band radius, angular rate, lifetime or motion requirements.");
        }
        return errors;
    }
    IEnumerable<DocumentDiagnostic> ValidateSweepControllers(BehaviorDocument document,string? entityId)
    {
        foreach(var owner in document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId))))foreach(var node in owner.Root.Descendants().Where(n=>IsSweepController(document,n)))
        {
            if(ReadSweepGeometry(document,node) is not{} location)
            {
                if(GeometryCreation(document,node) is{} found&&found.Creation.ArgumentList is{} args&&args.Arguments.All(a=>a.Expression is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax||GeometryConstant(a.Expression,found.Model)!=null||GeometryIndexes(a.Expression,found.Model)!=null))yield return new("Native sweep geometry violates the original constructor bounds or projectile index contract.",node.Id);
                continue;
            }
            var indexes=location.Geometry.Projectiles;
            if(node.Kind=="OrientationEcho")
            {var parameter=node.Parameters.SingleOrDefault(p=>p.Name=="projectileIndexes");if(parameter==null)continue;SemanticModel? model=null;ExpressionSyntax expression=SyntaxFactory.ParseExpression(parameter.Expression);if(document.SourceFile!=null){var bound=sourceSchemas.Bind(document.Source,document.SourceFile);model=bound.Compilation.GetSemanticModel(bound.Tree);expression=bound.Tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().First(e=>e.SpanStart==parameter.Start&&e.Span.Length==parameter.Length);}if(GeometryIndexes(expression,model) is not{} echoIndexes)continue;indexes=echoIndexes;}
            foreach(var caller in owner.Definition?.Encounters??new[]{owner.EntityId})foreach(var error in SweepXmlErrors(caller,location.Geometry,indexes,node.Kind=="OrientationEcho"))yield return new(error,node.Id);
        }
    }
}
