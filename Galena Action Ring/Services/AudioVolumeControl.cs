using System;
using System.Runtime.InteropServices;

namespace Galena_Action_Ring;

public static class AudioVolumeControl
{
    private static IMMDeviceEnumerator? _enumerator;

    private static IAudioEndpointVolume? GetEndpoint()
    {
        try
        {
            _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();

            var iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
            int hr = _enumerator.GetDefaultAudioEndpoint(0, 0, out IntPtr devicePtr);
            Marshal.ThrowExceptionForHR(hr);

            var device = (IMMDevice)Marshal.GetObjectForIUnknown(devicePtr);
            hr = device.Activate(ref iid, 0x17, IntPtr.Zero, out IntPtr epvPtr);
            Marshal.Release(devicePtr);
            Marshal.ThrowExceptionForHR(hr);

            var epv = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(epvPtr);
            Marshal.Release(epvPtr);
            return epv;
        }
        catch { return null; }
    }

    public static int GetVolume()
    {
        var epv = GetEndpoint();
        if (epv == null) return 50;
        try
        {
            int hr = epv.GetMasterVolumeLevelScalar(out float level);
            if (hr < 0) return 50;
            return (int)Math.Round(level * 100);
        }
        catch { return 50; }
        finally { Marshal.ReleaseComObject(epv); }
    }

    public static void SetVolume(int percent)
    {
        var epv = GetEndpoint();
        if (epv == null) return;
        try
        {
            float level = Math.Clamp(percent, 0, 100) / 100f;
            epv.SetMasterVolumeLevelScalar(level, IntPtr.Zero);
        }
        catch { }
        finally { Marshal.ReleaseComObject(epv); }
    }

    public static bool GetMute()
    {
        var epv = GetEndpoint();
        if (epv == null) return false;
        try
        {
            int hr = epv.GetMute(out int mute);
            if (hr < 0) return false;
            return mute != 0;
        }
        catch { return false; }
        finally { Marshal.ReleaseComObject(epv); }
    }

    public static void SetMute(bool mute)
    {
        var epv = GetEndpoint();
        if (epv == null) return;
        try
        {
            epv.SetMute(mute ? 1 : 0, IntPtr.Zero);
        }
        catch { }
        finally { Marshal.ReleaseComObject(epv); }
    }

    public static void Cleanup()
    {
        if (_enumerator != null)
        {
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr ptr);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
    [PreserveSig] int GetChannelCount(out uint pnChannelCount);
    [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, IntPtr pguidEventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, IntPtr pguidEventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
    [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, IntPtr pguidEventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, IntPtr pguidEventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
    [PreserveSig] int SetMute(int bMute, IntPtr pguidEventContext);
    [PreserveSig] int GetMute(out int pbMute);
    [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
    [PreserveSig] int VolumeStepUp(IntPtr pguidEventContext);
    [PreserveSig] int VolumeStepDown(IntPtr pguidEventContext);
    [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
    [PreserveSig] int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
}
