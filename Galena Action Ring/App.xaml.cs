using Microsoft.UI.Xaml;
using System;

using Microsoft.Win32;

namespace Galena_Action_Ring;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

        OsdService.Instance.Initialize();
        _window = new MainWindow();
        NativeMethods.HideWindow(_window);
    }

    public static string? GetSetting(string key)
    {
        try
        {
            using var reg = Registry.CurrentUser.OpenSubKey(@"Software\GalenaActionRing", false);
            return reg?.GetValue(key) as string;
        }
        catch { return null; }
    }

    public static void SetSetting(string key, string? value)
    {
        try
        {
            using var reg = Registry.CurrentUser.CreateSubKey(@"Software\GalenaActionRing");
            if (value != null)
                reg?.SetValue(key, value);
            else
                reg?.DeleteValue(key, false);
        }
        catch { }
    }
}
