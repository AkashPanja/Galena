using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Storage;
using Windows.UI;

namespace Galena_Action_Ring
{
    public sealed partial class MainWindow : Window
    {
        private readonly TrayService _trayService = new();
        private SerialPort? _serialPort;
        private DispatcherTimer? _pollTimer;
        private readonly StringBuilder _debugLog = new();
        private bool _isConnected;
        private bool _manualDisconnect;

        private readonly ObservableCollection<RingNode> _configNodes = new();
        private bool _isEditing; // prevent re-entrant handler during editor updates

        public ObservableCollection<DeviceItem> DeviceItems { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            DeviceListControl.ItemsSource = DeviceItems;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            NavView.SelectedItem = NavBluetooth;

            _trayService.ShowRequested += () =>
            {
                NativeMethods.ShowTopmost(this);
            };
            _trayService.HideRequested += () =>
            {
                NativeMethods.HideWindow(this);
            };
            _trayService.CloseRequested += async () =>
            {
                await ShowCloseDialog();
            };
            _trayService.ExitRequested += () =>
            {
                _trayService.Dispose();
                Application.Current.Exit();
            };

            _trayService.Setup(this);

            NodeActionTypeBox.ItemsSource = Enum.GetValues<ActionType>();
            NodeListBox.ItemsSource = _configNodes;
            LoadConfigFromProfile(OsdService.Instance.CurrentProfile);

            _ = RefreshDeviceListAsync();
            StartPollTimer();
        }

        public void HideToTray()
        {
            NativeMethods.HideWindow(this);
        }

        private async Task ShowCloseDialog()
        {
            var dialog = new ContentDialog
            {
                Title = "Galena Action Ring",
                Content = "Minimize to system tray or exit?",
                PrimaryButtonText = "Minimize to Tray",
                SecondaryButtonText = "Exit",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = AppRoot.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                HideToTray();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                _trayService.Dispose();
                Application.Current.Exit();
            }
        }

        private async Task RefreshDeviceListAsync()
        {
            DeviceItems.Clear();

            var portNames = SerialPort.GetPortNames();

            var friendlyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var deviceTypes = new Dictionary<string, DeviceType>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var selector = "System.Devices.InterfaceClassGuid:=\"{86e0d1e0-8089-11d0-9ce4-08003e301f73}\"";
                var devices = await DeviceInformation.FindAllAsync(selector, null);
                foreach (var device in devices)
                {
                    var portName = ExtractPortName(device.Name);
                    if (!string.IsNullOrEmpty(portName))
                    {
                        friendlyNames[portName] = device.Name;
                        var name = device.Name.ToLowerInvariant();
                        deviceTypes[portName] = name.Contains("bluetooth") || name.Contains("spp") || name.Contains("hc-0")
                            ? DeviceType.Bluetooth : DeviceType.Usb;
                    }
                }
            }
            catch { }

            foreach (var p in portNames.OrderBy(n => n))
            {
                DeviceItems.Add(new DeviceItem
                {
                    PortName = p,
                    FriendlyName = friendlyNames.TryGetValue(p, out var fn) ? fn : p,
                    Type = deviceTypes.TryGetValue(p, out var dt) ? dt : DeviceType.Unknown
                });
            }

            foreach (var item in DeviceItems)
                item.IsConnected = _isConnected && item.PortName == (_serialPort?.PortName ?? "");

            NoDevicesText.Visibility = DeviceItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            TryAutoConnect();
        }

