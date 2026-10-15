using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using TKR.WorldServer.core.connection;
using TKR.WorldServer.networking;
using TKR.WorldServer.networking.packets.outgoing;

namespace Studio.Raple;

public sealed record NativeAreaImpact(float Radius,ushort Damage,byte Condition,float ConditionDuration);
public sealed record NativeEffect(long Ms,byte Kind,int TargetObjectId,float X,float Y,float X2,float Y2,uint Color,int DurationMs,ushort ObjectType)
{
    // An area message has no ShowEffect kind or target object. Area discriminates its native payload.
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public NativeAreaImpact? Area {get;init;}
}

// An offline transport retains the real sender queue. No socket is opened and
// native visibility filtering still decides which packets reach this client.
sealed class NativePacketCapture : IDisposable
{
    readonly Client client;
    readonly SocketAsyncEventArgs send=new(),receive=new();
    readonly ConcurrentQueue<OutgoingMessage> queue;
    readonly object? previousHandler;
    readonly ProtocolState previousState;
    bool disposed;
    public NativePacketOrigin? EffectOrigin {get;private set;}
    static readonly FieldInfo Handler=typeof(Client).GetField("_handler",BindingFlags.Instance|BindingFlags.NonPublic)!;
    static readonly PropertyInfo State=typeof(Client).GetProperty("State")!;
    static readonly IReadOnlyDictionary<string,FieldInfo> AreaFields=typeof(AoeMessage).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).ToDictionary(f=>f.Name);
    public NativePacketCapture(Client client)
    {
        this.client=client;previousHandler=Handler.GetValue(client);previousState=client.State;
        var handler=new NetworkHandler(client,send,receive);
        var sender=(NetworkSendHandler)typeof(NetworkHandler).GetField("NetworkSendHandler",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(handler)!;
        var state=typeof(NetworkSendHandler).GetField("sendState",BindingFlags.Instance|BindingFlags.NonPublic)!;
        state.SetValue(sender,Enum.Parse(state.FieldType,"Sending"));
        queue=(ConcurrentQueue<OutgoingMessage>)typeof(NetworkSendHandler).GetField("Pending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(sender)!;
        Handler.SetValue(client,handler);State.SetValue(client,ProtocolState.Ready);
    }
    public IEnumerable<NativeEffect> Drain(long ms)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        var count=0;
        while(queue.TryDequeue(out var packet))
        {
            if(++count>65536)throw new InvalidOperationException("Native packet capture exceeded its per-tick limit.");
            if(packet is ShowEffect effect){EffectOrigin=NativePacketOrigins.Take(packet,client);yield return new(ms,(byte)effect.EffectType,effect.TargetObjectId,effect.Pos1.X,effect.Pos1.Y,effect.Pos2.X,effect.Pos2.Y,((uint)effect.Color.A<<24)|((uint)effect.Color.R<<16)|((uint)effect.Color.G<<8)|effect.Color.B,effect.Duration,effect.ObjectType); }
            else if(packet is AoeMessage area)
            {
                T Field<T>(string name)=>(T)AreaFields[name].GetValue(area)!;
                var pos=Field<TKR.WorldServer.core.structures.Position>("Pos");var color=Field<TKR.WorldServer.core.net.datas.ARGB>("Color");
                EffectOrigin=NativePacketOrigins.Take(packet,client);
                yield return new NativeEffect(ms,0,0,pos.X,pos.Y,0,0,((uint)color.A<<24)|((uint)color.R<<16)|((uint)color.G<<8)|color.B,0,Field<ushort>("OrigType"))
                {Area=new(Field<float>("Radius"),unchecked((ushort)Field<int>("Damage")),unchecked((byte)Field<TKR.Shared.resources.ConditionEffectIndex>("Effect")),Field<float>("Duration"))};
            }
        }
    }
    public void Dispose(){if(disposed)return;disposed=true;State.SetValue(client,previousState);Handler.SetValue(client,previousHandler);send.Dispose();receive.Dispose();while(queue.TryDequeue(out _)){} }
}
