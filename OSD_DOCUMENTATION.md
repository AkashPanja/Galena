# Floating OSD Menu — WinUI 3 Implementation

## Requirements
- WinUI 3 app (no WPF)
- Floating radial menu with 6 black circular buttons on a frameless window
- Truly transparent background — no white box, no DWM border, no shadow
- Window clipped to a circle (not square)
- Always on top of all windows including fullscreen games
- Buttons invert colors on hover (white bg + black icon) and press (dark gray)

## Techniques Used

### 1. Transparent Composition Surface
WinUI 3's default `SystemBackdrop` paints an opaque white surface. Use WinUIEx's `TransparentTintBackdrop` instead:

```csharp
SystemBackdrop = new TransparentTintBackdrop();
```

Package: `WinUIEx` 2.9.1

### 2. Remove Window Frame
Use `OverlappedPresenter` to strip the border and title bar, then `SetWindowStyle(WindowStyle.Tiled)` to remove the non-client area reservation (prevents content clipping):

```csharp
if (_appWindow.Presenter is OverlappedPresenter presenter)
{
    presenter.SetBorderAndTitleBar(false, false);
    presenter.IsResizable = false;
    presenter.IsMaximizable = false;
    presenter.IsMinimizable = false;
}

this.SetWindowStyle(WindowStyle.Tiled);
```

### 3. DWM Cleanup
DWM adds a 1px border and drop shadow on Windows 11 even on frameless windows. Disable them via these attributes:

```csharp
int showShadow = 0;
DwmSetWindowAttribute(hwnd, 28, ref showShadow, sizeof(int));

int ncrpDisabled = 1;
DwmSetWindowAttribute(hwnd, 2, ref ncrpDisabled, sizeof(int));

int doNotRound = DWMWCP_DONOTROUND; // = 1
DwmSetWindowAttribute(hwnd, 33, ref doNotRound, sizeof(int));

int noBorderColor = DWM_COLOR_NONE; // = 0xFFFFFFFE
DwmSetWindowAttribute(hwnd, 34, ref noBorderColor, sizeof(int));
```

| Attribute | Constant | Value | Effect |
|-----------|----------|-------|--------|
| 28 | DWMWA_SHOWSHADOW | 0 | Remove drop shadow |
| 2 | DWMWA_NCRENDERING_POLICY | 1 (DWMNCRP_DISABLED) | Disable non-client area rendering |
| 33 | DWMWA_WINDOW_CORNER_PREFERENCE | 1 (DWMWCP_DONOTROUND) | Prevent corner rounding |
| 34 | DWMWA_COLOR_WINDOW_BORDER | 0xFFFFFFFE (DWM_COLOR_NONE) | Make 1px border invisible |

### 4. Circular Window Clipping
Use GDI `CreateEllipticRgn` + `SetWindowRgn` to clip the window to a circle. Call AFTER all DWM cleanup, resize, and positioning:

```csharp
var region = CreateEllipticRgn(0, 0, _windowSize, _windowSize);
SetWindowRgn(hwnd, region, true);
```

### 5. Always on Top
Use `SetWindowPos` with `HWND_TOPMOST` (-1):

```csharp
SetWindowPos(hwnd, (IntPtr)(-1), 0, 0, 0, 0,
    SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
```

### 6. Radial Button Layout
Place 6 circular buttons evenly around a center point using trigonometry:

```csharp
double angle = i * (2 * Math.PI / itemCount);
double x = centerX + (radius * Math.Cos(angle)) - (buttonSize / 2);
double y = centerY + (radius * Math.Sin(angle)) - (buttonSize / 2);
```

### 7. Hover/Press Color Inversion
Use `PointerEntered` / `PointerExited` / `PointerPressed` / `PointerReleased` events to swap background and foreground colors on each button.

---

## Execution Order (Critical)
1. `InitializeComponent()`
2. `SystemBackdrop = new TransparentTintBackdrop()`
3. Get HWND via `WindowNative.GetWindowHandle(this)`
4. Get `AppWindow` via `AppWindow.GetFromWindowId(...)`
5. Configure `OverlappedPresenter` (border off, resize off, etc.)
6. Set DWM attributes (shadow=0, NCRP=1)
7. `SetWindowStyle(WindowStyle.Tiled)` — removes NC area space
8. `_appWindow.Resize(...)`
9. Center on screen via `DisplayArea.GetFromWindowId(...)`
10. Set DWM attributes (corner preference=1, border color=none)
11. `SetWindowRgn(...)` — clip to circle
12. `SetWindowPos(HWND_TOPMOST, ...)` — always on top
13. `CreateRadialMenu()`

---

## Required Packages

```xml
<PackageReference Include="WinUIEx" Version="2.9.1" />
```

---

## Complete File Listing

### `OsdWindow.xaml`
```xml
<Window x:Class="OsdTest.OsdWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Background="Transparent">
        <Canvas x:Name="OsdCanvas" Width="300" Height="300"
                HorizontalAlignment="Center" VerticalAlignment="Center" />
    </Grid>
</Window>
```

