#define NOMINMAX
#include <windows.h>
#include <shobjidl.h>
#include <propvarutil.h>
#include <propkey.h>
#include <SystemMediaTransportControlsInterop.h>
#undef GetCurrentTime
#include <winrt/Windows.Media.h>
#include <winrt/Windows.Foundation.h>
#include <vector>
#include <string>
#include <shellapi.h>
using namespace winrt;
using namespace Windows::Media;
static std::vector<HWND> windows;
static int ticks;
static bool mediaOnly;
static bool trayOnly;
static std::vector<UINT> trayIcons;
static void AddTray(HWND h,UINT id){NOTIFYICONDATAW d{};d.cbSize=sizeof(d);d.hWnd=h;d.uID=id;d.uFlags=NIF_ICON|NIF_TIP;d.hIcon=LoadIconW(nullptr,IDI_INFORMATION);wcscpy_s(d.szTip,L"LumiBar 临时托盘测试");if(Shell_NotifyIconW(NIM_ADD,&d))trayIcons.push_back(id);}
static void RemoveTray(HWND h){if(trayIcons.empty())return;NOTIFYICONDATAW d{};d.cbSize=sizeof(d);d.hWnd=h;d.uID=trayIcons.back();Shell_NotifyIconW(NIM_DELETE,&d);trayIcons.pop_back();}
static LRESULT CALLBACK Proc(HWND h,UINT m,WPARAM w,LPARAM l){
    if(m==WM_PAINT){PAINTSTRUCT ps;auto dc=BeginPaint(h,&ps);RECT r;GetClientRect(h,&r);DrawTextW(dc,L"LumiBar 布局压力测试\n测试窗口会自动关闭\n按 Esc 可提前结束",-1,&r,DT_CENTER|DT_WORDBREAK);EndPaint(h,&ps);return 0;}
    if(m==WM_KEYDOWN&&w==VK_ESCAPE){PostQuitMessage(0);return 0;}
    if(m==WM_CLOSE){DestroyWindow(h);return 0;}
    return DefWindowProcW(h,m,w,l);
}
static HWND MakeWindow(HINSTANCE instance,int index){
    auto title=L"LumiBar 布局测试 "+std::to_wstring(index);
    auto h=CreateWindowExW(WS_EX_NOACTIVATE|WS_EX_APPWINDOW,L"LumiBar.LayoutStress",title.c_str(),WS_OVERLAPPEDWINDOW,35,35,320,150,nullptr,nullptr,instance,nullptr);
    com_ptr<IPropertyStore> store;
    if(SUCCEEDED(SHGetPropertyStoreForWindow(h,IID_PPV_ARGS(store.put())))){
        auto id=L"LumiBar.LayoutStress."+std::to_wstring(GetCurrentProcessId())+L"."+std::to_wstring(index);PROPVARIANT value{};
        InitPropVariantFromString(id.c_str(),&value);store->SetValue(PKEY_AppUserModel_ID,value);store->Commit();PropVariantClear(&value);
    }
    ShowWindow(h,SW_SHOWNOACTIVATE);return h;
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,LPWSTR args,int){
    mediaOnly=wcscmp(args,L"--media-only")==0;
    trayOnly=wcscmp(args,L"--tray-only")==0;
    init_apartment(apartment_type::single_threaded);SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    WNDCLASSW wc{};wc.hInstance=instance;wc.lpfnWndProc=Proc;wc.lpszClassName=L"LumiBar.LayoutStress";wc.hbrBackground=(HBRUSH)(COLOR_WINDOW+1);wc.hCursor=LoadCursor(nullptr,IDC_ARROW);RegisterClassW(&wc);
    HWND main=MakeWindow(instance,0);SystemMediaTransportControls media{nullptr};
    if(mediaOnly)try {auto factory=get_activation_factory<SystemMediaTransportControls,ISystemMediaTransportControlsInterop>();check_hresult(factory->GetForWindow(main,guid_of<SystemMediaTransportControls>(),put_abi(media)));media.IsEnabled(true);media.IsPlayEnabled(true);media.IsPauseEnabled(true);media.IsNextEnabled(true);media.IsPreviousEnabled(true);auto display=media.DisplayUpdater();display.Type(MediaPlaybackType::Music);display.MusicProperties().Title(L"LumiBar 测试音轨");display.MusicProperties().Artist(L"布局验收 · 暂停保留");display.Update();media.PlaybackStatus(MediaPlaybackStatus::Paused);}catch(...){}
    SetTimer(main,1,400,[](HWND h,UINT,UINT_PTR,DWORD){
        ++ticks;
        if(mediaOnly){if(ticks>=750)PostQuitMessage(0);return;}
        if(trayOnly){if(ticks>=10&&ticks<=42&&ticks%2==0)AddTray(h,ticks);if(ticks>=65&&ticks%2==0)RemoveTray(h);if(ticks>=110)PostQuitMessage(0);return;}
        if(ticks>10&&ticks<=42)windows.push_back(MakeWindow((HINSTANCE)GetWindowLongPtrW(h,GWLP_HINSTANCE),ticks-10));
        if(ticks>=65&&!windows.empty()){DestroyWindow(windows.back());windows.pop_back();}
        if(ticks>=110)PostQuitMessage(0);
    });
    MSG msg;while(GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}
    if(media)media.IsEnabled(false);while(!trayIcons.empty())RemoveTray(main);for(auto h:windows)if(IsWindow(h))DestroyWindow(h);DestroyWindow(main);return 0;
}
