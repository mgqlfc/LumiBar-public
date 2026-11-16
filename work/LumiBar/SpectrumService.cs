using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LumiBar;
internal sealed class SpectrumService : IDisposable
{
    private sealed class CaptureSession : IDisposable
    {
        internal readonly MMDevice Device;internal readonly string DeviceId;internal readonly WasapiLoopbackCapture Capture;
        internal readonly SpectrumAnalyzer Analyzer=new();internal readonly WaveFormat Format;internal readonly bool Floating;internal readonly long Token;
        private readonly EventHandler<WaveInEventArgs> _data;private readonly EventHandler<StoppedEventArgs> _stopped;private int _disposed;
        internal CaptureSession(SpectrumService owner,MMDevice device,WasapiLoopbackCapture capture,long token,bool floating)
        {
            Device=device;DeviceId=device.ID;Capture=capture;Format=capture.WaveFormat;Token=token;Floating=floating;
            _data=(_,args)=>owner.OnData(this,args);_stopped=(_,args)=>owner.OnStopped(this,args);
            Capture.DataAvailable+=_data;Capture.RecordingStopped+=_stopped;
        }
        public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)!=0)return;Capture.DataAvailable-=_data;Capture.RecordingStopped-=_stopped;try{try{Capture.StopRecording();}catch{}Capture.Dispose();}finally{Device.Dispose();}}
    }
    private readonly object _lifecycle=new();private readonly AudioCaptureState _state=new();private CaptureSession? _current;private bool _disposed;
    public string Status=>_state.Status;
    public void EnsureDevice()
    {
        lock(_lifecycle)
        {
            if(_disposed)return;MMDevice? device=null;WasapiLoopbackCapture? capture=null;
            try
            {
                using var enumerator=new MMDeviceEnumerator();device=enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
                if(Volatile.Read(ref _current)?.DeviceId==device.ID){device.Dispose();return;}
                StopCore("正在切换音频设备");capture=new WasapiLoopbackCapture(device);var format=capture.WaveFormat;
                var floating=format.Encoding==WaveFormatEncoding.IeeeFloat||format is WaveFormatExtensible ext&&ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71");
                var pcm=format.Encoding==WaveFormatEncoding.Pcm||format is WaveFormatExtensible pcmExt&&pcmExt.SubFormat==new Guid("00000001-0000-0010-8000-00aa00389b71");
                if((!floating&&!pcm)||!AudioSamples.Supported(format.BitsPerSample,floating)||format.Channels<1||format.BlockAlign<format.Channels*(format.BitsPerSample/8))throw new NotSupportedException("音频格式暂不支持");
                var session=new CaptureSession(this,device,capture,_state.Begin(),floating);device=null;capture=null;Volatile.Write(ref _current,session);session.Capture.StartRecording();
            }
            catch(Exception ex){capture?.Dispose();device?.Dispose();StopCore(ex is NotSupportedException?"音频格式暂不支持":"音频设备暂不可用");App.Log("audio",ex);}
        }
    }
    private void OnData(CaptureSession session,WaveInEventArgs e)
    {
        if(!ReferenceEquals(Volatile.Read(ref _current),session))return;
        var format=session.Format;var bytes=format.BitsPerSample/8;float[]? latest=null;
        for(var offset=0;offset+format.BlockAlign<=e.BytesRecorded;offset+=format.BlockAlign){
            float sum=0;for(var ch=0;ch<format.Channels;ch++)sum+=AudioSamples.Read(e.Buffer.AsSpan(offset+ch*bytes,bytes),format.BitsPerSample,session.Floating);
            var levels=session.Analyzer.Add(sum/format.Channels,format.SampleRate);if(levels!=null)latest=levels;
        }
        if(latest!=null)_state.Publish(session.Token,latest,Environment.TickCount64);
    }
    private void OnStopped(CaptureSession session,StoppedEventArgs e)
    {
        if(ReferenceEquals(Interlocked.CompareExchange(ref _current,null,session),session))_state.End(session.Token,e.Exception==null?"音频已停止":"音频设备已断开");
        // NAudio can raise RecordingStopped from its capture thread. Release it elsewhere.
        _=Task.Run(session.Dispose);
    }
    public float[] Levels()=>_state.Read(Environment.TickCount64);
    private void StopCore(string status){var session=Interlocked.Exchange(ref _current,null);_state.Reset(status);session?.Dispose();}
    public void Stop(){lock(_lifecycle)StopCore("音频已停止");}
    public void Dispose(){lock(_lifecycle){_disposed=true;StopCore("音频已停止");}}
}