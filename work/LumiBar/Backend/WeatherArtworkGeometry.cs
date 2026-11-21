namespace LumiBar;

internal readonly record struct WeatherArtworkBounds(double Left,double Top,double Right,double Bottom)
{
    internal (double X,double Y) CenterOffset(double cellWidth,double cellHeight,double size)=>
        ((cellWidth-Left-Right)*size/(2*cellWidth),(cellHeight-Top-Bottom)*size/(2*cellHeight));
}

internal static class WeatherArtworkGeometry
{
    // Preserve every cell's scale. Only move its visible artwork; do not recrop
    // the atlas resource or allow an adjacent cell through the viewport.
    internal static WeatherArtworkBounds[] Measure(ReadOnlySpan<byte> pixels,int width,int height)
    {
        if(width<4||height<4||(long)width*height*4>pixels.Length)
            throw new ArgumentException("Expected a complete BGRA 4 by 4 weather atlas.");
        var cw=width/4d;var ch=height/4d;
        var bounds=Enumerable.Repeat(new WeatherArtworkBounds(cw,ch,0,0),16).ToArray();
        for(var y=0;y<height;y++)for(var x=0;x<width;x++){
            if(pixels[(y*width+x)*4+3]<16)continue;
            // The 1254-pixel source has half-pixel cell boundaries. Account for
            // every part of a boundary pixel instead of dropping atlas pixels.
            var firstCol=Math.Min(3,(int)(x/cw));var lastCol=Math.Min(3,(int)((x+1-1e-9)/cw));
            var firstRow=Math.Min(3,(int)(y/ch));var lastRow=Math.Min(3,(int)((y+1-1e-9)/ch));
            for(var row=firstRow;row<=lastRow;row++)for(var col=firstCol;col<=lastCol;col++){
                var index=row*4+col;var b=bounds[index];
                bounds[index]=new(Math.Min(b.Left,Math.Max(0,x-col*cw)),Math.Min(b.Top,Math.Max(0,y-row*ch)),Math.Max(b.Right,Math.Min(cw,x+1-col*cw)),Math.Max(b.Bottom,Math.Min(ch,y+1-row*ch)));
            }
        }
        for(var i=0;i<bounds.Length;i++)if(bounds[i].Right<=bounds[i].Left||bounds[i].Bottom<=bounds[i].Top)bounds[i]=new(0,0,cw,ch);
        return bounds;
    }
}
