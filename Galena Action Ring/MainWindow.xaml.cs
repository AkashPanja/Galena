using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Microsoft.Win32;
using WinRT.Interop;

namespace GalenaActionRing
{
    internal class DeviceEntry
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsConnected { get; set; }
        public SolidColorBrush StatusColor => IsConnected
            ? new SolidColorBrush(Color.FromArgb(255, 16, 185, 129))
            : new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    }

    public sealed partial class MainWindow : Window
    {
        private readonly TrayService _trayService = new();
        private IntPtr _hidReadHandle;
        private IntPtr _hidWriteHandle;
        private CancellationTokenSource? _hidReadCts;
        private string? _connectedDeviceId;
        private DeviceWatcher? _deviceWatcher;
        private readonly StringBuilder _debugLog = new();
        private readonly ObservableCollection<DeviceEntry> _deviceEntries = new();
        private volatile int _lastBrightness = 100;
        private volatile bool _lightBarOn = true;


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
        private RingProfile? _editingCopy;
        private bool _suppressTabChange;
        private bool _suppressColorChange;
        private bool _suppressActionTypeChange;

        // Canvas sizing
        private const double ActualOsdSize = 400;
        private const double CanvasViewport = 400;
        private double Scale => CanvasViewport / ActualOsdSize;

        // Brushes (synced with OsdWindow styling)
        private static SolidColorBrush InactiveFill = new(Color.FromArgb(128, 255, 255, 255));
        private static SolidColorBrush InactiveStroke = new(Color.FromArgb(255, 102, 102, 102));
        private static SolidColorBrush InactiveForeground = new(Color.FromArgb(255, 0, 0, 0));
        private static SolidColorBrush ActiveFill = new(Color.FromArgb(255, 0, 0, 0));
        private static SolidColorBrush ActiveStroke = new(Color.FromArgb(255, 255, 255, 255));
        private static SolidColorBrush ActiveForeground = new(Color.FromArgb(255, 255, 255, 255));
        private static readonly SolidColorBrush NodeLabelBrush = new(Colors.Black);
        private static readonly SolidColorBrush CenterFill = new(Color.FromArgb(128, 255, 255, 255));
        private bool _colorsExpanded = false;
        private ActionTypeItem? _lastActionTypeItem;

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            NavView.SelectedItem = NavBluetooth;

            _trayService.ShowRequested += () => NativeMethods.ShowTopmost(this!);
            _trayService.HideRequested += () => NativeMethods.HideWindow(this!);
            _trayService.CloseRequested += async () => await ShowCloseDialog();
            _trayService.ExitRequested += () =>
            {
                _trayService.Dispose();
                Application.Current.Exit();
            };
            _trayService.Setup(this);

            OsdService.Instance.OsdVisibilityChanged += _ => SendOsdState();

            // Register converters for XAML binding
            AppRoot.Resources["BoolToVis"] = new BoolToVisibilityConverter();
            AppRoot.Resources["InverseBoolToVis"] = new InverseBoolToVisibilityConverter();

            SetMinWindowSize();

            InitAppProfiles();
            LoadCurrentProfile();
            _ = FindHidDevicesAsync();

            PropActionTypeBox.ItemTemplate = (DataTemplate)AppRoot.Resources["ActionTypeItemTemplate"];
            PopulateActionTypeBox();

            if (!StartupService.IsFirstRunDone())
                _ = ShowFirstRunPromptAsync();
        }

        private void SetMinWindowSize()
        {
            NativeMethods.SetWindowPosition(this, 1000, 700);
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

        private async Task FindHidDevicesAsync()
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

                DeviceListView.ItemsSource = _deviceEntries;
                UpdateEmptyState();

                _deviceWatcher = DeviceInformation.CreateWatcher(selector);
                _deviceWatcher.Added += (_, info) =>
                {
                    if (info.Name.Contains("Galena"))
                        DispatcherQueue.TryEnqueue(async () =>
                        {
                            if (_deviceEntries.All(x => x.Id != info.Id))
                            {
                                _deviceEntries.Add(new DeviceEntry { Id = info.Id, DisplayName = info.Name });
                                UpdateEmptyState();
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
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        var entry = _deviceEntries.FirstOrDefault(x => x.Id == info.Id);
                        if (entry != null) _deviceEntries.Remove(entry);
                        if (info.Id == _connectedDeviceId) DisconnectHidDevice();
                        UpdateEmptyState();
                    });
                };
                _deviceWatcher.Start();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HID] FindHidDevicesAsync failed: {ex.Message}"); }
        }

        private void UpdateEmptyState()
        {
            EmptyDeviceText.Visibility = _deviceEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ConnectToHidDevice(string deviceId)
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
                    AppendHidEvent($"HID open failed (readErr={readErr}, writeErr={writeErr})");
                    return;
                }

                _connectedDeviceId = deviceId;
                App.SetSetting("HidDeviceId", deviceId);
                AppendHidEvent("Connected");

                foreach (var e in _deviceEntries) e.IsConnected = (e.Id == deviceId);
                DeviceListView.ItemsSource = null;
                DeviceListView.ItemsSource = _deviceEntries;

                if (NativeMethods.IsValidHandle(_hidReadHandle))
                {
                    _hidReadCts = new CancellationTokenSource();
                    _ = Task.Run(() => HidReadLoop(_hidReadHandle, _hidReadCts.Token));
                }
            }
            catch (Exception ex)
            {
                AppendHidEvent($"Exception connecting to HID device: {ex.Message}");
            }
        }

        private void SendOsdState()
        {
            try
            {
                if (!NativeMethods.IsValidHandle(_hidWriteHandle)) return;
                var osdByte = OsdService.Instance.IsVisible ? (byte)1 : (byte)0;
                bool ok = NativeMethods.SendHidFeatureReport(_hidWriteHandle, 0, new byte[] { osdByte });
                AppendHidEvent(ok ? $"OSD sent byte={osdByte}" : $"OSD send FAILED (Win32 err={Marshal.GetLastWin32Error()})");
            }
            catch (Exception ex) { AppendHidEvent($"OSD send FAILED: {ex.Message}"); }
        }

        private void DisconnectHidDevice()
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HID] Disconnect error: {ex.Message}"); }
            AppendHidEvent("Disconnected");

            foreach (var e in _deviceEntries) e.IsConnected = false;
            DeviceListView.ItemsSource = null;
            DeviceListView.ItemsSource = _deviceEntries;
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

                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        ProcessHidEvent(eventType, eventValue);
                    });
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HID] ReadLoop error: {ex.Message}");
                    Thread.Sleep(200);
                }
            }
        }

        private void ProcessHidEvent(byte eventType, sbyte eventValue)
        {
            switch (eventType)
            {
                case 1:
                    _lastBrightness = eventValue;
                    AppendHidEvent($"BRIGHTNESS {eventValue}%");
                    OsdService.Instance.UpdateBrightnessPreviewIfShowing(eventValue, _lightBarOn);
                    break;
                case 2:
                    AppendHidEvent(eventValue > 0 ? "ENCODER +1" : "ENCODER -1");
                    if (OsdService.Instance.IsVisible)
                    {
                        if (eventValue > 0) OsdService.Instance.SelectNext();
                        else OsdService.Instance.SelectPrev();
                    }
                    else
                    {
                        _lastBrightness = Math.Clamp(_lastBrightness + eventValue * 3, 0, 100);
                        OsdService.Instance.ShowBrightnessPreview(_lastBrightness, _lightBarOn);
                    }
                    break;
                case 3:
                    var label = eventValue switch { 0 => "tap", 1 => "press", 2 => "hold", _ => eventValue.ToString() };
                    AppendHidEvent($"BUTTON {label}");
                    if (eventValue == 0) OsdService.Instance.Click();
                    else if (eventValue == 2)
                    {
                        _lightBarOn = !_lightBarOn;
                        OsdService.Instance.ShowBrightnessPreview(_lastBrightness, _lightBarOn);
                    }
                    break;
                case 4:
                    SendOsdState();
                    break;
                case 5:
                    var ackState = eventValue == 1 ? "Open" : "Closed";
                    OsdStateText.Text = $"OSD: {ackState}";
                    OsdStatusIndicator.Fill = eventValue == 1
                        ? new SolidColorBrush(Microsoft.UI.Colors.LimeGreen)
                        : new SolidColorBrush(Microsoft.UI.Colors.Gray);
                    break;
                case 6:
                    _lightBarOn = eventValue == 1;
                    AppendHidEvent(_lightBarOn ? "LIGHT ON" : "LIGHT OFF");
                    OsdService.Instance.UpdateBrightnessPreviewIfShowing(_lastBrightness, _lightBarOn);
                    break;
            }
        }

        private void AppendHidEvent(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            _debugLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            DebugLogText.Text = _debugLog.ToString();
            LogScrollView?.ChangeView(0, double.MaxValue, 1);
        }

        private void DeviceListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.FirstOrDefault() is not DeviceEntry entry) return;
            if (entry.IsConnected)
                DisconnectHidDevice();
            else
                ConnectToHidDevice(entry.Id);
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

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            var tag = (args.InvokedItemContainer as NavigationViewItem)?.Tag as string;
            var isBT = tag == "Bluetooth";
            BluetoothPage.Visibility = isBT ? Visibility.Visible : Visibility.Collapsed;
            BluetoothScrollViewer.Visibility = isBT ? Visibility.Visible : Visibility.Collapsed;
            ConfigurePage.Visibility = tag == "Configure" ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
            DevicesTitle.Visibility = isBT ? Visibility.Visible : Visibility.Collapsed;
            DevicesTitle.Text = isBT ? "Devices" : "";
            if (tag == "Settings")
                StartupToggle.IsOn = StartupService.IsEnabled();
        }

        private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            await StartupService.SetEnabledAsync(StartupToggle.IsOn);
        }

        private async void ResetAppButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Reset App",
                Content = "This will clear all profiles and settings, and restart the app.\nAre you sure?",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = AppRoot.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            await StartupService.SetEnabledAsync(false);

            _appProfiles.Clear();
            foreach (var name in ProfileService.ListProfiles())
                ProfileService.DeleteProfile(name);

            var def = ProfileService.CreateDefault();
            ProfileService.SaveProfile(def);
            _appProfiles.Add(def);
            _selectedProfileIndex = 0;
            RefreshAppProfileTabs();
            LoadCurrentProfile();
            PopulateIconGallery();

            try
            {
                var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\GalenaActionRing", true);
                key?.DeleteValue("FirstRunDone", false);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MainWindow] ResetAppButton: {ex.Message}"); }

            OsdService.Instance.ReloadProfile("Default");
            NavView.SelectedItem = NavConfigure;
            StartupToggle.IsOn = false;
            _ = ShowFirstRunPromptAsync();
        }

        private async Task ShowFirstRunPromptAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "Welcome to Galena Action Ring",
                Content = "Would you like Galena Action Ring to start automatically\nwhen you sign in to Windows?",
                PrimaryButtonText = "Yes, enable startup",
                CloseButtonText = "Not now",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = AppRoot.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await StartupService.SetEnabledAsync(true);
                StartupToggle.IsOn = true;
            }
            StartupService.MarkFirstRunDone();
        }

        #region Canvas Editor

        private void InitAppProfiles()
        {
            _appProfiles.Clear();
            var existing = ProfileService.ListProfiles();
            foreach (var name in existing)
            {
                var p = ProfileService.LoadProfile(name) ?? new RingProfile { Name = name };
                // Auto-repair: root Nodes should contain at least one Folder or Group category node.
                // If every node is Individual/category 0 and all are simple action types, the profile
                // likely has sub-menu children stored as root (old save bug). Reset to default.
                if (p.Nodes.Count > 0 && p.Nodes.All(n => n.Category == ActionCategory.Individual &&
                    n.ActionType is ActionType.MediaPlayPause or ActionType.MediaNext or
                    ActionType.MediaPrevious or ActionType.MediaSeekForward or ActionType.MediaSeekBackward))
                {
                    var fresh = ProfileService.CreateDefault();
                    fresh.Name = p.Name;
                    fresh.ProcessName = p.ProcessName;
                    p = fresh;
                }
                // Migrate old playlist_play glyph to autoplay for Folder nodes
                MigrateFolderGlyphs(p.Nodes);
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
            PopulateIconGallery();
        }

        private static void MigrateFolderGlyphs(List<RingNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.ActionType == ActionType.Folder && node.Glyph == "\uE05F")
                    node.Glyph = "\uF6B5";
                if (node.Children != null)
                    MigrateFolderGlyphs(node.Children);
            }
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
                    Icon = p.ProcessName != null ? "\uE89E" : "\uE8B8",
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


        private void LoadCurrentProfile()
        {
            if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _appProfiles.Count) return;
            var profile = _appProfiles[_selectedProfileIndex];
            _editingCopy = profile.DeepCopy();
            OsdService.Instance.CurrentProfile = profile;
            _canvasStack.Clear();
            _activeNodes = _editingCopy.Nodes;
            _canvasNodes = _activeNodes;
            CanvasTitle.Text = profile.Name;
            CanvasBackBtn.Visibility = Visibility.Collapsed;
            _selectedCanvasIndex = -1;
            SelectProfileColors();
            RenderCanvas();
            OsdService.Instance.ReloadProfile(profile.Name);
        }

        private void SaveCurrentProfile()
        {
            if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _appProfiles.Count) return;
            var profile = _appProfiles[_selectedProfileIndex];
            if (_editingCopy == null) return;

            var duplicate = _appProfiles
                .Select((p, i) => (p, i))
                .FirstOrDefault(x => x.i != _selectedProfileIndex &&
                                     string.Equals(x.p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            if (duplicate.p != null)
            {
                _ = ShowErrorDialog($"A ring named \"{profile.Name}\" already exists. Choose a different name.");
                return;
            }

            // Copy edited data back to the original profile
            profile.Nodes = _editingCopy.Nodes.ConvertAll(n => n.DeepCopy());
            profile.Radius = _editingCopy.Radius;

            ProfileService.SaveProfile(profile);
            OsdService.Instance.ReloadProfile(profile.Name);
        }

        private async Task ShowErrorDialog(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Error",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = AppRoot.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async void AddRingBtn_Click(object sender, RoutedEventArgs e)
        {
            var nameBox = new TextBox
            {
                PlaceholderText = "Enter ring name",
                Margin = new Thickness(0, 8, 0, 0)
            };

            var dialog = new ContentDialog
            {
                Title = "New Ring",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = AppRoot.XamlRoot,
                Content = nameBox,
            };

            dialog.Loaded += (_, _) => nameBox.Focus(FocusState.Programmatic);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var ringName = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ringName))
            {
                await ShowErrorDialog("Ring name cannot be empty.");
                return;
            }

            if (_appProfiles.Any(p => string.Equals(p.Name, ringName, StringComparison.OrdinalIgnoreCase)))
            {
                await ShowErrorDialog($"A ring named \"{ringName}\" already exists. Choose a different name.");
                return;
            }

            var template = ProfileService.CreateDefault();
            var newProfile = new RingProfile
            {
                Name = ringName,
                Radius = template.Radius,
                PrimaryColor = template.PrimaryColor,
                SecondaryColor = template.SecondaryColor,
                Nodes = template.Nodes.ConvertAll(n => n.DeepCopy())
            };
            ProfileService.SaveProfile(newProfile);
            _appProfiles.Add(newProfile);
            _selectedProfileIndex = _appProfiles.Count - 1;
            RefreshAppProfileTabs();
            LoadCurrentProfile();
        }

        private async void DeleteRingBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_appProfiles.Count <= 1)
            {
                await ShowErrorDialog("You must have at least one ring. Cannot delete the last ring.");
                return;
            }

            var profile = _appProfiles[_selectedProfileIndex];
            var confirm = new ContentDialog
            {
                Title = "Delete Ring",
                Content = $"Delete \"{profile.Name}\"? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = AppRoot.XamlRoot
            };

            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            ProfileService.DeleteProfile(profile.Name);
            _appProfiles.RemoveAt(_selectedProfileIndex);
            _selectedProfileIndex = Math.Max(0, _selectedProfileIndex - 1);
            RefreshAppProfileTabs();
            LoadCurrentProfile();
        }

        private async void RenameRingBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _appProfiles.Count) return;
            var profile = _appProfiles[_selectedProfileIndex];

            var nameBox = new TextBox { Text = profile.Name, Margin = new Thickness(0, 8, 0, 0) };

            var dialog = new ContentDialog
            {
                Title = "Rename Ring",
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = AppRoot.XamlRoot,
                Content = nameBox,
            };

            dialog.Loaded += (_, _) =>
            {
                nameBox.Focus(FocusState.Programmatic);
                nameBox.SelectAll();
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var newName = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                await ShowErrorDialog("Ring name cannot be empty.");
                return;
            }

            if (_appProfiles.Any(p => p != profile && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                await ShowErrorDialog($"A ring named \"{newName}\" already exists. Choose a different name.");
                return;
            }

            ProfileService.DeleteProfile(profile.Name);
            profile.Name = newName;
            ProfileService.SaveProfile(profile);
            RefreshAppProfileTabs();
            CanvasTitle.Text = profile.Name;
        }

        // --- Canvas Rendering ---
        private void RenderCanvas()
        {
            RingCanvas.Children.Clear();
            _canvasElements.Clear();
            var count = _canvasNodes.Count;

            var radius = 120.0 * Scale;
            var circleSize = 64.0 * Scale;
            var fontSize = 28.0 * Scale;
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

                    var isSelected = i == _selectedCanvasIndex;
                    var ellipse = new Ellipse
                    {
                        Width = circleSize,
                        Height = circleSize,
                        Fill = isSelected ? ActiveFill : InactiveFill,
                        Stroke = isSelected ? ActiveStroke : null,
                        StrokeThickness = isSelected ? 2 : 0,
                    };
                    grid.Children.Add(ellipse);

                    var icon = new FontIcon
                    {
                        FontFamily = new FontFamily(MaterialIcons.FontFamilyName),
                        Glyph = _canvasNodes[i].Glyph,
                        FontSize = fontSize,
                        Foreground = isSelected ? ActiveForeground : InactiveForeground,
                    };
                    grid.Children.Add(icon);
                    RingCanvas.Children.Add(grid);
                    _canvasElements.Add(grid);
                }
            }

            var ccs = 24.0 * Scale;
            var cg = new Grid { Width = ccs, Height = ccs };
            Canvas.SetLeft(cg, cx - ccs / 2);
            Canvas.SetTop(cg, cy - ccs / 2);
            cg.Children.Add(new Ellipse { Width = ccs, Height = ccs, Fill = CenterFill, Stroke = InactiveStroke, StrokeThickness = 1 });
            cg.Children.Add(new FontIcon
            {
                FontFamily = new FontFamily(MaterialIcons.FontFamilyName),
                Glyph = _canvasStack.Count > 0 ? "\uE5C4" : "\uE5CD",
                FontSize = 10,
                Foreground = InactiveForeground,
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

        private bool IsHitCenterButton(Windows.Foundation.Point position)
        {
            var cx = CanvasViewport / 2;
            var cy = CanvasViewport / 2;
            var halfSize = 12.0 * Scale; // 24 / 2
            return position.X >= cx - halfSize && position.X <= cx + halfSize &&
                   position.Y >= cy - halfSize && position.Y <= cy + halfSize;
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
            else if (_canvasStack.Count > 0 && IsHitCenterButton(pos))
            {
                CanvasBackBtn_Click(sender, new RoutedEventArgs());
                return;
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
            NodeEditDefaultText.Visibility = Visibility.Collapsed;
            NodeEditPanel.Visibility = Visibility.Visible;
            CollapseColors();

            PropGlyphPreview.FontFamily = new FontFamily(MaterialIcons.FontFamilyName);
            PropGlyphPreview.Glyph = node.Glyph;
            PropLabelDisplay.Text = node.Label;
            PropActionDisplay.Text = FormatActionTypeName(node.ActionType);
            PropLabelBox.Text = node.Label;

            // Show/hide conditional fields
            PropUrlBox.Visibility = node.ActionType == ActionType.OpenUrl ? Visibility.Visible : Visibility.Collapsed;
            if (node.ActionType == ActionType.OpenUrl)
                PropUrlBox.Text = node.ActionData;

            PropAppPathSection.Visibility = node.ActionType == ActionType.LaunchApp ? Visibility.Visible : Visibility.Collapsed;
            if (node.ActionType == ActionType.LaunchApp)
                PropAppPathBox.Text = node.ActionData;

            EditSubmenuBtn.Visibility = node.ActionType == ActionType.Folder
                ? Visibility.Visible : Visibility.Collapsed;

            // Show icon gallery for non-toggle nodes (load items source only when first shown)
            var showIconGallery = !IsToggleType(node.ActionType);
            IconSection.Visibility = showIconGallery ? Visibility.Visible : Visibility.Collapsed;
            if (showIconGallery) EnsureIconGalleryLoaded();

            // Select current ActionType
            _suppressActionTypeChange = true;
            foreach (ActionTypeItem ati in PropActionTypeBox.Items)
            {
                if (ati.Type == node.ActionType)
                {
                    PropActionTypeBox.SelectedItem = ati;
                    _lastActionTypeItem = ati;
                    break;
                }
            }
            _suppressActionTypeChange = false;
        }

        private void PopulateActionTypeBox()
        {
            var items = new List<ActionTypeItem>();

            void AddItem(ActionType at, string name, string glyph) =>
                items.Add(new ActionTypeItem { Type = at, Name = name, Glyph = glyph });

            void AddSeparator() =>
                items.Add(new ActionTypeItem { IsSeparator = true });

            AddSeparator();
            AddItem(ActionType.VolumeControl, "Volume Control", "\uE050");
            AddItem(ActionType.BrightnessControl, "Brightness Control", "\uE3AB");

            AddSeparator();
            AddItem(ActionType.Folder, "Playback Control", "\uF6B5");

            AddSeparator();
            AddItem(ActionType.MediaPlayPause, "Play / Pause", "\uE037");
            AddItem(ActionType.MediaNext, "Next Track", "\uE044");
            AddItem(ActionType.MediaPrevious, "Previous Track", "\uE045");
            AddItem(ActionType.MediaSeekForward, "Seek Forward", "\uEAC9");
            AddItem(ActionType.MediaSeekBackward, "Seek Backward", "\uEAC3");

            AddSeparator();
            AddItem(ActionType.VolumeUp, "Volume Up", "\uE050");
            AddItem(ActionType.VolumeDown, "Volume Down", "\uE04D");
            AddItem(ActionType.MuteToggle, "Mute Toggle", "\uE710");

            AddSeparator();
            AddItem(ActionType.BrightnessUp, "Brightness Up", "\uE3AC");
            AddItem(ActionType.BrightnessDown, "Brightness Down", "\uE3FA");

            AddSeparator();
            AddItem(ActionType.LaunchApp, "Launch App", "\uEF40");
            AddItem(ActionType.OpenUrl, "Open URL", "\uE250");

            AddSeparator();
            AddItem(ActionType.CloseOsd, "Close OSD", "\uE5CD");
            AddItem(ActionType.ToggleNightLight, "Night Light Toggle", "\uF159");
            AddItem(ActionType.TextExpansion, "Text Expansion", "\uE14F");

            AddSeparator();
            AddItem(ActionType.None, "None", "\uF08C");

            foreach (var item in items)
                PropActionTypeBox.Items.Add(item);
        }

        private string FormatActionTypeName(ActionType at)
        {
            return at switch
            {
                ActionType.Folder => "Playback Control",
                ActionType.LaunchApp => "Launch App",
                ActionType.OpenUrl => "Open URL",
                ActionType.VolumeUp => "Volume Up",
                ActionType.VolumeDown => "Volume Down",
                ActionType.MuteToggle => "Mute Toggle",
                ActionType.BrightnessUp => "Brightness Up",
                ActionType.BrightnessDown => "Brightness Down",
                ActionType.MediaPlayPause => "Play / Pause",
                ActionType.MediaNext => "Next Track",
                ActionType.MediaPrevious => "Previous Track",
                ActionType.MediaSeekForward => "Seek Forward",
                ActionType.MediaSeekBackward => "Seek Backward",
                ActionType.TextExpansion => "Text Expansion",
                ActionType.CloseOsd => "Close OSD",
                ActionType.ToggleNightLight => "Night Light Toggle",
                ActionType.VolumeControl => "Volume Control",
                ActionType.BrightnessControl => "Brightness Control",
                ActionType.None => "None",
                _ => at.ToString()
            };
        }

        private static bool IsToggleType(ActionType type) => type switch
        {
            ActionType.MuteToggle => true,
            ActionType.MediaPlayPause => true,
            ActionType.ToggleNightLight => true,
            _ => false
        };

        private void HideNodeProperties()
        {
            NodeEditPanel.Visibility = Visibility.Collapsed;
            NodeEditDefaultText.Visibility = Visibility.Visible;
            _selectedCanvasIndex = -1;
            RenderCanvas();
        }

        private void PropLabelBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            _canvasNodes[_selectedCanvasIndex].Label = PropLabelBox.Text;
            PropLabelDisplay.Text = PropLabelBox.Text;
            RenderCanvas();
        }

        private void PropActionTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_suppressActionTypeChange) return;
                if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
                if (PropActionTypeBox.SelectedItem is not ActionTypeItem selected) return;
                if (selected.IsSeparator)
                {
                    if (_lastActionTypeItem != null)
                        PropActionTypeBox.SelectedItem = _lastActionTypeItem;
                    return;
                }
                _lastActionTypeItem = selected;
                var newType = selected.Type ?? ActionType.None;

                var node = _canvasNodes[_selectedCanvasIndex];
                node.ActionType = newType;
                node.Category = GetCategoryForType(newType);

                var (defaultGlyph, defaultLabel) = GetDefaultsForAction(newType);
                node.Glyph = defaultGlyph;
                node.Label = defaultLabel;
                PropLabelBox.Text = defaultLabel;
                PropLabelDisplay.Text = defaultLabel;
                PropGlyphPreview.Glyph = defaultGlyph;
                node.ActionData = newType switch
                {
                    ActionType.LaunchApp => "calc",
                    ActionType.OpenUrl => "",
                    ActionType.TextExpansion => "",
                    _ => ""
                };
                PropActionDisplay.Text = FormatActionTypeName(newType);

                PropUrlBox.Visibility = newType == ActionType.OpenUrl ? Visibility.Visible : Visibility.Collapsed;
                if (newType == ActionType.OpenUrl)
                    PropUrlBox.Text = node.ActionData;

                PropAppPathSection.Visibility = newType == ActionType.LaunchApp ? Visibility.Visible : Visibility.Collapsed;
                if (newType == ActionType.LaunchApp)
                    PropAppPathBox.Text = node.ActionData;

                var showIcon = !IsToggleType(newType);
                IconSection.Visibility = showIcon ? Visibility.Visible : Visibility.Collapsed;
                if (showIcon) EnsureIconGalleryLoaded();

                EditSubmenuBtn.Visibility = newType == ActionType.Folder
                    ? Visibility.Visible : Visibility.Collapsed;
                RenderCanvas();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MainWindow] PropActionTypeBox: {ex.Message}"); }
        }

        private void NodeEditSaveBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentProfile();
            OsdService.Instance.ReloadProfile(OsdService.Instance.CurrentProfile.Name);
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

        private void AddActionToRing(string glyph, ActionType type, string label)
        {
            var (defaultGlyph, defaultLabel) = GetDefaultsForAction(type);
            var newNode = new RingNode
            {
                Glyph = !string.IsNullOrEmpty(glyph) ? glyph : defaultGlyph,
                Label = !string.IsNullOrEmpty(label) ? label : defaultLabel,
                ActionType = type,
                Category = GetCategoryForType(type),
                ActionData = type switch
                {
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

        private static ActionCategory GetCategoryForType(ActionType type) => type switch
        {
            ActionType.VolumeControl or ActionType.BrightnessControl => ActionCategory.Group,
            ActionType.Folder => ActionCategory.Folder,
            _ => ActionCategory.Individual
        };

        private static (string glyph, string label) GetDefaultsForAction(ActionType type) => type switch
        {
            ActionType.VolumeUp => ("\uE050", "Volume Up"),
            ActionType.VolumeDown => ("\uE04D", "Volume Down"),
            ActionType.MuteToggle => ("\uE710", "Mute"),
            ActionType.VolumeControl => ("\uE050", "Volume"),
            ActionType.BrightnessUp => ("\uE1AC", "Brightness Up"),
            ActionType.BrightnessDown => ("\uE1AD", "Brightness Down"),
            ActionType.BrightnessControl => ("\uE1AC", "Brightness"),
            ActionType.MediaPlayPause => ("\uEF6A", "Play"),
            ActionType.MediaNext => ("\uE044", "Next"),
            ActionType.MediaPrevious => ("\uE045", "Prev"),
            ActionType.MediaSeekForward => ("\uEAC9", "Seek"),
            ActionType.MediaSeekBackward => ("\uE020", "Rewind"),
            ActionType.Folder => ("\uF6B5", "Playback Control"),
            ActionType.LaunchApp => ("\uEA5F", "App"),
            ActionType.OpenUrl => ("\uE250", "Website"),
            ActionType.TextExpansion => ("\uE86F", "Type"),
            ActionType.ToggleNightLight => ("\uF03D", "Night Light"),
            ActionType.CloseOsd => ("\uE5CD", "Close"),
            _ => ("\uE8B8", "Action"),
        };

        #endregion

        #region Color & Icon Picker

        private bool _iconGalleryLoaded;

        private void PopulateIconGallery()
        {
            _iconGalleryLoaded = false;
            IconGallery.ItemsSource = null;
        }

        private void EnsureIconGalleryLoaded()
        {
            if (_iconGalleryLoaded) return;
            _iconGalleryLoaded = true;
            SearchIcons();
        }

        private void SearchIcons()
        {
            var search = IconSearchBox.Text?.Trim().ToLowerInvariant() ?? "";
            IconGallery.ItemsSource = string.IsNullOrEmpty(search)
                ? MaterialIcons.AllIcons.Take(200).ToList()
                : MaterialIcons.AllIcons
                    .Where(icon => icon.DisplayName.ToLowerInvariant().Contains(search) ||
                                  icon.Name.ToLowerInvariant().Contains(search))
                    .Take(500).ToList();
        }

        private void IconSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_iconGalleryLoaded)
            {
                if (IconSection.Visibility != Visibility.Visible) return;
                _iconGalleryLoaded = true;
            }
            SearchIcons();
        }

        private void IconGallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            if (IconGallery.SelectedItem is not MaterialIconInfo icon) return;
            var node = _canvasNodes[_selectedCanvasIndex];
            node.Glyph = icon.Glyph;
            PropGlyphPreview.Glyph = icon.Glyph;
            RenderCanvas();
        }

        private static string ColorToHex(Color c)
        {
            return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private static Color ComplementaryColor(Color c)
        {
            var r = (byte)(255 - c.R);
            var g = (byte)(255 - c.G);
            var b = (byte)(255 - c.B);
            // Blend with white for a softer complement
            return Color.FromArgb(255,
                (byte)((r + 255) / 2),
                (byte)((g + 255) / 2),
                (byte)((b + 255) / 2));
        }

        private void PrimaryColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (_suppressColorChange || _selectedProfileIndex < 0 || _selectedProfileIndex >= _appProfiles.Count) return;
            var profile = _appProfiles[_selectedProfileIndex];
            var primary = args.NewColor;
            var secondary = ComplementaryColor(primary);

            SecondaryPreview.Background = new SolidColorBrush(secondary);

            profile.PrimaryColor = ColorToHex(primary);
            profile.SecondaryColor = ColorToHex(secondary);
            SaveCurrentProfile();
            SyncCanvasColors(primary, secondary);
            RenderCanvas();
            OsdService.Instance.ReloadProfile(profile.Name);
        }

        private void SyncCanvasColors(Color primary, Color secondary)
        {
            ActiveFill = new SolidColorBrush(primary);
            ActiveForeground = (byte)((primary.R * 299 + primary.G * 587 + primary.B * 114) / 1000) > 128
                ? new SolidColorBrush(Color.FromArgb(255, 0, 0, 0))
                : new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            ActiveStroke = new SolidColorBrush(Color.FromArgb(255,
                (byte)(255 - primary.R), (byte)(255 - primary.G), (byte)(255 - primary.B)));

            InactiveFill = new SolidColorBrush(secondary);
            var bgLum = (byte)((secondary.R * 299 + secondary.G * 587 + secondary.B * 114) / 1000);
            InactiveForeground = bgLum > 128
                ? new SolidColorBrush(Color.FromArgb(255, 0, 0, 0))
                : new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            InactiveStroke = new SolidColorBrush(Color.FromArgb(255, 102, 102, 102));
        }

        private void CollapseColors(bool animate = false)
        {
            if (!_colorsExpanded) return;
            if (animate)
            {
                var currentHeight = ColorBody.ActualHeight;
                if (currentHeight <= 0) currentHeight = 300;
                ColorBody.Height = currentHeight;
                var sb = new Storyboard();
                var da = new DoubleAnimation
                {
                    From = currentHeight,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EnableDependentAnimation = true
                };
                da.Completed += (_, _) => ColorBody.Height = 0;
                Storyboard.SetTarget(da, ColorBody);
                Storyboard.SetTargetProperty(da, "Height");
                sb.Children.Add(da);
                sb.Begin();
            }
            else
            {
                ColorBody.Height = 0;
            }
            ColorChevron.Glyph = "\uE316";
            _colorsExpanded = false;
        }

        private void ExpandColors()
        {
            if (_colorsExpanded) return;
            ColorBody.Height = double.NaN;
            ColorBody.UpdateLayout();
            var targetHeight = ColorBody.ActualHeight;
            if (targetHeight <= 0) targetHeight = 300;
            ColorBody.Height = 0;
            var sb = new Storyboard();
            var da = new DoubleAnimation
            {
                From = 0,
                To = targetHeight,
                Duration = TimeSpan.FromMilliseconds(250),
                EnableDependentAnimation = true
            };
            da.Completed += (_, _) => ColorBody.Height = double.NaN;
            Storyboard.SetTarget(da, ColorBody);
            Storyboard.SetTargetProperty(da, "Height");
            sb.Children.Add(da);
            sb.Begin();
            ColorChevron.Glyph = "\uE313";
            _colorsExpanded = true;
        }

        private void ColorToggleBtn_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_colorsExpanded)
                CollapseColors(animate: true);
            else
                ExpandColors();
        }

        private void SelectProfileColors()
        {
            if (_selectedProfileIndex < 0 || _selectedProfileIndex >= _appProfiles.Count) return;
            var profile = _appProfiles[_selectedProfileIndex];
            _suppressColorChange = true;
            if (TryParseColor(profile.PrimaryColor, out var primary))
            {
                PrimaryColorPicker.Color = primary;
            }
            if (TryParseColor(profile.SecondaryColor, out var secondary))
                SecondaryPreview.Background = new SolidColorBrush(secondary);
            if (TryParseColor(profile.PrimaryColor, out var primColor) &&
                TryParseColor(profile.SecondaryColor, out var secColor))
                SyncCanvasColors(primColor, secColor);
            _suppressColorChange = false;
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            color = Colors.Transparent;
            try
            {
                if (hex.Length >= 7 && hex[0] == '#')
                {
                    var a = hex.Length >= 9 ? System.Convert.ToByte(hex.Substring(1, 2), 16) : (byte)255;
                    var r = System.Convert.ToByte(hex.Substring(hex.Length - 6, 2), 16);
                    var g = System.Convert.ToByte(hex.Substring(hex.Length - 4, 2), 16);
                    var b = System.Convert.ToByte(hex.Substring(hex.Length - 2, 2), 16);
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MainWindow] TryParseColor: {ex.Message}"); }
            return false;
        }

        #endregion

        #region URL & App Picker

        private void PropUrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            _canvasNodes[_selectedCanvasIndex].ActionData = PropUrlBox.Text;
        }

        private void PropAppPathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedCanvasIndex < 0 || _selectedCanvasIndex >= _canvasNodes.Count) return;
            _canvasNodes[_selectedCanvasIndex].ActionData = PropAppPathBox.Text;
        }

        private async void PropChangeAppBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
            };
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".lnk");
            picker.FileTypeFilter.Add(".bat");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                PropAppPathBox.Text = file.Path;
                if (_selectedCanvasIndex >= 0 && _selectedCanvasIndex < _canvasNodes.Count)
                    _canvasNodes[_selectedCanvasIndex].ActionData = file.Path;
            }
        }

        #endregion

    }

    public class ProfileTabItem
    {
        public RingProfile Profile { get; set; } = new();
        public string DisplayName { get; set; } = "";
        public string Icon { get; set; } = "\uE8B8";
        public SolidColorBrush IconBrush { get; set; } = new(Colors.Gray);
    }

    public class AppListItem
    {
        public string DisplayName { get; set; } = "";
        public string FullPath { get; set; } = "";
    }
}
