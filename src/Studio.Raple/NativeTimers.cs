using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Raple;
public sealed record NativeTimerBinding(long TimerId,int DelayMs,long? SchedulingCallbackSequence,int? SchedulingActorId,string SchedulingBehavior,string SchedulingStage,string Handler,int Invocation,bool? Returned,bool EntityOwned,int? OwnerActorId,NativeStateOrigin? SchedulingSource);
internal static class NativeTimers
{
    const string Registry="TKR.WorldServer.logic.StudioNativeStateOrigins";
    internal static NativeTimerBinding? FromData(string[] data)
    {
        if(data.Length<43||string.IsNullOrEmpty(data[27]))return null;
        return new(long.Parse(data[27],CultureInfo.InvariantCulture),int.Parse(data[28],CultureInfo.InvariantCulture),long.TryParse(data[29],out var sequence)?sequence:null,int.TryParse(data[30],out var actor)?actor:null,data[32]??"",data[33]??"",data[31]??"",int.Parse(data[39],CultureInfo.InvariantCulture),bool.TryParse(data[40],out var result)?result:null,!string.IsNullOrEmpty(data[41]),int.TryParse(data[41],out var owner)?owner:null,string.IsNullOrEmpty(data[34])?null:new(data[34],int.Parse(data[35],CultureInfo.InvariantCulture),int.Parse(data[36],CultureInfo.InvariantCulture),data[37],Guid.TryParse(data[38],out var id)?id:null));
    }
    internal const string RegistrySource="""
namespace TKR.WorldServer.logic {
    internal static partial class StudioNativeStateOrigins {
        sealed class TimerRegistration { internal long Id;internal int Delay,Invocation;internal string[] Source;internal string Owner,OwnerName; }
        static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::TKR.WorldServer.core.worlds.WorldTimer,TimerRegistration> TimerRegistrations=new();
        static long TimerSequence;
        static void ResetTimers(){TimerRegistrations.Clear();TimerSequence=0;}
        public static void RegisterWorldTimer(global::TKR.WorldServer.core.worlds.WorldTimer timer,int delay,global::System.Delegate callback){
            if(TimerSequence>=100000)throw new global::System.InvalidOperationException("Native timer observation exceeded its 100,000-registration limit.");
            TimerRegistrations.Add(timer,new TimerRegistration{Id=++TimerSequence,Delay=delay,Source=CurrentCallback?.Data==null?null:(string[])CurrentCallback.Data.Clone()});
        }
        public static global::TKR.WorldServer.core.worlds.WorldTimer LinkWorldTimer(global::TKR.WorldServer.core.worlds.WorldTimer timer,global::TKR.WorldServer.core.objects.Entity source){
            if(timer!=null&&TimerRegistrations.TryGetValue(timer,out var registration)){registration.Owner=source?.Id.ToString(global::System.Globalization.CultureInfo.InvariantCulture);registration.OwnerName=source?.ObjectDesc?.IdName;}
            return timer;
        }
        public static CallbackScope BeginWorldTimer(global::TKR.WorldServer.core.worlds.WorldTimer timer,global::TKR.WorldServer.core.worlds.TickTime time,global::System.Delegate callback,int delay,string stage){
            if(CallbackSequence>=100000)throw new global::System.InvalidOperationException("Native callback transcript exceeded its 100,000-call limit.");
            var registration=TimerRegistrations.GetValue(timer,_=>new TimerRegistration{Delay=delay});var source=registration.Source;
            var data=new string[43];data[0]=(++CallbackSequence).ToString(global::System.Globalization.CultureInfo.InvariantCulture);data[1]=(CurrentCallback?.Sequence??0).ToString(global::System.Globalization.CultureInfo.InvariantCulture);data[2]=time.TotalElapsedMs.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            data[3]=registration.Owner??source?[3]??"0";data[4]=registration.OwnerName??source?[4]??"";data[5]="TKR.WorldServer.core.worlds.WorldTimer";data[6]=stage;data[7]="completed";data[8]="";data[9]=data[10]="0";data[11]=data[12]="";
            data[27]=registration.Id.ToString(global::System.Globalization.CultureInfo.InvariantCulture);data[28]=registration.Delay.ToString(global::System.Globalization.CultureInfo.InvariantCulture);data[29]=source?[0]??"";data[30]=source?[3]??"";
            var handlers=callback?.GetInvocationList();data[31]=handlers==null?"null":handlers.Length>256?"Multicast delegate · more than 256 handlers":global::System.String.Join("; ",global::System.Linq.Enumerable.Select(handlers,h=>(h.Method.DeclaringType?.FullName??"<dynamic>")+"."+h.Method.Name));
            data[32]=source?[5]??"";data[33]=source?[6]??"";data[34]=source?[8]??"";data[35]=source?[9]??"0";data[36]=source?[10]??"0";data[37]=source?[11]??"";data[38]=source?[12]??"";data[39]=(++registration.Invocation).ToString(global::System.Globalization.CultureInfo.InvariantCulture);data[40]="";data[41]=registration.Owner??"";data[42]=registration.OwnerName??"";
            var scope=new CallbackScope{Parent=CurrentCallback,Data=data,Sequence=CallbackSequence};CurrentCallback=scope;CallbackRecords.Add(data);return scope;
        }
    }
}
""";
    internal static CSharpCompilation Instrument(CSharpCompilation compilation)
    {
        var result=compilation;var constructors=0;var actions=0;var functions=0;var links=0;
        foreach(var tree in compilation.SyntaxTrees)
        {
            var model=compilation.GetSemanticModel(tree);var replacements=new Dictionary<SyntaxNode,Func<SyntaxNode,SyntaxNode>>();
            foreach(var ctor in tree.GetRoot().DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                if(model.GetDeclaredSymbol(ctor) is not IMethodSymbol symbol||symbol.ContainingType.ToDisplayString()!="TKR.WorldServer.core.worlds.WorldTimer")continue;
                if(ctor.Body==null||ctor.Initializer!=null||symbol.Parameters.Length!=2||symbol.Parameters[0].Type.SpecialType!=SpecialType.System_Int32||symbol.Parameters[1].Type.TypeKind!=TypeKind.Delegate||symbol.ContainingType.GetMembers("Total").OfType<IFieldSymbol>().SingleOrDefault() is not{IsReadOnly:true,Type.SpecialType:SpecialType.System_Int32})throw new ArgumentException("Native WorldTimer constructor changed; timer observation requires review.");
                var callback=symbol.Parameters[1].Name;constructors++;
                replacements.Add(ctor,n=>((ConstructorDeclarationSyntax)n).WithBody(((ConstructorDeclarationSyntax)n).Body!.AddStatements(SyntaxFactory.ParseStatement($"global::{Registry}.RegisterWorldTimer(this,this.Total,{callback});"))));
            }
            foreach(var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if(model.GetDeclaredSymbol(method) is not IMethodSymbol symbol)continue;
                if(symbol.ContainingType.ToDisplayString()=="TKR.WorldServer.core.worlds.WorldTimer"&&symbol.Name=="Tick")
                {
                    foreach(var call in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        if(call.Expression is not MemberAccessExpressionSyntax {Expression:IdentifierNameSyntax field}||model.GetSymbolInfo(field).Symbol is not IFieldSymbol member||member.ContainingType.ToDisplayString()!=symbol.ContainingType.ToDisplayString()||member.Name is not("Callback" or "FuncCallback"))continue;
                        var statement=call.Parent;var action=member.Name=="Callback";
                        if(action&&statement is not ExpressionStatementSyntax||!action&&statement is not ReturnStatementSyntax)throw new ArgumentException("Native timer invocation shape changed.");
                        if(action&&(statement!.Parent is not BlockSyntax block||block.Statements.LastOrDefault() is not ReturnStatementSyntax{Expression:LiteralExpressionSyntax literal}||!literal.IsKind(SyntaxKind.TrueLiteralExpression)))throw new ArgumentException("Native action timer completion changed; observation requires review.");
                        var variable="__studioTimerScope"+call.SpanStart.ToString(CultureInfo.InvariantCulture);while(method.DescendantTokens().Any(t=>t.ValueText==variable||t.ValueText==variable+"Result"))variable+="_";
                        var time=symbol.Parameters[1].Name;
                        var begin=$"using var {variable}=global::{Registry}.BeginWorldTimer(this,{time},{member.Name},this.Total,\"Timer {(action?"action":"function")}\");";
                        var body=action?call.ToString()+$";{variable}.TimerReturned(true);":$"var {variable}Result={call};{variable}.TimerReturned({variable}Result);return {variable}Result;";
                        replacements.Add(statement!,_=>SyntaxFactory.ParseStatement("{"+begin+"try{"+body+"}catch{"+variable+".Failed();throw;}}").WithTriviaFrom(statement!));if(action)actions++;else functions++;
                    }
                }
                if(symbol.ContainingType.ToDisplayString()=="TKR.WorldServer.core.worlds.World"&&symbol.Name=="StartEntityTimer")
                {
                    foreach(var returned in method.DescendantNodes().OfType<ReturnStatementSyntax>().Where(r=>r.Expression!=null&&!r.Ancestors().TakeWhile(a=>a!=method).Any(a=>a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
                    {
                        replacements.Add(returned,n=>((ReturnStatementSyntax)n).WithExpression(SyntaxFactory.ParseExpression($"global::{Registry}.LinkWorldTimer({((ReturnStatementSyntax)n).Expression},{symbol.Parameters[0].Name})")));links++;
                    }
                }
            }
            if(replacements.Count>0){var root=tree.GetRoot().ReplaceNodes(replacements.Keys,(old,updated)=>replacements[old](updated));result=result.ReplaceSyntaxTree(tree,tree.WithRootAndOptions(root,tree.Options));}
        }
        if(constructors!=2||actions!=1||functions!=1||links!=4)throw new ArgumentException("Native timer construction/dispatch boundaries changed; observation requires review: constructors="+constructors+", actions="+actions+", functions="+functions+", links="+links+".");
        return result.AddSyntaxTrees(CSharpSyntaxTree.ParseText(Microsoft.CodeAnalysis.Text.SourceText.From(RegistrySource,System.Text.Encoding.UTF8),new CSharpParseOptions(LanguageVersion.CSharp12),"_Studio.NativeTimers.g.cs"));
    }
}
