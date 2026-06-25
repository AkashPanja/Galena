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
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", false);
                var exists = key?.GetValue("GalenaActionRing") != null;
                Log($"  Registry.Run key exists={exists}");

                try
                {
                    var task = StartupTask.GetAsync("GalenaActionRing").GetAwaiter().GetResult();
                    if (task.State == StartupTaskState.Enabled ||
                        task.State == StartupTaskState.EnabledByPolicy)
                    {
                        Log("  Migrating from old StartupTask to Registry Run key");
                        task.Disable();
                    }
                }
                catch { }

                return exists;
            }
            catch (Exception ex)
            {
                Log($"  Registry check error: {ex.Message}");
                return false;
            }
        }
        var enabled = UnpackagedStartupManager.IsStartupEnabled();
        Log($"  UnpackagedStartupManager.IsStartupEnabled={enabled}");
        return enabled;
    }

    public static Task SetEnabledAsync(bool enable)
    {
        Log($"SetEnabledAsync({enable}) — IsPackaged={IsPackaged}");
        try
        {
            CleanupOldArtifacts();

            if (IsPackaged)
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (enable)
                {
                    key?.SetValue("GalenaActionRing", $"\"{GetProcessPath()}\" --startup");
                    Log("  Registry Run key written");
                }
                else
                {
                    key?.DeleteValue("GalenaActionRing", false);
                    Log("  Registry Run key deleted");
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
        return Task.CompletedTask;
    }

    private static string GetProcessPath()
    {
        if (IsPackaged)
        {
            var pkgPath = Package.Current.InstalledLocation.Path;
            return Path.Combine(pkgPath, "Galena Action Ring.exe");
        }
        return Environment.ProcessPath ?? "";
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
        if (IsPackaged)
        {
            try
            {
                var task = StartupTask.GetAsync("GalenaActionRing").GetAwaiter().GetResult();
                task.Disable();
                Log("  Old StartupTask disabled");
            }
            catch { }
        }
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
