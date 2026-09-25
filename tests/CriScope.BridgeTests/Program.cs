using CriScope.Core;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static async Task Until(Func<bool> condition,string message)
{using var timeout=new CancellationTokenSource(5000);while(!condition()){if(timeout.IsCancellationRequested)throw new Exception(message);await Task.Delay(10);}}
static byte[] Json(object value)=>JsonSerializer.SerializeToUtf8Bytes(value);
static byte[] Hello(string client,string capture,string machine)
{
    var body=Json(new{version=3,clientId=client,captureId=capture,name="Bridge fixture",machine,platform="Windows",pid=12345});
    var bytes=new byte[8+body.Length];"CSB3"u8.CopyTo(bytes);BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4),body.Length);body.CopyTo(bytes,8);return bytes;
}
static byte[] Frame(int kind,long seq,int epoch,long micros,byte[] body)
{
    var bytes=new byte[25+body.Length];BinaryPrimitives.WriteInt32BigEndian(bytes,body.Length);bytes[4]=(byte)kind;
    BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(5),seq);BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(13),epoch);
    BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(17),micros);body.CopyTo(bytes,25);return bytes;
}
static byte[] Native(ulong micros)
{
    var bytes=new byte[38];BinaryPrimitives.WriteUInt32BigEndian(bytes,38);BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4),31);bytes[6]=8;
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8),micros);BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16),2138);
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(32),137);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(34),3);return bytes;
}
static byte[] Listener(ulong micros,bool complete)
{
    using var payload=new MemoryStream();
    void U16(ushort value){byte[] b=new byte[2];BinaryPrimitives.WriteUInt16BigEndian(b,value);payload.Write(b);}
    void F(float value){byte[] b=new byte[4];BinaryPrimitives.WriteInt32BigEndian(b,BitConverter.SingleToInt32Bits(value));payload.Write(b);}
    U16(50);payload.Write(new byte[]{0,0,0,0,0,0,0,1});
    U16(164);F(1);F(2);F(3);
    if(complete){U16(168);F(11);F(12);F(13);U16(173);F(.5f);}
    var result=new byte[32+payload.Length];BinaryPrimitives.WriteUInt32BigEndian(result,(uint)result.Length);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4),31);result[6]=8;
    BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(8),micros);BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(16),2144);
    payload.ToArray().CopyTo(result,32);return result;
}
static async Task Send(TcpClient client,byte[] data,bool fragmented=false)
{
    var stream=client.GetStream();int step=fragmented?3:data.Length;
    for(int offset=0;offset<data.Length;offset+=step)await stream.WriteAsync(data.AsMemory(offset,Math.Min(step,data.Length-offset)));
}
static async Task<TcpClient> Connect(int port,string client,string capture,string machine)
{var peer=new TcpClient();await peer.ConnectAsync(IPAddress.Loopback,port);await Send(peer,Hello(client,capture,machine),true);return peer;}
static async Task Closed(TcpClient client)
{using var deadline=new CancellationTokenSource(5000);var b=new byte[1];try{Check(await client.GetStream().ReadAsync(b,deadline.Token)==0,"Rejected bridge remains open");}catch(IOException){}}

var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();int port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();
Check(port!=2002&&port!=18961,"Test must not use production ports");
using var collector=new Collector(Path.GetTempPath());collector.Start(port);
string a=Guid.NewGuid().ToString(),b=Guid.NewGuid().ToString(),captureA=Guid.NewGuid().ToString(),captureB=Guid.NewGuid().ToString();
using var first=await Connect(port,a,captureA,"machine-A");using var second=await Connect(port,b,captureB,"machine-B");
await Until(()=>collector.Sessions.Length==4,"Two clients did not produce four isolated channels");
Session Find(string client,string capture,string channel)=>collector.Sessions.Single(s=>s.ClientId==client&&s.CaptureId==capture&&s.Channel==channel);
await Send(first,Frame(1,1,1,1200000000,Native(7000000)),true);
await Send(first,Frame(2,2,0,1200100000,Json(new WireEvent{kind="metric",time=1000,name="Atom内存",value=1024})),true);
await Send(second,Frame(2,1,0,8000000,Json(new WireEvent{kind="metric",time=6,name="Atom内存",value=2048})),true);
await Until(()=>Find(a,captureA,"native").Total==1&&Find(a,captureA,"sdk").Total==1&&Find(b,captureB,"sdk").Total==1,"Fragmented frames not accepted");
var native=Find(a,captureA,"native").Snapshot().Single();var sdk=Find(a,captureA,"sdk").Snapshot().Single();
Check(native.time==7&&native.observedTime==1200&&sdk.time==1000&&sdk.observedTime==1200.1,"Independent clocks were incorrectly merged");
Check(native.seq==1&&sdk.seq==1&&native.epoch==1&&native.channel=="native"&&sdk.channel=="sdk","Per-channel sequence/epoch attribution failed");
Check(Find(b,captureB,"sdk").Snapshot().Single().value==2048&&sdk.value==1024,"Same PID on different machines cross-contaminated values");
await Send(first,Frame(4,3,0,1200200000,Json(new{count=9,channel=2,reason="queue overflow"})),true);
await Until(()=>Find(a,captureA,"sdk").Dropped==9,"Gap was not attributed to SDK channel");
Check(Find(a,captureA,"native").Dropped==0,"SDK gap polluted native channel");
string captureNew=Guid.NewGuid().ToString();using var replacement=await Connect(port,a,captureNew,"machine-A");
await Until(()=>collector.Sessions.Length==6&&!Find(a,captureA,"sdk").Connected,"New capture did not isolate/close prior connection");
await Send(replacement,Frame(2,1,0,1000000,Json(new WireEvent{kind="metric",time=.5,name="New capture",value=3})));
await Until(()=>Find(a,captureNew,"sdk").Total==1,"Reconnect did not reset transport sequence");
Check(Find(a,captureA,"sdk").Total==2&&Find(b,captureB,"sdk").Connected,"Reconnect modified unrelated session");

