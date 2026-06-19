using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
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

        private readonly ObservableCollection<SlotItem> _slotItems = new();
        private bool _isEditing;
        private int _selectedSlotIndex = -1;
        private static readonly Color _white = Color.FromArgb(180, 255, 255, 255);
        private static readonly Color _black = Color.FromArgb(255, 0, 0, 0);

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

            SlotActionTypeBox.ItemsSource = Enum.GetValues<ActionType>();
            SlotListBox.ItemsSource = _slotItems;
            InitSlots();

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

        private void InitSlots()
        {
            var profile = OsdService.Instance.CurrentProfile;
            _slotItems.Clear();
            for (int i = 0; i < 8; i++)
            {
                var node = i < profile.Nodes.Count ? profile.Nodes[i] : new RingNode();
                _slotItems.Add(new SlotItem(i, node));
            }
            ProfileNameBox.Text = profile.Name;
            _selectedSlotIndex = -1;
            SlotListBox.SelectedItem = null;
            UpdatePreview();
        }

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            var profile = OsdService.Instance.CurrentProfile;
            profile.Name = ProfileNameBox.Text;
            profile.Nodes = _slotItems.Select(s => s.Node).ToList();
            ProfileService.SaveProfile(profile);
            OsdService.Instance.ReloadProfile(profile.Name);
        }

        private void LoadProfile_Click(object sender, RoutedEventArgs e)
        {
            var profiles = ProfileService.ListProfiles();
            var target = profiles.Length > 0 ? profiles[0] : "Default";

            var profile = ProfileService.LoadProfile(target) ?? ProfileService.CreateDefault();
            OsdService.Instance.CurrentProfile = profile;
            InitSlots();
            OsdService.Instance.ReloadProfile(profile.Name);
        }

        private void SlotList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.FirstOrDefault() is SlotItem slot)
            {
                _selectedSlotIndex = slot.Index;
                _isEditing = true;
                SlotLabelBox.Text = slot.Node.Label;
                SlotGlyphBox.Text = slot.Node.Glyph;
                SlotGlyphPreview.Glyph = slot.Node.Glyph;
                SlotActionTypeBox.SelectedItem = slot.Node.ActionType;
                UpdateActionDataPlaceholder(slot.Node.ActionType);
                SlotActionDataBox.Text = slot.Node.ActionData;
                _isEditing = false;
            }
            else
            {
                _selectedSlotIndex = -1;
                ClearEditor();
            }
        }

        private void SlotLabelBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isEditing || _selectedSlotIndex < 0) return;
            _slotItems[_selectedSlotIndex].Node.Label = SlotLabelBox.Text;
        }

        private void SlotGlyphBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isEditing || _selectedSlotIndex < 0) return;
            _slotItems[_selectedSlotIndex].Node.Glyph = SlotGlyphBox.Text;
            SlotGlyphPreview.Glyph = SlotGlyphBox.Text;
            UpdatePreview();
        }

        private void SlotActionTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isEditing || _selectedSlotIndex < 0) return;
            if (SlotActionTypeBox.SelectedItem is ActionType at)
            {
                _slotItems[_selectedSlotIndex].Node.ActionType = at;
                UpdateActionDataPlaceholder(at);
            }
        }

        private void SlotActionDataBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isEditing || _selectedSlotIndex < 0) return;
            _slotItems[_selectedSlotIndex].Node.ActionData = SlotActionDataBox.Text;
        }

        private void ClearEditor()
        {
            SlotLabelBox.Text = "";
            SlotGlyphBox.Text = "";
            SlotGlyphPreview.Glyph = "";
            SlotActionTypeBox.SelectedIndex = -1;
            SlotActionDataBox.Text = "";
        }

        private void UpdateActionDataPlaceholder(ActionType at)
        {
            SlotActionDataBox.PlaceholderText = at switch
            {
                ActionType.LaunchApp => "Path to .exe or shortcut",
                ActionType.OpenUrl => "https://...",
                ActionType.TextExpansion => "Text snippet to paste",
                _ => ""
            };
        }

        #region Icon Picker

        private static readonly (string Glyph, string Label)[] CommonIcons =
        {
            ("\uE774", "Globe"), ("\uE8B9", "Video"), ("\uE995", "Vol+"), ("\uE994", "Vol-"),
            ("\uE74F", "Mute"), ("\uE767", "Mute2"), ("\uE706", "Bright+"), ("\uE708", "Bright-"),
            ("\uE768", "Play"), ("\uE769", "Pause"), ("\uE7E8", "Next"), ("\uE7E9", "Prev"),
            ("\uEC4F", "Music"), ("\uE713", "Gear"), ("\uE80F", "Home"), ("\uE721", "Search"),
            ("\uE722", "Camera"), ("\uE72E", "Lock"), ("\uE702", "BT"), ("\uE88E", "USB"),
            ("\uE701", "WiFi"), ("\uE7BE", "Light"), ("\uE73E", "Check"), ("\uE711", "Close"),
            ("\uE710", "Add"), ("\uE74D", "Delete"), ("\uE70F", "Edit"), ("\uE72D", "Share"),
            ("\uE734", "Star"), ("\uE72C", "Refresh"), ("\uE736", "Down"), ("\uE735", "Up"),
            ("\uE121", "Clock"), ("\uE787", "Cal"), ("\uE716", "People"), ("\uE717", "Phone"),
            ("\uE714", "Video2"), ("\uE71B", "Link"), ("\uE8B7", "Folder"), ("\uE7C3", "File"),
            ("\uE74E", "Save"), ("\uE8EF", "Calc"),
        };

        private void IconPickerBtn_Click(object sender, RoutedEventArgs e)
        {
            var flyout = new Flyout();
            var grid = new Grid();
            var columns = 6;
            var rows = (int)Math.Ceiling((double)CommonIcons.Length / columns);

            for (int i = 0; i < rows; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            for (int i = 0; i < columns; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

            for (int i = 0; i < CommonIcons.Length; i++)
            {
                var btn = new Button
                {
                    Width = 44, Height = 44,
                    Content = new FontIcon
                    {
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        Glyph = CommonIcons[i].Glyph,
                        FontSize = 18,
                    },
                    Tag = CommonIcons[i].Glyph,
                    Margin = new Thickness(2),
                };
                var glyph = CommonIcons[i].Glyph;
                btn.Click += (s, args) =>
                {
                    if (_selectedSlotIndex >= 0)
                    {
                        _slotItems[_selectedSlotIndex].Node.Glyph = glyph;
                        SlotGlyphBox.Text = glyph;
                        SlotGlyphPreview.Glyph = glyph;
                        UpdatePreview();
                    }
                    flyout.Hide();
                };
                Grid.SetRow(btn, i / columns);
                Grid.SetColumn(btn, i % columns);
                grid.Children.Add(btn);
            }

            var scroll = new ScrollViewer { Content = grid, Width = 310, Height = 250 };
            flyout.Content = scroll;
            IconPickerBtn.Flyout = flyout;
            flyout.ShowAt(IconPickerBtn);
        }

        #endregion

        #region Live Preview

        private void UpdatePreview()
        {
            PreviewContainer.Children.Clear();
            var radius = 60.0;
            var count = 8;
            var step = 360.0 / count;

            for (int i = 0; i < count; i++)
            {
                var angle = i * step * Math.PI / 180;
                var cx = 90.0 + radius * Math.Sin(angle) - 14;
                var cy = 90.0 - radius * Math.Cos(angle) - 14;

                var ellipse = new Ellipse
                {
                    Width = 28, Height = 28,
                    Fill = new SolidColorBrush(_white),
                };
                Canvas.SetLeft(ellipse, cx);
                Canvas.SetTop(ellipse, cy);
                PreviewContainer.Children.Add(ellipse);

                var glyph = i < _slotItems.Count ? _slotItems[i].Node.Glyph : "";
                var icon = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Glyph = glyph,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(_black),
                };
                Canvas.SetLeft(icon, cx + 7);
                Canvas.SetTop(icon, cy + 7);
                PreviewContainer.Children.Add(icon);
            }
        }

        #endregion

    }

    public class SlotItem : INotifyPropertyChanged
    {
        public int Index { get; }
        public string SlotLabel => (Index + 1).ToString();
        public RingNode Node { get; }

        public SlotItem(int index, RingNode node)
        {
            Index = index;
            Node = node;
            Node.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(Glyph));
                OnPropertyChanged(nameof(Label));
            };
        }

        public string Glyph => Node.Glyph;
        public string Label => Node.Label;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
