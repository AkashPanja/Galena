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
