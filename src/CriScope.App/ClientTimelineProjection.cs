using System.Text.Json;
using CriScope.Core;
namespace CriScope.App;

// View-only projection: recordings retain both channels and their original clocks.
public static class ClientTimelineProjection
{
    public static WireEvent[] Combine(WireEvent[] native, WireEvent[] sdk)
    {
        var additions=sdk.Where(e=>e.kind is "beat" or "sequence" or "block" or "cue-info" || CueCatalogIndex.IsMetadata(e) || e.kind=="gap").ToArray();
        if(additions.Length==0)return native;
        var anchors=native.Where(e=>e.observedTime>0&&!e.baseline&&e.time>0&&e.kind!="gap"&&!e.detail.Contains("起点未知",StringComparison.Ordinal))
            .GroupBy(e=>(long)(e.observedTime*2)).Select(g=>g.First()).OrderBy(e=>e.observedTime).ToArray();
        // Static directories can use a hot-join observation as a clock anchor;
        // callbacks still require a real native event and a playback lifetime.
        var metadataAnchors=anchors.Length>0?anchors:native.Where(e=>e.observedTime>0&&e.time>0&&e.kind!="gap")
            .OrderBy(e=>e.observedTime).ToArray();
        if(metadataAnchors.Length==0)return native;
        var boundaries=native.Where(e=>e.kind=="gap"||e.entity=="capture-segment").ToArray();
        var requests=native.Where(e=>e.kind=="request"&&e.entity=="cue").Select(e=>(Event:e,Id:PlaybackId(e))).Where(p=>p.Id!=null).ToArray();
        var ends=native.Where(EventSemantics.IsPlaybackEnd).GroupBy(e=>e.objectId).ToDictionary(g=>g.Key,g=>g.Min(e=>e.time));
        var result=new List<WireEvent>(native);
        foreach(var e in additions)
        {
            var metadata=CueCatalogIndex.IsMetadata(e)||e.kind=="gap";
            var clock=e.observedTime>0?e.observedTime:e.time;
            var availableAnchors=metadata?metadataAnchors:anchors;
            if(availableAnchors.Length==0)continue;
            if(!metadata&&(clock<availableAnchors[0].observedTime-1||clock>availableAnchors[^1].observedTime+2))continue;
            int lo=0,hi=availableAnchors.Length-1;
            while(lo<hi){int mid=(lo+hi)/2;if(availableAnchors[mid].observedTime<clock)lo=mid+1;else hi=mid;}
            var anchor=availableAnchors[lo];if(lo>0&&Math.Abs(availableAnchors[lo-1].observedTime-clock)<Math.Abs(anchor.observedTime-clock))anchor=availableAnchors[lo-1];
            if(!metadata&&boundaries.Any(b=>b.observedTime>Math.Min(clock,anchor.observedTime)&&b.observedTime<=Math.Max(clock,anchor.observedTime)))continue;
            var projected=anchor.time+clock-anchor.observedTime;
            var id=e.objectId.StartsWith("playback:",StringComparison.Ordinal)?e.objectId[9..]:null;
            var owners=requests.Where(r=>r.Id==id&&r.Event.time<=projected+.05&&(!ends.TryGetValue(r.Event.objectId,out var end)||projected<=end+.05)).ToArray();
            var owner=owners.Length==1?owners[0].Event:null;
            if(e.kind=="cue-info" && owner?.name!=e.name)owner=null;
            var clone=e.Clone();
            clone.originalTime=e.time;clone.estimatedTime=true;clone.time=projected;clone.seq=-Math.Abs(e.seq);
            if(!metadata) {
                clone.parentId=owner?.objectId??"";clone.objectId=owner?.objectId??"sdk:"+e.objectId;
                clone.detail="SDK 约时 · "+(owner==null?"未关联播放实例 · ":"")+e.detail;
            }
            result.Add(clone);
        }
        return result.OrderBy(e=>e.time).ThenBy(e=>e.seq).ToArray();
    }
    public static string? PlaybackId(WireEvent e)
    {
        try { using var doc=JsonDocument.Parse(e.raw);if(doc.RootElement.TryGetProperty("parameters",out var fields))
            foreach(var field in fields.EnumerateArray())if(field.GetProperty("name").GetString()=="CriAtomExPlaybackId")return field.GetProperty("value").ToString(); }
        catch(JsonException){}catch(InvalidOperationException){}catch(KeyNotFoundException){}
        return null;
    }
}
