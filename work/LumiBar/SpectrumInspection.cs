using Microsoft.UI.Dispatching;
using System.Diagnostics;

namespace LumiBar;

internal sealed partial class SpectrumService
{
    internal Action<float[]> BeginInspection()
    {
        if(!App.FrontendBackend||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR"))||!Environment.GetCommandLineArgs().Contains("--inspect-spectrum-ipc"))throw new InvalidOperationException("Spectrum inspection requires isolated preview mode.");
        var token=_state.Begin();return levels=>_state.Publish(token,levels,Environment.TickCount64);
    }
}

public partial class App
{
    private void InspectSpectrumIpc(DispatcherQueue dispatcher)
    {
        var publish=Spectrum.BeginInspection();
        Prefs.Spectrum=true;Track=new("Spectrum inspection","LumiBar","Synthetic samples; no audio output","inspection",true,0,60,null,Windows.UI.Color.FromArgb(255,255,50,160),"spectrum-inspection");
        _frontendApi!.Notify("media");
        _=Task.Run(async()=>{
            var analyzer=new SpectrumAnalyzer();long sample=0;using var cadence=new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while(!Exiting&&await cadence.WaitForNextTickAsync()){
                for(var i=0;i<960;i++,sample++){
                    var amplitude=.15+.1*Math.Sin(sample/5000d);var frame=analyzer.Add((float)(amplitude*Math.Sin(2*Math.PI*(400+200*Math.Sin(sample/7000d))*sample/48000)),48000);
                    if(frame!=null)publish(frame);
                }
            }
        });
        File.WriteAllText(Path.Combine(Preferences.DataPath,"inspection-ready.json"),"{\"synthetic\":true,\"audioOutput\":false,\"uiBlockMs\":450}");
        _=Task.Run(async()=>{for(var i=0;i<5&&!Exiting;i++){await Task.Delay(2500);dispatcher.TryEnqueue(()=>{Trace("spectrum-inspection","UI blocked for 450ms");Thread.Sleep(450);});}});
    }
}
