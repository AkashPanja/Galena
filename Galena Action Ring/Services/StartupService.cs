using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Galena_Action_Ring;

public static class StartupService
{
    private const string SentinelPath = @"Software\GalenaActionRing";
    private const string SentinelValue = "FirstRunDone";

    private static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GalenaActionRing", "startup.log");

    private static void Log(string msg)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    public static bool IsPackaged
    {
        get
        {
            try { return Windows.ApplicationModel.Package.Current != null; }
            catch { return false; }
        }
    }

    public static bool IsEnabled()
    {
        Log($"IsEnabled() — IsPackaged={IsPackaged}");
        if (IsPackaged)
        {
            try
            {
                var task = StartupTask.GetAsync("GalenaActionRing").GetAwaiter().GetResult();
                Log($"  StartupTask.State={task.State}");
                return task.State == StartupTaskState.Enabled ||
                       task.State == StartupTaskState.EnabledByPolicy;
            }
            catch (Exception ex)
            {
                Log($"  StartupTask error: {ex.Message}");
                return UnpackagedStartupManager.IsStartupEnabled();
            }
        }
        var enabled = UnpackagedStartupManager.IsStartupEnabled();
        Log($"  UnpackagedStartupManager.IsStartupEnabled={enabled}");
        return enabled;
    }

    public static async Task SetEnabledAsync(bool enable)
    {
        Log($"SetEnabledAsync({enable}) — IsPackaged={IsPackaged}");
        try
        {
            CleanupOldArtifacts();

            if (IsPackaged)
            {
                var task = await StartupTask.GetAsync("GalenaActionRing");
                if (enable)
                {
                    var result = await task.RequestEnableAsync();
                    Log($"  RequestEnableAsync() returned: {result}");
                    Log($"  NOTE: If app doesn't start at login, switch VS profile to 'Galena Action Ring (Unpackaged)' and retry");
                }
                else
                {
                    task.Disable();
                    Log("  Disable() called");
                }
            }
            else
            {
                UnpackagedStartupManager.ToggleStartup(enable);
                Log("  UnpackagedStartupManager.ToggleStartup completed");
            }
        }
        catch (Exception ex)
        {
            Log($"  ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void CleanupOldArtifacts()
    {
        try
        {
            var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            foreach (var f in new[] { "Galena Action Ring.lnk", "GalenaActionRing.vbs" })
            {
                var p = Path.Combine(startup, f);
                if (File.Exists(p)) File.Delete(p);
            }
        }
        catch { }
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            k?.DeleteValue("GalenaActionRing", false);
            k?.DeleteValue("GalenaActionRingStartupTask", false);
        }
        catch { }
    }

    public static bool IsFirstRunDone()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SentinelPath, false);
            return key?.GetValue(SentinelValue) != null;
        }
        catch { return false; }
    }

    public static void MarkFirstRunDone()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SentinelPath);
            key?.SetValue(SentinelValue, 1);
        }
        catch { }
    }
}
