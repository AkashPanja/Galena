using Microsoft.UI.Xaml;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace Galena_Action_Ring;

public class OsdService
{
    private static readonly Lazy<OsdService> _instance = new(() => new OsdService());
    public static OsdService Instance => _instance.Value;

    private OsdWindow? _osdWindow;
    private DispatcherTimer? _timeoutTimer;
    private int _selectedIndex;
    private bool _isVisible;
    private RingNode? _previousFolderNode;
    private List<RingNode>? _parentNodes;
    private bool _inRadialMode;
    private int _radialValue = 50;
    private bool _seekMode;
    private readonly Dictionary<string, bool> _toggleStates = new();

    public RingProfile CurrentProfile { get; internal set; } = new();

    public int SelectedIndex => _selectedIndex;
    public bool IsVisible => _isVisible;

    private OsdService() { }

    public void Initialize()
    {
        var loaded = ProfileService.LoadProfile("Default");
        if (loaded != null)
        {
            CurrentProfile = loaded;
        }
        else
        {
            CurrentProfile = ProfileService.CreateDefault();
            ProfileService.SaveProfile(CurrentProfile);
        }

        _osdWindow = new OsdWindow();
        _osdWindow.ApplyProfileColors(CurrentProfile.PrimaryColor, CurrentProfile.SecondaryColor);
        _osdWindow.LoadNodes(CurrentProfile.Nodes, CurrentProfile.Radius);
        ApplyToggleStatesToOsd();
        NativeMethods.SetWindowSize(_osdWindow);
        NativeMethods.SetWindowPosition(_osdWindow);
        _osdWindow.Activate();
        NativeMethods.HideWindow(_osdWindow);

        _timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _timeoutTimer.Tick += (_, _) => Hide();
    }

    public void ReloadProfile(string profileName = "Default")
    {
        if (_osdWindow == null) return;
        CurrentProfile = ProfileService.LoadProfile(profileName) ?? ProfileService.CreateDefault();
        _osdWindow.ApplyProfileColors(CurrentProfile.PrimaryColor, CurrentProfile.SecondaryColor);
        _osdWindow.LoadNodes(CurrentProfile.Nodes, CurrentProfile.Radius);
        ApplyToggleStatesToOsd();
    }

    public void Show()
    {
        if (_osdWindow == null) return;
        _inRadialMode = false;
        _seekMode = false;
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
        if (_inRadialMode) ExitRadialMode(false);
        _seekMode = false;
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
        if (_seekMode) { keybd_event(0xB2, 0, 0, 0); keybd_event(0xB2, 0, 2, 0); ResetTimeout(); return; }
        if (_inRadialMode) { UpdateRadialValue(1); return; }
        var maxIndex = CurrentProfile.NodeCount;
        _selectedIndex = (_selectedIndex + 1) % (maxIndex + 1);
        _osdWindow?.SelectOption(_selectedIndex);
        ResetTimeout();
    }

    public void SelectPrev()
    {
        if (!_isVisible) return;
        if (_seekMode) { keybd_event(0xB4, 0, 0, 0); keybd_event(0xB4, 0, 2, 0); ResetTimeout(); return; }
        if (_inRadialMode) { UpdateRadialValue(-1); return; }
        var maxIndex = CurrentProfile.NodeCount;
        _selectedIndex = (_selectedIndex + maxIndex) % (maxIndex + 1);
        _osdWindow?.SelectOption(_selectedIndex);
        ResetTimeout();
    }

    public void Click()
    {
        if (!_isVisible)
        {
            if (_previousFolderNode != null)
            {
                CurrentProfile.Nodes = _parentNodes ?? CurrentProfile.Nodes;
                _osdWindow?.LoadNodes(CurrentProfile.Nodes, CurrentProfile.Radius);
                _previousFolderNode = null;
                _parentNodes = null;
                _osdWindow?.SetCenterGlyph("\uE5CD");
            }
            Show();
            return;
        }

        if (_seekMode)
        {
            _seekMode = false;
            return;
        }

        if (_inRadialMode)
        {
            ExitRadialMode(true);
            return;
        }

        if (_selectedIndex == 0)
        {
            if (_previousFolderNode != null)
            {
                ExitSubMenu();
                return;
            }
            Hide();
            return;
        }

        var nodeIndex = _selectedIndex - 1;
        var nodes = CurrentProfile.Nodes;
        if (nodeIndex < 0 || nodeIndex >= nodes.Count) return;

        var node = nodes[nodeIndex];

        // Check category first
        if (node.Category == ActionCategory.Group)
        {
            EnterRadialMode(node);
            ResetTimeout();
            return;
        }

        ExecuteAction(node, nodeIndex);
        ResetTimeout();
    }

