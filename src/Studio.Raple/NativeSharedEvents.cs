using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TKR.WorldServer.logic;
namespace Studio.Raple;
public sealed record NativeSharedEventBinding(int ReceiverActorId,int? SubscriberActorId,long? SubscriptionCallbackSequence,string Handler,string SubscriberBehavior,string SubscriberStage);
internal static class NativeSharedEvents
{
    const string Registry="TKR.WorldServer.logic.StudioNativeStateOrigins";
    static readonly Action<long>? setTime=typeof(Behavior).Assembly.GetType(Registry)?.GetMethod("SetSharedEventTime")?.CreateDelegate<Action<long>>();
    internal static void SetTime(long ms)=>setTime?.Invoke(ms);
    internal static NativeSharedEventBinding? FromData(string[] data)=>data.Length<18?null:new(int.Parse(data[3],CultureInfo.InvariantCulture),int.TryParse(data[13],out var actor)?actor:null,long.TryParse(data[14],out var sequence)?sequence:null,data[15],data[16],data[17]);
    internal const string RegistrySource="""
namespace TKR.WorldServer.logic {
    internal static partial class StudioNativeStateOrigins {
        sealed class EventBindings { internal readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Action,EventSources> Roots=new(); }
        sealed class EventSources { internal global::System.Collections.Generic.List<string[]> Values=new(); }
        static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::TKR.WorldServer.core.objects.Enemy,EventBindings> RemovalBindings=new();
        static long SharedEventTime;
        public static void SetSharedEventTime(long ms)=>SharedEventTime=ms;
        public static void RecordRemovalChange(global::TKR.WorldServer.core.objects.Enemy actor,global::System.Action oldValue,global::System.Action newValue,global::System.Action operand,string operation) {
            if(newValue==null)return;
            var bindings=RemovalBindings.GetOrCreateValue(actor);var oldHandlers=oldValue?.GetInvocationList()??global::System.Array.Empty<global::System.Delegate>();
            var sources=oldValue!=null&&bindings.Roots.TryGetValue(oldValue,out var previous)?new global::System.Collections.Generic.List<string[]>(previous.Values):new global::System.Collections.Generic.List<string[]>(global::System.Linq.Enumerable.Repeat<string[]>(null,oldHandlers.Length));
            var added=operand?.GetInvocationList()??global::System.Array.Empty<global::System.Delegate>();
            if(operation=="=")sources.Clear();
            if(operation=="="||operation=="+=")foreach(var handler in added)sources.Add(CurrentCallback?.Data==null?null:(string[])CurrentCallback.Data.Clone());
            if(operation=="-="&&added.Length>0)for(var i=oldHandlers.Length-added.Length;i>=0;i--){var matches=true;for(var j=0;j<added.Length;j++)if(!oldHandlers[i+j].Equals(added[j])){matches=false;break;}if(matches){sources.RemoveRange(i,added.Length);break;}}
            if(sources.Count!=newValue.GetInvocationList().Length)throw new global::System.InvalidOperationException("Native removal subscription observation did not match the original delegate operation.");
            bindings.Roots.Remove(newValue);bindings.Roots.Add(newValue,new EventSources{Values=sources});
        }
        public static void InvokeRemoval(global::TKR.WorldServer.core.objects.Enemy actor,global::System.Action handlers) {
            if(handlers==null)return;
            EventSources origins=null;if(RemovalBindings.TryGetValue(actor,out var bindings))bindings.Roots.TryGetValue(handlers,out origins);
            var calls=handlers.GetInvocationList();
            for(var i=0;i<calls.Length;i++){
                if(CallbackSequence>=100000)throw new global::System.InvalidOperationException("Native callback transcript exceeded its 100,000-call limit.");
                var source=origins!=null&&i<origins.Values.Count?origins.Values[i]:null;
                var handler=calls[i];var name=(handler.Method.DeclaringType?.FullName??"(dynamic)")+"."+handler.Method.Name;
                var data=new[]{(++CallbackSequence).ToString(global::System.Globalization.CultureInfo.InvariantCulture),(CurrentCallback?.Sequence??0).ToString(global::System.Globalization.CultureInfo.InvariantCulture),CurrentCallback?.Data?[2]??SharedEventTime.ToString(global::System.Globalization.CultureInfo.InvariantCulture),actor.Id.ToString(global::System.Globalization.CultureInfo.InvariantCulture),actor.ObjectDesc?.IdName??"",name,"Encounter removed","completed",source?[8]??"",source?[9]??"0",source?[10]??"0",source?[11]??"",source?[12]??"",source?[3]??"",source?[0]??"",name,source?[5]??"",source?[6]??""};
                var scope=new CallbackScope{Parent=CurrentCallback,Data=data,Sequence=CallbackSequence};CurrentCallback=scope;CallbackRecords.Add(data);
                using(scope){try{((global::System.Action)handler)();}catch{scope.Failed();throw;}}
            }
        }
    }
}
""";
    internal static CSharpCompilation Instrument(CSharpCompilation compilation)
    {
        var result=compilation;var dispatches=0;
        foreach(var tree in compilation.SyntaxTrees)
        {
            var model=compilation.GetSemanticModel(tree);var changes=new Dictionary<ExpressionStatementSyntax,Func<ExpressionStatementSyntax,StatementSyntax>>();
            foreach(var statement in tree.GetRoot().DescendantNodes().OfType<ExpressionStatementSyntax>())
            {
                if(statement.Expression is AssignmentExpressionSyntax {Left:MemberAccessExpressionSyntax member} assignment&&member.Name.Identifier.ValueText=="EncounterRemoved"&&model.GetSymbolInfo(member).Symbol is IPropertySymbol property&&property.ContainingType.ToDisplayString()=="TKR.WorldServer.core.objects.Enemy"&&assignment.Kind() is SyntaxKind.SimpleAssignmentExpression or SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression)
                {
                    if(property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not PropertyDeclarationSyntax {AccessorList:{} accessors}||accessors.Accessors.Any(a=>a.Body!=null||a.ExpressionBody!=null))continue;
                    if(statement.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().Any(lambda=>model.GetTypeInfo(lambda).ConvertedType is INamedTypeSymbol type&&type.OriginalDefinition.ToDisplayString()=="System.Linq.Expressions.Expression<TDelegate>"))continue;
                    if(statement.Ancestors().OfType<ExpressionSyntax>().Any(call=>call is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax&&model.GetSymbolInfo(call).Symbol is IMethodSymbol targetMethod&&targetMethod.Parameters.Any(p=>p.GetAttributes().Any(a=>a.AttributeClass?.ToDisplayString()=="System.Runtime.CompilerServices.CallerArgumentExpressionAttribute"))))continue;
                    var suffix=statement.SpanStart.ToString(CultureInfo.InvariantCulture);var target="__studioEventTarget"+suffix;var old="__studioEventOld"+suffix;var value="__studioEventValue"+suffix;
                    while(statement.Ancestors().Last().DescendantTokens().Any(t=>t.ValueText==target||t.ValueText==old||t.ValueText==value)){target+="_";old+="_";value+="_";}
                    changes.Add(statement,current=>{
                        var actual=(AssignmentExpressionSyntax)current.Expression;var actualMember=(MemberAccessExpressionSyntax)actual.Left;
                        var receiver=$"global::TKR.WorldServer.core.objects.Enemy {target}=({actualMember.Expression});";
                        var read=$"global::System.Action {old}={target}.EncounterRemoved;";var right=$"global::System.Action {value}=({actual.Right});";
                        var body=receiver+(actual.IsKind(SyntaxKind.SimpleAssignmentExpression)?right+read:read+right)+$"{target}.EncounterRemoved {actual.OperatorToken.Text} {value};global::{Registry}.RecordRemovalChange({target},{old},{target}.EncounterRemoved,{value},\"{actual.OperatorToken.Text}\");";
                        return SyntaxFactory.ParseStatement("{"+body+"}").WithTriviaFrom(current);
                    });
                }
                else if(statement.Expression is ConditionalAccessExpressionSyntax {Expression:IdentifierNameSyntax {Identifier.ValueText:"removed"},WhenNotNull:InvocationExpressionSyntax {Expression:MemberBindingExpressionSyntax {Name.Identifier.ValueText:"Invoke"}}}&&statement.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is{} method&&model.GetDeclaredSymbol(method)?.ContainingType.ToDisplayString()=="TKR.WorldServer.core.objects.Entity"&&method.Identifier.ValueText=="Expunge")
                {changes.Add(statement,current=>SyntaxFactory.ParseStatement($"global::{Registry}.InvokeRemoval(enemy,removed);").WithTriviaFrom(current));dispatches++;}
            }
            if(changes.Count>0){var root=tree.GetRoot().ReplaceNodes(changes.Keys,(original,current)=>changes[original](current));result=result.ReplaceSyntaxTree(tree,tree.WithRootAndOptions(root,tree.Options));}
        }
        if(dispatches!=1)throw new ArgumentException("The supplied native removal dispatch boundary changed. Shared-event observation requires review.");
        return result.AddSyntaxTrees(CSharpSyntaxTree.ParseText(Microsoft.CodeAnalysis.Text.SourceText.From(RegistrySource,System.Text.Encoding.UTF8),new CSharpParseOptions(LanguageVersion.CSharp12),"_Studio.NativeSharedEvents.g.cs"));
    }
}