async Task Bad(byte[] frame)
{
    string id=Guid.NewGuid().ToString(),capture=Guid.NewGuid().ToString();using var peer=await Connect(port,id,capture,"bad-fixture");
    await Until(()=>collector.Sessions.Any(s=>s.ClientId==id),"Malformed fixture greeting missing");
    await Send(peer,frame);await Closed(peer);
    await Until(()=>collector.Sessions.Where(s=>s.ClientId==id).All(s=>!s.Connected),"Malformed bridge channels not disconnected");
    Check(Find(b,captureB,"sdk").Connected,"Malformed bridge disconnected healthy client");
}
var oversized=Frame(2,1,0,0,[]);BinaryPrimitives.WriteInt32BigEndian(oversized,8*1024*1024+1);await Bad(oversized);
await Bad(Frame(2,0,0,0,Json(new WireEvent{kind="log"})));
await Bad(Frame(2,1,0,0,[0xff,0xff]));
await Bad(Frame(3,1,0,0,Json(new{channel=42,connected=true,status="must reject"})));
await Bad(Frame(4,1,0,0,Encoding.UTF8.GetBytes("{\"channel\":2,\"count\":9999999999999999999999999999,\"reason\":\"overflow\"}")));
// Valid framing with undecodable native payload produces an explicit channel gap and continues.
await Send(second,Frame(1,2,2,9000000,[1,2,3]));
await Send(second,Frame(1,3,2,9000001,Native(8000000)),true);
await Until(()=>Find(b,captureB,"native").Total==2,"Bad native payload desynchronized CSB3 framing");
Check(Find(b,captureB,"native").Dropped==1,"Native decode failure not reported as gap");
await Send(second,Frame(1,4,1,9000002,Native(8000001)));await Closed(second);
await Until(()=>!Find(b,captureB,"native").Connected,"Native epoch regression silently accepted");
await Send(replacement,Frame(1,2,1,10000000,Listener(1000000,true)));
await Until(()=>Find(a,captureNew,"native").Snapshot().Any(e=>e.entity=="distance-listener"),"Complete listener did not derive a distance point");
await Send(replacement,Frame(1,3,1,10000001,[1,2,3]));
await Send(replacement,Frame(1,4,1,10000002,Listener(2000000,false)));
await Until(()=>Find(a,captureNew,"native").Snapshot().Any(e=>e.entity=="listener"&&e.time==2),"Post-gap listener not processed");
Check(!Find(a,captureNew,"native").Snapshot().Any(e=>e.entity=="distance-listener"&&e.time==2),"Bad native frame retained old focus cache and fabricated a derived point");
Check(Find(a,captureNew,"native").Snapshot().All(e=>e.epoch==1),"Decode recovery changed native identity epoch");
await Send(replacement,Frame(1,5,1,10000003,Listener(3000000,true)));
await Send(replacement,Frame(4,6,1,10000004,Json(new{count=1,channel=1,reason="native queue gap"})));
await Send(replacement,Frame(1,7,1,10000005,Listener(4000000,false)));
await Until(()=>Find(a,captureNew,"native").Snapshot().Any(e=>e.entity=="listener"&&e.time==4),"Explicit-gap recovery not processed");
Check(!Find(a,captureNew,"native").Snapshot().Any(e=>e.entity=="distance-listener"&&e.time==4),"Explicit native gap retained cached focus");
// Stop while one socket has only half a greeting and another is blocked mid-frame.
using var halfGreeting=new TcpClient();await halfGreeting.ConnectAsync(IPAddress.Loopback,port);await Send(halfGreeting,"CS"u8.ToArray());
byte[] unfinished=Frame(2,8,0,10000006,[]);BinaryPrimitives.WriteInt32BigEndian(unfinished,1024);await Send(replacement,unfinished);
var watch=System.Diagnostics.Stopwatch.StartNew();collector.Dispose();watch.Stop();
Check(watch.Elapsed<TimeSpan.FromSeconds(1),"Collector.Dispose blocked on bridge reads");
await Closed(halfGreeting);await Closed(replacement);
await Until(()=>collector.Sessions.All(s=>!s.Connected),"Cancelled bridge sessions remain connected");
Console.WriteLine("PASS CSB3 TCP fragmentation, client/machine isolation, capture reconnect, independent clocks and sequences, explicit gaps, malformed-frame disconnect isolation, native recovery and epoch regression");
Console.WriteLine("PASS decode/transport gaps invalidate mapper cache without epoch changes; partial greeting/frame disposal closes promptly");
