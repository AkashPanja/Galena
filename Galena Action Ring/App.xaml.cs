using Microsoft.UI.Xaml;
using System;

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
        OsdService.Instance.Initialize();
        _window = new MainWindow();
    }
}
