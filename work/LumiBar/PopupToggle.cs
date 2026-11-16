using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
namespace LumiBar;

internal static class PopupToggle
{
    internal static void Bind(Button button,Func<SurfaceWindow> window,Action open)
    {
        bool? pressedOpen=null;
        button.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,e)=>{
            if(e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)pressedOpen=window().IsOpen;
        }),true);
        button.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,_)=>button.DispatcherQueue.TryEnqueue(()=>pressedOpen=null)),true);
        button.PointerCanceled+=(_,_)=>pressedOpen=null;
        button.KeyDown+=(_,_)=>pressedOpen=null;
        button.Click+=(_,_)=>{var close=pressedOpen??window().IsOpen;pressedOpen=null;if(close)window().HideSurface();else open();};
    }
}