    private void ExecuteAction(RingNode node, int nodeIndex = -1)
    {
        switch (node.ActionType)
        {
            case ActionType.LaunchApp:
                if (!string.IsNullOrEmpty(node.ActionData))
                    Process.Start(new ProcessStartInfo(node.ActionData) { UseShellExecute = true });
                break;

            case ActionType.OpenUrl:
                if (!string.IsNullOrEmpty(node.ActionData))
                    Process.Start(new ProcessStartInfo(node.ActionData) { UseShellExecute = true });
                break;

            case ActionType.VolumeUp:
                keybd_event(0xAF, 0, 0, 0);
                keybd_event(0xAF, 0, 2, 0);
                break;

            case ActionType.VolumeDown:
                keybd_event(0xAE, 0, 0, 0);
                keybd_event(0xAE, 0, 2, 0);
                break;

            case ActionType.MuteToggle:
                keybd_event(0xAD, 0, 0, 0);
                keybd_event(0xAD, 0, 2, 0);
                ToggleNodeState(nodeIndex);
                break;

            case ActionType.BrightnessUp:
            case ActionType.BrightnessDown:
                SetBrightness(node.ActionType == ActionType.BrightnessUp);
                break;

            case ActionType.MediaPlayPause:
                keybd_event(0xB3, 0, 0, 0);
                keybd_event(0xB3, 0, 2, 0);
                ToggleNodeState(nodeIndex);
                break;

            case ActionType.MediaNext:
                keybd_event(0xB0, 0, 0, 0);
                keybd_event(0xB0, 0, 2, 0);
                break;

            case ActionType.MediaPrevious:
                keybd_event(0xB1, 0, 0, 0);
                keybd_event(0xB1, 0, 2, 0);
                break;

            case ActionType.MediaSeekForward:
            case ActionType.MediaSeekBackward:
                _seekMode = true;
                break;

            case ActionType.ToggleNightLight:
                Process.Start(new ProcessStartInfo("ms-settings:nightlight") { UseShellExecute = true });
                break;

            case ActionType.Folder:
                if (node.Children != null && node.Children.Count > 0)
                    EnterSubMenu(node);
                break;

            case ActionType.TextExpansion:
                if (!string.IsNullOrEmpty(node.ActionData))
                {
                    Windows.ApplicationModel.DataTransfer.DataPackage dp = new();
                    dp.SetText(node.ActionData);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                    // Simulate Ctrl+V
                    keybd_event(0x11, 0, 0, 0);
                    keybd_event(0x56, 0, 0, 0);
                    keybd_event(0x56, 0, 2, 0);
                    keybd_event(0x11, 0, 2, 0);
                }
                break;
        }
    }

    private void EnterSubMenu(RingNode folderNode)
    {
        _parentNodes = new List<RingNode>(CurrentProfile.Nodes);
        _previousFolderNode = folderNode;
        _selectedIndex = 0;

        Hide();
        CurrentProfile.Nodes = folderNode.Children!;
        _osdWindow?.LoadNodes(CurrentProfile.Nodes, CurrentProfile.Radius);
        _osdWindow?.SetCenterGlyph("\uE5C4");
        Show();
    }

    private void ExitSubMenu()
    {
        if (_parentNodes != null)
            CurrentProfile.Nodes = _parentNodes;
        _previousFolderNode = null;
        _parentNodes = null;
        _selectedIndex = 0;

        Hide();
        _osdWindow?.LoadNodes(CurrentProfile.Nodes, CurrentProfile.Radius);
        _osdWindow?.SetCenterGlyph("\uE5CD");
        Show();
    }

    private void EnterRadialMode(RingNode node)
    {
        int initialPercent = 50;
        if (node.ActionType == ActionType.VolumeControl)
            initialPercent = GetSystemVolume();
        else if (node.ActionType == ActionType.BrightnessControl)
            initialPercent = GetSystemBrightness();

        _radialValue = initialPercent;
        _osdWindow?.ShowRadialProgress(node.Label, initialPercent);
        _inRadialMode = true;
    }

