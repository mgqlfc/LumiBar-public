using System.Runtime.InteropServices;
using System.Text;

namespace LumiBar;

internal sealed record InstalledFont(string Family,string DisplayName,string[] Aliases);
internal sealed class SystemFontCatalog
{
    private readonly SemaphoreSlim _gate=new(1,1);private InstalledFont[]? _cache;
    internal async Task<InstalledFont[]> Get(bool refresh=false,CancellationToken ct=default)
    {
        await _gate.WaitAsync(ct);
        try{if(_cache==null||refresh)_cache=await Task.Run(Enumerate,ct);return _cache;}
        finally{_gate.Release();}
    }
    internal async Task<string?> Resolve(string requested,CancellationToken ct=default)
    {
        static InstalledFont? Find(InstalledFont[] fonts,string name)=>fonts.FirstOrDefault(x=>x.Family.Equals(name,StringComparison.OrdinalIgnoreCase)||x.Aliases.Contains(name,StringComparer.OrdinalIgnoreCase));
        var font=Find(await Get(ct:ct),requested.Trim());font??=Find(await Get(true,ct),requested.Trim());return font?.Family;
    }
    private static InstalledFont[] Enumerate()
    {
        var iid=typeof(IDWriteFactory).GUID;Marshal.ThrowExceptionForHR(DWriteCreateFactory(1,ref iid,out var instance));
        var factory=(IDWriteFactory)instance;IDWriteFontCollection? collection=null;
        try
        {
            Marshal.ThrowExceptionForHR(factory.GetSystemFontCollection(out collection,true));var result=new List<InstalledFont>();
            for(uint i=0;i<collection.GetFontFamilyCount();i++)
            {
                IDWriteFontFamily? family=null;IDWriteLocalizedStrings? names=null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.GetFontFamily(i,out family));Marshal.ThrowExceptionForHR(family.GetFamilyNames(out names));
                    var aliases=new List<string>();for(uint n=0;n<names.GetCount();n++)aliases.Add(Read(names,n));
                    if(aliases.Count==0)continue;
                    var canonical=Localized(names,"en-us")??aliases[0];
                    var display=Localized(names,"zh-cn")??Localized(names,"zh-hans")??canonical;
                    result.Add(new(canonical,display,aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
                }
                catch(Exception ex) when(ex is COMException or InvalidDataException){}
                finally{if(names!=null)Marshal.ReleaseComObject(names);if(family!=null)Marshal.ReleaseComObject(family);}
            }
            return result.DistinctBy(x=>x.Family,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x.DisplayName,StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally{if(collection!=null)Marshal.ReleaseComObject(collection);Marshal.ReleaseComObject(factory);}
    }
    private static string? Localized(IDWriteLocalizedStrings names,string locale){Marshal.ThrowExceptionForHR(names.FindLocaleName(locale,out var index,out var exists));return exists?Read(names,index):null;}
    private static string Read(IDWriteLocalizedStrings names,uint index){Marshal.ThrowExceptionForHR(names.GetStringLength(index,out var length));if(length>4096)throw new InvalidDataException("字体名称过长。");var buffer=new StringBuilder((int)length+1);Marshal.ThrowExceptionForHR(names.GetString(index,buffer,length+1));return buffer.ToString();}

    [DllImport("dwrite.dll",ExactSpelling=true)]private static extern int DWriteCreateFactory(int factoryType,ref Guid iid,[MarshalAs(UnmanagedType.IUnknown)]out object factory);
    [ComImport,Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IDWriteFactory
    {
        [PreserveSig]int GetSystemFontCollection(out IDWriteFontCollection collection,[MarshalAs(UnmanagedType.Bool)]bool checkForUpdates);
    }
    [ComImport,Guid("a84cee02-3eea-4eee-a827-87c1a02a0fcc"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IDWriteFontCollection
    {
        [PreserveSig]uint GetFontFamilyCount();
        [PreserveSig]int GetFontFamily(uint index,out IDWriteFontFamily family);
    }
    [ComImport,Guid("da20d8ef-812a-4c43-9802-62ec4abd7add"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IDWriteFontFamily
    {
        [PreserveSig]int GetFontCollection(out nint collection);
        [PreserveSig]uint GetFontCount();
        [PreserveSig]int GetFont(uint index,out nint font);
        [PreserveSig]int GetFamilyNames(out IDWriteLocalizedStrings names);
    }
    [ComImport,Guid("08256209-099a-4b34-b86d-c22b110e7771"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IDWriteLocalizedStrings
    {
        [PreserveSig]uint GetCount();
        [PreserveSig]int FindLocaleName([MarshalAs(UnmanagedType.LPWStr)]string locale,out uint index,[MarshalAs(UnmanagedType.Bool)]out bool exists);
        [PreserveSig]int GetLocaleNameLength(uint index,out uint length);
        [PreserveSig]int GetLocaleName(uint index,[Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,uint size);
        [PreserveSig]int GetStringLength(uint index,out uint length);
        [PreserveSig]int GetString(uint index,[Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,uint size);
    }
}
