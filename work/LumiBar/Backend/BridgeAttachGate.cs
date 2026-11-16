namespace LumiBar;

// The diagnostics service replaces its previous site during initialization.
// Wait before touching it, while the old site finishes removing its spacer.
internal static class BridgeAttachGate
{
    internal static string Name(uint explorerPid)=>$"Local\\LumiBar.Reservation.Owner.{explorerPid}";
    internal static bool Busy(uint explorerPid)
    {
        try { if(Mutex.TryOpenExisting(Name(explorerPid),out var owner)){owner.Dispose();return true;}return false; }
        catch(UnauthorizedAccessException){return true;}
    }
    internal static async Task<bool> WaitAsync(uint explorerPid,TimeSpan timeout,CancellationToken ct=default)
    {
        var deadline=Environment.TickCount64+(long)timeout.TotalMilliseconds;var quiet=0;
        do {
            ct.ThrowIfCancellationRequested();
            quiet=Busy(explorerPid)?0:quiet+1;
            if(quiet>=3)return true;
            await Task.Delay(75,ct);
        } while(Environment.TickCount64<deadline);
        return false;
    }
}
