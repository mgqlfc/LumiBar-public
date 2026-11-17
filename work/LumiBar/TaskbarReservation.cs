using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace LumiBar;

/// <summary>Owns the in-box XAML diagnostics connection and a bounded native layout lease.</summary>
internal sealed class TaskbarReservation : IDisposable
{
    private readonly CancellationTokenSource _stop=new();
    private readonly Task _worker;
    private readonly nint _ownerWindow;
    private double _desiredWidth;
    private sealed record CapsuleHandles(nint Music,nint Weather,int Pid);
    private CapsuleHandles _capsules=new(0,0,0);
    internal void SetCapsules(nint music,nint weather,int pid)=>Volatile.Write(ref _capsules,new(music,weather,pid));
    private ReservationSnapshot _snapshot=new("starting",0,null,0,0,0);
    internal ReservationSnapshot Snapshot=>Volatile.Read(ref _snapshot);
    internal event Action? Changed;
    internal TaskbarReservation(){_ownerWindow=WinRT.Interop.WindowNative.GetWindowHandle(App.Instance.WeatherCapsule);_worker=Task.Run(Run);}
    internal void SetWidth(double dip)=>Volatile.Write(ref _desiredWidth,double.IsFinite(dip)?Math.Clamp(dip,0,700):0);
    private void Publish(ReservationSnapshot value){var old=Interlocked.Exchange(ref _snapshot,value);if(old.Status!=value.Status||old.ExplorerPid!=value.ExplorerPid||old.Error!=value.Error)App.Trace("reservation",$"{old.Status}->{value.Status}; explorer={value.ExplorerPid}; error=0x{value.Error:x8}; callbacks={value.CallbackCount}; candidates={value.CandidateCount}; discovery=0x{value.DiscoveryError:x8}; container={value.ContainerKind}");if(old.Status!=value.Status||old.Slot!=value.Slot||old.ExplorerPid!=value.ExplorerPid)Changed?.Invoke();}
    private async Task Run()
    {
        (uint pid,nint hwnd) attempted=default;long nextAttempt=0;
        try {
            while(!_stop.IsCancellationRequested){
                var bar=Native.FindWindow("Shell_TrayWnd",null);GetWindowThreadProcessId(bar,out var pid);
                if(bar==0||pid==0){Publish(new("waiting-for-explorer",0,null,0,0,0));await Task.Delay(500,_stop.Token);continue;}
                if(attempted==(pid,bar)&&Environment.TickCount64<nextAttempt){await Task.Delay(500,_stop.Token);continue;}
                attempted=(pid,bar);
                await Connect(bar,pid);
                nextAttempt=Environment.TickCount64+30000;
            }
        }catch(OperationCanceledException){}catch(Exception ex){App.Log("taskbar-reservation",ex);Publish(new("failed",ex.HResult,null,0,attempted.pid,0));}
    }
    private async Task Connect(nint bar,uint pid)
    {
        Publish(new("waiting-for-release",0,null,0,pid,Environment.TickCount64));
        if(!await BridgeAttachGate.WaitAsync(pid,TimeSpan.FromSeconds(8),_stop.Token)){
            Publish(new("previous-owner-busy",unchecked((int)0x800700aa),null,0,pid,Environment.TickCount64));return;
        }
        if(Native.FindWindow("Shell_TrayWnd",null)!=bar)return;
        var dll=Path.Combine(AppContext.BaseDirectory,"LumiBar.TaskbarBridge.v6.dll");
        if(!File.Exists(dll)){Publish(new("component-missing",0,null,0,pid,0));return;}
        // Explorer retains the native module until it exits. Load a verified,
        // content-addressed user-data copy so upgrades never replace a loaded DLL.
        dll=TaskbarComponentCache.Prepare(dll,Preferences.DataPath);
        var name=$"Local\\LumiBar.Reservation.{Environment.ProcessId}.{Guid.NewGuid():N}";
        using var mapping=MemoryMappedFile.CreateNew(name,144,MemoryMappedFileAccess.ReadWrite);
        using var view=mapping.CreateViewAccessor();
        view.Write(0,0x4C425231);view.Write(4,2);view.Write(32,(long)bar);view.Write(40,Environment.ProcessId);
        int sequence=0;
        void Write(int mode,double width){view.Write(8,++sequence);Thread.MemoryBarrier();view.Write(12,mode);view.Write(16,width);view.Write(24,Environment.TickCount64);var capsules=Volatile.Read(ref _capsules);view.Write(112,(long)capsules.Music);view.Write(120,(long)capsules.Weather);view.Write(128,capsules.Pid);Thread.MemoryBarrier();view.Write(8,++sequence);}
        Write(0,0);
        Publish(new("connecting",0,null,0,pid,Environment.TickCount64));
        // The native entry point may take time. Keep the lease alive independently.
        using var pulse=new Timer(_=>{lock(view)Write(1,Volatile.Read(ref _desiredWidth));},null,0,200);
        try {
            int hr=unchecked((int)0x80070490);var clsid=new Guid("717163a7-1a1c-4717-a313-710e7e627148");
            var broker=FindBroker(pid);
            if(broker!=0){
                var text=Marshal.StringToHGlobalUni(name);
                try{var packet=new CopyData{Kind=(nuint)0x4c423602,Length=(uint)((name.Length+1)*2),Data=text};hr=SendMessageTimeout(broker,0x4a,_ownerWindow,ref packet,3,1500,out var accepted)!=0&&accepted==1?0:unchecked((int)0x800700aa);}
                finally{Marshal.FreeHGlobal(text);}
                App.Trace("reservation",$"reuse resident broker explorer={pid}; result=0x{hr:x8}");
            }else{
                App.Trace("reservation",$"initialize resident broker explorer={pid}");
                for(int i=0;i<=256&&!_stop.IsCancellationRequested;i++){
                    hr=InitializeXamlDiagnosticsEx($"VisualDiagConnection{i}",pid,"Windows.UI.Xaml.dll",dll,clsid,name);
                    if(hr!=unchecked((int)0x80070490))break;
                }
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
                        var label=status switch{1=>"connecting",2=>"ready",3=>"reserved",4=>"released",5=>"unsupported",6=>"failed",7=>"layer-only",_=>"connecting"};
                        if(at>0&&Environment.TickCount64-at>1500)label="stale";
                        Publish(new(label,error,status==3?rect:null,actual,pid,at,view.ReadInt32(132),view.ReadUInt32(136),view.ReadUInt32(140),view.ReadUInt32(96),view.ReadUInt32(100),view.ReadInt32(104),view.ReadUInt32(108)));
                        if(status is 4 or 5 or 6||Environment.TickCount64-connectedAt>8000&&status<2)break;
                    }
                }
                await Task.Delay(50,_stop.Token);
            }
        }finally{
            await pulse.DisposeAsync();lock(view)Write(2,0);
            var until=Environment.TickCount64+1800;
            while(Environment.TickCount64<until&&BridgeAttachGate.Busy(pid))await Task.Delay(50);
            App.Trace("reservation",$"release explorer={pid}; ownerBusy={BridgeAttachGate.Busy(pid)}");
        }
    }
    public void Dispose(){_stop.Cancel();SetWidth(0);}
    internal async Task StopAsync(){Dispose();try{await _worker.WaitAsync(TimeSpan.FromSeconds(3));}catch(TimeoutException){App.Trace("reservation","release wait timed out; next attachment still waits for native owner");}}
    private static nint FindBroker(uint pid){nint window=0;while((window=FindWindowEx(new nint(-3),window,"LumiBar.Reservation.Dispatch.v6",null))!=0){GetWindowThreadProcessId(window,out var owner);if(owner==pid)return window;}return 0;}
    [StructLayout(LayoutKind.Sequential)]private struct CopyData{internal nuint Kind;internal uint Length;internal nint Data;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint FindWindowEx(nint parent,nint after,string name,string? title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint SendMessageTimeout(nint hwnd,uint message,nint sender,ref CopyData data,uint flags,uint timeout,out nuint result);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint h,out uint pid);
    [DllImport("Windows.UI.Xaml.dll",CharSet=CharSet.Unicode,ExactSpelling=true),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern int InitializeXamlDiagnosticsEx(string endpoint,uint pid,string diagnostics,string tap,Guid clsid,string data);
}
