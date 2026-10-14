using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TKR.WorldServer.logic;

namespace Studio.Raple;
public sealed record NativePacketOrigin(long CallbackSequence,int CallbackActorId,string Behavior,string Stage,NativeStateOrigin? Source);
internal static class NativePacketOrigins
{
    const string Registry="TKR.WorldServer.logic.StudioNativeStateOrigins";
    static readonly Func<object,object,string[]>? take=typeof(Behavior).Assembly.GetType(Registry)?.GetMethod("TakePacketOrigin")?.CreateDelegate<Func<object,object,string[]>>();
    internal static NativePacketOrigin? Take(object packet,object recipient)
    {
        return FromData(take?.Invoke(packet,recipient));
    }
    internal static NativePacketOrigin? FromData(string[]? data)
    {
        if(data is not {Length:13})return null;
        return new(long.Parse(data[0],CultureInfo.InvariantCulture),int.Parse(data[3],CultureInfo.InvariantCulture),data[5],data[6],data[8]==""?null:new(data[8],int.Parse(data[9],CultureInfo.InvariantCulture),int.Parse(data[10],CultureInfo.InvariantCulture),data[11],Guid.TryParse(data[12],out var id)?id:null));
    }
    internal const string RegistrySource="""
namespace TKR.WorldServer.logic {
    internal static partial class StudioNativeStateOrigins {
        static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<object,global::System.Collections.Generic.Dictionary<object,global::System.Collections.Generic.Queue<string[]>>> EffectOrigins=new();
        public static string[] CurrentPacketOrigin()=>CurrentCallback?.Data==null?null:(string[])CurrentCallback.Data.Clone();
        public static void RecordPacketOrigin(object packet,object recipient) {
            if(packet==null||recipient==null)return;
            var recipients=EffectOrigins.GetOrCreateValue(packet);
            if(!recipients.TryGetValue(recipient,out var queue)){queue=new();recipients.Add(recipient,queue);}
            queue.Enqueue(CurrentCallback?.Data==null?global::System.Array.Empty<string>():(string[])CurrentCallback.Data.Clone());
        }
        public static string[] TakePacketOrigin(object packet,object recipient) {
            if(packet==null||recipient==null||!EffectOrigins.TryGetValue(packet,out var recipients)||!recipients.TryGetValue(recipient,out var queue)||queue.Count==0)return null;
            var data=queue.Dequeue();if(queue.Count==0)recipients.Remove(recipient);return data;
        }
    }
}
""";
    internal static CSharpCompilation Instrument(CSharpCompilation compilation)
    {
        var result=compilation;var shots=0;var effects=0;var structs=0;
        foreach(var tree in compilation.SyntaxTrees)
        {
            var model=compilation.GetSemanticModel(tree);var targets=new Dictionary<ExpressionStatementSyntax,string>();
            foreach(var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if(model.GetDeclaredSymbol(method) is not IMethodSymbol symbol)continue;
                var ack=symbol.ContainingType.ToDisplayString()=="TKR.WorldServer.core.objects.Player"&&symbol.Name=="EnemyShoot"&&symbol.Parameters.Length==1;
                var effect=symbol.ContainingType.ToDisplayString()=="TKR.WorldServer.core.connection.NetworkSendHandler"&&symbol.Name=="SendMessage"&&symbol.Parameters.Length==1;
                if(!ack&&!effect)continue;
                foreach(var statement in method.DescendantNodes().OfType<ExpressionStatementSyntax>())
                {
                    if(statement.Expression is not InvocationExpressionSyntax {Expression:MemberAccessExpressionSyntax member} call||member.Name.Identifier.ValueText!="Enqueue"||model.GetSymbolInfo(member.Expression).Symbol is not IFieldSymbol field)continue;
                    if(ack&&field.Name=="PendingShootAcknowlegements"){targets.Add(statement,$"{call.ArgumentList.Arguments.Single().Expression}.StudioPacketOrigin=global::{Registry}.CurrentPacketOrigin();");shots++;}
                    if(effect&&field.Name=="Pending"&&call.ArgumentList.Arguments.SingleOrDefault()?.Expression is IdentifierNameSyntax id&&id.Identifier.ValueText==symbol.Parameters[0].Name){targets.Add(statement,$"if({symbol.Parameters[0].Name} is global::TKR.WorldServer.networking.packets.outgoing.ShowEffect)global::{Registry}.RecordPacketOrigin({symbol.Parameters[0].Name},Client);");effects++;}
                }
            }
            var acknowledgements=tree.GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>().Where(s=>model.GetDeclaredSymbol(s)?.ToDisplayString()=="TKR.WorldServer.core.objects.Player.ShootAcknowledgement").ToArray();
            if(targets.Count==0&&acknowledgements.Length==0)continue;
            var changed=tree.GetRoot().ReplaceNodes(targets.Keys,(original,current)=>SyntaxFactory.Block(SyntaxFactory.ParseStatement(targets[original]),current));
            foreach(var acknowledgement in acknowledgements)
            {
                if(acknowledgement.Members.OfType<FieldDeclarationSyntax>().SelectMany(f=>f.Declaration.Variables).Any(v=>v.Identifier.ValueText=="StudioPacketOrigin"))throw new ArgumentException("Native acknowledgement conflicts with the packet provenance field.");
                var actual=changed.DescendantNodes().OfType<StructDeclarationSyntax>().Single(s=>s.Identifier.ValueText==acknowledgement.Identifier.ValueText);changed=changed.ReplaceNode(actual,actual.AddMembers(SyntaxFactory.ParseMemberDeclaration("public string[] StudioPacketOrigin=null;")!));structs++;
            }
            result=result.ReplaceSyntaxTree(tree,tree.WithRootAndOptions(changed,tree.Options));
        }
        if(shots!=1||effects!=1||structs!=1)throw new ArgumentException("Native packet provenance requires the original enemy acknowledgement and effect enqueue boundaries.");
        return result.AddSyntaxTrees(CSharpSyntaxTree.ParseText(Microsoft.CodeAnalysis.Text.SourceText.From(RegistrySource,System.Text.Encoding.UTF8),new CSharpParseOptions(LanguageVersion.CSharp12),"_Studio.NativePacketOrigins.g.cs"));
    }
}
