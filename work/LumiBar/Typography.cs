using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace LumiBar;

// Use explicit script runs: a Chinese font often contains Latin glyphs too,
// so a comma-separated fallback family cannot keep the two choices independent.
internal static class Typography
{
    private static readonly DependencyProperty ManagedProperty=DependencyProperty.RegisterAttached("Managed",typeof(bool),typeof(Typography),new PropertyMetadata(false));
    private static readonly DependencyProperty UpdatingProperty=DependencyProperty.RegisterAttached("Updating",typeof(bool),typeof(Typography),new PropertyMetadata(false));
    private static readonly Dictionary<string,FontFamily> Families=new(StringComparer.Ordinal);
    internal static FontFamily Chinese => Family(App.Instance?.Prefs.ChineseFontFamily,true);
    internal static FontFamily Western => Family(App.Instance?.Prefs.WesternFontFamily,false);
    internal static FontFamily Default => Cached($"{Western.Source}, {Chinese.Source}");
    private static FontFamily Cached(string source){if(!Families.TryGetValue(source,out var font))Families[source]=font=new FontFamily(source);return font;}
    private static FontFamily Family(string? name,bool chinese,int weight=400,bool italic=false)
    {
        if((chinese&&string.IsNullOrWhiteSpace(name))||FontDefaults.IsChinese(name))return Cached($"ms-appx:///Assets/Fonts/PingFangSC-{FontDefaults.ChineseFile(weight)}.otf#PingFang SC");
        if((!chinese&&string.IsNullOrWhiteSpace(name))||FontDefaults.IsWestern(name))return Cached($"ms-appx:///Assets/Fonts/Geist-{(italic?"Italic-":"")}VariableFont_wght.ttf#Geist");
        return Cached(name!);
    }

    internal static void Attach(TextBlock block)
    {
        if((bool)block.GetValue(ManagedProperty))return;
        block.SetValue(ManagedProperty,true);
        block.RegisterPropertyChangedCallback(TextBlock.TextProperty,(sender,_)=>Apply((TextBlock)sender));
        block.RegisterPropertyChangedCallback(TextBlock.FontWeightProperty,(sender,_)=>Apply((TextBlock)sender));
        block.RegisterPropertyChangedCallback(TextBlock.FontStyleProperty,(sender,_)=>Apply((TextBlock)sender));
        block.Loaded+=(_,_)=>Apply(block);
        Apply(block);
    }
    internal static void Refresh(DependencyObject root)
    {
        // Traverse the native tree: WinRT may collect a managed wrapper while
        // its native TextBlock remains mounted, so managed weak lists miss labels.
        if(root is TextBlock block&&(bool)block.GetValue(ManagedProperty))Apply(block);
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)Refresh(VisualTreeHelper.GetChild(root,i));
    }
    internal static bool IsChinese(Rune rune)=>rune.Value is >=0x2E80 and <=0xA4CF
        or >=0xF900 and <=0xFAFF or >=0xFE10 and <=0xFE1F or >=0xFE30 and <=0xFE4F
        or >=0xFF01 and <=0xFF60 or >=0xFFE0 and <=0xFFE6 or >=0x20000 and <=0x323AF;
    private static void Apply(TextBlock block)
    {
        if((bool)block.GetValue(UpdatingProperty))return;
        block.SetValue(UpdatingProperty,true);
        try
        {
            var text=block.Text??"";var parts=new List<(bool Chinese,string Text)>();var chunk=new StringBuilder();bool? current=null;
            foreach(var rune in text.EnumerateRunes())
            {
                var chinese=IsChinese(rune);
                if(current.HasValue&&current!=chinese){parts.Add((current.Value,chunk.ToString()));chunk.Clear();}
                current=chinese;chunk.Append(rune.ToString());
            }
            if(current.HasValue)parts.Add((current.Value,chunk.ToString()));
            var chineseFamily=Family(App.Instance.Prefs.ChineseFontFamily,true,block.FontWeight.Weight,block.FontStyle==Windows.UI.Text.FontStyle.Italic);
            var westernFamily=Family(App.Instance.Prefs.WesternFontFamily,false,block.FontWeight.Weight,block.FontStyle==Windows.UI.Text.FontStyle.Italic);
            block.FontFamily=parts.Count==1&&parts[0].Chinese?chineseFamily:westernFamily;
            if(parts.Count>1)
            {
                block.Inlines.Clear();
                foreach(var part in parts)block.Inlines.Add(new Run{Text=part.Text,FontFamily=part.Chinese?chineseFamily:westernFamily});
            }
            else { block.Text=text; }
            block.InvalidateMeasure();
        }
        finally{block.SetValue(UpdatingProperty,false);}
    }
}
