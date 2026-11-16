namespace LumiBar;

internal readonly record struct ScreenRect(int Left,int Top,int Right,int Bottom);
internal readonly record struct TaskbarItem(ScreenRect Bounds,bool Tray);
internal readonly record struct TaskbarSpaceResult(int Left,int Right);
internal static class TaskbarSpace
{
    internal static TaskbarSpaceResult? Measure(ScreenRect bar,IReadOnlyList<TaskbarItem> items,bool recognizedFrame)
    {
        if(!recognizedFrame||bar.Right<=bar.Left||bar.Bottom<=bar.Top)return null;
        var visible=items.Where(x=>x.Bounds.Right>x.Bounds.Left&&x.Bounds.Bottom>x.Bounds.Top&&x.Bounds.Right>bar.Left&&x.Bounds.Left<bar.Right&&x.Bounds.Top<bar.Bottom&&x.Bounds.Bottom>bar.Top).ToList();
        var tray=visible.Where(x=>x.Tray).ToList();var buttons=visible.Where(x=>!x.Tray).ToList();
        if(tray.Count==0||buttons.Count==0)return null;
        var right=Math.Clamp(tray.Min(x=>x.Bounds.Left),bar.Left,bar.Right);
        var left=Math.Clamp(buttons.Max(x=>x.Bounds.Right),bar.Left,bar.Right);
        return new(Math.Min(left,right),right);
    }
}
