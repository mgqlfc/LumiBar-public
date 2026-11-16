using System;
using System.Runtime.InteropServices;

namespace AntigravityFrontend.Services;

/// <summary>
/// Lightweight Windows CoreAudio endpoint volume helper for the music flyout volume slider and mute button.
/// </summary>
public static class AudioVolumeHelper
{
    private static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidIAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public static (bool Available, double Volume01, bool Muted) GetMasterVolume()
    {
        try
        {
            var ep = GetEndpointVolume();
            if (ep == null) return (false, 0, false);
            try
            {
                Marshal.ThrowExceptionForHR(ep.GetMasterVolumeLevelScalar(out float level));
                Marshal.ThrowExceptionForHR(ep.GetMute(out bool muted));
                return (true, Math.Clamp(level, 0f, 1f), muted);
            }
            finally
            {
                Marshal.ReleaseComObject(ep);
            }
        }
        catch
        {
            return (false, 0, false);
        }
    }

    public static bool SetMasterVolume(double volume01)
    {
        if(!double.IsFinite(volume01)||volume01<0||volume01>1)return false;
        if(Environment.GetEnvironmentVariable("LUMIBAR_AUDIO_DRY_RUN")=="1"){App.LogStartup($"AUDIO_WRITE_BLOCKED volume={volume01:R}");return false;}
        try
        {
            var ep = GetEndpointVolume();
            if (ep == null) return false;
            try
            {
                var ctx = Guid.Empty;
                Marshal.ThrowExceptionForHR(ep.SetMasterVolumeLevelScalar((float)Math.Clamp(volume01, 0, 1), ref ctx));
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(ep);
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool SetMute(bool mute)
    {
        if(Environment.GetEnvironmentVariable("LUMIBAR_AUDIO_DRY_RUN")=="1"){App.LogStartup($"AUDIO_WRITE_BLOCKED mute={mute}");return false;}
        try
        {
            var ep = GetEndpointVolume();
            if (ep == null) return false;
            try
            {
                var ctx = Guid.Empty;
                Marshal.ThrowExceptionForHR(ep.SetMute(mute, ref ctx));
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(ep);
            }
        }
        catch
        {
            return false;
        }
    }

    private static IAudioEndpointVolume? GetEndpointVolume()
    {
        var enumeratorType = Type.GetTypeFromCLSID(ClsidMmDeviceEnumerator);
        if (enumeratorType == null) return null;
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device));
            try
            {
                var iid = IidIAudioEndpointVolume;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var obj));
                return (IAudioEndpointVolume)obj;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }
}