### `OsdWindow.xaml.cs`
```csharp
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;
using WinUIEx;

namespace OsdTest;

public sealed partial class OsdWindow : Window
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWA_COLOR_WINDOW_BORDER = 34;
    private const int DWM_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    private AppWindow _appWindow;
    private readonly int _windowSize = 300;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateEllipticRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public OsdWindow()
    {
        InitializeComponent();

        SystemBackdrop = new TransparentTintBackdrop();

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        int showShadow = 0;
        DwmSetWindowAttribute(hwnd, 28, ref showShadow, sizeof(int));

        int ncrpDisabled = 1;
        DwmSetWindowAttribute(hwnd, 2, ref ncrpDisabled, sizeof(int));

        this.SetWindowStyle(WindowStyle.Tiled);

        _appWindow.Resize(new SizeInt32(_windowSize, _windowSize));

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
        int centerX = displayArea.WorkArea.X + ((displayArea.WorkArea.Width - _windowSize) / 2);
        int centerY = displayArea.WorkArea.Y + ((displayArea.WorkArea.Height - _windowSize) / 2);
        _appWindow.Move(new PointInt32(centerX, centerY));

        int doNotRound = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref doNotRound, sizeof(int));

        int noBorderColor = DWM_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_COLOR_WINDOW_BORDER, ref noBorderColor, sizeof(int));

        var region = CreateEllipticRgn(0, 0, _windowSize, _windowSize);
        SetWindowRgn(hwnd, region, true);

        SetWindowPos(hwnd, (IntPtr)(-1), 0, 0, 0, 0,
            SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);

        CreateRadialMenu();
    }

    private void CreateRadialMenu()
    {
        string[] iconGlyphs = { "\uE701", "\uE702", "\uE703", "\uE704", "\uE705", "\uE706" };
        int itemCount = iconGlyphs.Length;
        double radius = 100;
        double buttonSize = 64;
        double centerX = OsdCanvas.Width / 2;
        double centerY = OsdCanvas.Height / 2;

        for (int i = 0; i < itemCount; i++)
        {
            double angle = i * (2 * Math.PI / itemCount);
            double x = centerX + (radius * Math.Cos(angle)) - (buttonSize / 2);
            double y = centerY + (radius * Math.Sin(angle)) - (buttonSize / 2);

            var optionButton = new Button
            {
                Width = buttonSize,
                Height = buttonSize,
                CornerRadius = new CornerRadius(buttonSize / 2),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
                BorderThickness = new Thickness(0),
                Content = new FontIcon
                {
                    Glyph = iconGlyphs[i],
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    FontSize = 24
                }
            };

            optionButton.PointerEntered += (s, e) =>
            {
                optionButton.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
                ((FontIcon)optionButton.Content).Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black);
            };
            optionButton.PointerExited += (s, e) =>
            {
                optionButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
                ((FontIcon)optionButton.Content).Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            };
            optionButton.PointerPressed += (s, e) =>
            {
                optionButton.Background = new SolidColorBrush(Microsoft.UI.Colors.DarkGray);
                ((FontIcon)optionButton.Content).Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black);
            };
            optionButton.PointerReleased += (s, e) =>
            {
                optionButton.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
                ((FontIcon)optionButton.Content).Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black);
            };

            Canvas.SetLeft(optionButton, x);
            Canvas.SetTop(optionButton, y);
            OsdCanvas.Children.Add(optionButton);
        }
    }
}
```

### `MainWindow.xaml`
```xml
<Window x:Class="OsdTest.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Background="#1e1e1e">
        <Button x:Name="ShowOsdBtn" Content="Show OSD" Click="ShowOsdBtn_Click"
                HorizontalAlignment="Center" VerticalAlignment="Center"
                Width="200" Height="60" FontSize="20" />
    </Grid>
</Window>
```

### `MainWindow.xaml.cs`
```csharp
using Microsoft.UI.Xaml;

namespace OsdTest;

public sealed partial class MainWindow : Window
{
    private OsdWindow? _osdWindow;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void ShowOsdBtn_Click(object sender, RoutedEventArgs e)
    {
        _osdWindow ??= new OsdWindow();
        _osdWindow.Activate();
    }
}
```

---

## Build & Run
```
dotnet build -p:Platform=x64
dotnet run -p:Platform=x64
```

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| White background / white box | WinUI 3 default backdrop paints opaque composition surface | Use `TransparentTintBackdrop` from WinUIEx |
| Right/bottom content clipped | Non-client area reserves space | Call `SetWindowStyle(WindowStyle.Tiled)` after presenter config |
| Rectangular frame persists | DWM border and shadow | Set `DWMWA_SHOWSHADOW=0`, `DWMWA_NCRENDERING_POLICY=1` |
| DWM border still visible as 1px line | Windows 11 always draws a thin frame border | Use `DWMWA_COLOR_WINDOW_BORDER=DWM_COLOR_NONE (0xFFFFFFFE)` |
| Corner artifacts/jagged white lines after SetWindowRgn | DWM corner rounding conflicts with region clip | Set `DWMWA_WINDOW_CORNER_PREFERENCE=1 (DWMWCP_DONOTROUND)` not 2 |
| Window not topmost | Not set after all window changes | Call `SetWindowPos(HWND_TOPMOST)` last |
