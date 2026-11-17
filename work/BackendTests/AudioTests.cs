using LumiBar;

internal static class AudioTests
{
    internal static List<string> Run()
    {
        var result=new List<string>();void Check(bool value,string name)=>result.Add($"{(value?"PASS":"FAIL")} {name}");
        Check(AudioSamples.Read([0,0,128],24,false)==-1&&AudioSamples.Read([255,255,127],24,false)>.999f,"24-bit PCM sign extension handles full range");
        Check(AudioSamples.Read([0,128],16,false)==-1&&AudioSamples.Read([0,0],16,false)==0,"16-bit PCM silence and negative full scale decode correctly");
        Check(AudioSamples.Read([128],8,false)==0&&AudioSamples.Read([0],8,false)==-1,"Unsigned 8-bit PCM centers on 128");
        Check(AudioSamples.Read(BitConverter.GetBytes(float.NaN),32,true)==0&&AudioSamples.Read(BitConverter.GetBytes(double.PositiveInfinity),64,true)==0,"Non-finite audio input becomes silence");
        var tone=new SpectrumAnalyzer();float[]? levels=null;for(var i=0;i<2048;i++)levels=tone.Add((float)Math.Sin(2*Math.PI*1000*i/48000),48000);
        Check(levels!=null&&Array.IndexOf(levels,levels.Max())==10,"1 kHz tone peaks in the expected logarithmic frequency band");
        Check(levels!.All(x=>float.IsFinite(x)&&x>=0&&x<=1),"FFT returns twenty finite normalized magnitudes");
        foreach(var rate in new[]{48000,44100,16000}){
            var analyzer=new SpectrumAnalyzer();var frames=0;var valid=true;
            for(var i=0;i<rate;i++){var frame=analyzer.Add((float)Math.Sin(2*Math.PI*1000*i/rate),rate);if(frame!=null){frames++;valid&=frame.Length==20&&frame.All(x=>float.IsFinite(x)&&x>=0&&x<=1);}}
            Check(frames>=30&&valid,$"Overlapping FFT delivers 30+ fresh valid frames at {rate} Hz including warmup");
        }
        var silent=new SpectrumAnalyzer();for(var i=0;i<2048;i++)levels=silent.Add(0,48000);Check(levels!.All(x=>x==0),"Silent input does not fabricate spectrum activity");
        var half=new SpectrumAnalyzer();for(var i=0;i<1000;i++)half.Add(1,48000);var replacement=new SpectrumAnalyzer();for(var i=0;i<1048;i++)levels=replacement.Add(0,48000);Check(levels==null,"Replacement capture does not inherit a partial FFT frame");
        var state=new AudioCaptureState();var old=state.Begin();state.Publish(old,Enumerable.Repeat(.5f,20).ToArray(),1000);var current=state.Begin();state.Publish(current,Enumerable.Repeat(.8f,20).ToArray(),1100);
        Check(!state.Publish(old,new float[20],1200)&&state.Read(1200)[0]==.8f,"Old capture callback cannot overwrite new device spectrum");
        Check(!state.End(old,"old stopped")&&state.Read(1200)[0]==.8f,"Late stopped event cannot clear new capture");
        var copy=state.Read(1200);copy[0]=0;Check(state.Read(1200)[0]==.8f,"Clients cannot mutate shared audio state");
        Check(state.Read(1500).All(x=>x==0),"Stale capture fades to zero after 300 ms without data");
        Check(state.End(current,"stopped")&&state.Read(1200).All(x=>x==0),"Stopping active capture clears its spectrum");
        var prefs=new Preferences{FontFamily="https://example.invalid/font.ttf"};prefs.Normalize();Check(prefs.FontFamily==null,"Custom font setting cannot become a remote font URI");
        return result;
    }
}
