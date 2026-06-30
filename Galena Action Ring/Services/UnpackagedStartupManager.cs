using System;
using System.IO;
using Microsoft.Win32.TaskScheduler;

namespace GalenaActionRing;

public static class UnpackagedStartupManager
{
    private const string TaskName = "GalenaActionRingStartupTask";

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

    public static void ToggleStartup(bool enable)
    {
        try
        {
            using var ts = new TaskService();
            ts.RootFolder.DeleteTask(TaskName, false);

            if (!enable) return;

            var exePath = Environment.ProcessPath;
            if (exePath == null) { Log("ERROR: ProcessPath null"); return; }
            var workingDir = AppDomain.CurrentDomain.BaseDirectory;

            Log($"Creating task: exe={exePath}, dir={workingDir}");

            var td = ts.NewTask();
            td.RegistrationInfo.Description = "Galena Action Ring";
            td.Settings.DisallowStartIfOnBatteries = false;
            td.Settings.StopIfGoingOnBatteries = false;
            td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
            td.Settings.Priority = System.Diagnostics.ProcessPriorityClass.Normal;
            td.Triggers.Add(new LogonTrigger { UserId = Environment.UserName });
            td.Actions.Add(new ExecAction(exePath, "--startup", workingDir));
            ts.RootFolder.RegisterTaskDefinition(TaskName, td);
            Log("Task registered successfully");
        }
        catch (Exception ex) { Log($"ERROR: {ex.GetType().Name}: {ex.Message}"); }
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using var ts = new TaskService();
            var task = ts.GetTask(TaskName);
            return task != null && task.Enabled;
        }
        catch { return false; }
    }
}