    public void UpdateRadialValue(int delta)
    {
        if (!_inRadialMode) return;

        _radialValue = Math.Clamp(_radialValue + delta, 0, 100);
        _osdWindow?.UpdateRadialPercent(_radialValue);
        ResetTimeout();

        if (CurrentProfile.Nodes.Count > 0 && _selectedIndex > 0)
        {
            var node = CurrentProfile.Nodes[_selectedIndex - 1];
            if (node.ActionType == ActionType.VolumeControl)
                AudioVolumeControl.SetVolume(_radialValue);
            else if (node.ActionType == ActionType.BrightnessControl)
                SetSystemBrightness(_radialValue);
        }
    }

    public void ExitRadialMode(bool applyValue = true)
    {
        if (!_inRadialMode) return;
        _inRadialMode = false;
        _osdWindow?.HideRadialProgress();

        // Final value is already applied via UpdateRadialValue in real-time
        _radialValue = 50;
    }



    private static int GetSystemVolume()
    {
        return AudioVolumeControl.GetVolume();
    }

    private static int GetSystemBrightness()
    {
        try
        {
            var scope = new ManagementScope(@"root\wmi");
            scope.Connect();
            using var mos = new ManagementObjectSearcher(
                scope, new SelectQuery("SELECT * FROM WmiMonitorBrightness"));
            foreach (ManagementBaseObject mbo in mos.Get())
            {
                using var mo = (ManagementObject)mbo;
                return Convert.ToInt32(mo["CurrentBrightness"]);
            }
        }
        catch { }
        return 50;
    }

    private static void SetSystemBrightness(int percent)
    {
        try
        {
            var scope = new ManagementScope(@"root\wmi");
            scope.Connect();
            using var mos = new ManagementObjectSearcher(
                scope, new SelectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));
            foreach (ManagementBaseObject mbo in mos.Get())
            {
                using var mo = (ManagementObject)mbo;
                var inParams = mo.GetMethodParameters("WmiSetBrightness");
                if (inParams != null)
                {
                    inParams["Brightness"] = (uint)percent;
                    inParams["Timeout"] = 0u;
                    mo.InvokeMethod("WmiSetBrightness", inParams, null);
                }
            }
        }
        catch { }
    }

    private static void SetBrightness(bool increase)
    {
        try
        {
            var scope = new ManagementScope(@"root\wmi");
            scope.Connect();
            using var mos = new ManagementObjectSearcher(
                scope,
                new SelectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));
            foreach (ManagementBaseObject mbo in mos.Get())
            {
                using var mo = (ManagementObject)mbo;
                var inParams = mo.GetMethodParameters("WmiSetBrightness");
                if (inParams != null)
                {
                    inParams["Brightness"] = increase ? 100u : 0u;
                    inParams["Timeout"] = 0u;
                    mo.InvokeMethod("WmiSetBrightness", inParams, null);
                }
            }
        }
        catch { }
    }

    private void ResetTimeout()
    {
        _timeoutTimer?.Stop();
        _timeoutTimer?.Start();
    }

    private void ApplyToggleStatesToOsd()
    {
        if (_osdWindow == null) return;
        for (int i = 0; i < CurrentProfile.Nodes.Count; i++)
        {
            var node = CurrentProfile.Nodes[i];
            var key = $"{CurrentProfile.Name}:{i}";
            if (_toggleStates.TryGetValue(key, out var isOn) && isOn)
            {
                var onGlyph = node.ActionType == ActionType.MuteToggle ? "\uE04F" : "\uE034";
                _osdWindow.UpdateNodeIcon(i, onGlyph);
            }
        }
    }

    private void ToggleNodeState(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= CurrentProfile.Nodes.Count) return;
        var node = CurrentProfile.Nodes[nodeIndex];
        var key = $"{CurrentProfile.Name}:{nodeIndex}";
        var isOn = _toggleStates.GetValueOrDefault(key);
        _toggleStates[key] = !isOn;

        // Update OSD icon to reflect toggle state
        var onGlyph = node.ActionType == ActionType.MuteToggle ? "\uE04F" : "\uE034";
        var offGlyph = node.ActionType == ActionType.MuteToggle ? "\uE04E" : "\uE037";
        var glyph = !isOn ? onGlyph : offGlyph;
        _osdWindow?.UpdateNodeIcon(nodeIndex, glyph);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 2;
}
