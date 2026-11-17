using System.Diagnostics;
using System.Text.Json;
using AntigravityFrontend.Sdk;
using Microsoft.UI.Dispatching;

namespace AntigravityFrontend.Services;

internal static class SpectrumSelfTest
{
    internal static async void Start(App app,string endpoint,string output)
    {
        var frames=new List<double>();var values=new HashSet<string>();var valid=true;var clock=new Stopwatch();
        var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Directory.CreateDirectory(output);int code=1;
        try{
            Environment.SetEnvironmentVariable("LUMIBAR_ENDPOINT_FILE",endpoint);
            using var bridge=new LumiBarBridgeService(DispatcherQueue.GetForCurrentThread(),new PluginConfig{EndpointPath=endpoint,ConfigVersion=6,UseMockMode=false,EnableTaskbarTakeover=false});
            bridge.SpectrumUpdated+=()=>{
                if(!clock.IsRunning){if(bridge.CurrentSpectrum.Playing&&bridge.CurrentSpectrum.Bands.Any(x=>x>.001))ready.TrySetResult(true);return;}
                if(clock.Elapsed.TotalSeconds<1)return;
                var bands=bridge.CurrentSpectrum.Bands;
                valid&=bands.Length==20&&bands.All(x=>double.IsFinite(x)&&x>=0&&x<=1);
                frames.Add(clock.Elapsed.TotalMilliseconds);values.Add(string.Join(',',bands.Select(x=>Math.Round(x,5))));
            };
            bridge.Start();await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));clock.Start();await Task.Delay(7500);
            var gaps=frames.Zip(frames.Skip(1),(a,b)=>b-a).Order().ToArray();
            var hz=frames.Count>1?(frames.Count-1)*1000/(frames[^1]-frames[0]):0;
            var p95=gaps.Length>0?gaps[(int)Math.Floor((gaps.Length-1)*.95)]:double.PositiveInfinity;
            var freshHz=frames.Count>1?values.Count*1000/(frames[^1]-frames[0]):0;
            var pass=valid&&hz>=30&&freshHz>=30&&p95<60;
            File.WriteAllText(Path.Combine(output,"spectrum-result.json"),JsonSerializer.Serialize(new{pass,hz,freshHz,measurementSeconds=frames.Count>1?(frames[^1]-frames[0])/1000:0,frames=frames.Count,distinctFrames=values.Count,p95IntervalMs=p95,maxIntervalMs=gaps.LastOrDefault(),bands=20,synthetic=true,audioOutput=false,valid},new JsonSerializerOptions{WriteIndented=true}));
            code=pass?0:1;
        }catch(Exception ex){File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());}
        finally{app.Exit();Environment.Exit(code);}
    }
}
