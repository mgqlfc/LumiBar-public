using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace LumiBar;
internal sealed partial class SettingsWindow
{
    private const string PluginPagePrefix="plugin-menu:";
    private readonly StackPanel _pluginNavigation=Ui.Stack(6);
    private readonly List<Button> _pluginNavigationButtons=[];
    internal void RefreshPluginMenus()
    {
        foreach(var button in _pluginNavigationButtons)_navButtons.Remove(button);
        _pluginNavigationButtons.Clear();_pluginNavigation.Children.Clear();
        var entries=App.Instance.PluginMenus.Views(Environment.TickCount64).Where(x=>PluginMenuRegistry.ForLocation(x.Items,"settings").Length>0).ToArray();
        if(entries.Length>0){_pluginNavigation.Children.Add(new Border{Height=1,Margin=new Thickness(0,8,0,8),Background=Ui.Brush(Windows.UI.Color.FromArgb(24,128,128,128))});}
        foreach(var menu in entries){
            var row=Ui.Row(12);row.Children.Add(Ui.Icon("\uEA86",18));
            var label=Ui.Text(menu.NavigationTitle,15);label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxWidth=94;row.Children.Add(label);
            var page=PluginPagePrefix+menu.RegistrationId;var button=new Button{Content=row,Tag=page,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,Height=42,Padding=new Thickness(12,0,8,0),CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(0)};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,menu.NavigationTitle);ToolTipService.SetToolTip(button,menu.NavigationTitle);button.Click+=(_,_)=>Render(page);NavigationState(button,_selected==page);_navButtons.Add(button);_pluginNavigationButtons.Add(button);_pluginNavigation.Children.Add(button);
        }
        if(_selected.StartsWith(PluginPagePrefix,StringComparison.Ordinal))Render(entries.Any(x=>PluginPagePrefix+x.RegistrationId==_selected)?_selected:"插件");
    }
    private void RenderPluginMenuPage(string page)
    {
        var id=page[PluginPagePrefix.Length..];var menu=App.Instance.PluginMenus.Views(Environment.TickCount64).FirstOrDefault(x=>x.RegistrationId==id);
        if(menu==null){Heading("插件已离线","插件重新连接后，这里的入口会自动恢复。");return;}
        Heading(menu.NavigationTitle,string.IsNullOrEmpty(menu.NavigationDescription)?$"由 {menu.DisplayName} 提供。":menu.NavigationDescription);
        var status=Ui.Text("",12,.75);status.Visibility=Visibility.Collapsed;
        void Add(IEnumerable<PluginMenuItem> source,int depth,bool enabled){
            foreach(var item in source){
                if(item.Kind=="separator"){Separator();continue;}
                if(item.Kind=="submenu"){
                    var heading=Ui.Text(item.Label,15,.85);heading.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;heading.Margin=new Thickness(depth*12,4,0,0);_page.Children.Add(heading);Add(item.Children!,depth+1,enabled&&item.Enabled);continue;
                }
                var row=Ui.Row(10);if(item.Checked is {} check)row.Children.Add(Ui.Icon(check?"\uE73D":"\uE739",16));row.Children.Add(Ui.Text(item.Label,14));
                var button=new Button{Content=row,IsEnabled=enabled&&item.Enabled,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,MinHeight=38,Margin=new Thickness(depth*12,0,0,0)};
                Ui.SoftAccentHover(button);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,item.Label);
                if(item.Checked is {} checkedState)Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(button,checkedState?"已勾选":"未勾选");
                button.Click+=(_,_)=>{status.Text=App.Instance.InvokePluginMenu(menu,item,"settings")?"已交给插件处理。":"插件暂时不可用，请稍后重试。";status.Visibility=Visibility.Visible;};_page.Children.Add(button);
            }
        }
        Add(PluginMenuRegistry.ForLocation(menu.Items,"settings"),0,true);_page.Children.Add(status);
        if(App.FrontendBackend&&!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR"))&&Environment.GetCommandLineArgs().Contains("--inspect-plugin-menus"))Action("预览托盘菜单",ShowQuickMenu);
    }
}
