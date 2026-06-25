using Microsoft.UI.Xaml;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Media.Control;

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
        InitPlayPauseIcons();
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
        InitPlayPauseIcons();
        ApplyToggleStatesToOsd();
    }

    public void Show()
    {
        if (_osdWindow == null) return;
        _inRadialMode = false;
        if (_seekMode) { _seekMode = false; _osdWindow?.StopSeekLoop(); }
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
        if (_seekMode) { _seekMode = false; _osdWindow?.StopSeekLoop(); }
        _osdWindow?.HideSubMenuBg();
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
        if (_seekMode) { _ = SeekMediaAsync(true); _osdWindow?.ShowSeekIndicator(true); ResetTimeout(); return; }
        if (_inRadialMode) { UpdateRadialValue(1); return; }
        var maxIndex = CurrentProfile.NodeCount;
        _selectedIndex = (_selectedIndex + 1) % (maxIndex + 1);
        _osdWindow?.SelectOption(_selectedIndex);
        ResetTimeout();
    }

    public void SelectPrev()
    {
        if (!_isVisible) return;
        if (_seekMode) { _ = SeekMediaAsync(false); _osdWindow?.ShowSeekIndicator(false); ResetTimeout(); return; }
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
                InitPlayPauseIcons();
                _previousFolderNode = null;
                _parentNodes = null;
                _osdWindow?.SetCenterGlyph("\uE5CD");
                _osdWindow?.HideSubMenuBg();
                ApplyToggleStatesToOsd();
            }
            Show();
            return;
        }

        if (_seekMode)
        {
            _seekMode = false;
            _osdWindow?.StopSeekLoop();
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

    private async void ExecuteAction(RingNode node, int nodeIndex = -1)
    {
        try
        {
            switch (node.ActionType)
            {
                case ActionType.LaunchApp:
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
                    AudioVolumeControl.SetMute(!AudioVolumeControl.GetMute());
                    UpdateToggleIcon(nodeIndex, node);
                    break;

                case ActionType.BrightnessUp:
                case ActionType.BrightnessDown:
                    SetBrightness(node.ActionType == ActionType.BrightnessUp);
                    break;

                case ActionType.MediaPlayPause:
                    var playStatus = await GetMediaStatusAsync();
                    if (playStatus != null)
                    {
                        keybd_event(0xB3, 0, 0, 0);
                        keybd_event(0xB3, 0, 2, 0);
                        // Toggle icon predictively — media app hasn't processed the key yet
                        var nextGlyph = playStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                            ? "\uE037" : "\uE034";
                        _osdWindow?.UpdateNodeIcon(nodeIndex, nextGlyph);
                    }
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
        catch { }
    }

    private void EnterSubMenu(RingNode folderNode)
    {
        _parentNodes = new List<RingNode>(CurrentProfile.Nodes);
        _previousFolderNode = folderNode;
        _selectedIndex = 0;

        Hide();
        CurrentProfile.Nodes = folderNode.Children!;
        _osdWindow?.LoadNodes(CurrentProfile.Nodes, CurrentProfile.Radius);
        InitPlayPauseIcons();
        _osdWindow?.SetCenterGlyph("\uE5C4");
        _osdWindow?.ShowSubMenuBg();
        ApplyToggleStatesToOsd();
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
        InitPlayPauseIcons();
        _osdWindow?.SetCenterGlyph("\uE5CD");
        _osdWindow?.HideSubMenuBg();
        ApplyToggleStatesToOsd();
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

    private void InitPlayPauseIcons()
    {
        if (_osdWindow == null) return;
        for (int i = 0; i < CurrentProfile.Nodes.Count; i++)
        {
            if (CurrentProfile.Nodes[i].ActionType == ActionType.MediaPlayPause)
                _osdWindow.UpdateNodeIcon(i, "\uEF6A");
        }
    }

    private static async Task<GlobalSystemMediaTransportControlsSessionPlaybackStatus?> GetMediaStatusAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session == null) return null;
            var info = session.GetPlaybackInfo();
            return info?.PlaybackStatus;
        }
        catch { return null; }
    }

    private async void ApplyToggleStatesToOsd()
    {
        if (_osdWindow == null) return;
        for (int i = 0; i < CurrentProfile.Nodes.Count; i++)
        {
            var node = CurrentProfile.Nodes[i];
            var key = $"{CurrentProfile.Name}:{i}";
            if (node.ActionType == ActionType.MuteToggle)
            {
                var muted = AudioVolumeControl.GetMute();
                _toggleStates[key] = muted;
                var glyph = muted ? "\uE04F" : "\uE050";
                _osdWindow.UpdateNodeIcon(i, glyph);
            }
            else if (node.ActionType == ActionType.MediaPlayPause)
            {
                var status = await GetMediaStatusAsync();
                string glyph;
                if (status == null)
                    glyph = "\uEF6A";
                else if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    glyph = "\uE034";
                else
                    glyph = "\uE037";
                _osdWindow.UpdateNodeIcon(i, glyph);
            }
            else if (_toggleStates.TryGetValue(key, out var isOn) && isOn)
            {
                var onGlyph = "\uE034";
                _osdWindow.UpdateNodeIcon(i, onGlyph);
            }
        }
    }

    private async void UpdateMediaPlayPauseIcon(int nodeIndex)
    {
        var status = await GetMediaStatusAsync();
        string glyph;
        if (status == null)
            glyph = "\uEF6A";
        else if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            glyph = "\uE034";
        else
            glyph = "\uE037";
        _osdWindow?.UpdateNodeIcon(nodeIndex, glyph);
    }

    private void UpdateToggleIcon(int nodeIndex, RingNode node)
    {
        if (nodeIndex < 0 || nodeIndex >= CurrentProfile.Nodes.Count) return;
        var key = $"{CurrentProfile.Name}:{nodeIndex}";
        if (node.ActionType == ActionType.MuteToggle)
        {
            var muted = AudioVolumeControl.GetMute();
            _toggleStates[key] = muted;
            var glyph = muted ? "\uE04F" : "\uE050";
            _osdWindow?.UpdateNodeIcon(nodeIndex, glyph);
        }
        else if (node.ActionType == ActionType.MediaPlayPause)
        {
            UpdateMediaPlayPauseIcon(nodeIndex);
        }
        else
        {
            var isOn = _toggleStates.GetValueOrDefault(key);
            _toggleStates[key] = !isOn;
            var glyph = !isOn ? "\uE034" : "\uE037";
            _osdWindow?.UpdateNodeIcon(nodeIndex, glyph);
        }
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 2;

    private static async Task SeekMediaAsync(bool forward)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session == null) return;
            var timeline = session.GetTimelineProperties();
            if (timeline == null) return;
            var delta = TimeSpan.FromSeconds(5);
            var newPos = forward
                ? (timeline.Position + delta > timeline.EndTime ? timeline.EndTime : timeline.Position + delta)
                : (timeline.Position - delta < TimeSpan.Zero ? TimeSpan.Zero : timeline.Position - delta);
            await session.TryChangePlaybackPositionAsync(newPos.Ticks);
        }
        catch { }
    }
}
