#define NOMINMAX
#include <windows.h>
#include <ocidl.h>
#include <xamlom.h>
#undef GetCurrentTime
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <atomic>
#include <mutex>
#include <vector>
#include <cmath>
#include "BridgeProtocol.h"
using namespace winrt;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
using namespace Windows::UI::Xaml::Media;
static HINSTANCE g_module;
static constexpr wchar_t WindowClass[]=L"LumiBar.Reservation.Dispatch.v6";
static std::atomic<HWND> g_layerWindow{};
static std::atomic<bool> g_layerQueued{false};
static void CALLBACK LayerEvent(HWINEVENTHOOK,DWORD event,HWND hwnd,LONG,LONG,DWORD,DWORD){
    auto window=g_layerWindow.load();if(!window)return;
    if(event!=EVENT_SYSTEM_FOREGROUND&&event!=EVENT_OBJECT_REORDER){auto bar=FindWindowW(L"Shell_TrayWnd",nullptr);if(hwnd!=bar&&GetAncestor(hwnd,GA_ROOT)!=bar)return;}
    if(!g_layerQueued.exchange(true)&&!PostMessageW(window,WM_APP+2,0,0))g_layerQueued=false;
}

struct Bridge : implements<Bridge,IObjectWithSite,IVisualTreeServiceCallback2> {
    com_ptr<IXamlDiagnostics> diagnostics;
    com_ptr<IVisualTreeService> tree;
    HANDLE mapping{},owner{},worker{},exclusive{};
    BridgeMemory* shared{};
    HWND window{};
    HWINEVENTHOOK layerForeground{},layerObjects{};
    std::atomic<bool> stop{false};
    std::mutex handlesMutex;
    std::mutex leaseMutex;
    std::vector<InstanceHandle> candidates;
    Grid parent{nullptr},spacer{nullptr};
    StackPanel stack{nullptr};
    ColumnDefinition column{nullptr};
    bool inserted{};
    uint64_t started{};
    HRESULT treeError{S_OK};
    HRESULT Attach(std::wstring const& name,HWND sender=nullptr) {
        if(shared)return HRESULT_FROM_WIN32(ERROR_BUSY);
        if(name.rfind(L"Local\\LumiBar.Reservation.",0)!=0||name.size()>160)return E_INVALIDARG;
        auto nextMapping=OpenFileMappingW(FILE_MAP_READ|FILE_MAP_WRITE,FALSE,name.c_str());if(!nextMapping)return HRESULT_FROM_WIN32(GetLastError());
        auto next=(BridgeMemory*)MapViewOfFile(nextMapping,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(BridgeMemory));
        auto reject=[&](HRESULT hr){if(next)UnmapViewOfFile(next);CloseHandle(nextMapping);return hr;};
        if(!next||next->magic!=BridgeMagic||next->version!=2)return reject(E_INVALIDARG);
        DWORD targetPid{};GetWindowThreadProcessId((HWND)next->taskbar,&targetPid);if(targetPid!=GetCurrentProcessId())return reject(E_INVALIDARG);
        if(sender){DWORD senderPid{};GetWindowThreadProcessId(sender,&senderPid);if(senderPid!=next->ownerPid)return reject(E_ACCESSDENIED);}
        DWORD ownerSession{},thisSession{};
        if(!ProcessIdToSessionId(next->ownerPid,&ownerSession)||!ProcessIdToSessionId(GetCurrentProcessId(),&thisSession)||ownerSession!=thisSession)return reject(E_ACCESSDENIED);
        auto nextOwner=OpenProcess(SYNCHRONIZE,FALSE,next->ownerPid);if(!nextOwner)return reject(HRESULT_FROM_WIN32(GetLastError()));
        std::wstring lockName=L"Local\\LumiBar.Reservation.Owner."+std::to_wstring(GetCurrentProcessId());
        auto nextExclusive=CreateMutexW(nullptr,FALSE,lockName.c_str());
        if(!nextExclusive||GetLastError()==ERROR_ALREADY_EXISTS){if(nextExclusive)CloseHandle(nextExclusive);CloseHandle(nextOwner);return reject(HRESULT_FROM_WIN32(ERROR_BUSY));}
        {std::lock_guard lock(leaseMutex);mapping=nextMapping;shared=next;owner=nextOwner;exclusive=nextExclusive;started=GetTickCount64();Report(1);}return S_OK;
    }
    void ReleaseLease() noexcept {
        if(!shared)return;
        RestoreBands();try{RemoveSpacer();}catch(...){}
        parent=nullptr;stack=nullptr;column=nullptr;spacer=nullptr;inserted=false;
        Report(4);{std::lock_guard lock(leaseMutex);auto old=shared;shared=nullptr;UnmapViewOfFile(old);}
        if(mapping){CloseHandle(mapping);mapping=nullptr;}if(owner){CloseHandle(owner);owner=nullptr;}
        if(exclusive){CloseHandle(exclusive);exclusive=nullptr;}
    }
    ~Bridge(){if(shared)UnmapViewOfFile(shared);if(mapping)CloseHandle(mapping);if(owner)CloseHandle(owner);if(worker)CloseHandle(worker);if(exclusive)CloseHandle(exclusive);}
    void Report(uint32_t status,HRESULT error=S_OK,RECT rect={},double width=0) noexcept {
        if(!shared)return;
        InterlockedIncrement(&shared->responseSequence);shared->status=status;shared->error=error;
        shared->slot=rect;shared->actualDip=width;shared->responseAt=GetTickCount64();
        MemoryBarrier();InterlockedIncrement(&shared->responseSequence);
    }
    HRESULT __stdcall SetSite(IUnknown* site) noexcept override {
        if(!site){stop=true;return S_OK;}
        try {
            check_hresult(site->QueryInterface(diagnostics.put()));tree=diagnostics.as<IVisualTreeService>();
            BSTR data{};check_hresult(diagnostics->GetInitializationData(&data));
            std::wstring name=data?data:L"";SysFreeString(data);
            check_hresult(Attach(name));
            WNDCLASSW wc{};wc.hInstance=g_module;wc.lpfnWndProc=WindowProc;wc.lpszClassName=WindowClass;RegisterClassW(&wc);
            window=CreateWindowExW(0,WindowClass,L"",0,0,0,0,0,HWND_MESSAGE,nullptr,g_module,this);
            if(!window)return HRESULT_FROM_WIN32(GetLastError());
            started=GetTickCount64();Report(1);AddRef();
            worker=CreateThread(nullptr,0,[](void* p)->DWORD {
                auto self=(Bridge*)p;CoInitializeEx(nullptr,COINIT_MULTITHREADED);
                HRESULT hr=self->tree->AdviseVisualTreeChange(self);
                if(FAILED(hr))PostMessageW(self->window,WM_APP+1,(WPARAM)hr,0);
                while(!self->stop)Sleep(100);
                self->tree->UnadviseVisualTreeChange(self);
                PostMessageW(self->window,WM_CLOSE,0,0);
                CoUninitialize();self->Release();return 0;
            },this,0,nullptr);
            if(!worker){Release();DestroyWindow(window);return HRESULT_FROM_WIN32(GetLastError());}
            g_layerWindow=window;
            layerForeground=SetWinEventHook(EVENT_SYSTEM_FOREGROUND,EVENT_SYSTEM_FOREGROUND,nullptr,LayerEvent,0,0,WINEVENT_OUTOFCONTEXT);
            layerObjects=SetWinEventHook(EVENT_OBJECT_SHOW,EVENT_OBJECT_REORDER,nullptr,LayerEvent,0,0,WINEVENT_OUTOFCONTEXT);
            SetTimer(window,1,50,nullptr);return S_OK;
        }catch(...){Report(6,to_hresult());return to_hresult();}
    }
    HRESULT __stdcall GetSite(REFIID iid,void** result) noexcept override {return diagnostics?diagnostics->QueryInterface(iid,result):E_FAIL;}
    HRESULT __stdcall OnElementStateChanged(InstanceHandle,VisualElementState,LPCWSTR) noexcept override{return S_OK;}
    HRESULT __stdcall OnVisualTreeChange(ParentChildRelation,VisualElement element,VisualMutationType mutation) noexcept override {
        try {
            {std::lock_guard lock(leaseMutex);if(shared)InterlockedIncrement(&shared->callbackCount);}
            if(mutation==Add&&element.Name&&wcscmp(element.Name,L"SystemTrayFrameGrid")==0){std::lock_guard lock(handlesMutex);if(candidates.size()<16)candidates.push_back(element.Handle);}
            if(mutation==Remove){std::lock_guard lock(handlesMutex);std::erase(candidates,element.Handle);}
        }catch(...){}return S_OK;
    }
    void Discover() {
        std::vector<InstanceHandle> copy;{std::lock_guard lock(handlesMutex);copy=candidates;}
        shared->candidateCount=(LONG)copy.size();
        // Do not guess between indistinguishable monitor roots.
        if(copy.size()>1){Report(5,HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED));return;}
        for(auto handle:copy){
            com_ptr<::IInspectable> object;auto hr=diagnostics->GetIInspectableFromHandle(handle,object.put());shared->discoverError=hr;if(FAILED(hr))continue;
            auto element=object.try_as<FrameworkElement>();if(!element||element.Name()!=L"SystemTrayFrameGrid"||!element.XamlRoot())continue;
            auto grid=element.try_as<Grid>();auto panel=element.try_as<StackPanel>();
            if(!grid&&(!panel||panel.Orientation()!=Orientation::Horizontal))continue;
            // Initial implementation accepts the single primary horizontal taskbar only.
            RECT bar{};GetWindowRect((HWND)shared->taskbar,&bar);if(bar.right-bar.left<=bar.bottom-bar.top)continue;
            if(element.ActualWidth()<=0||grid&&grid.ColumnDefinitions().Size()==0)continue;
            parent=grid;stack=panel;shared->containerKind=grid?1:2;shared->targetHandle=handle;Report(2);return;
        }
    }
    void RemoveSpacer() {
        if(!inserted)return;
        uint32_t index{};
        if(stack){if(stack.Children().IndexOf(spacer,index))stack.Children().RemoveAt(index);inserted=false;spacer=nullptr;return;}
        if(!parent)return;
        if(parent.Children().IndexOf(spacer,index))parent.Children().RemoveAt(index);
        if(parent.ColumnDefinitions().IndexOf(column,index)){
            parent.ColumnDefinitions().RemoveAt(index);
            for(auto child:parent.Children())if(auto element=child.try_as<FrameworkElement>();element&&Grid::GetColumn(element)>(int)index)Grid::SetColumn(element,Grid::GetColumn(element)-1);
        }
        inserted=false;spacer=nullptr;column=nullptr;
    }
    using GetBandFn=BOOL(WINAPI*)(HWND,DWORD*);
    using SetBandFn=BOOL(WINAPI*)(HWND,HWND,DWORD);
    struct CapsuleBand {HWND hwnd{};DWORD pid{},original{};};
    CapsuleBand bands[2]{};
    GetBandFn getBand=(GetBandFn)GetProcAddress(GetModuleHandleW(L"user32.dll"),"GetWindowBand");
    SetBandFn setBand=(SetBandFn)GetProcAddress(GetModuleHandleW(L"user32.dll"),"SetWindowBand");
    void RestoreBands() noexcept {
        for(auto& saved:bands){DWORD pid{};GetWindowThreadProcessId(saved.hwnd,&pid);if(saved.hwnd&&pid==saved.pid&&setBand)setBand(saved.hwnd,HWND_TOP,saved.original);saved={};}
    }
    void SyncBands(HWND music,HWND weather,DWORD capsulePid) noexcept {
        if(!getBand||!setBand){shared->layerError=ERROR_PROC_NOT_FOUND;return;}
        DWORD barBand{};if(!getBand((HWND)shared->taskbar,&barBand))return;
        // Only follow the ordinary desktop and Start-menu taskbar bands.
        // Never enter lock-screen, secure/system notification bands.
        if(barBand!=1&&barBand!=6){shared->layerError=ERROR_NOT_SUPPORTED;return;}
        shared->taskbarBand=barBand;shared->layerError=0;
        HWND targets[]{music,weather};
        for(int i=0;i<2;i++){
            auto& saved=bands[i];auto hwnd=targets[i];DWORD pid{};GetWindowThreadProcessId(hwnd,&pid);
            if(!hwnd||pid!=capsulePid||GetAncestor(hwnd,GA_ROOT)!=hwnd)continue;
            if(saved.hwnd!=hwnd||saved.pid!=pid){
                DWORD oldPid{};GetWindowThreadProcessId(saved.hwnd,&oldPid);if(saved.hwnd&&oldPid==saved.pid)setBand(saved.hwnd,HWND_TOP,saved.original);
                DWORD original{};if(!getBand(hwnd,&original))continue;saved={hwnd,pid,original};
            }
            DWORD band{};if(!getBand(hwnd,&band))continue;
            if(band!=barBand&&!setBand(hwnd,HWND_TOP,barBand)){shared->layerError=GetLastError();continue;}
            // Show Desktop after Start can leave two windows in the same band
            // but in disjoint Win32 Z-order chains. Hit-test the actual overlap
            // instead of treating an unobservable chain as correctly ordered.
            RECT rect{},barRect{},overlap{};
            if(IsWindowVisible(hwnd)&&!IsIconic(hwnd)&&GetWindowRect(hwnd,&rect)&&GetWindowRect((HWND)shared->taskbar,&barRect)&&IntersectRect(&overlap,&rect,&barRect)){
                POINT center{overlap.left+(overlap.right-overlap.left)/2,overlap.top+(overlap.bottom-overlap.top)/2};
                auto hit=WindowFromPoint(center),bar=(HWND)shared->taskbar;
                if(hit==bar||GetAncestor(hit,GA_ROOT)==bar){
                    // Async avoids blocking Explorer on another process's UI.
                    if(SetWindowPos(hwnd,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_NOOWNERZORDER|SWP_ASYNCWINDOWPOS))shared->layerRepairs++;
                    else shared->layerError=GetLastError();
                }
            }
        }
    }
    void SyncLayerRequest() noexcept {
        if(stop||!shared)return;auto sequence=InterlockedCompareExchange(&shared->requestSequence,0,0);if(sequence&1)return;
        auto mode=shared->mode;auto heartbeat=shared->heartbeat;auto music=(HWND)shared->musicHwnd,weather=(HWND)shared->weatherHwnd;auto pid=shared->capsulePid;
        MemoryBarrier();if(sequence!=InterlockedCompareExchange(&shared->requestSequence,0,0)||mode!=1||GetTickCount64()-heartbeat>3000||WaitForSingleObject(owner,0)!=WAIT_TIMEOUT)return;
        SyncBands(music,weather,pid);
    }
    void Tick() noexcept {
        try {
            if(stop||!shared)return;
            const auto sequence=InterlockedCompareExchange(&shared->requestSequence,0,0);if(sequence&1)return;
            const auto mode=shared->mode;const double width=shared->widthDip;const auto heartbeat=shared->heartbeat;
            const auto music=(HWND)shared->musicHwnd,weather=(HWND)shared->weatherHwnd;const auto capsulePid=shared->capsulePid;
            MemoryBarrier();if(sequence!=InterlockedCompareExchange(&shared->requestSequence,0,0))return;
            // A client exit releases only its layout lease. Keep the diagnostics
            // subscription alive: re-enumerating roots on every application
            // restart can fail-fast inside XamlDiagnostics::GiveRootsToCallback.
            if(mode==2||GetTickCount64()-heartbeat>3000||WaitForSingleObject(owner,0)==WAIT_OBJECT_0){ReleaseLease();return;}
            SyncBands(music,weather,capsulePid);
            // Visual-tree enumeration can fail independently of window layering.
            // Keep this broker and its lease alive so Start/Search band changes
            // still reach the capsules, without claiming a reserved XAML slot.
            if(FAILED(treeError)){Report(7,treeError);return;}
            if(!parent&&!stack)Discover();
            if(!parent&&!stack){if(GetTickCount64()-started>5000)Report(5);return;}
            if(!(parent?parent.XamlRoot():stack.XamlRoot())){RemoveSpacer();parent=nullptr;stack=nullptr;Report(1);return;}
            if(mode==0||width==0){RemoveSpacer();Report(2);return;}
            if(mode!=1||!std::isfinite(width)||width<1||width>700){Report(6,E_INVALIDARG);return;}
            if(!inserted){
                spacer=Grid();spacer.Name(L"LumiBarReservedSpace");spacer.IsHitTestVisible(false);spacer.Width(width);spacer.VerticalAlignment(VerticalAlignment::Stretch);
                if(stack){stack.Children().InsertAt(0,spacer);inserted=true;return;}
                column=ColumnDefinition();column.Width(GridLengthHelper::Auto());
                // Prepare all objects before modifying Explorer's tree; rollback on error.
                parent.ColumnDefinitions().InsertAt(0,column);
                std::vector<std::pair<FrameworkElement,int>> moved;
                try {for(auto child:parent.Children())if(auto element=child.try_as<FrameworkElement>()){int original=Grid::GetColumn(element);moved.emplace_back(element,original);Grid::SetColumn(element,original+1);}Grid::SetColumn(spacer,0);parent.Children().Append(spacer);inserted=true;}
                catch(...){uint32_t index{};if(parent.Children().IndexOf(spacer,index))parent.Children().RemoveAt(index);if(parent.ColumnDefinitions().IndexOf(column,index))parent.ColumnDefinitions().RemoveAt(index);for(auto& [element,original]:moved)Grid::SetColumn(element,original);spacer=nullptr;column=nullptr;throw;}
                return; // Wait for XAML Measure/Arrange to commit before publishing geometry.
            }
            if(std::abs(spacer.Width()-width)>.1){spacer.Width(width);Report(2);return;}
            const double actual=spacer.ActualWidth();
            if(std::abs(actual-width)>1){Report(2);return;}
            // The slot is the leftmost tray column. Convert its XAML-root coordinates using the host HWND.
            auto point=spacer.TransformToVisual(nullptr).TransformPoint({0,0});
            HWND bar=(HWND)shared->taskbar,host=FindWindowExW(bar,nullptr,L"Windows.UI.Composition.DesktopWindowContentBridge",nullptr);
            if(!host)host=bar;RECT rect{},barRect{};GetWindowRect(host,&rect);GetWindowRect(bar,&barRect);
            double scale=spacer.XamlRoot().RasterizationScale();
            RECT slot{rect.left+(LONG)std::lround(point.X*scale),barRect.top,rect.left+(LONG)std::lround((point.X+actual)*scale),barRect.bottom};
            if(slot.left<barRect.left||slot.right>barRect.right||slot.right<=slot.left){Report(6,E_BOUNDS);return;}
            Report(3,S_OK,slot,actual);
        }catch(...){auto hr=to_hresult();try{RemoveSpacer();}catch(...){}Report(6,hr);}
    }
    static LRESULT CALLBACK WindowProc(HWND hwnd,UINT message,WPARAM wp,LPARAM lp) {
        auto self=(Bridge*)GetWindowLongPtrW(hwnd,GWLP_USERDATA);
        if(message==WM_NCCREATE){self=(Bridge*)((CREATESTRUCTW*)lp)->lpCreateParams;self->AddRef();SetWindowLongPtrW(hwnd,GWLP_USERDATA,(LONG_PTR)self);}
        if(self){
            if(message==WM_COPYDATA){
                auto packet=(COPYDATASTRUCT*)lp;
                if(!wp||!packet||packet->dwData!=0x4c423602||packet->cbData<sizeof(wchar_t)||packet->cbData>322||packet->cbData%sizeof(wchar_t))return 0;
                auto value=(wchar_t*)packet->lpData;auto count=packet->cbData/sizeof(wchar_t);if(!value||value[count-1]!=0)return 0;
                try{return SUCCEEDED(self->Attach(std::wstring(value,count-1),(HWND)wp))?1:0;}catch(...){return 0;}
            }
            if(message==WM_TIMER){self->Tick();return 0;}
            if(message==WM_APP+2){g_layerQueued=false;self->SyncLayerRequest();return 0;}
            if(message==WM_APP+1){self->treeError=(HRESULT)wp;self->Report(7,self->treeError);return 0;}
            if(message==WM_CLOSE){g_layerWindow=nullptr;g_layerQueued=false;if(self->layerForeground)UnhookWinEvent(self->layerForeground);if(self->layerObjects)UnhookWinEvent(self->layerObjects);self->ReleaseLease();KillTimer(hwnd,1);DestroyWindow(hwnd);return 0;}
            if(message==WM_NCDESTROY){SetWindowLongPtrW(hwnd,GWLP_USERDATA,0);self->window=nullptr;self->Release();}
        }
        return DefWindowProcW(hwnd,message,wp,lp);
    }
};
struct Factory : implements<Factory,IClassFactory> {
    HRESULT __stdcall CreateInstance(IUnknown* outer,REFIID iid,void** result) noexcept override {if(outer)return CLASS_E_NOAGGREGATION;try{return make_self<Bridge>()->QueryInterface(iid,result);}catch(...){return to_hresult();}}
    HRESULT __stdcall LockServer(BOOL) noexcept override{return S_OK;}
};
HRESULT __stdcall DllGetClassObject(REFCLSID clsid,REFIID iid,void** result){if(clsid!=BridgeClass)return CLASS_E_CLASSNOTAVAILABLE;try{return make_self<Factory>()->QueryInterface(iid,result);}catch(...){return to_hresult();}}
HRESULT __stdcall DllCanUnloadNow(){return S_FALSE;}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){g_module=module;DisableThreadLibraryCalls(module);}return TRUE;}