        private static string ExtractPortName(string deviceName)
        {
            var idx = deviceName.LastIndexOf('(');
            if (idx >= 0)
            {
                var end = deviceName.LastIndexOf(')');
                if (end > idx)
                {
                    var inner = deviceName.Substring(idx + 1, end - idx - 1);
                    if (inner.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                        return inner;
                }
            }
            return "";
        }

        private void StartPollTimer()
        {
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _pollTimer.Tick += async (_, _) =>
            {
                var currentPorts = SerialPort.GetPortNames();
                var saved = ApplicationData.Current.LocalSettings.Values["ComPort"] as string;

                var knownPorts = DeviceItems.Select(d => d.PortName).ToArray();
                if (!currentPorts.OrderBy(p => p).SequenceEqual(knownPorts.OrderBy(p => p)))
                    await RefreshDeviceListAsync();

                if (!string.IsNullOrEmpty(saved) && !_isConnected && !_manualDisconnect && currentPorts.Contains(saved))
                {
                    var device = DeviceItems.FirstOrDefault(d => d.PortName == saved);
                    ConnectToPort(saved, device?.FriendlyName ?? "");
                }
                else if (_isConnected && _serialPort != null && !currentPorts.Contains(_serialPort.PortName))
                    DisconnectPort();
            };
            _pollTimer.Start();
        }

        private async void TryAutoConnect()
        {
            var saved = ApplicationData.Current.LocalSettings.Values["ComPort"] as string;
            if (!string.IsNullOrEmpty(saved) && SerialPort.GetPortNames().Contains(saved))
            {
                var device = DeviceItems.FirstOrDefault(d => d.PortName == saved);
                ConnectToPort(saved, device?.FriendlyName ?? "");
            }
        }

        private void ConnectToPort(string portName, string friendlyName = "")
        {
            _manualDisconnect = false;
            if (string.IsNullOrEmpty(friendlyName))
            {
                var device = DeviceItems.FirstOrDefault(d => d.PortName == portName);
                friendlyName = device?.FriendlyName ?? "";
            }
            try
            {
                if (_serialPort is { IsOpen: true })
                {
                    _serialPort.Close();
                    _serialPort.Dispose();
                    _serialPort = null;
                }

                _serialPort = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };
                _serialPort.DataReceived += OnDataReceived;
                _serialPort.Open();

                _isConnected = true;
                UpdateStatusUI(true, portName, friendlyName);
                ApplicationData.Current.LocalSettings.Values["ComPort"] = portName;
                ApplicationData.Current.LocalSettings.Values["ComPortFriendlyName"] = friendlyName;
                AppendDebugLog("RX", $"Connected to {portName}");
                UpdateDeviceItemsState();
            }
            catch (UnauthorizedAccessException)
            {
                UpdateStatusUI(false, "");
            }
            catch (Exception)
            {
                UpdateStatusUI(false, "");
            }
        }

