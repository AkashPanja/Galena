using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.InteropServices;
using Windows.UI;

namespace GalenaActionRing.Services;

internal class TransparentBackdrop : SystemBackdrop
{
    private static readonly Lazy<Windows.UI.Composition.Compositor> _compositor = new(() =>
    {
        WindowsSystemDispatcherQueueHelper.EnsureWindowsSystemDispatcherQueueController();
        return new Windows.UI.Composition.Compositor();
    });

    private static Windows.UI.Composition.Compositor Compositor => _compositor.Value;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        connectedTarget.SystemBackdrop = Compositor.CreateColorBrush(Color.FromArgb(0, 255, 0, 255));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        disconnectedTarget.SystemBackdrop = null;
    }
}

internal static class WindowsSystemDispatcherQueueHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        internal int dwSize;
        internal int threadType;
        internal int apartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController([In] DispatcherQueueOptions options, [In, Out, MarshalAs(UnmanagedType.IUnknown)] ref object dispatcherQueueController);

    private static object? _dispatcherQueueController;

    public static void EnsureWindowsSystemDispatcherQueueController()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null)
            return;

        if (_dispatcherQueueController == null)
        {
            var options = new DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf(typeof(DispatcherQueueOptions)),
                threadType = 2,
                apartmentType = 2,
            };
            object controller = null!;
            CreateDispatcherQueueController(options, ref controller);
            _dispatcherQueueController = controller;
        }
    }
}
