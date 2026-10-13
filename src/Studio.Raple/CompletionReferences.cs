using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;

public sealed record CompletionMovement(Guid ReferenceId,string Expression,string Kind,string Phase,string SourceFile,int Line)
{
    public string TransitionKind=>Kind+"CompleteTransition";
    public override string ToString()=>Expression+" · "+(Kind=="LockedFollowDistance"?"Locked follow":"Bounded follow")+(Phase.Length==0?"":" · "+Phase);
}
public sealed record CompletionBinding(string Kind,Guid? MovementReferenceId,string Detail);

public sealed partial class RapleAdapter
{
    public bool IsMovementCompletion(BehaviorDocument document,BehaviorNode node)=>node.Kind is "FollowDistanceCompleteTransition" or "LockedFollowDistanceCompleteTransition"&&NativeKind(document,node)==node.Kind;
    BehaviorNode? CompletionPhase(BehaviorDocument document,BehaviorNode node)
    {for(var current=node;current!=null;current=document.Parent(current.Id))if(current.Kind=="State")return current;return null;}
    (SemanticModel Model,SyntaxNode Root)? CompletionContext(BehaviorDocument document)
    {
        if(document.SourceFile==null||!File.Exists(document.SourceFile))return null;
        var bound=sourceSchemas.Bind(document.Source,document.SourceFile);return(bound.Compilation.GetSemanticModel(bound.Tree),bound.Tree.GetRoot());
    }
    static ExpressionSyntax? AtExpression(SyntaxNode root,BehaviorNode node)=>root.DescendantNodes().OfType<ExpressionSyntax>().FirstOrDefault(e=>e.SpanStart==node.Start&&e.Span.Length==node.Length);
    // Bind storage identity rather than evaluating a getter/factory. Alias chains
    // may resolve to the same initialized local or readonly field instance.
    static ISymbol? StableMovement(SemanticModel model,ExpressionSyntax expression,HashSet<ISymbol>? seen=null)
    {
        while(expression is ParenthesizedExpressionSyntax bracket)expression=bracket.Expression;
        if(expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))return null;
        var symbol=model.GetSymbolInfo(expression).Symbol;if(symbol is not (ILocalSymbol or IFieldSymbol))return null;
        if(symbol is IFieldSymbol genericField)
            for(var owner=genericField.ContainingType;owner!=null;owner=owner.ContainingType)if(owner.IsGenericType)return null;
        if(symbol is IFieldSymbol field&&(!field.IsReadOnly||!field.IsStatic&&expression is MemberAccessExpressionSyntax {Expression:not ThisExpressionSyntax}))return null;
        seen??=new(SymbolEqualityComparer.Default);if(!seen.Add(symbol))return null;
        var declaration=symbol.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;if(declaration?.Initializer?.Value is not {} initializer)return null;
        var declaredModel=model.Compilation.GetSemanticModel(declaration.SyntaxTree);
        if(symbol is IFieldSymbol readonlyField)
            foreach(var typePart in readonlyField.ContainingType.DeclaringSyntaxReferences)
            {
                var part=typePart.GetSyntax();var partModel=model.Compilation.GetSemanticModel(part.SyntaxTree);
                foreach(var assignment in part.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    if(SymbolEqualityComparer.Default.Equals(partModel.GetSymbolInfo(assignment.Left).Symbol,symbol))return null;
            }
        if(symbol is ILocalSymbol)
        {
            var scope=declaration.Ancestors().FirstOrDefault(n=>n is AnonymousFunctionExpressionSyntax or MethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax);
            if(scope==null)return null;
            foreach(var name in scope.DescendantNodes().OfType<IdentifierNameSyntax>().Where(n=>n.Identifier.ValueText==symbol.Name&&SymbolEqualityComparer.Default.Equals(declaredModel.GetSymbolInfo(n).Symbol,symbol)))
                if(name.Ancestors().Any(n=>n is AssignmentExpressionSyntax assignment&&assignment.Left.Span.Contains(name.Span)||n is ArgumentSyntax argument&&argument.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword))return null;
        }
        if(initializer is BaseObjectCreationExpressionSyntax&&declaredModel.GetSymbolInfo(initializer).Symbol is IMethodSymbol constructor&&constructor.ContainingType.ToDisplayString() is "TKR.WorldServer.logic.behaviors.FollowDistance" or "TKR.WorldServer.logic.behaviors.LockedFollowDistance")return symbol;
        return StableMovement(declaredModel,initializer,seen);
    }
    public IReadOnlyList<CompletionMovement> CompletionMovements(BehaviorDocument document,BehaviorNode node)
    {
        if(CompletionPhase(document,node) is not {} phase||CompletionContext(document) is not {} context)return [];
        var required=IsMovementCompletion(document,node)?node.Kind.Replace("CompleteTransition",""):null;var result=new List<CompletionMovement>();var instances=new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        for(BehaviorNode? owner=phase;owner!=null;owner=document.Parent(owner.Id))
        {
            if(owner.Kind!="State")continue;
            foreach(var reference in owner.StateChildren.Where(n=>n.Kind=="Reference"))
            {
                var expression=AtExpression(context.Root,reference);if(expression==null)continue;
                var type=context.Model.GetTypeInfo(expression).Type;var kind=type?.ToDisplayString() switch{"TKR.WorldServer.logic.behaviors.FollowDistance"=>"FollowDistance","TKR.WorldServer.logic.behaviors.LockedFollowDistance"=>"LockedFollowDistance",_=>null};
                if(kind==null||required!=null&&required!=kind||StableMovement(context.Model,expression) is not {} storage||!instances.Add(storage))continue;
                result.Add(new(reference.Id,expression.ToString(),kind,document.StateName(owner.Id)??owner.Label,document.SourceFile!,expression.GetLocation().GetLineSpan().StartLinePosition.Line+1));
            }
        }
        return result;
    }
    public CompletionBinding? ReadCompletionBinding(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsMovementCompletion(document,node))return null;
        if(CompletionContext(document) is not {} context||AtExpression(context.Root,node) is not BaseObjectCreationExpressionSyntax creation)return new(node.Kind,null,"Source context is needed to identify this movement instance.");
        var args=creation.ArgumentList?.Arguments??default;var movement=args.FirstOrDefault(a=>a.NameColon?.Name.Identifier.ValueText=="movement")??args.FirstOrDefault(a=>a.NameColon==null);
        var identity=movement==null?null:StableMovement(context.Model,movement.Expression);
        if(identity!=null)
            foreach(var candidate in CompletionMovements(document,node))
                if(AtExpression(context.Root,document.Find(candidate.ReferenceId)) is {} expression&&SymbolEqualityComparer.Default.Equals(identity,StableMovement(context.Model,expression)))return new(node.Kind,candidate.ReferenceId,"Uses the same initialized movement instance as "+candidate.Expression+" in "+candidate.Phase+".");
        return new(node.Kind,null,"No stable shared instance is proven among this phase's direct or inherited movement references. New constructors, getters, reassigned locals and generated dispatch need Source review.");
    }
    public Guid AddCompletionRoute(BehaviorDocument document,Guid phaseId,Guid movementReference,long revision,string target,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate=null)
    {
        if(document.Revision!=revision)throw new InvalidOperationException("This completion proposal is stale.");var phase=document.Find(phaseId);if(phase.Kind!="State")throw new ArgumentException("Choose the native phase that owns this route.");
        var movement=CompletionMovements(document,phase).SingleOrDefault(m=>m.ReferenceId==movementReference)??throw new ArgumentException("Choose an existing stable movement reference in this phase or its parents.");
        var before=document.AllNodes.Select(n=>n.Id).ToHashSet();var literal=SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,SyntaxFactory.Literal(target)).ToFullString();
        document.AddChild(phase.Id,"new "+CreationTypeName(movement.TransitionKind)+"(movement: "+movement.Expression+", targetState: "+literal+")",validate);
        return document.Find(phase.Id).StateChildren.Single(n=>!before.Contains(n.Id)&&n.Kind==movement.TransitionKind).Id;
    }
    public void RelinkCompletionRoute(BehaviorDocument document,Guid transitionId,Guid movementReference,long revision,Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate=null)
    {
        if(document.Revision!=revision)throw new InvalidOperationException("This completion proposal is stale.");var transition=document.Find(transitionId);if(!IsMovementCompletion(document,transition))throw new ArgumentException("Choose a native movement completion transition.");
        var movement=CompletionMovements(document,transition).SingleOrDefault(m=>m.ReferenceId==movementReference)??throw new ArgumentException("Choose a stable movement reference of the native required type.");
        document.ReplaceChild(transition.Id,movement.Expression,validate);
    }
}