        private void DisconnectPort()
        {
            try
            {
                if (_serialPort is { IsOpen: true })
                {
                    var name = _serialPort.PortName;
                    _serialPort.Close();
                    _serialPort.Dispose();
                    _serialPort = null;
                    _isConnected = false;
                    UpdateStatusUI(false, "");
                    AppendDebugLog("RX", $"Disconnected from {name}");
                }
            }
            catch { }
            UpdateDeviceItemsState();
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort is not { IsOpen: true }) return;
            try
            {
                var data = _serialPort.ReadExisting();
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    AppendDebugLog("RX", data.TrimEnd('\r', '\n'));
                    if (data.Contains("HALO|R+")) OsdService.Instance.SelectNext();
                    if (data.Contains("HALO|R-")) OsdService.Instance.SelectPrev();
                    if (data.Contains("HALO|C")) OsdService.Instance.Click();
                });
            }
            catch { }
        }

        private void AppendDebugLog(string direction, string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            var prefix = direction == "TX" ? TxPrefixBox.Text : RxPrefixBox.Text;
            _debugLog.AppendLine($"{prefix}{data}");
            DebugLogText.Text = _debugLog.ToString();
            LogScrollView?.ChangeView(0, double.MaxValue, 1);
        }

        private void UpdateStatusUI(bool connected, string portName, string friendlyName = "")
        {
            StatusDot.Fill = connected
                ? new SolidColorBrush(Color.FromArgb(255, 16, 185, 129))
                : new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
            var display = string.IsNullOrEmpty(friendlyName) ? portName : friendlyName;
            StatusText.Text = connected ? $"Connected ({display})" : "Disconnected";
            ConnectButton.Content = connected ? "Disconnect" : "Connect";
        }

        private void UpdateDeviceItemsState()
        {
            var connectedPort = _serialPort?.PortName ?? "";
            foreach (var item in DeviceItems)
                item.IsConnected = _isConnected && item.PortName == connectedPort;
        }

        private async Task ShowDeviceNotAvailableError(string? savedPort)
        {
            var friendlyName = ApplicationData.Current.LocalSettings.Values["ComPortFriendlyName"] as string;
            var displayName = !string.IsNullOrEmpty(friendlyName) ? $"{friendlyName} ({savedPort})" : savedPort;
            var msg = string.IsNullOrEmpty(savedPort)
                ? "No previously connected device found. Please select a device from the list below."
                : $"The last connected device ({displayName}) is not currently available. Please check the device and try again.";

            var dialog = new ContentDialog
            {
                Title = "Device Unavailable",
                Content = msg,
                CloseButtonText = "OK",
                XamlRoot = AppRoot.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
            {
                _manualDisconnect = true;
                DisconnectPort();
            }
            else
            {
                _manualDisconnect = false;
                var saved = ApplicationData.Current.LocalSettings.Values["ComPort"] as string;
                if (!string.IsNullOrEmpty(saved) && SerialPort.GetPortNames().Contains(saved))
                {
                    var device = DeviceItems.FirstOrDefault(d => d.PortName == saved);
                    ConnectToPort(saved, device?.FriendlyName ?? "");
                }
                else
                {
                    _ = ShowDeviceNotAvailableError(saved);
                }
            }
        }

        private void DeviceItemConnect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string port) return;

            if (_isConnected && _serialPort?.PortName == port)
            {
                _manualDisconnect = true;
                DisconnectPort();
            }
            else
            {
                _manualDisconnect = false;
                var device = DeviceItems.FirstOrDefault(d => d.PortName == port);
                ConnectToPort(port, device?.FriendlyName ?? "");
            }
        }

        private void DebugToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (DebugToggle.IsOn)
            {
                DebugContentCard.Visibility = Visibility.Visible;
                DebugToggleCard.CornerRadius = new CornerRadius(8, 8, 0, 0);
                DebugToggleCard.BorderThickness = new Thickness(1, 1, 1, 0);
                DebugContentCard.CornerRadius = new CornerRadius(0, 0, 8, 8);
                DebugContentCard.BorderThickness = new Thickness(1, 0, 1, 1);
            }
            else
            {
                DebugContentCard.Visibility = Visibility.Collapsed;
                DebugToggleCard.CornerRadius = new CornerRadius(8);
                DebugToggleCard.BorderThickness = new Thickness(1);
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _debugLog.Clear();
            DebugLogText.Text = "";
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_serialPort is not { IsOpen: true }) return;
            var data = SendTextBox.Text;
            if (string.IsNullOrEmpty(data)) return;

            try
            {
                _serialPort.WriteLine(data);
                AppendDebugLog("TX", data);
                SendTextBox.Text = "";
            }
            catch (Exception ex)
            {
                AppendDebugLog("TX", $"Send error: {ex.Message}");
            }
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            var tag = (args.InvokedItemContainer as NavigationViewItem)?.Tag as string;
            BluetoothPage.Visibility = tag == "Bluetooth" ? Visibility.Visible : Visibility.Collapsed;
            ConfigurePage.Visibility = tag == "Configure" ? Visibility.Visible : Visibility.Collapsed;
            DevicesTitle.Text = tag == "Bluetooth" ? "Devices" : "";
        }

        private void LoadConfigFromProfile(RingProfile profile)
    {
        _configNodes.Clear();
        foreach (var node in profile.Nodes)
            _configNodes.Add(node);
        ProfileNameBox.Text = profile.Name;
        NodeListBox.SelectedItem = null;
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = OsdService.Instance.CurrentProfile;
        profile.Name = ProfileNameBox.Text;
        profile.Nodes = _configNodes.ToList();
        ProfileService.SaveProfile(profile);
        OsdService.Instance.ReloadProfile();
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        var profiles = ProfileService.ListProfiles();
        if (profiles.Length == 0) return;

        var dialog = new ContentDialog
        {
            Title = "Load Profile",
            Content = "Default profile will be loaded.",
            CloseButtonText = "OK",
            XamlRoot = AppRoot.XamlRoot
        };
        _ = dialog.ShowAsync();

        var profile = ProfileService.LoadProfile("Default") ?? ProfileService.CreateDefault();
        OsdService.Instance.CurrentProfile = profile;
        LoadConfigFromProfile(profile);
        OsdService.Instance.ReloadProfile();
    }

    private void AddNode_Click(object sender, RoutedEventArgs e)
    {
        var node = new RingNode { Glyph = "\uE774", Label = "New Node" };
        _configNodes.Add(node);
        NodeListBox.SelectedItem = node;
    }

    private void RemoveNode_Click(object sender, RoutedEventArgs e)
    {
        if (NodeListBox.SelectedItem is RingNode node)
        {
            _configNodes.Remove(node);
            NodeListBox.SelectedItem = null;
        }
    }

    private void NodeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveNodeBtn.IsEnabled = NodeListBox.SelectedItem != null;
        if (NodeListBox.SelectedItem is RingNode node)
        {
            _isEditing = true;
            NodeLabelBox.Text = node.Label;
            NodeGlyphBox.Text = node.Glyph;
            NodeGlyphPreview.Glyph = node.Glyph;
            NodeActionTypeBox.SelectedItem = node.ActionType;
            NodeActionDataBox.Text = node.ActionData;
            _isEditing = false;
        }
        else
        {
            NodeLabelBox.Text = "";
            NodeGlyphBox.Text = "";
            NodeGlyphPreview.Glyph = "";
            NodeActionTypeBox.SelectedIndex = -1;
            NodeActionDataBox.Text = "";
        }
    }

    private void NodeLabelBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isEditing || NodeListBox.SelectedItem is not RingNode node) return;
        node.Label = NodeLabelBox.Text;
        var index = _configNodes.IndexOf(node);
        if (index >= 0)
        {
            _configNodes.RemoveAt(index);
            _configNodes.Insert(index, node);
        }
    }

    private void NodeGlyphBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isEditing || NodeListBox.SelectedItem is not RingNode node) return;
        node.Glyph = NodeGlyphBox.Text;
        NodeGlyphPreview.Glyph = node.Glyph;
    }

    private void NodeActionTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isEditing || NodeListBox.SelectedItem is not RingNode node) return;
        if (NodeActionTypeBox.SelectedItem is ActionType at)
            node.ActionType = at;
    }

    private void NodeActionDataBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isEditing || NodeListBox.SelectedItem is not RingNode node) return;
        node.ActionData = NodeActionDataBox.Text;
    }

    }

    public enum DeviceType { Usb, Bluetooth, Unknown }

    public class DeviceItem : INotifyPropertyChanged
    {
        public string PortName { get; set; } = "";
        public string FriendlyName { get; set; } = "";
        public DeviceType Type { get; set; }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ButtonText)); OnPropertyChanged(nameof(IconBrush)); }
        }

        public string ButtonText => IsConnected ? "Disconnect" : "Connect";

        public string IconGlyph => Type switch
        {
            DeviceType.Bluetooth => "\uE702",
            DeviceType.Usb => "\uE88E",
            _ => "\uE770"
        };

        private static readonly SolidColorBrush ConnectedBrush = new(Color.FromArgb(255, 16, 185, 129));
        private static readonly SolidColorBrush DisconnectedBrush = new(Color.FromArgb(255, 128, 128, 128));

        public SolidColorBrush IconBrush => IsConnected ? ConnectedBrush : DisconnectedBrush;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }


}
