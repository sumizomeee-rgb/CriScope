using CriScope.Core;
using System.IO.Compression;
using System.Text.Json;

var directory=Path.Combine(Path.GetTempPath(),"CriScope-bundles-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try {
    var hello=new WireEvent {kind="hello",value=1,session=Guid.NewGuid().ToString(),name="Test",clientId=Guid.NewGuid().ToString(),captureId=Guid.NewGuid().ToString(),channel="native"};
    using(var state=new Session(hello)) {
        state.Accept(E(1,1,"position","source","s"));
        state.Accept(E(2,2,"remove","source","s"));
        state.Accept(E(3,130,"metric","",""));
        Check(state.ViewSnapshot().Any(e=>e.kind=="remove") && !state.ViewSnapshot().Any(e=>e.kind=="position"),"销毁墓碑跨窗口保留，不复活旧音源");
        state.StartRecording(directory);
        var baseline=state.EvidenceSnapshot().Events;
        Check(baseline.Any(e=>e.kind=="remove" && e.baseline),"开始记录保存显式状态基线");
        state.Accept(E(4,131,"gap","",""));
        Check(!state.Baseline().Any(),"缺失后旧状态失效");
        state.StopRecording();
        state.Accept(E(5,132,"request","cue","c"));
        var finished=E(6,133,"log","cue","c"); finished.detail="CRI 播放实例释放"; state.Accept(finished);
        Check(!state.Baseline().Any(e=>e.kind=="request"),"已释放播放实例不混入记录起点基线");
    }
    using var collector=new Collector(directory);
    var input=Path.Combine(directory,"fixture.criscope");
    File.WriteAllLines(input,new[]{hello.ToJson(),E(1,1,"position","source","s").ToJson(),E(2,2,"remove","source","s").ToJson(),E(3,4,"metric","","",12).ToJson()});
    var session=collector.LoadRecording(input);
    var zip=ProblemBundle.Export(collector,session,directory,3,5,[1,2,3],"复现说明");
    using var archive=ZipFile.OpenRead(zip);
    var log=archive.Entries.Single(e=>e.Name.EndsWith(".criscope"));
    using(var reader=new StreamReader(log.Open())) {
        var lines=reader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(WireEvent.Parse).ToArray();
        Check(lines.Any(e=>e.kind=="remove" && e.baseline) && !lines.Any(e=>e.kind=="position"),"区间起点重建尊重销毁");
        Check(lines.Single(e=>e.kind=="metric").time==4,"区间事件保留原始时间");
    }
    using(var reader=new StreamReader(archive.GetEntry("manifest.json")!.Open())) {
        using var manifest=JsonDocument.Parse(reader.ReadToEnd());
        Check(manifest.RootElement.GetProperty("description").GetString()=="复现说明","问题说明写入包");
    }
    Check(archive.GetEntry("screenshot.png")!=null,"软件截图随包导出");
    using(var live=new Session(hello)) {
        var registered=(System.Collections.Concurrent.ConcurrentDictionary<string,Session>)typeof(Collector)
            .GetField("sessions",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(collector)!;
        registered["bundle-live-fixture"]=live;
        live.Accept(E(1,1,"play","voice","long-bgm"));live.StartRecording(directory);
        live.Accept(E(2,60,"metric","","",5));live.StopRecording();
        live.Accept(E(3,90,"metric","","",42));live.Accept(E(4,100,"log","",""));
        var merged=live.EvidenceSnapshot();
        Check(merged.Events.Any(e=>e.seq==3&&e.value==42)&&merged.Events.GroupBy(e=>e.seq).All(g=>g.Count()==1),"停止录制后合并最新窗口，按序号去重");
        Check(!merged.HasUnrecordedGap,"完整未录尾部不误报缺失");
        var recent=ProblemBundle.Export(collector,live,directory,80,101,[1,2,3]);
        using(var pack=ZipFile.OpenRead(recent))
        using(var reader=new StreamReader(pack.Entries.Single(e=>e.Name.EndsWith(".criscope")).Open())) {
            var exported=reader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(WireEvent.Parse).ToArray();
            Check(exported.Any(e=>e.seq==3&&e.value==42&&!e.baseline),"90–100秒区间包含停止录制后的真实新数据");
        }
        // The unrecorded stop and following data disappear from the bounded window.
        live.Accept(E(5,110,"stop","voice","long-bgm"));live.Accept(E(6,300,"log","",""));
        merged=live.EvidenceSnapshot();
        Check(merged.HasUnrecordedGap&&merged.RecordedThroughSequence==2&&merged.ContextAfterSequence==6,"中段逐出显式标记序号缺口");
        var gapPack=ProblemBundle.Export(collector,live,directory,250,301,[1,2,3]);
        using(var pack=ZipFile.OpenRead(gapPack)) {
            using(var reader=new StreamReader(pack.Entries.Single(e=>e.Name.EndsWith(".criscope")).Open())) {
                var exported=reader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(WireEvent.Parse).ToArray();
                Check(!exported.Any(e=>e.kind=="play"),"缺口前旧BGM不会虚构成缺口后的活动基线");
                Check(!exported.Any(e=>e.kind=="gap"),"导出不伪造原生缺失事件");
            }
            using(var reader=new StreamReader(pack.GetEntry("manifest.json")!.Open())) {
                using var manifest=JsonDocument.Parse(reader.ReadToEnd());
                var source=manifest.RootElement.GetProperty("sources")[0];
                Check(source.GetProperty("HasUnrecordedGap").GetBoolean()&&source.GetProperty("knownGap").GetBoolean(),"缺口明确进入问题包元数据");
            }
        }
        registered.TryRemove("bundle-live-fixture",out _);
    }
    Console.WriteLine("问题包与状态基线：15项通过");
    WireEvent E(long seq,double time,string kind,string entity,string id,double value=0)=>new(){session=hello.session,seq=seq,time=time,kind=kind,entity=entity,objectId=id,value=value};
} finally {Directory.Delete(directory,true);}
static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("通过："+message);}
