using System.Text.Json;
using LumiBar;

internal static class ExtensionTests
{
    internal static async Task<List<string>> Run()
    {
        var results=new List<string>();void Check(bool value,string name)=>results.Add($"{(value?"PASS":"FAIL")} {name}");
        using var solar=JsonDocument.Parse("""{"sunrise":["2026-11-14T06:42"],"sunset":["2026-11-14T16:57"],"is_day":0}""");
        Check(WeatherClock.DailyTime(solar.RootElement,"sunrise")==new DateTime(2026,11,14,6,42,0),"Sunrise retains city wall-clock time");
        Check(WeatherClock.DailyTime(solar.RootElement,"sunset")?.Kind==DateTimeKind.Unspecified,"Solar time is not silently converted to machine timezone");
        Check(WeatherClock.IsDay(solar.RootElement)==false,"Provider night flag overrides fixed 06-18 assumption");
        using var polar=JsonDocument.Parse("""{"sunrise":[null],"sunset":[],"is_day":1}""");
        Check(WeatherClock.DailyTime(polar.RootElement,"sunrise")==null&&WeatherClock.DailyTime(polar.RootElement,"sunset")==null&&WeatherClock.IsDay(polar.RootElement)==true,"Polar day works without invented sunrise or sunset");
        var feed=new ChangeFeed();var wait=feed.Wait(feed.Version,1000,CancellationToken.None);feed.Publish("media");
        var changed=await wait;Check(changed.Version==1&&changed.Topics.SequenceEqual(["media"])&&!changed.TimedOut,"Pending event wait wakes on media change");
        feed.Publish("weather");feed.Publish("media");var accumulated=await feed.Wait(0,1000,CancellationToken.None);
        Check(accumulated.Topics.Length==2&&!accumulated.ResyncRequired,"Events coalesce topics without losing revisions");
        var idle=await feed.Wait(feed.Version,50,CancellationToken.None);Check(idle.TimedOut&&idle.Topics.Length==0,"Idle long poll returns a heartbeat");
        var restarted=await feed.Wait(999,50,CancellationToken.None);Check(restarted.ResyncRequired,"Client revision from old backend requests resync");
        using(var cancel=new CancellationTokenSource()){var pending=feed.Wait(feed.Version,1000,cancel.Token);cancel.Cancel();try{await pending;Check(false,"Cancelled event wait releases caller");}catch(OperationCanceledException){Check(true,"Cancelled event wait releases caller");}}
        for(var i=0;i<70;i++)feed.Publish("windows");Check((await feed.Wait(0,50,CancellationToken.None)).ResyncRequired,"Event history overflow requests a full refresh");
        var custom=new PlacementPolicy().Calculate(0,240,160,300,8,1,64,80);Check(custom.Lite&&custom.MusicWidth==64&&custom.WeatherWidth==80,"Frontend minimum DIP widths control Lite layout");
        var bar=new ScreenRect(0,1000,1000,1080);var full=new PlacementPolicy().Calculate(200,900,104,238,8,1);var slots=SlotPlanner.Calculate(bar,900,8,34,full,true);
        Check(slots.MusicSlot?.Rect?.Right+8==slots.WeatherSlot?.Rect?.Left&&slots.WeatherSlot?.Rect?.Right==892,"Backend assigns ordered capsule slots with safe gaps");
        Check(slots.MusicSlot?.Rect?.Top==1023&&slots.MusicSlot.Rect.Value.Bottom==1057,"Slots vertically center within taskbar");
        var blocked=SlotPlanner.Calculate(bar,900,8,34,new PlacementPolicy().Calculate(850,900,104,238,8,1),true);
        Check(blocked.State=="reservation-required"&&blocked.MusicSlot==null&&blocked.WeatherSlot==null,"Unavailable reserved space never creates overlapping slot coordinates");
        var hidden=SlotPlanner.Calculate(bar,900,8,34,full,false);Check(hidden.State=="hidden"&&hidden.MusicSlot?.Mode=="hidden","Hidden taskbar yields hidden slots");
        var reservationPolicy=new ReservationLayoutPolicy();var nativeBar=new ScreenRect(0,1000,2000,1080);
        var roomy=reservationPolicy.Calculate(nativeBar,500,1700,null,1,104,238,8,true);
        Check(!roomy.Capsules.Lite&&roomy.ReserveDip==366&&!roomy.Elevated,"Verified blank space keeps full capsules in the taskbar during native acknowledgement");
        var room=new ScreenRect(1334,1000,1700,1080);
        var approaching=reservationPolicy.Calculate(nativeBar,1290,1334,room,1,104,238,8,true);
        Check(approaching.Capsules.Lite&&approaching.Capsules.MusicWidth==48&&approaching.Capsules.WeatherWidth==64&&approaching.ReserveDip==136,"Both capsules enter Lite one icon step before native pressure");
        var packed=reservationPolicy.Calculate(nativeBar,1560,1564,new(1564,1000,1700,1080),1,104,238,8,true);
        Check(packed.Capsules.Lite&&!packed.Elevated&&packed.ReserveDip==136,"System overflow keeps both Lite capsules in acknowledged native space");
        var recoveryEdge=reservationPolicy.Calculate(nativeBar,1290,1564,new(1564,1000,1700,1080),1,104,238,8,true);
        Check(recoveryEdge.Capsules.Lite,"Recovery hysteresis prevents full/Lite oscillation near pressure boundary");
        var recovered=reservationPolicy.Calculate(nativeBar,1000,1564,new(1564,1000,1700,1080),1,104,238,8,true);
        Check(!recovered.Capsules.Lite&&!recovered.Elevated,"Recovered measured blank space requests larger reservation without lifting the capsule windows");
        var offline=reservationPolicy.Calculate(nativeBar,1650,1700,null,1,104,238,8,false);
        Check(offline.Elevated&&offline.Capsules.MusicWidth>0&&offline.Capsules.WeatherWidth>0,"Unavailable bridge retains both capsules without drawing over taskbar apps");
        return results;
    }
}
