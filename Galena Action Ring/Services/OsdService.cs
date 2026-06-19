using Microsoft.UI.Xaml;
using System;

namespace Galena_Action_Ring;

public class OsdService
{
    private static readonly Lazy<OsdService> _instance = new(() => new OsdService());
    public static OsdService Instance => _instance.Value;

    private OsdWindow? _osdWindow;
    private DispatcherTimer? _timeoutTimer;
    private int _selectedIndex;
    private bool _isVisible;

    public RingProfile CurrentProfile { get; internal set; } = new();

    public int SelectedIndex => _selectedIndex;
    public bool IsVisible => _isVisible;

    private OsdService() { }

    public void Initialize()
    {
        CurrentProfile = ProfileService.LoadProfile("Default") ?? ProfileService.CreateDefault();
        if (ProfileService.LoadProfile("Default") == null)
            ProfileService.SaveProfile(CurrentProfile);

        _osdWindow = new OsdWindow();
        _osdWindow.LoadNodes(CurrentProfile.Nodes, CurrentProfile.Radius);
        NativeMethods.SetWindowSize(_osdWindow);
        NativeMethods.SetWindowPosition(_osdWindow);
        _osdWindow.Activate();
        NativeMethods.HideWindow(_osdWindow);

        _timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timeoutTimer.Tick += (_, _) => Hide();
    }

    public void ReloadProfile(string profileName = "Default")
    {
        if (_osdWindow == null) return;
        CurrentProfile = ProfileService.LoadProfile(profileName) ?? ProfileService.CreateDefault();
        _osdWindow.LoadNodes(CurrentProfile.Nodes, CurrentProfile.Radius);
    }

    public void Show()
    {
        if (_osdWindow == null) return;
        NativeMethods.ShowTopmost(_osdWindow);
        _isVisible = true;
        _selectedIndex = 0;
        _osdWindow.SelectOption(0);
        _osdWindow.ShowMenu();
        ResetTimeout();
    }

    public void Hide()
    {
        if (_osdWindow == null || !_isVisible) return;
        _isVisible = false;
        _timeoutTimer?.Stop();
        _osdWindow.HideMenu(() =>
        {
            NativeMethods.HideWindow(_osdWindow);
        });
    }

    public void SelectNext()
    {
        if (!_isVisible) return;
        var maxIndex = CurrentProfile.NodeCount;
        _selectedIndex = (_selectedIndex + 1) % (maxIndex + 1);
        _osdWindow?.SelectOption(_selectedIndex);
        ResetTimeout();
    }

    public void SelectPrev()
    {
        if (!_isVisible) return;
        var maxIndex = CurrentProfile.NodeCount;
        _selectedIndex = (_selectedIndex + maxIndex) % (maxIndex + 1);
        _osdWindow?.SelectOption(_selectedIndex);
        ResetTimeout();
    }

    public void Click()
    {
        if (!_isVisible)
        {
            Show();
            return;
        }

        if (_selectedIndex == 0)
            Hide();
        else
            ResetTimeout();
    }

    private void ResetTimeout()
    {
        _timeoutTimer?.Stop();
        _timeoutTimer?.Start();
    }
}
