using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

        public ObservableCollection<DeviceItem> DeviceItems { get; } = new();

        // Canvas editor state
        private List<RingNode> _canvasNodes = new();
        private readonly List<Grid> _canvasElements = new();
        private int _selectedCanvasIndex = -1;
        private bool _isDragging;
        private int _dragIndex = -1;
        private double _dragStartAngle;

        // Sub-menu navigation
        private readonly Stack<(List<RingNode> Nodes, string Title, List<RingNode> ParentNodes)> _canvasStack = new();
        private List<RingNode> _activeNodes = new();

        // App profiles
        private readonly List<RingProfile> _appProfiles = new();
        private int _selectedProfileIndex;
        private bool _suppressTabChange;

        // Canvas sizing
        private const double ActualOsdSize = 400;
        private const double CanvasViewport = 400;
        private double Scale => CanvasViewport / ActualOsdSize;

        // Brushes
        private static readonly SolidColorBrush NodeFill = new(Color.FromArgb(255, 26, 26, 26));
        private static readonly SolidColorBrush NodeForeground = new(Colors.White);
        private static readonly SolidColorBrush NodeActiveStroke = new(Color.FromArgb(255, 0, 120, 212));
        private static readonly SolidColorBrush NodeLabelBrush = new(Colors.Black);
        private static readonly SolidColorBrush CenterBrush = new(Color.FromArgb(255, 200, 200, 200));

        public MainWindow()
        {
            InitializeComponent();
            DeviceListControl.ItemsSource = DeviceItems;

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            NavView.SelectedItem = NavBluetooth;

            _trayService.ShowRequested += () => NativeMethods.ShowTopmost(this);
            _trayService.HideRequested += () => NativeMethods.HideWindow(this);
            _trayService.CloseRequested += async () => await ShowCloseDialog();
            _trayService.ExitRequested += () =>
            {
                _trayService.Dispose();
                Application.Current.Exit();
            };
            _trayService.Setup(this);

            InitAppProfiles();
            BuildActionCategories();
            LoadCurrentProfile();

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
            var isBT = tag == "Bluetooth";
            BluetoothPage.Visibility = isBT ? Visibility.Visible : Visibility.Collapsed;
            ConfigurePage.Visibility = tag == "Configure" ? Visibility.Visible : Visibility.Collapsed;
            DevicesTitle.Visibility = isBT ? Visibility.Visible : Visibility.Collapsed;
            DevicesTitle.Text = isBT ? "Devices" : "";
        }

        #region Canvas Editor

        private void InitAppProfiles()
        {
            _appProfiles.Clear();
            var existing = ProfileService.ListProfiles();
            foreach (var name in existing)
            {
                var p = ProfileService.LoadProfile(name) ?? new RingProfile { Name = name };
                _appProfiles.Add(p);
            }
            if (_appProfiles.Count == 0)
            {
                var def = ProfileService.CreateDefault();
                ProfileService.SaveProfile(def);
                _appProfiles.Add(def);
            }
            _selectedProfileIndex = 0;
            RefreshAppProfileTabs();
        }

        private void RefreshAppProfileTabs()
        {
            _suppressTabChange = true;
            AppProfileTabs.Items.Clear();
            for (int i = 0; i < _appProfiles.Count; i++)
            {
                var p = _appProfiles[i];
                var tab = new ProfileTabItem
                {
                    Profile = p,
                    DisplayName = p.ProcessName ?? p.Name,
                    Icon = p.ProcessName != null ? "\uE774" : "\uE713",
                    IconBrush = new SolidColorBrush(p.ProcessName != null
                        ? Color.FromArgb(255, 0, 120, 212)
                        : Color.FromArgb(255, 100, 100, 100)),
                };
                AppProfileTabs.Items.Add(tab);
            }
            if (_selectedProfileIndex >= 0 && _selectedProfileIndex < AppProfileTabs.Items.Count)
                AppProfileTabs.SelectedIndex = _selectedProfileIndex;
            _suppressTabChange = false;
        }

        private void AppProfileTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressTabChange) return;
            if (AppProfileTabs.SelectedIndex < 0) return;
            _selectedProfileIndex = AppProfileTabs.SelectedIndex;
            LoadCurrentProfile();
        }

        private void AddAppProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            var newProfile = new RingProfile
            {
                Name = $"Profile {_appProfiles.Count + 1}",
                Radius = 120,
                Nodes = ProfileService.CreateDefault().Nodes
            };
            ProfileService.SaveProfile(newProfile);
            _appProfiles.Add(newProfile);
            _selectedProfileIndex = _appProfiles.Count - 1;
            RefreshAppProfileTabs();
            LoadCurrentProfile();
        }

        private void LoadCurrentProfile()
        {
            if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _appProfiles.Count) return;
            var profile = _appProfiles[_selectedProfileIndex];
            OsdService.Instance.CurrentProfile = profile;
            _canvasStack.Clear();
            _activeNodes = profile.Nodes;
            _canvasNodes = _activeNodes;
            CanvasTitle.Text = profile.Name;
            CanvasBackBtn.Visibility = Visibility.Collapsed;
            _selectedCanvasIndex = -1;
            RenderCanvas();
        }

        private void SaveCurrentProfile()
        {
            if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _appProfiles.Count) return;
            var profile = _appProfiles[_selectedProfileIndex];
            profile.Nodes = _activeNodes.ToList();
            ProfileService.SaveProfile(profile);
            OsdService.Instance.ReloadProfile(profile.Name);
        }

        private void SaveProfileBtn_Click(object sender, RoutedEventArgs e) => SaveCurrentProfile();

        // --- Canvas Rendering ---
        private void RenderCanvas()
        {
            RingCanvas.Children.Clear();
            _canvasElements.Clear();
            var count = _canvasNodes.Count;

            var radius = 120.0 * Scale;
            var circleSize = 56.0 * Scale;
            var fontSize = 24.0 * Scale;
            var labelFontSize = 13.0;
            var cx = CanvasViewport / 2;
            var cy = CanvasViewport / 2;

            if (count > 0)
            {
                var step = 360.0 / count;

                for (int i = 0; i < count; i++)
                {
                    var angle = i * step * Math.PI / 180;
                    var x = cx + radius * Math.Sin(angle);
                    var y = cy - radius * Math.Cos(angle);

                    var grid = new Grid { Width = circleSize, Height = circleSize };
                    Canvas.SetLeft(grid, x - circleSize / 2);
                    Canvas.SetTop(grid, y - circleSize / 2);

                    var ellipse = new Ellipse { Width = circleSize, Height = circleSize, Fill = NodeFill };
                    if (i == _selectedCanvasIndex)
                    {
                        ellipse.Stroke = NodeActiveStroke;
                        ellipse.StrokeThickness = 3;
                    }
                    grid.Children.Add(ellipse);

                    var icon = new FontIcon
                    {
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        Glyph = _canvasNodes[i].Glyph,
                        FontSize = fontSize,
                        Foreground = NodeForeground,
                    };
                    grid.Children.Add(icon);
                    RingCanvas.Children.Add(grid);
                    _canvasElements.Add(grid);

                    var label = new TextBlock
                    {
                        Text = _canvasNodes[i].Label,
                        FontSize = labelFontSize,
                        Foreground = NodeLabelBrush,
                        TextAlignment = TextAlignment.Center,
                        MaxWidth = 100,
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    var labelDist = circleSize / 2 + 10;
                    var lx = cx + (radius + labelDist) * Math.Sin(angle);
                    var ly = cy - (radius + labelDist) * Math.Cos(angle);
                    Canvas.SetLeft(label, lx - 50);
                    Canvas.SetTop(label, ly - 8);
                    RingCanvas.Children.Add(label);
                }
            }

            var ccs = 24.0 * Scale;
            var cg = new Grid { Width = ccs, Height = ccs };
            Canvas.SetLeft(cg, cx - ccs / 2);
            Canvas.SetTop(cg, cy - ccs / 2);
            cg.Children.Add(new Ellipse { Width = ccs, Height = ccs, Fill = CenterBrush });
            cg.Children.Add(new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = _canvasStack.Count > 0 ? "\uE72B" : "\uE711",
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.Black),
            });
            RingCanvas.Children.Add(cg);
        }

        private int HitTestNode(Windows.Foundation.Point position)
        {
            for (int i = 0; i < _canvasElements.Count; i++)
            {
                var el = _canvasElements[i];
                var left = Canvas.GetLeft(el);
                var top = Canvas.GetTop(el);
                if (position.X >= left && position.X <= left + el.Width &&
                    position.Y >= top && position.Y <= top + el.Height)
                    return i;
            }
            return -1;
        }

        // --- Pointer Handlers ---
        private void RingCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var pos = e.GetCurrentPoint(RingCanvas).Position;
            var hitIndex = HitTestNode(pos);

            if (hitIndex >= 0)
            {
                _selectedCanvasIndex = hitIndex;
                _isDragging = true;
                _dragIndex = hitIndex;
                var cx = CanvasViewport / 2;
                var cy = CanvasViewport / 2;
                _dragStartAngle = Math.Atan2(pos.X - cx, cy - pos.Y);
                RenderCanvas();
                ShowNodeProperties(_canvasNodes[hitIndex]);
            }
            else
            {
                _selectedCanvasIndex = -1;
                HideNodeProperties();
                RenderCanvas();
            }
            RingCanvas.CapturePointer(e.Pointer);
        }

        private void RingCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging || _dragIndex < 0 || _canvasNodes.Count < 2) return;
            var pos = e.GetCurrentPoint(RingCanvas).Position;
            var cx = CanvasViewport / 2;
            var cy = CanvasViewport / 2;
            var currentAngle = Math.Atan2(pos.X - cx, cy - pos.Y);

            var count = _canvasNodes.Count;
            var step = 360.0 / count;
            var angleDeg = ((currentAngle * 180 / Math.PI) + 360) % 360;
            var nearestIndex = (int)Math.Round(angleDeg / step) % count;

            if (nearestIndex != _dragIndex)
            {
                var temp = _canvasNodes[_dragIndex];
                _canvasNodes.RemoveAt(_dragIndex);
                _canvasNodes.Insert(nearestIndex, temp);
                _dragIndex = nearestIndex;
                _selectedCanvasIndex = nearestIndex;
                RenderCanvas();
                ShowNodeProperties(_canvasNodes[nearestIndex]);
            }
        }

        private void RingCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
            _dragIndex = -1;
            RingCanvas.ReleasePointerCapture(e.Pointer);
        }

        private void RingCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
            _dragIndex = -1;
        }

        // --- Node Properties ---
        private void ShowNodeProperties(RingNode node)
        {
            NodePropertiesCard.Visibility = Visibility.Visible;
            PropGlyphPreview.Glyph = node.Glyph;
            PropLabelDisplay.Text = node.Label;
            PropActionDisplay.Text = node.ActionType.ToString();
            PropLabelBox.Text = node.Label;
            PropActionDataBox.Text = node.ActionData;
            PropActionDataBox.PlaceholderText = node.ActionType switch
            {
                ActionType.LaunchApp => "Path to .exe or shortcut",
                ActionType.OpenUrl => "https://...",
                ActionType.TextExpansion => "Text snippet to paste",
                _ => ""
            };
            EditSubmenuBtn.Visibility = node.ActionType == ActionType.Folder
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HideNodeProperties()
        {
            NodePropertiesCard.Visibility = Visibility.Collapsed;
        }

        private void PropLabelBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            _canvasNodes[_selectedCanvasIndex].Label = PropLabelBox.Text;
            PropLabelDisplay.Text = PropLabelBox.Text;
            RenderCanvas();
        }

        private void PropActionDataBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            _canvasNodes[_selectedCanvasIndex].ActionData = PropActionDataBox.Text;
        }

        // --- Add/Remove Nodes ---
        private void AddNodeBtn_Click(object sender, RoutedEventArgs e)
        {
            var newNode = new RingNode { Glyph = "\uE710", Label = "New", ActionType = ActionType.None };
            _activeNodes.Add(newNode);
            _selectedCanvasIndex = _activeNodes.Count - 1;
            RenderCanvas();
            ShowNodeProperties(newNode);
        }

        private void RemoveNodeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCanvasIndex < 0 || _canvasNodes.Count <= 2) return;
            _activeNodes.RemoveAt(_selectedCanvasIndex);
            _selectedCanvasIndex = -1;
            HideNodeProperties();
            RenderCanvas();
        }

        // --- Sub-menu Navigation ---
        private void EditSubmenuBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            var node = _canvasNodes[_selectedCanvasIndex];
            if (node.ActionType != ActionType.Folder) return;
            if (node.Children == null) node.Children = new List<RingNode>();

            _canvasStack.Push((_canvasNodes, CanvasTitle.Text, _activeNodes));
            _activeNodes = node.Children;
            _canvasNodes = _activeNodes;
            CanvasTitle.Text = node.Label;
            CanvasBackBtn.Visibility = Visibility.Visible;
            _selectedCanvasIndex = -1;
            HideNodeProperties();
            RenderCanvas();
        }

        private void CanvasBackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_canvasStack.Count == 0) return;
            var (nodes, title, parentNodes) = _canvasStack.Pop();
            _activeNodes = parentNodes;
            _canvasNodes = _activeNodes;
            CanvasTitle.Text = title;
            CanvasBackBtn.Visibility = _canvasStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _selectedCanvasIndex = -1;
            HideNodeProperties();
            RenderCanvas();
        }

        // --- Action Categories ---
        private void BuildActionCategories()
        {
            ActionCategories.Children.Clear();

            var categories = new (string Title, string Icon, (string Glyph, string Label, ActionType Type)[] Actions)[]
            {
                ("MEDIA & VOLUME", "\uE767", new[]
                {
                    ("\uE995", "Volume Up", ActionType.VolumeUp),
                    ("\uE994", "Volume Down", ActionType.VolumeDown),
                    ("\uE74F", "Mute", ActionType.MuteToggle),
                    ("\uE768", "Play/Pause", ActionType.MediaPlayPause),
                    ("\uE7E8", "Next Track", ActionType.MediaNext),
                    ("\uE7E9", "Previous Track", ActionType.MediaPrevious),
                }),
                ("OPEN", "\uE774", new[]
                {
                    ("\uE774", "Launch App", ActionType.LaunchApp),
                    ("\uE770", "Open URL", ActionType.OpenUrl),
                }),
                ("NAVIGATION", "\uE72B", Array.Empty<(string, string, ActionType)>()),
                ("UTILITIES", "\uE713", new[]
                {
                    ("\uE70F", "Text Expansion", ActionType.TextExpansion),
                    ("\uE711", "Close OSD", ActionType.CloseOsd),
                }),
                ("SYSTEM", "\uE770", new[]
                {
                    ("\uE706", "Brightness Up", ActionType.BrightnessUp),
                    ("\uE708", "Brightness Down", ActionType.BrightnessDown),
                }),
                ("ADVANCED", "\uE8B7", new[]
                {
                    ("\uE8B7", "Folder (Sub-menu)", ActionType.Folder),
                }),
            };

            foreach (var (title, icon, actions) in categories)
            {
                var expander = new Expander
                {
                    Header = title,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0),
                };

                if (actions.Length > 0)
                {
                    var panel = new StackPanel { Spacing = 0 };
                    foreach (var (glyph, label, actionType) in actions)
                    {
                        var btn = new Button
                        {
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Background = new SolidColorBrush(Colors.Transparent),
                            BorderThickness = new Thickness(0),
                            Padding = new Thickness(16, 8, 16, 8),
                            Tag = (glyph, actionType),
                        };

                        var row = new Grid { Width = 280 };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        row.Children.Add(new FontIcon
                        {
                            FontFamily = new FontFamily("Segoe MDL2 Assets"),
                            Glyph = glyph,
                            FontSize = 16,
                            VerticalAlignment = VerticalAlignment.Center,
                        });
                        var lbl = new TextBlock
                        {
                            Text = label,
                            FontSize = 13,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(12, 0, 0, 0),
                        };
                        Grid.SetColumn(lbl, 2);
                        row.Children.Add(lbl);
                        btn.Content = row;

                        var capturedGlyph = glyph;
                        var capturedType = actionType;
                        var capturedLabel = label;
                        btn.Click += (_, _) => AddActionToRing(capturedGlyph, capturedType, capturedLabel);
                        panel.Children.Add(btn);
                    }
                    expander.Content = panel;
                }
                else
                {
                    expander.Content = new TextBlock
                    {
                        Text = "Coming soon",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        Margin = new Thickness(16, 8, 16, 8),
                    };
                }

                ActionCategories.Children.Add(expander);
            }

            ActionSearchBox.TextChanged += ActionSearchBox_TextChanged;
        }

        private void AddActionToRing(string glyph, ActionType type, string label)
        {
            var newNode = new RingNode
            {
                Glyph = glyph,
                Label = label,
                ActionType = type,
                ActionData = type switch
                {
                    ActionType.LaunchApp => "",
                    ActionType.OpenUrl => "",
                    ActionType.Folder => "",
                    _ => ""
                },
            };
            if (type == ActionType.Folder)
                newNode.Children = new List<RingNode>();

            _activeNodes.Add(newNode);
            _selectedCanvasIndex = _activeNodes.Count - 1;
            RenderCanvas();
            ShowNodeProperties(newNode);
        }

        private void ActionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = ActionSearchBox.Text?.Trim().ToLowerInvariant() ?? "";
            foreach (var child in ActionCategories.Children)
            {
                if (child is Expander exp && exp.Content is StackPanel panel)
                {
                    var hasMatch = string.IsNullOrEmpty(query);
                    foreach (var btn in panel.Children.OfType<Button>())
                    {
                        if (btn.Content is Grid grid && grid.Children.Count >= 3)
                        {
                            var lbl = grid.Children.OfType<TextBlock>().FirstOrDefault();
                            if (lbl != null)
                            {
                                var match = string.IsNullOrEmpty(query) ||
                                            lbl.Text.ToLowerInvariant().Contains(query);
                                btn.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                                if (match && !string.IsNullOrEmpty(query)) hasMatch = true;
                            }
                        }
                    }
                    exp.Visibility = hasMatch ? Visibility.Visible : Visibility.Collapsed;
                    if (!string.IsNullOrEmpty(query) && hasMatch) exp.IsExpanded = true;
                }
            }
        }

        #endregion

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
            var root = new StackPanel { Spacing = 8, Width = 310 };

            var searchBox = new TextBox { PlaceholderText = "Search icons...", Margin = new Thickness(0) };
            var grid = new Grid();
            var columns = 6;
            var rows = (int)Math.Ceiling((double)CommonIcons.Length / columns);
            for (int i = 0; i < rows; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            for (int i = 0; i < columns; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

            var allBtns = new List<(Button Btn, string Label)>();
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
                    Margin = new Thickness(2),
                };
                var glyph = CommonIcons[i].Glyph;
                btn.Click += (_, _) =>
                {
                    if (_selectedCanvasIndex >= 0 && _selectedCanvasIndex < _canvasNodes.Count)
                    {
                        _canvasNodes[_selectedCanvasIndex].Glyph = glyph;
                        PropGlyphPreview.Glyph = glyph;
                        RenderCanvas();
                    }
                    flyout.Hide();
                };
                Grid.SetRow(btn, i / columns);
                Grid.SetColumn(btn, i % columns);
                grid.Children.Add(btn);
                allBtns.Add((btn, CommonIcons[i].Label.ToLowerInvariant()));
            }

            searchBox.TextChanged += (_, _) =>
            {
                var q = searchBox.Text?.Trim().ToLowerInvariant() ?? "";
                foreach (var (btn, label) in allBtns)
                    btn.Visibility = string.IsNullOrEmpty(q) || label.Contains(q)
                        ? Visibility.Visible : Visibility.Collapsed;
            };

            root.Children.Add(searchBox);
            root.Children.Add(new ScrollViewer { Content = grid, Height = 250 });
            flyout.Content = root;
            IconPickerBtn.Flyout = flyout;
            flyout.ShowAt(IconPickerBtn);
        }

        #endregion

    }

    public class ProfileTabItem
    {
        public RingProfile Profile { get; set; } = new();
        public string DisplayName { get; set; } = "";
        public string Icon { get; set; } = "\uE713";
        public SolidColorBrush IconBrush { get; set; } = new(Colors.Gray);
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
