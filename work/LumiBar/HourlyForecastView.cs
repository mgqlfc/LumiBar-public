using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace LumiBar;
/// <summary>One shared column grid aligns temperature readings, curve nodes and hourly conditions.</summary>
internal sealed class HourlyForecastView : StackPanel
{
    private readonly Canvas _chart=new(){Height=61,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly HourForecast[] _hours;
    internal HourlyForecastView(IReadOnlyList<HourForecast> hours)
    {
        Spacing=5;
        var count=Math.Min(8,hours.Count);
        _hours=Enumerable.Range(0,count).Select(i=>hours[count==1?0:(int)Math.Round(i*(hours.Count-1d)/(count-1))]).ToArray();
        Children.Add(_chart);var row=new Grid();
        foreach(var h in _hours){
            var index=row.ColumnDefinitions.Count;row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var cell=Ui.Stack(3);cell.HorizontalAlignment=HorizontalAlignment.Center;
            cell.Children.Add(new WeatherGlyph(h.Code,h.IsDay,26){HorizontalAlignment=HorizontalAlignment.Center});
            var time=Ui.Text(h.Time.ToString("HH:mm"),11,.8);time.HorizontalAlignment=HorizontalAlignment.Center;cell.Children.Add(time);
            var temperature=Ui.Text(App.Instance.Temperature(h.Temperature),13);temperature.HorizontalAlignment=HorizontalAlignment.Center;cell.Children.Add(temperature);
            Grid.SetColumn(cell,index);row.Children.Add(cell);
        }
        Children.Add(row);_chart.SizeChanged+=(_,_)=>Draw();ActualThemeChanged+=(_,_)=>Draw();
    }
    private void Draw()
    {
        _chart.Children.Clear();if(_hours.Length==0||_chart.ActualWidth<=0)return;
        var dark=ActualTheme==ElementTheme.Dark;var accent=dark?ColorHelper.FromArgb(255,92,205,207):ColorHelper.FromArgb(255,18,137,145);
        var min=_hours.Min(h=>h.Temperature);var max=_hours.Max(h=>h.Temperature);var span=Math.Max(3,max-min);
        var column=_chart.ActualWidth/_hours.Length;
        var points=_hours.Select((h,i)=>new Point(column*(i+.5),48-(h.Temperature-min)/span*25)).ToArray();
        PathFigure Curve(){var fig=new PathFigure{StartPoint=points[0],IsClosed=false,IsFilled=false};for(var i=1;i<points.Length;i++){var a=points[i-1];var b=points[i];var dx=(b.X-a.X)/3;fig.Segments.Add(new BezierSegment{Point1=new(a.X+dx,a.Y),Point2=new(b.X-dx,b.Y),Point3=b});}return fig;}
        if(points.Length>1){
            var area=Curve();area.IsClosed=true;area.IsFilled=true;area.Segments.Add(new LineSegment{Point=new(points[^1].X,61)});area.Segments.Add(new LineSegment{Point=new(points[0].X,61)});
            _chart.Children.Add(new Microsoft.UI.Xaml.Shapes.Path{Data=new PathGeometry{Figures={area}},Fill=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1),GradientStops={new GradientStop{Color=TemperaturePalette.At(1,38),Offset=0},new GradientStop{Color=TemperaturePalette.At(0,0),Offset=1}}}});
            _chart.Children.Add(new Microsoft.UI.Xaml.Shapes.Path{Data=new PathGeometry{Figures={Curve()}},Stroke=TemperaturePalette.Range(1,0,true),StrokeThickness=1.8});
        }
        for(var i=0;i<points.Length;i++){
            var p=points[i];var dot=new Ellipse{Width=5,Height=5,Fill=Ui.Brush(dark?ColorHelper.FromArgb(255,35,53,62):ColorHelper.FromArgb(255,237,248,249)),Stroke=TemperaturePalette.Range(Math.Clamp((_hours[i].Temperature-min)/span-.08,0,1),Math.Clamp((_hours[i].Temperature-min)/span+.08,0,1)),StrokeThickness=1.4};Canvas.SetLeft(dot,p.X-2.5);Canvas.SetTop(dot,p.Y-2.5);_chart.Children.Add(dot);
            var label=Ui.Text(App.Instance.Temperature(_hours[i].Temperature),12);label.Width=column;label.TextAlignment=TextAlignment.Center;Canvas.SetLeft(label,p.X-column/2);Canvas.SetTop(label,p.Y-23);_chart.Children.Add(label);
        }
    }
}


