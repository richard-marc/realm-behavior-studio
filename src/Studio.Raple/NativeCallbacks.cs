using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TKR.WorldServer.logic;

namespace Studio.Raple;
public sealed record NativeCallbackRecord(long Sequence,long? ParentSequence,long Ms,int ActorId,string Actor,string Behavior,string Stage,string Outcome,NativeStateOrigin? Origin)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public NativeSharedEventBinding? SharedEvent {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public NativeTransitionResult? Transition {get;init;}
}
internal static class NativeCallbacks
{
    const string Registry="TKR.WorldServer.logic.StudioNativeStateOrigins";
    static readonly Func<string[][]>? drain=typeof(Behavior).Assembly.GetType(Registry)?.GetMethod("DrainCallbacks")?.CreateDelegate<Func<string[][]>>();
    static readonly Action? reset=typeof(Behavior).Assembly.GetType(Registry)?.GetMethod("ResetCallbacks")?.CreateDelegate<Action>();
    internal static void Reset()=>reset?.Invoke();
    internal static NativeCallbackRecord[] Drain()=>(drain?.Invoke()??[]).Select(d=>new NativeCallbackRecord(long.Parse(d[0],CultureInfo.InvariantCulture),d[1]=="0"?null:long.Parse(d[1],CultureInfo.InvariantCulture),long.Parse(d[2],CultureInfo.InvariantCulture),int.Parse(d[3],CultureInfo.InvariantCulture),d[4],d[5],d[6],d[7],d[8]==""?null:new(d[8],int.Parse(d[9],CultureInfo.InvariantCulture),int.Parse(d[10],CultureInfo.InvariantCulture),d[11],Guid.TryParse(d[12],out var id)?id:null)){SharedEvent=NativeSharedEvents.FromData(d),Transition=NativeTransitionCallbacks.FromData(d)}).ToArray();
    internal const string RegistrySource="""
namespace TKR.WorldServer.logic {
    internal static partial class StudioNativeStateOrigins {
        [global::System.ThreadStatic] static CallbackScope CurrentCallback;
        static long CallbackSequence;
        static readonly global::System.Collections.Generic.List<string[]> CallbackRecords=new();
        public static T TrackBehavior<T>(T value,string file,int start,int length,string hash,string id) where T:Behavior {
            Origins.Add(value,new[]{file,start.ToString(global::System.Globalization.CultureInfo.InvariantCulture),length.ToString(global::System.Globalization.CultureInfo.InvariantCulture),hash,id});return value;
        }
        public static T TrackTransition<T>(T value,string file,int start,int length,string hash,string id) where T:Transition {
            Origins.Add(value,new[]{file,start.ToString(global::System.Globalization.CultureInfo.InvariantCulture),length.ToString(global::System.Globalization.CultureInfo.InvariantCulture),hash,id});return value;
        }
        public static void ResetCallbacks(){CurrentCallback=null;CallbackSequence=0;CallbackRecords.Clear();}
        public static string[][] DrainCallbacks(){var data=CallbackRecords.ToArray();CallbackRecords.Clear();return data;}
        public static CallbackScope BeginCallback(object behavior,global::TKR.WorldServer.core.objects.Entity host,global::TKR.WorldServer.core.worlds.TickTime time,string stage){
            if(host==null||host.World==null&&behavior is Behavior)return new CallbackScope();
            if(CallbackSequence>=100000)throw new global::System.InvalidOperationException("Native callback transcript exceeded its 100,000-call limit.");
            var origin=Find(behavior);var data=new[]{(++CallbackSequence).ToString(global::System.Globalization.CultureInfo.InvariantCulture),(CurrentCallback?.Sequence??0).ToString(global::System.Globalization.CultureInfo.InvariantCulture),time.TotalElapsedMs.ToString(global::System.Globalization.CultureInfo.InvariantCulture),host.Id.ToString(global::System.Globalization.CultureInfo.InvariantCulture),host.ObjectDesc?.IdName??"",behavior.GetType().FullName,stage,"completed",origin?[0]??"",origin?[1]??"0",origin?[2]??"0",origin?[3]??"",origin?[4]??""};
            global::System.Array.Resize(ref data,27);var scope=new CallbackScope{Parent=CurrentCallback,Data=data,Sequence=CallbackSequence};CurrentCallback=scope;CallbackRecords.Add(data);return scope;
        }
        internal sealed class CallbackScope:global::System.IDisposable {
            internal CallbackScope Parent;internal string[] Data;internal long Sequence;
            // Transition result members are inserted from the observer's source.
            public void Failed(){if(Data!=null)Data[7]="faulted";}
            public void Dispose(){if(Data!=null)CurrentCallback=Parent;}
        }
    }
}
""";
    internal static CSharpCompilation InstrumentDispatch(CSharpCompilation compilation)
    {
        var result=compilation;
        foreach(var tree in compilation.SyntaxTrees)
        {
            var model=compilation.GetSemanticModel(tree);var methods=tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m=>
            {
                if(model.GetDeclaredSymbol(m) is not IMethodSymbol method)return false;
                var native=method.ContainingType.ToDisplayString()==typeof(Behavior).FullName||method.ContainingType.ToDisplayString()==typeof(Transition).FullName;
                var death=false;if(method.IsOverride&&method.Name=="OnDeath")for(var parent=method.OverriddenMethod;parent!=null;parent=parent.OverriddenMethod)if(parent.ContainingType.ToDisplayString()==typeof(Behavior).FullName||parent.ContainingType.ToDisplayString()==typeof(Transition).FullName)death=true;
                return native&&method.DeclaredAccessibility==Accessibility.Public&&(method.Name is "OnStateEntry" or "OnStateExit" or "Tick" or "TickOrdered" or "OnDeath")||death;
            }).ToArray();
            if(methods.Length==0)continue;
            var changed=tree.GetRoot().ReplaceNodes(methods,(original,current)=>
            {
                var symbol=(IMethodSymbol)model.GetDeclaredSymbol(original)!;if(symbol.Parameters.Length!=2)return current;
                var body=current.Body??(current.ExpressionBody==null?null:SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(current.ExpressionBody.Expression)));if(body==null)return current;
                var variable="__studioCallbackScope";while(body.DescendantTokens().Any(t=>t.ValueText==variable))variable+="_";
                var nativeTransition=false;for(var owner=symbol.ContainingType;owner!=null;owner=owner.BaseType)if(owner.ToDisplayString()==typeof(Transition).FullName)nativeTransition=true;
                var stage=symbol.Name switch{"OnStateEntry"=>"Entry","OnStateExit"=>"Exit","Tick"=>nativeTransition?"Transition tick":"Tick","TickOrdered"=>"Ordered tick",_=>nativeTransition?"Transition death":"Death"};
                if(nativeTransition&&symbol.Name=="Tick")body=NativeTransitionCallbacks.ObserveSwitch(body,variable,symbol.Parameters[0].Name);
                string Name(int index)=>SyntaxFacts.GetKeywordKind(symbol.Parameters[index].Name)==SyntaxKind.None?symbol.Parameters[index].Name:"@"+symbol.Parameters[index].Name;
                var prefix=SyntaxFactory.ParseStatement($"using var {variable}=global::{Registry}.BeginCallback(this,{Name(0)},{Name(1)},\"{stage}\");");
                var guarded=SyntaxFactory.TryStatement(body,SyntaxFactory.SingletonList(SyntaxFactory.CatchClause().WithBlock(SyntaxFactory.Block(SyntaxFactory.ParseStatement(variable+".Failed();"),SyntaxFactory.ThrowStatement()))),null);
                return current.WithBody(SyntaxFactory.Block(prefix,guarded)).WithExpressionBody(null).WithSemicolonToken(default);
            });result=result.ReplaceSyntaxTree(tree,tree.WithRootAndOptions(changed,tree.Options));
        }
        var registrySource=RegistrySource.Replace("// Transition result members are inserted from the observer's source.",NativeTransitionCallbacks.ScopeSource,StringComparison.Ordinal);
        return NativeSharedEvents.Instrument(NativePacketOrigins.Instrument(result.AddSyntaxTrees(CSharpSyntaxTree.ParseText(Microsoft.CodeAnalysis.Text.SourceText.From(registrySource,System.Text.Encoding.UTF8),new CSharpParseOptions(LanguageVersion.CSharp12),"_Studio.NativeCallbacks.g.cs"))));
    }
}
