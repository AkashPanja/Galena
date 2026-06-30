using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GalenaActionRing;

internal class HidService : IDisposable
{
    private IntPtr _hidReadHandle;
    private IntPtr _hidWriteHandle;
    private CancellationTokenSource? _hidReadCts;
    private string? _connectedDeviceId;
    private DeviceWatcher? _deviceWatcher;
    private readonly StringBuilder _debugLog = new();
    private readonly ObservableCollection<DeviceEntry> _deviceEntries = new();
    private const int MaxLogLines = 1000;
    private int _logLineCount;

    public volatile int LastBrightness = 100;
    public volatile bool LightBarOn = true;

    public ObservableCollection<DeviceEntry> DeviceEntries => _deviceEntries;
    public bool IsConnected => NativeMethods.IsValidHandle(_hidReadHandle) || NativeMethods.IsValidHandle(_hidWriteHandle);
    public string? ConnectedDeviceId => _connectedDeviceId;

    public event Action<string>? DebugMessage;
    public event Action<int>? BrightnessReceived;
    public event Action<int>? EncoderReceived;
    public event Action<int>? ButtonReceived;
    public event Action<int>? OsdAckReceived;
    public event Action<int>? LightStateReceived;
    public event Action? DevicesChanged;
    public event Action<bool>? ConnectionChanged;

