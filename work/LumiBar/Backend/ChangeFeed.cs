namespace LumiBar;

internal sealed record ChangeBatch(string InstanceId,long Version,string[] Topics,bool ResyncRequired,bool TimedOut);
internal sealed class ChangeFeed
{
    private readonly object _gate=new();
    private readonly Queue<(long version,string topic)> _history=new();
    private TaskCompletionSource _changed=NewSignal();
    private long _version;
    internal string InstanceId {get;}=Guid.NewGuid().ToString("N");
    internal long Version {get{lock(_gate)return _version;}}
    private static TaskCompletionSource NewSignal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void Publish(string topic)
    {
        TaskCompletionSource signal;
        lock(_gate){_history.Enqueue((++_version,topic));while(_history.Count>64)_history.Dequeue();signal=_changed;_changed=NewSignal();}
        signal.TrySetResult();
    }
    internal async Task<ChangeBatch> Wait(long afterVersion,int timeoutMs,CancellationToken ct)
    {
        Task signal;
        lock(_gate){if(afterVersion!=_version)return Batch(afterVersion,false);signal=_changed.Task;}
        try{await signal.WaitAsync(TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs,50,25000)),ct).ConfigureAwait(false);}
        catch(TimeoutException){lock(_gate)return Batch(afterVersion,_version==afterVersion);}
        lock(_gate)return Batch(afterVersion,false);
    }
    private ChangeBatch Batch(long after,bool timedOut)=>new(InstanceId,_version,_history.Where(x=>x.version>after).Select(x=>x.topic).Distinct().ToArray(),after>_version||(_history.Count>0&&after<_history.Peek().version-1),timedOut);
}
