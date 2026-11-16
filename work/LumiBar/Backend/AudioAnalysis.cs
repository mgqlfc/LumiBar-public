using System.Buffers.Binary;

namespace LumiBar;

internal static class AudioSamples
{
    internal static bool Supported(int bits,bool floating)=>floating?bits is 32 or 64:bits is 8 or 16 or 24 or 32;
    internal static float Read(ReadOnlySpan<byte> data,int bits,bool floating)
    {
        double sample;
        if(floating)sample=bits==32?BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data)):BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data));
        else sample=bits switch{
            8=>(data[0]-128)/128d,
            16=>BinaryPrimitives.ReadInt16LittleEndian(data)/32768d,
            24=>((data[0]|data[1]<<8|data[2]<<16)<<8>>8)/8388608d,
            32=>BinaryPrimitives.ReadInt32LittleEndian(data)/2147483648d,
            _=>0
        };
        return double.IsFinite(sample)?(float)Math.Clamp(sample,-1,1):0;
    }
}

/// <summary>One analyzer per capture. Partial frames never survive a device replacement.</summary>
internal sealed class SpectrumAnalyzer
{
    private const int Size=2048;
    private readonly double[] _real=new double[Size],_imag=new double[Size];
    private readonly float[] _samples=new float[Size];
    private readonly double[] _window=Enumerable.Range(0,Size).Select(i=>.5*(1-Math.Cos(2*Math.PI*i/(Size-1)))).ToArray();
    private int _count;
    internal float[]? Add(float sample,int sampleRate)
    {
        _samples[_count++]=float.IsFinite(sample)?sample:0;
        if(_count<Size)return null;_count=0;
        for(var i=0;i<Size;i++){_real[i]=_samples[i]*_window[i];_imag[i]=0;}
        for(int i=1,j=0;i<Size;i++){
            var bit=Size>>1;for(; (j&bit)!=0;bit>>=1)j^=bit;j^=bit;
            if(i<j){(_real[i],_real[j])=(_real[j],_real[i]);(_imag[i],_imag[j])=(_imag[j],_imag[i]);}
        }
        for(var length=2;length<=Size;length<<=1){
            var angle=-2*Math.PI/length;var baseR=Math.Cos(angle);var baseI=Math.Sin(angle);
            for(var start=0;start<Size;start+=length){
                double wr=1,wi=0;
                for(var j=0;j<length/2;j++){
                    var a=start+j;var b=a+length/2;var vr=_real[b]*wr-_imag[b]*wi;var vi=_real[b]*wi+_imag[b]*wr;
                    _real[b]=_real[a]-vr;_imag[b]=_imag[a]-vi;_real[a]+=vr;_imag[a]+=vi;
                    (wr,wi)=(wr*baseR-wi*baseI,wr*baseI+wi*baseR);
                }
            }
        }
        var levels=new float[20];if(sampleRate<=0)return levels;
        for(var band=0;band<20;band++){
            var low=45*Math.Pow(16000d/45,band/20d);var high=45*Math.Pow(16000d/45,(band+1)/20d);if(low>=sampleRate/2d)continue;
            var start=Math.Clamp((int)Math.Ceiling(low*Size/sampleRate),1,Size/2-1);var end=Math.Clamp((int)Math.Ceiling(high*Size/sampleRate),start+1,Size/2);
            double magnitude=0;for(var i=start;i<end;i++)magnitude=Math.Max(magnitude,Math.Sqrt(_real[i]*_real[i]+_imag[i]*_imag[i])/Size);
            levels[band]=(float)Math.Clamp((20*Math.Log10(magnitude+1e-9)+75)/65,0,1);
        }
        return levels;
    }
}

internal sealed class AudioCaptureState
{
    private readonly object _gate=new();private long _sequence,_active,_at;private float[] _levels=new float[20];private string _status="等待音频";
    internal string Status{get{lock(_gate)return _status;}}
    internal long Begin(){lock(_gate){_active=++_sequence;_levels=new float[20];_at=0;_status="系统输出音频";return _active;}}
    internal bool Publish(long session,float[] levels,long monotonicMs){lock(_gate){if(session==0||session!=_active||levels.Length!=20)return false;_levels=levels.Select(x=>float.IsFinite(x)?Math.Clamp(x,0,1):0).ToArray();_at=monotonicMs;return true;}}
    internal bool End(long session,string status){lock(_gate){if(session==0||session!=_active)return false;_active=0;_levels=new float[20];_at=0;_status=status;return true;}}
    internal void Reset(string status){lock(_gate){_active=0;_levels=new float[20];_at=0;_status=status;}}
    internal float[] Read(long monotonicMs){lock(_gate)return _active==0||monotonicMs-_at>300?new float[20]:(float[])_levels.Clone();}
}
