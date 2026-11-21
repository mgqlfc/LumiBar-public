using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
namespace LumiBar;
internal sealed class RainPreview:Grid
{
    private readonly Canvas _chart=new(){Height=74};private readonly PrecipitationTrend? _trend;
    internal RainPreview(PrecipitationTrend? trend)
    {
        _trend=trend;CornerRadius=new CornerRadius(16);Padding=new Thickness(14,10,14,10);Background=Ui.Brush(ColorHelper.FromArgb(24,255,255,255));BorderThickness=new Thickness(1);BorderBrush=Ui.Brush(ColorHelper.FromArgb(32,255,255,255));
        var grid=new Grid{ColumnSpacing=12};grid.ColumnDefinitions.Add(new(){Width=new GridLength(144)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});var words=Ui.Stack(5);words.VerticalAlignment=VerticalAlignment.Top;var title=Ui.Text("未来 1 小时降水",15);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;words.Children.Add(title);words.Children.Add(Ui.Text(trend?.IntervalMinutes==5?"5 分钟临近预报":"15 分钟模式趋势",10,.8));grid.Children.Add(words);Grid.SetColumn(_chart,1);grid.Children.Add(_chart);Children.Add(grid);_chart.SizeChanged+=(_,_)=>Draw();ActualThemeChanged+=(_,_)=>Draw();
    }
    private void Draw()
    {
        _chart.Children.Clear();var w=_chart.ActualWidth;if(w<30)return;const double x0=28,y0=19,y1=55;var width=w-x0-3;var accent=ColorHelper.FromArgb(255,34,181,190);
        void Text(string text,double x,double y,double width=30){var label=Ui.Text(text,10,.85);label.Width=width;Canvas.SetLeft(label,x);Canvas.SetTop(label,y);_chart.Children.Add(label);}
        var max=Math.Max(2,_trend?.Intervals.Max(x=>x.AmountMm??0)??0);
        var tickNumbers=Enumerable.Range(0,3).Select(i=>Ui.Text((max*(2-i)/2).ToString("0.#"),10,.85)).ToArray();
        foreach(var number in tickNumbers){number.TextWrapping=TextWrapping.NoWrap;number.TextAlignment=TextAlignment.Center;number.VerticalAlignment=VerticalAlignment.Top;number.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));}
        var numberWidth=Math.Ceiling(tickNumbers.Max(n=>n.DesiredSize.Width));
        var unitProbe=Ui.Text("mm",9,.85);unitProbe.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));var unitWidth=Math.Ceiling(unitProbe.DesiredSize.Width);
        for(int i=0;i<3;i++){
            var y=y0+i*(y1-y0)/2;var tick=new Grid{Width=numberWidth+unitWidth};
            tick.ColumnDefinitions.Add(new(){Width=new GridLength(numberWidth)});tick.ColumnDefinitions.Add(new(){Width=new GridLength(unitWidth)});
            tick.Children.Add(tickNumbers[i]);var unit=Ui.Text("mm",9,.85);unit.TextWrapping=TextWrapping.NoWrap;unit.VerticalAlignment=VerticalAlignment.Top;unit.Margin=new Thickness(0,tickNumbers[i].BaselineOffset-unitProbe.BaselineOffset,0,0);Grid.SetColumn(unit,1);tick.Children.Add(unit);
            // The unit stays anchored; only the number centres inside its shared column.
            // All added label width extends left, leaving the plot and grid lines unchanged.
            Canvas.SetLeft(tick,10-tick.Width);Canvas.SetTop(tick,y-5);_chart.Children.Add(tick);
            var line=new Line{X1=x0,X2=w,Y1=y,Y2=y,Stroke=Ui.Brush(ColorHelper.FromArgb(20,128,128,128)),StrokeThickness=1};_chart.Children.Add(line);
        }
        Text("现在",x0,59);Text("30 分钟",x0+width/2-18,59,45);Text("60 分钟",w-40,59,42);
        if(_trend==null||!_trend.Complete){Text(_trend==null?"暂无数据":"数据不完整",x0+20,25,100);return;}
        var values=_trend.Intervals.Select(x=>x.AmountMm??0).ToArray();if(values.Length==0)return;var points=_trend.Intervals.Select(v=>new Point(x0+width*Math.Clamp((v.End-_trend.WindowStart).TotalMinutes/60,0,1),y1-v.AmountMm!.Value/max*(y1-y0))).ToArray();
        PathFigure Curve(){var f=new PathFigure{StartPoint=points[0]};for(int i=1;i<points.Length;i++){var a=points[i-1];var b=points[i];f.Segments.Add(new BezierSegment{Point1=new(a.X+(b.X-a.X)/3,a.Y),Point2=new(b.X-(b.X-a.X)/3,b.Y),Point3=b});}return f;}
        var area=Curve();area.IsClosed=true;area.Segments.Add(new LineSegment{Point=new(points[^1].X,y1)});area.Segments.Add(new LineSegment{Point=new(points[0].X,y1)});_chart.Children.Add(new Microsoft.UI.Xaml.Shapes.Path{Data=new PathGeometry{Figures={area}},Fill=AccentTheme.Area(70)});_chart.Children.Add(new Microsoft.UI.Xaml.Shapes.Path{Data=new PathGeometry{Figures={Curve()}},Stroke=Ui.AccentBrush(),StrokeThickness=1.6});
    }
}
