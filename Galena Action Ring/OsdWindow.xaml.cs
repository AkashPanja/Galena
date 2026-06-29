using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using WinUIEx;

namespace Galena_Action_Ring;

public sealed partial class OsdWindow : Window
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWA_COLOR_WINDOW_BORDER = 34;
    private const int DWM_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOZORDER = 0x0004;
    private readonly List<Grid> _optionElements = new();
    private readonly List<Ellipse> _optionCircles = new();
    private readonly List<FontIcon> _optionIcons = new();
    private Storyboard? _currentStoryboard;
    private Storyboard? _seekLoop;
    private DoubleAnimation _seekAnimX = null!;
    private DoubleAnimation _seekFade = null!;
    private bool _seekLoopInitialized;
    private bool _seekLoopActive;
    private bool _lastSeekForward;
    private double _radius = 120;

    private SolidColorBrush InactiveFill = new(Color.FromArgb(128, 255, 255, 255));
    private SolidColorBrush InactiveStroke = new(Color.FromArgb(255, 102, 102, 102));
    private SolidColorBrush InactiveForeground = new(Color.FromArgb(255, 0, 0, 0));
    private SolidColorBrush ActiveFill = new(Color.FromArgb(255, 0, 0, 0));
    private SolidColorBrush _seekIconBrush = new(Colors.White);
    private SolidColorBrush ActiveStroke = new(Color.FromArgb(255, 255, 255, 255));
    private SolidColorBrush ActiveForeground = new(Color.FromArgb(255, 255, 255, 255));

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateEllipticRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public OsdWindow()
    {
        InitializeComponent();

        SystemBackdrop = new TransparentTintBackdrop();

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
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

        int doNotRound = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref doNotRound, sizeof(int));

        int noBorderColor = DWM_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_COLOR_WINDOW_BORDER, ref noBorderColor, sizeof(int));

        var region = CreateEllipticRgn(0, 0, 400, 400);
        SetWindowRgn(hwnd, region, true);

        SetWindowPos(hwnd, (IntPtr)(-1), 0, 0, 0, 0,
            SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x80;
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_TOOLWINDOW));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    public void ApplyProfileColors(string primaryHex, string secondaryHex)
    {
        if (!string.IsNullOrEmpty(primaryHex) && TryParseColor(primaryHex, out var primary))
        {
            ActiveFill = new SolidColorBrush(primary);
            ActiveForeground = Luminance(primary) > 128 ? new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)) : new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            ActiveStroke = new SolidColorBrush(Color.FromArgb(255,
                (byte)(255 - primary.R), (byte)(255 - primary.G), (byte)(255 - primary.B)));
            // Update sub-menu background radial gradient with primary color
            var gradient = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = new Windows.Foundation.Point(80, 80),
                GradientOrigin = new Windows.Foundation.Point(80, 80),
                RadiusX = 80,
                RadiusY = 80,
            };
            gradient.GradientStops.Add(new GradientStop { Offset = 0.0, Color = Color.FromArgb(0xCC, primary.R, primary.G, primary.B) });
            gradient.GradientStops.Add(new GradientStop { Offset = 1.0, Color = Color.FromArgb(0x00, primary.R, primary.G, primary.B) });
            SubMenuBgEllipse.Fill = gradient;
        }
        if (!string.IsNullOrEmpty(secondaryHex) && TryParseColor(secondaryHex, out var secondary))
        {
            InactiveFill = new SolidColorBrush(secondary);
            var bgLum = Luminance(secondary);
            InactiveForeground = bgLum > 128 ? new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)) : new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            InactiveStroke = new SolidColorBrush(Color.FromArgb(255, 102, 102, 102));
            _seekIconBrush = new SolidColorBrush(secondary);
        }
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
        catch { }
        return false;
    }

    private static byte Luminance(Color c)
    {
        return (byte)((c.R * 299 + c.G * 587 + c.B * 114) / 1000);
    }

    #region Radial Progress

    public void ShowRadialProgress(string label, int percent)
    {
        RadialLabel.Text = label;
        UpdateRadialPercent(percent);
        RadialProgressLayer.Visibility = Visibility.Visible;
        RadialProgressLayer.Opacity = 0;

        OptionsContainer.Visibility = Visibility.Collapsed;
        CenterButton.Visibility = Visibility.Collapsed;

        var fadeIn = new DoubleAnimation
        {
            From = 0, To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
        };
        Storyboard.SetTarget(fadeIn, RadialProgressLayer);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(fadeIn);
        sb.Begin();
    }

    public void UpdateRadialPercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        RadialPercent.Text = $"{percent}%";
    }

    public void UpdateRadialStatus(string status)
    {
        RadialStatus.Text = status;
    }

    public void HideRadialProgress()
    {
        var fadeOut = new DoubleAnimation
        {
            From = 1, To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
        };
        Storyboard.SetTarget(fadeOut, RadialProgressLayer);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(fadeOut);
        sb.Completed += (_, _) =>
        {
            RadialProgressLayer.Visibility = Visibility.Collapsed;
            OptionsContainer.Visibility = Visibility.Visible;
            CenterButton.Visibility = Visibility.Visible;
        };
        sb.Begin();
    }

    public void HideRadialLayer()
    {
        RadialProgressLayer.Visibility = Visibility.Collapsed;
        RadialProgressLayer.Opacity = 0;
        OptionsContainer.Visibility = Visibility.Visible;
        CenterButton.Visibility = Visibility.Visible;
    }

    #endregion

    public void SetCenterGlyph(string glyph)
    {
        CenterIcon.Glyph = glyph;
    }

    public void LoadNodes(List<RingNode> nodes, int radius)
    {
        _radius = radius;
        OptionsContainer.Children.Clear();
        _optionElements.Clear();
        _optionCircles.Clear();
        _optionIcons.Clear();

        var count = nodes.Count;
        if (count == 0) return;

        double step = 360.0 / count;

        for (int i = 0; i < count; i++)
        {
            var angle = i * step * Math.PI / 180;
            var targetX = _radius * Math.Sin(angle);
            var targetY = -_radius * Math.Cos(angle);

            var grid = new Grid
            {
                Width = 56,
                Height = 56,
                Opacity = 0,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            };

            var transform = new CompositeTransform
            {
                TranslateX = 0,
                TranslateY = 0,
            };
            grid.RenderTransform = transform;

            var ellipse = new Ellipse
            {
                Width = 56,
                Height = 56,
                Fill = InactiveFill,
                StrokeThickness = 0,
            };

            var icon = new FontIcon
            {
                FontFamily = new FontFamily(MaterialIcons.FontFamilyName),
                Glyph = nodes[i].Glyph,
                Foreground = InactiveForeground,
                FontSize = 24,
            };

            grid.Children.Add(ellipse);
            grid.Children.Add(icon);
            OptionsContainer.Children.Add(grid);
            _optionElements.Add(grid);
            _optionCircles.Add(ellipse);
            _optionIcons.Add(icon);
        }
    }

    public void ShowMenu()
    {
        if (_optionElements.Count == 0) return;

        _currentStoryboard?.Stop();
        var storyboard = new Storyboard();
        _currentStoryboard = storyboard;

        foreach (var opt in _optionElements)
        {
            var t = (CompositeTransform)opt.RenderTransform;
            t.TranslateX = 0;
            t.TranslateY = 0;
            t.ScaleX = 0.8;
            t.ScaleY = 0.8;
            opt.Opacity = 0;
        }

        CenterButtonTransform.ScaleX = 0.8;
        CenterButtonTransform.ScaleY = 0.8;

        var centerScaleX = new DoubleAnimation
        {
            From = 0.8, To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 },
        };
        Storyboard.SetTarget(centerScaleX, CenterButton);
        Storyboard.SetTargetProperty(centerScaleX, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)");
        storyboard.Children.Add(centerScaleX);

        var centerScaleY = new DoubleAnimation
        {
            From = 0.8, To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 },
        };
        Storyboard.SetTarget(centerScaleY, CenterButton);
        Storyboard.SetTargetProperty(centerScaleY, "(UIElement.RenderTransform).(CompositeTransform.ScaleY)");
        storyboard.Children.Add(centerScaleY);

        var centerFade = new DoubleAnimation
        {
            From = 0, To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(100)),
        };
        Storyboard.SetTarget(centerFade, CenterButton);
        Storyboard.SetTargetProperty(centerFade, "Opacity");
        storyboard.Children.Add(centerFade);

        int count = _optionElements.Count;
        double step = 360.0 / count;

        for (int i = 0; i < count; i++)
        {
            var opt = _optionElements[i];
            var angle = i * step * Math.PI / 180;
            var targetX = _radius * Math.Sin(angle);
            var targetY = -_radius * Math.Cos(angle);
            var delay = TimeSpan.FromMilliseconds(i * 30);

            var animX = new DoubleAnimation
            {
                From = 0, To = targetX,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                BeginTime = delay,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 },
            };
            Storyboard.SetTarget(animX, opt);
            Storyboard.SetTargetProperty(animX, "(UIElement.RenderTransform).(CompositeTransform.TranslateX)");
            storyboard.Children.Add(animX);

            var animY = new DoubleAnimation
            {
                From = 0, To = targetY,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                BeginTime = delay,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 },
            };
            Storyboard.SetTarget(animY, opt);
            Storyboard.SetTargetProperty(animY, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)");
            storyboard.Children.Add(animY);

            var scaleX = new DoubleAnimation
            {
                From = 0.8, To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                BeginTime = delay,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 },
            };
            Storyboard.SetTarget(scaleX, opt);
            Storyboard.SetTargetProperty(scaleX, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)");
            storyboard.Children.Add(scaleX);

            var scaleY = new DoubleAnimation
            {
                From = 0.8, To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                BeginTime = delay,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 },
            };
            Storyboard.SetTarget(scaleY, opt);
            Storyboard.SetTargetProperty(scaleY, "(UIElement.RenderTransform).(CompositeTransform.ScaleY)");
            storyboard.Children.Add(scaleY);

            var animOp = new DoubleAnimation
            {
                From = 0, To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(100)),
                BeginTime = delay,
            };
            Storyboard.SetTarget(animOp, opt);
            Storyboard.SetTargetProperty(animOp, "Opacity");
            storyboard.Children.Add(animOp);
        }

        storyboard.Begin();
    }

    public void HideMenu(Action? onCompleted = null)
    {
        _currentStoryboard?.Stop();
        var storyboard = new Storyboard();
        _currentStoryboard = storyboard;

        storyboard.Completed += (_, _) =>
        {
            _currentStoryboard = null;
            onCompleted?.Invoke();
        };

        var centerFade = new DoubleAnimation
        {
            From = CenterButton.Opacity, To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
        };
        Storyboard.SetTarget(centerFade, CenterButton);
        Storyboard.SetTargetProperty(centerFade, "Opacity");
        storyboard.Children.Add(centerFade);

        var centerScaleX = new DoubleAnimation
        {
            From = CenterButtonTransform.ScaleX, To = 0.9,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
        };
        Storyboard.SetTarget(centerScaleX, CenterButton);
        Storyboard.SetTargetProperty(centerScaleX, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)");
        storyboard.Children.Add(centerScaleX);

        var centerScaleY = new DoubleAnimation
        {
            From = CenterButtonTransform.ScaleY, To = 0.9,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
        };
        Storyboard.SetTarget(centerScaleY, CenterButton);
        Storyboard.SetTargetProperty(centerScaleY, "(UIElement.RenderTransform).(CompositeTransform.ScaleY)");
        storyboard.Children.Add(centerScaleY);

        for (int i = 0; i < _optionElements.Count; i++)
        {
            var opt = _optionElements[i];
            var t = (CompositeTransform)opt.RenderTransform;
            var fromX = t.TranslateX;
            var fromY = t.TranslateY;

            var animX = new DoubleAnimation
            {
                From = fromX, To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn },
            };
            Storyboard.SetTarget(animX, opt);
            Storyboard.SetTargetProperty(animX, "(UIElement.RenderTransform).(CompositeTransform.TranslateX)");
            storyboard.Children.Add(animX);

            var animY = new DoubleAnimation
            {
                From = fromY, To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn },
            };
            Storyboard.SetTarget(animY, opt);
            Storyboard.SetTargetProperty(animY, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)");
            storyboard.Children.Add(animY);

            var scaleX = new DoubleAnimation
            {
                From = t.ScaleX, To = 0.9,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            };
            Storyboard.SetTarget(scaleX, opt);
            Storyboard.SetTargetProperty(scaleX, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)");
            storyboard.Children.Add(scaleX);

            var scaleY = new DoubleAnimation
            {
                From = t.ScaleY, To = 0.9,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            };
            Storyboard.SetTarget(scaleY, opt);
            Storyboard.SetTargetProperty(scaleY, "(UIElement.RenderTransform).(CompositeTransform.ScaleY)");
            storyboard.Children.Add(scaleY);

            var animOp = new DoubleAnimation
            {
                From = opt.Opacity, To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            };
            Storyboard.SetTarget(animOp, opt);
            Storyboard.SetTargetProperty(animOp, "Opacity");
            storyboard.Children.Add(animOp);
        }

        storyboard.Begin();
    }

    public void ShowSubMenuBg()
    {
        SubMenuBgLayer.Visibility = Visibility.Visible;
    }

    public void HideSubMenuBg()
    {
        SubMenuBgLayer.Visibility = Visibility.Collapsed;
    }

    public void ShowSeekIndicator(bool forward)
    {
        if (!_seekLoopInitialized)
        {
            _seekLoop = new Storyboard();
            _seekLoop.Completed += (_, _) =>
            {
                _seekLoopActive = false;
                SeekIndicator.Opacity = 0;
            };

            _seekAnimX = new DoubleAnimation
            {
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(_seekAnimX, SeekIndicator);
            Storyboard.SetTargetProperty(_seekAnimX, "(UIElement.RenderTransform).(CompositeTransform.TranslateX)");
            _seekLoop.Children.Add(_seekAnimX);

            _seekFade = new DoubleAnimation
            {
                Duration = TimeSpan.FromMilliseconds(400),
            };
            Storyboard.SetTarget(_seekFade, SeekIndicator);
            Storyboard.SetTargetProperty(_seekFade, "Opacity");
            _seekLoop.Children.Add(_seekFade);

            _seekLoopInitialized = true;
        }

        SeekIndicator.Glyph = forward ? "\uEAC9" : "\uEAC3";
        SeekIndicator.Foreground = _seekIconBrush;

        double currentX;
        double currentOpacity;

        if (!_seekLoopActive)
        {
            currentX = forward ? 20 : -20;
            currentOpacity = 1.0;
            SeekIndicatorTransform.TranslateX = currentX;
            SeekIndicator.Opacity = currentOpacity;
        }
        else if (forward != _lastSeekForward)
        {
            _seekLoop?.Stop();
            currentX = forward ? 20 : -20;
            currentOpacity = 1.0;
            SeekIndicatorTransform.TranslateX = currentX;
            SeekIndicator.Opacity = currentOpacity;
        }
        else
        {
            _seekLoop?.Stop();
            currentX = SeekIndicatorTransform.TranslateX;
            currentOpacity = SeekIndicator.Opacity;
        }

        _seekAnimX.From = currentX;
        _seekAnimX.To = forward ? 80 : -80;
        _seekFade.From = currentOpacity;
        _seekFade.To = 0.0;
        _seekLoopActive = true;
        _lastSeekForward = forward;
        _seekLoop?.Begin();
    }

    public void StopSeekLoop()
    {
        _seekLoop?.Stop();
        _seekLoopActive = false;
        SeekIndicator.Opacity = 0;
    }

    public void UpdateNodeIcon(int nodeIndex, string glyph)
    {
        if (nodeIndex < 0 || nodeIndex >= _optionIcons.Count) return;
        _optionIcons[nodeIndex].Glyph = glyph;
    }

    public void SelectOption(int index)
    {
        SetAllOptionsInactive();

        if (index == 0)
        {
            CenterEllipse.Fill = ActiveFill;
            CenterIcon.Foreground = ActiveForeground;
        }
        else
        {
            CenterEllipse.Fill = InactiveFill;
            CenterIcon.Foreground = InactiveForeground;

            var optIndex = index - 1;
            if (optIndex < _optionCircles.Count)
            {
                _optionCircles[optIndex].Fill = ActiveFill;
                _optionCircles[optIndex].Stroke = ActiveStroke;
                _optionIcons[optIndex].Foreground = ActiveForeground;
            }
        }
    }

    private void SetAllOptionsInactive()
    {
        for (int i = 0; i < _optionElements.Count; i++)
        {
            _optionCircles[i].Fill = InactiveFill;
            _optionCircles[i].Stroke = InactiveStroke;
            _optionIcons[i].Foreground = InactiveForeground;
        }
    }
}
