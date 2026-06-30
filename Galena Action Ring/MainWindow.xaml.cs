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
using GalenaActionRing.Services;

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


        private readonly ProfileManager _profileManager = new();
        private CanvasEditor _canvasEditor = null!;
        private bool _suppressTabChange;

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

            _canvasEditor = new CanvasEditor(new CanvasEditorDependencies
            {
                RingCanvas = RingCanvas,
                NodeEditDefaultText = NodeEditDefaultText,
                NodeEditPanel = NodeEditPanel,
                PropGlyphPreview = PropGlyphPreview,
                PropLabelDisplay = PropLabelDisplay,
                PropActionDisplay = PropActionDisplay,
                PropLabelBox = PropLabelBox,
                PropUrlBox = PropUrlBox,
                PropAppPathBox = PropAppPathBox,
                PropAppPathSection = PropAppPathSection,
                PropActionTypeBox = PropActionTypeBox,
                EditSubmenuBtn = EditSubmenuBtn,
                CanvasBackBtn = CanvasBackBtn,
                CanvasTitle = CanvasTitle,
                IconSection = IconSection,
                IconGallery = IconGallery,
                IconSearchBox = IconSearchBox,
                ColorBody = ColorBody,
                ColorChevron = ColorChevron,
                PrimaryColorPicker = PrimaryColorPicker,
                SecondaryPreview = SecondaryPreview,
                AppRoot = AppRoot,
            })
            {
                ProfileManager = _profileManager,
            };

            _profileManager.ProfilesChanged += OnProfilesChanged;
            _profileManager.ProfileLoaded += OnProfileLoaded;
            _profileManager.Init();
            _profileManager.Load();
            _ = FindHidDevicesAsync();

            _canvasEditor.PopulateActionTypeBox((DataTemplate)AppRoot.Resources["ActionTypeItemTemplate"]);

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

            _profileManager.Reset();

            try
            {
                var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\GalenaActionRing", true);
                key?.DeleteValue("FirstRunDone", false);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MainWindow] ResetAppButton: {ex.Message}"); }

            _profileManager.Load();
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

        #region Profile & Canvas

        private void OnProfilesChanged()
        {
            _suppressTabChange = true;
            AppProfileTabs.Items.Clear();
            for (int i = 0; i < _profileManager.Profiles.Count; i++)
            {
                var p = _profileManager.Profiles[i];
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
            if (_profileManager.SelectedIndex >= 0 && _profileManager.SelectedIndex < AppProfileTabs.Items.Count)
                AppProfileTabs.SelectedIndex = _profileManager.SelectedIndex;
            _suppressTabChange = false;
            PopulateIconGallery();
        }

        private void OnProfileLoaded(RingProfile profile)
        {
            OsdService.Instance.CurrentProfile = profile;
            var editingNodes = _profileManager.EditingCopy?.Nodes ?? profile.Nodes;
            _canvasEditor.LoadProfile(editingNodes, profile);
            OsdService.Instance.ReloadProfile(profile.Name);
        }

        private void AppProfileTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressTabChange) return;
            if (AppProfileTabs.SelectedIndex < 0) return;
            _profileManager.SelectIndex(AppProfileTabs.SelectedIndex);
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

            if (_profileManager.Add(ringName) == null)
            {
                await ShowErrorDialog($"A ring named \"{ringName}\" already exists. Choose a different name.");
                return;
            }

            _profileManager.Load();
        }

        private async void DeleteRingBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_profileManager.CanDelete)
            {
                await ShowErrorDialog("You must have at least one ring. Cannot delete the last ring.");
                return;
            }

            var profile = _profileManager.Profiles[_profileManager.SelectedIndex];
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

            _profileManager.Delete();
            _profileManager.Load();
        }

        private async void RenameRingBtn_Click(object sender, RoutedEventArgs e)
        {
            var profile = _profileManager.Profiles[_profileManager.SelectedIndex];

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
            var error = _profileManager.Rename(newName);
            if (error != null)
            {
                await ShowErrorDialog(error);
                return;
            }

            CanvasTitle.Text = newName;
        }

        #region Canvas Delegates

        private void RingCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var pos = e.GetCurrentPoint(RingCanvas).Position;
            _canvasEditor.PointerPressed(pos, e);
        }

        private void RingCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var pos = e.GetCurrentPoint(RingCanvas).Position;
            _canvasEditor.PointerMoved(pos);
        }

        private void RingCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _canvasEditor.PointerReleased(e);
        }

        private void RingCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            _canvasEditor.PointerCanceled();
        }

        private void PropLabelBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _canvasEditor.PropLabelChanged();
        }

        private void PropActionTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _canvasEditor.PropActionTypeChanged();
        }

        private void NodeEditSaveBtn_Click(object sender, RoutedEventArgs e)
        {
            var error = _profileManager.Save();
            if (error != null)
                _ = ShowErrorDialog(error);
            else
                OsdService.Instance.ReloadProfile(OsdService.Instance.CurrentProfile.Name);
        }

        private void EditSubmenuBtn_Click(object sender, RoutedEventArgs e)
        {
            _canvasEditor.EnterSubmenu();
        }

        private void CanvasBackBtn_Click(object sender, RoutedEventArgs e)
        {
            _canvasEditor.Back();
        }

        private void AddActionToRing(string glyph, ActionType type, string label)
        {
            _canvasEditor.AddNode(glyph, type, label);
        }

        private void PopulateIconGallery()
        {
            _canvasEditor.PopulateIconGallery();
        }

        private void IconSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _canvasEditor.IconSearchChanged();
        }

        private void IconGallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _canvasEditor.IconSelected();
        }

        private void PrimaryColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            var profile = _profileManager.Profiles.ElementAtOrDefault(_profileManager.SelectedIndex);
            _canvasEditor.PrimaryColorChanged(args.NewColor, profile);
            if (profile != null)
                OsdService.Instance.ReloadProfile(profile.Name);
        }

        private void ColorToggleBtn_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _canvasEditor.ToggleColors();
        }

        private void PropUrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _canvasEditor.PropUrlChanged();
        }

        private void PropAppPathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _canvasEditor.PropAppPathChanged();
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
                _canvasEditor.SetAppPathOnSelected(file.Path);
            }
        }

        #endregion

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
