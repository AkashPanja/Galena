using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GalenaActionRing;

internal static class BootstrapInitializer
{
    private const uint BOOTSTRAP_MAJOR_MINOR = 0x00020002;

    [DllImport("Microsoft.WindowsAppRuntime.Bootstrap.dll", CharSet = CharSet.Unicode)]
    private static extern int MddBootstrapInitialize2(uint majorMinorVersion, string versionTag, ulong minVersion);

    [ModuleInitializer]
    internal static void InitializeBootstrapper()
    {
        try
        {
            MddBootstrapInitialize2(BOOTSTRAP_MAJOR_MINOR, "", 0);
        }
        catch (DllNotFoundException)
        {
        }
        catch
        {
        }
    }
}
