namespace LumiBar;
// Opt-in counters for the glass-density reproduction. No media or location data.
internal static class GlassDiagnostics
{
    internal static readonly bool Enabled=Environment.GetEnvironmentVariable("LUMIBAR_TRACE_GLASS")=="1";
    private static long _last;
    private static int _refresh,_source,_configuration,_mask,_size,_preview,_raster,_completed,_sliders;
    private static int _connect,_disconnect,_theme,_chrome,_erase;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,int> _events=new();
    internal static void Event(string kind,string name){if(!Enabled)return;_events.AddOrUpdate(kind+":"+name,1,(_,n)=>n+1);}
    internal static void Connected(string name){if(Enabled){Interlocked.Increment(ref _connect);Event("connect",name);}}
    internal static void Disconnected(string name){if(Enabled){Interlocked.Increment(ref _disconnect);Event("disconnect",name);}}
    internal static void Theme(string name){if(Enabled){Interlocked.Increment(ref _theme);Event("theme",name);}}
    internal static void Chrome(){if(Enabled)Interlocked.Increment(ref _chrome);}
    internal static void Erase(){if(Enabled)Interlocked.Increment(ref _erase);}
    internal static void Refresh(){if(Enabled)Interlocked.Increment(ref _refresh);}
    internal static void Source(){if(Enabled)Interlocked.Increment(ref _source);}
    internal static void Configuration(){if(Enabled)Interlocked.Increment(ref _configuration);}
    internal static void Mask(){if(Enabled)Interlocked.Increment(ref _mask);}
    internal static void Size(){if(Enabled)Interlocked.Increment(ref _size);}
    internal static void Preview(){if(Enabled)Interlocked.Increment(ref _preview);}
    internal static void Raster(){if(Enabled)Interlocked.Increment(ref _raster);}
    internal static void Completed(){if(Enabled)Interlocked.Increment(ref _completed);}
    internal static void Slider(string title,double oldValue,double newValue){if(Enabled&&Interlocked.Increment(ref _sliders)<=12)App.Trace("glass-slider",$"{title}: {oldValue:F4}->{newValue:F4}");}
    internal static void Report()
    {
        if(!Enabled||Environment.TickCount64-_last<1000)return;_last=Environment.TickCount64;
        App.Trace("glass-metrics",$"refresh={Interlocked.Exchange(ref _refresh,0)} source={Interlocked.Exchange(ref _source,0)} config={Interlocked.Exchange(ref _configuration,0)} mask={Interlocked.Exchange(ref _mask,0)} size={Interlocked.Exchange(ref _size,0)} preview={Interlocked.Exchange(ref _preview,0)} raster={Interlocked.Exchange(ref _raster,0)} completed={Interlocked.Exchange(ref _completed,0)} sliders={Interlocked.Exchange(ref _sliders,0)} density={App.Instance.Prefs.GlassOpacity:F4}");
        var events=string.Join("; ",_events.OrderBy(x=>x.Key).Select(x=>$"{x.Key}={x.Value}"));_events.Clear();
        App.Trace("glass-lifecycle",$"connect={Interlocked.Exchange(ref _connect,0)} disconnect={Interlocked.Exchange(ref _disconnect,0)} theme={Interlocked.Exchange(ref _theme,0)} chrome={Interlocked.Exchange(ref _chrome,0)} erase={Interlocked.Exchange(ref _erase,0)}; {events}");
    }
}