    public async Task FindHidDevicesAsync()
    {
        try
        {
            _deviceWatcher?.Stop();
            _deviceEntries.Clear();

            var selector = HidDevice.GetDeviceSelector(0xFF00, 0x01);
            var devices = await DeviceInformation.FindAllAsync(selector);
            var savedId = App.GetSetting("HidDeviceId");
            foreach (var d in devices.Where(x => x.Name.Contains("Galena")))
            {
                var entry = new DeviceEntry { Id = d.Id, DisplayName = d.Name };
                _deviceEntries.Add(entry);
                if (!string.IsNullOrEmpty(savedId) && d.Id == savedId)
                    ConnectToHidDevice(d.Id);
            }

            DevicesChanged?.Invoke();

            _deviceWatcher = DeviceInformation.CreateWatcher(selector);
            _deviceWatcher.Added += (_, info) =>
            {
                if (info.Name.Contains("Galena"))
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(async () =>
                    {
                        if (_deviceEntries.All(x => x.Id != info.Id))
                        {
                            _deviceEntries.Add(new DeviceEntry { Id = info.Id, DisplayName = info.Name });
                            DevicesChanged?.Invoke();
                        }
                        if (!NativeMethods.IsValidHandle(_hidReadHandle))
                        {
                            await Task.Delay(300);
                            ConnectToHidDevice(info.Id);
                        }
                    });
            };
            _deviceWatcher.Removed += (_, info) =>
            {
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                {
                    var entry = _deviceEntries.FirstOrDefault(x => x.Id == info.Id);
                    if (entry != null) _deviceEntries.Remove(entry);
                    if (info.Id == _connectedDeviceId) DisconnectHidDevice();
                    DevicesChanged?.Invoke();
                });
            };
            _deviceWatcher.Start();
        }
        catch (Exception ex) { Debug.WriteLine($"[HidService] FindHidDevicesAsync failed: {ex.Message}"); }
    }

    public void ConnectToHidDevice(string deviceId)
    {
        try
        {
            DisconnectHidDevice();

            _hidReadHandle = NativeMethods.OpenHidDeviceRead(deviceId, out int readErr);
            _hidWriteHandle = NativeMethods.OpenHidDeviceWrite(deviceId, out int writeErr);

            if (!NativeMethods.IsValidHandle(_hidReadHandle) && !NativeMethods.IsValidHandle(_hidWriteHandle))
            {
                _hidReadHandle = NativeMethods.OpenHidDeviceReadWrite(deviceId, out int rwErr);
                _hidWriteHandle = _hidReadHandle;
            }

            if (!NativeMethods.IsValidHandle(_hidReadHandle) && !NativeMethods.IsValidHandle(_hidWriteHandle))
            {
                EmitDebug($"HID open failed (readErr={readErr}, writeErr={writeErr})");
                return;
            }

            _connectedDeviceId = deviceId;
            App.SetSetting("HidDeviceId", deviceId);
            EmitDebug("Connected");

            foreach (var e in _deviceEntries) e.IsConnected = (e.Id == deviceId);
            DevicesChanged?.Invoke();

            if (NativeMethods.IsValidHandle(_hidReadHandle))
            {
                _hidReadCts = new CancellationTokenSource();
                _ = Task.Run(() => HidReadLoop(_hidReadHandle, _hidReadCts.Token));
            }

            ConnectionChanged?.Invoke(true);
        }
        catch (Exception ex)
        {
            EmitDebug($"Exception connecting to HID device: {ex.Message}");
        }
    }

    public void DisconnectHidDevice()
    {
        try
        {
            _hidReadCts?.Cancel();
            NativeMethods.CancelRead(_hidReadHandle);
            if (_hidReadHandle == _hidWriteHandle)
            {
                NativeMethods.CloseHidDevice(_hidReadHandle);
            }
            else
            {
                NativeMethods.CloseHidDevice(_hidReadHandle);
                NativeMethods.CloseHidDevice(_hidWriteHandle);
            }
            _hidReadHandle = IntPtr.Zero;
            _hidWriteHandle = IntPtr.Zero;
            _connectedDeviceId = null;
        }
        catch (Exception ex) { Debug.WriteLine($"[HidService] Disconnect error: {ex.Message}"); }
        EmitDebug("Disconnected");

        foreach (var e in _deviceEntries) e.IsConnected = false;
        DevicesChanged?.Invoke();
        ConnectionChanged?.Invoke(false);
    }

    public void SendOsdState(byte osdByte)
    {
        try
        {
            if (!NativeMethods.IsValidHandle(_hidWriteHandle)) return;
            bool ok = NativeMethods.SendHidFeatureReport(_hidWriteHandle, 0, new byte[] { osdByte });
            EmitDebug(ok ? $"OSD sent byte={osdByte}" : $"OSD send FAILED (Win32 err={Marshal.GetLastWin32Error()})");
        }
        catch (Exception ex) { EmitDebug($"OSD send FAILED: {ex.Message}"); }
    }

    private void HidReadLoop(IntPtr handle, CancellationToken ct)
    {
        ushort inputLen = NativeMethods.GetInputReportByteLength(handle);
        if (inputLen == 0) inputLen = 9;
        var buf = new byte[inputLen];

        while (!ct.IsCancellationRequested)
        {
            try
            {
                bool ok = NativeMethods.ReadInputReport(handle, buf, (uint)inputLen, out uint bytesRead);
                if (!ok || bytesRead < 3)
                {
                    Thread.Sleep(50);
                    continue;
                }

                var eventType = buf[1];
                var eventValue = (sbyte)buf[2];

                ProcessHidEvent(eventType, eventValue);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HidService] ReadLoop error: {ex.Message}");
                Thread.Sleep(200);
            }
        }
    }

    private void ProcessHidEvent(byte eventType, sbyte eventValue)
    {
        switch (eventType)
        {
            case 1:
                LastBrightness = eventValue;
                EmitDebug($"BRIGHTNESS {eventValue}%");
                BrightnessReceived?.Invoke(eventValue);
                break;
            case 2:
                EmitDebug(eventValue > 0 ? "ENCODER +1" : "ENCODER -1");
                EncoderReceived?.Invoke(eventValue);
                break;
            case 3:
                var label = eventValue switch { 0 => "tap", 1 => "press", 2 => "hold", _ => eventValue.ToString() };
                EmitDebug($"BUTTON {label}");
                ButtonReceived?.Invoke(eventValue);
                break;
            case 4:
                EmitDebug("OSD REQUEST");
                SendOsdState((byte)(OsdService.Instance.IsVisible ? 1 : 0));
                break;
            case 5:
                EmitDebug($"OSD ACK state={eventValue}");
                OsdAckReceived?.Invoke(eventValue);
                break;
            case 6:
                LightBarOn = eventValue == 1;
                EmitDebug(LightBarOn ? "LIGHT ON" : "LIGHT OFF");
                LightStateReceived?.Invoke(eventValue);
                break;
        }
    }

    private void EmitDebug(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        _debugLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
        _logLineCount++;
        if (_logLineCount > MaxLogLines)
        {
            int trimIdx = _debugLog.ToString().IndexOf('\n');
            if (trimIdx > 0)
            {
                _debugLog.Remove(0, trimIdx + 1);
                _logLineCount--;
            }
        }
        DebugMessage?.Invoke(msg);
    }

    public void Dispose()
    {
        _deviceWatcher?.Stop();
        _deviceWatcher = null;
        DisconnectHidDevice();
    }
}
