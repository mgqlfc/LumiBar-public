using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace LumiBar;

internal sealed record ReservationSnapshot(string Status,int Error,ScreenRect? Slot,double WidthDip,uint ExplorerPid,long At)
{
    internal bool Compatible=>Status is "ready" or "reserved";
    internal bool Reserved=>Status=="reserved"&&Slot is {} r&&r.Right>r.Left&&Environment.TickCount64-At<800;
}

/// <summary>Owns the in-box XAML diagnostics connection and a bounded native layout lease.</summary>
internal sealed class TaskbarReservation : IDisposable
{
    private readonly CancellationTokenSource _stop=new();
    private readonly Task _worker;
    private double _desiredWidth;
    private ReservationSnapshot _snapshot=new("starting",0,null,0,0,0);
    internal ReservationSnapshot Snapshot=>Volatile.Read(ref _snapshot);
    internal event Action? Changed;
    internal TaskbarReservation(){_worker=Task.Run(Run);}
    internal void SetWidth(double dip)=>Volatile.Write(ref _desiredWidth,double.IsFinite(dip)?Math.Clamp(dip,0,700):0);
    private void Publish(ReservationSnapshot value){var old=Interlocked.Exchange(ref _snapshot,value);if(old.Status!=value.Status||old.Slot!=value.Slot||old.ExplorerPid!=value.ExplorerPid)Changed?.Invoke();}
    private async Task Run()
    {
        (uint pid,nint hwnd) attempted=default;
        try {
            while(!_stop.IsCancellationRequested){
                var bar=Native.FindWindow("Shell_TrayWnd",null);GetWindowThreadProcessId(bar,out var pid);
                if(bar==0||pid==0){Publish(new("waiting-for-explorer",0,null,0,0,0));await Task.Delay(500,_stop.Token);continue;}
                if(attempted==(pid,bar)){await Task.Delay(500,_stop.Token);continue;}
                attempted=(pid,bar);
                await Connect(bar,pid);
            }
        }catch(OperationCanceledException){}catch(Exception ex){App.Log("taskbar-reservation",ex);Publish(new("failed",ex.HResult,null,0,attempted.pid,0));}
    }
    private async Task Connect(nint bar,uint pid)
    {
        var dll=Path.Combine(AppContext.BaseDirectory,"LumiBar.TaskbarBridge.dll");
        if(!File.Exists(dll)){Publish(new("component-missing",0,null,0,pid,0));return;}
        var name=$"Local\\LumiBar.Reservation.{Environment.ProcessId}.{Guid.NewGuid():N}";
        using var mapping=MemoryMappedFile.CreateNew(name,112,MemoryMappedFileAccess.ReadWrite);
        using var view=mapping.CreateViewAccessor();
        view.Write(0,0x4C425231);view.Write(4,1);view.Write(32,(long)bar);view.Write(40,Environment.ProcessId);
        int sequence=0;
        void Write(int mode,double width){view.Write(8,++sequence);Thread.MemoryBarrier();view.Write(12,mode);view.Write(16,width);view.Write(24,Environment.TickCount64);Thread.MemoryBarrier();view.Write(8,++sequence);}
        Write(0,0);
        Publish(new("connecting",0,null,0,pid,Environment.TickCount64));
        // The native entry point may take time. Keep the lease alive independently.
        using var pulse=new Timer(_=>{lock(view)Write(1,Volatile.Read(ref _desiredWidth));},null,0,200);
        try {
            int hr=unchecked((int)0x80070490);var clsid=new Guid("582ffb86-1107-42a6-8391-eab07d3f4a61");
            for(int i=1;i<=64&&!_stop.IsCancellationRequested;i++){
                hr=InitializeXamlDiagnosticsEx($"VisualDiagConnection{i}",pid,"Windows.UI.Xaml.dll",dll,clsid,name);
                if(hr!=unchecked((int)0x80070490))break;
            }
            if(hr<0){Publish(new("unsupported",hr,null,0,pid,Environment.TickCount64));return;}
            var connectedAt=Environment.TickCount64;
            while(!_stop.IsCancellationRequested){
                if(Native.FindWindow("Shell_TrayWnd",null)!=bar)break;
                var seq=view.ReadInt32(44);Thread.MemoryBarrier();
                if((seq&1)==0){
                    var status=view.ReadInt32(48);var error=view.ReadInt32(52);var at=view.ReadInt64(56);
                    var rect=new ScreenRect(view.ReadInt32(64),view.ReadInt32(68),view.ReadInt32(72),view.ReadInt32(76));var actual=view.ReadDouble(80);
                    Thread.MemoryBarrier();
                    if(seq==view.ReadInt32(44)){
                        var label=status switch{1=>"connecting",2=>"ready",3=>"reserved",4=>"released",5=>"unsupported",6=>"failed",_=>"connecting"};
                        if(at>0&&Environment.TickCount64-at>1500)label="stale";
                        Publish(new(label,error,status==3?rect:null,actual,pid,at));
                        if(status is 4 or 5 or 6||Environment.TickCount64-connectedAt>8000&&status<2)break;
                    }
                }
                await Task.Delay(50,_stop.Token);
            }
        }finally{await pulse.DisposeAsync();lock(view)Write(2,0);await Task.Delay(200);}
    }
    public void Dispose(){_stop.Cancel();SetWidth(0);}
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint h,out uint pid);
    [DllImport("Windows.UI.Xaml.dll",CharSet=CharSet.Unicode,ExactSpelling=true),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern int InitializeXamlDiagnosticsEx(string endpoint,uint pid,string diagnostics,string tap,Guid clsid,string data);
}
