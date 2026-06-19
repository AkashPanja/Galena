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

    private readonly List<Grid> _optionElements = new();
    private readonly List<Ellipse> _optionCircles = new();
    private readonly List<FontIcon> _optionIcons = new();
    private Storyboard? _currentStoryboard;
    private const double Radius = 120;

    private static readonly RadialOption[] Options =
    {
        new("\uE774", "Chrome"),
        new("\uE8B9", "VLC"),
        new("\uE995", "Vol+"),
        new("\uE994", "Vol-"),
        new("\uE74F", "Mute"),
        new("\uE706", "Bright+"),
        new("\uE708", "Bright-"),
        new("\uE768", "Play"),
    };

    private static readonly SolidColorBrush InactiveFill = new(Color.FromArgb(128, 255, 255, 255));
    private static readonly SolidColorBrush InactiveStroke = new(Color.FromArgb(255, 102, 102, 102));
    private static readonly SolidColorBrush InactiveForeground = new(Color.FromArgb(255, 0, 0, 0));
    private static readonly SolidColorBrush ActiveFill = new(Color.FromArgb(255, 0, 0, 0));
    private static readonly SolidColorBrush ActiveStroke = new(Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush ActiveForeground = new(Color.FromArgb(255, 255, 255, 255));

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

        BuildOptions();
    }

    private void BuildOptions()
    {
        for (int i = 0; i < Options.Length; i++)
        {
            var angle = i * 45 * Math.PI / 180;
            var targetX = Radius * Math.Sin(angle);
            var targetY = -Radius * Math.Cos(angle);

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
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = Options[i].Glyph,
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

        for (int i = 0; i < _optionElements.Count; i++)
        {
            var opt = _optionElements[i];
            var angle = i * 45 * Math.PI / 180;
            var targetX = Radius * Math.Sin(angle);
            var targetY = -Radius * Math.Cos(angle);
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
            _optionCircles[optIndex].Fill = ActiveFill;
            _optionCircles[optIndex].Stroke = ActiveStroke;
            _optionIcons[optIndex].Foreground = ActiveForeground;
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
