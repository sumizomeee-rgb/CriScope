using CriScope.Core;
using System.Text.Json;

static void Check(bool ok,string message) {if(!ok) throw new Exception(message);}
static NativeParameter V(string n,object v)=>new(0,n,v);
static NativePacket P(string f,ulong t,params NativeParameter[] v)=>new(31,8,0,t,0,f,0,v);
static NativeParameter H(string id)=>V("CriAtomEx3dListenerHn",id);
var m=new NativeEventMapper("spatial");
WireEvent[] Map(string f,ulong t,params NativeParameter[] v)=>m.Map(P(f,t,v)).ToArray();
WireEvent? Distance(WireEvent[] v)=>v.SingleOrDefault(e=>e.entity=="distance-listener"&&e.kind=="position");
Map("StartLogging",1);
Check(Distance(Map("Ex3dListener_Update",2,H("unknown"),V("3dPosVector_Position",new float[]{1,2,3})))==null,"Missing weight assumed zero");
var zero=Distance(Map("Ex3dListener_Update",3,H("zero"),V("3dPosVector_Position",new float[]{1,2,3}),V("3dDistanceFocusLevel",0f)));
Check(zero?.x==1&&zero.y==2&&zero.z==3,"Weight zero incorrectly requires Focus");
var one=Distance(Map("Ex3dListener_Update",4,H("one"),V("3dPosVector_FocusPoint",new float[]{4,5,6}),V("3dDistanceFocusLevel",1f)));
Check(one?.x==4&&one.y==5&&one.z==6,"Weight one incorrectly requires Listener position");
Check(Distance(Map("Ex3dListener_Update",5,H("half"),V("3dPosVector_Position",new float[]{0,2,4}),V("3dDistanceFocusLevel",.5f)))==null,"Missing Focus defaulted to zero");
Check(Distance(Map("Ex3dListener_SetFocusPoint",6,H("half"),V("3dPosVector_FocusPoint",new float[]{10,12,14})))==null,"Setter falsely treated as submitted listener update");
var half=Distance(Map("Ex3dListener_Update",7,H("half")));
Check(half?.x==5&&half.y==7&&half.z==9,"Distance interpolation incorrect");
using(var raw=JsonDocument.Parse(half!.raw))
    Check(raw.RootElement.GetProperty("derived").GetProperty("inputs").EnumerateArray().Any(x=>x.GetProperty("name").GetString()=="3dPosVector_FocusPoint"&&x.GetProperty("observedAtMicroseconds").GetUInt64()==6),"Input observation time lost");
Map("Ex3dListener_SetDistanceFocusLevel",8,H("half"),V("3dDistanceFocusLevel",.8f));
Map("Ex3dListener_SetDistanceFocusLevel",9,H("half"),V("3dDistanceFocusLevel",.2f));
var changed=Distance(Map("Ex3dListener_Update",10,H("half")));
Check(Math.Abs(changed!.x-2)<.00001,"Fast last-written weight lost through rate limiting");
var destroyed=Map("Ex3dListener_Destroy",11,H("half"));
Check(destroyed.Count(e=>e.kind=="remove")==2&&destroyed.All(e=>e.objectId=="half"),"Listener/derived destruction missing");
Check(Distance(Map("Ex3dListener_Update",12,H("half")))==null,"Destroyed listener retained state");
Map("StartLogging",13);
Check(Distance(Map("Ex3dListener_Update",14,H("zero")))==null,"New capture segment retained listener fields");

Map("Ex3dSource_SetPosition",15,V("CriAtomEx3dSourceHn","0x100"),V("3dPosVector_Position",new float[]{1,3,5}));
Map("ExPlaybackId",16,V("ExPlaybackId_unique64",1UL),V("cue_name","Cue A"));
var allocation=Map("SoundVoice_Allocate",17,V("CriAtomSoundVoiceId_unique64",11UL),V("ExPlaybackId_unique64",1UL),V("CriAtomEx3dSourceHn","0x100"));
Check(allocation.Single(e=>e.entity=="source").name=="Cue A","Source not named after actual playback Cue");
Map("ExPlaybackId",18,V("ExPlaybackId_unique64",2UL),V("cue_name","Cue B"));
var second=Map("SoundVoice_Allocate",19,V("CriAtomSoundVoiceId_unique64",12UL),V("ExPlaybackId_unique64",2UL),V("CriAtomEx3dSourceHn","0x100"));
Check(second.Single(e=>e.entity=="source").name.Contains("Cue A · Cue B"),"Multiple source playbacks collapsed");
using(var raw=JsonDocument.Parse(second.Single(e=>e.entity=="source").raw))
    Check(raw.RootElement.GetProperty("derived").GetProperty("positionObservedAtMicroseconds").GetUInt64()==15,"Association update forged position observation time");
var freed=Map("SoundVoice_FreeVoice",20,V("CriAtomSoundVoiceId_unique64",11UL),V("ExPlaybackId_unique64",1UL));
Check(freed.Single(e=>e.entity=="source").name=="Cue B","Freed Voice left stale source Cue association");
var last=Map("SoundVoice_FreeVoice",21,V("CriAtomSoundVoiceId_unique64",12UL),V("ExPlaybackId_unique64",2UL));
Check(last.Single(e=>e.entity=="source").name.Contains("未观测到播放关联"),"Unassigned source name pretends known Cue");
Check(Map("Ex3dSource_Destroy",22,V("CriAtomEx3dSourceHn","0x100")).Single().kind=="remove","Source destroy not observable");
Console.WriteLine("PASS distance endpoints, unknown fields, Update commit, timestamps, rapid writes, segment/destruction reset, multi-Cue source association");
