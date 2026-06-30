using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace GalenaActionRing;

public sealed partial class TestOsdWindow : Window
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOPMOST = 0x8;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_CAPTION = 0xC00000;
    private const int WS_THICKFRAME = 0x40000;
    private const int WS_SYSMENU = 0x80000;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_FRAMECHANGED = 0x0020;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const uint LWA_COLORKEY = 0x00000001;
    private const uint LWA_ALPHA = 0x00000002;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private readonly IntPtr _hwnd;
    private Grid? _option;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };

    public TestOsdWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        SetupTransparency();
        BuildOption();
        CenterWindow();
        _timer.Tick += (_, _) => Hide();
    }

    private void SetupTransparency()
    {
        // Remove frame (popup style)
        var style = (int)GetWindowLongPtr(_hwnd, GWL_STYLE);
        SetWindowLongPtr(_hwnd, GWL_STYLE, (IntPtr)(unchecked((int)0x80000000) | style & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU)));

        // Add layered + transparent + topmost + toolwindow
        var exStyle = (int)GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)(exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW));

        // Color-key: magenta â†’ transparent
        SetLayeredWindowAttributes(_hwnd, 0x00FF00FF, 255, LWA_COLORKEY);

        // Win11 corner fix
        int cornerPref = 0;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    private void BuildOption()
    {
        var angle = 0.0;
        var rad = angle * Math.PI / 180.0;
        var x = 120.0 * Math.Cos(rad);
        var y = 120.0 * Math.Sin(rad);

        var glyph = new TextBlock
        {
            Text = "\uE8A7",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 22,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _option = new Grid
        {
            Width = 52,
            Height = 52,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            CornerRadius = new CornerRadius(26),
            RenderTransform = new CompositeTransform { TranslateX = x, TranslateY = y },
            Opacity = 0,
        };

        var tooltip = new ToolTip { Content = "Chrome" };
        ToolTipService.SetToolTip(_option, tooltip);
        _option.Children.Add(glyph);
        OptionsContainer.Children.Add(_option);
    }

    public void Show()
    {
        var el = _option;
        if (el == null) return;

        el.Opacity = 0;
        ((CompositeTransform)el.RenderTransform).TranslateX = 0;
        ((CompositeTransform)el.RenderTransform).TranslateY = 0;

        var sb = new Storyboard();

        var fade = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(1000)), EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fade, el);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);

        var xa = new DoubleAnimation { From = 0, To = 120.0, Duration = new Duration(TimeSpan.FromMilliseconds(1000)), EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(xa, el);
        Storyboard.SetTargetProperty(xa, "(UIElement.RenderTransform).(CompositeTransform.TranslateX)");
        sb.Children.Add(xa);

        sb.Begin();

        var hwnd = _hwnd;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

        _timer.Stop();
        _timer.Start();
    }

    public void Hide()
    {
        _timer.Stop();
        var el = _option;
        if (el == null) return;

        var sb = new Storyboard();
        var dur = new Duration(TimeSpan.FromMilliseconds(300));

        var fade = new DoubleAnimation { From = 1, To = 0, Duration = dur };
        Storyboard.SetTarget(fade, el);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);

        var xa = new DoubleAnimation { From = 120.0, To = 0, Duration = dur };
        Storyboard.SetTarget(xa, el);
        Storyboard.SetTargetProperty(xa, "(UIElement.RenderTransform).(CompositeTransform.TranslateX)");
        sb.Children.Add(xa);

        sb.Completed += (_, _) => ShowWindow(_hwnd, 0);
        sb.Begin();
    }

    private void CenterWindow()
    {
        var screenW = GetSystemMetrics(0);
        var screenH = GetSystemMetrics(1);
        var x = (screenW - 400) / 2;
        var y = (screenH - 400) / 2;
        SetWindowPos(_hwnd, IntPtr.Zero, x, y, 400, 400, SWP_NOZORDER);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
