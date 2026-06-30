using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace GalenaActionRing;

internal class TrayService : IDisposable
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;
    private const int SC_MINIMIZE = 0xF020;
    private const int WM_TRAYICON = 0x0400 + 100;
    private const int WM_COMMAND = 0x0111;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int NIM_ADD = 0;
    private const int NIM_DELETE = 2;
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int GWL_WNDPROC = -4;
    private const int MF_STRING = 0;
    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_BOTTOMALIGN = 0x0020;
    private const int ID_OPEN = 1;
    private const int ID_CLOSE = 2;

    private Window _window = null!;
    private IntPtr _hwnd;
    private IntPtr _originalWndProc;
    private WndProcDelegate _wndProcDelegate = null!;
    private bool _disposed;
    private bool _iconAdded;
    private static int _nextId = 1;
    private readonly int _id;

    public event Action? ShowRequested;
    public event Action? HideRequested;
    public event Action? CloseRequested;
    public event Action? ExitRequested;

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string exeFileName, int iconIndex);

    private static readonly IntPtr IDI_APPLICATION = new(32512);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(IntPtr hMenu, int uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    public TrayService()
    {
        _id = _nextId++;
    }

    public void Setup(Window window)
    {
        _window = window;
        _hwnd = WindowNative.GetWindowHandle(window);

        _wndProcDelegate = WndProc;
        _originalWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

        AddTrayIcon();
    }

    private void AddTrayIcon()
    {
        var hIcon = ExtractIcon(IntPtr.Zero, Environment.ProcessPath ?? "", 0);
        if (hIcon == IntPtr.Zero)
            hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _id,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = "Galena Action Ring"
        };

        Shell_NotifyIcon(NIM_ADD, ref nid);
        _iconAdded = true;
    }

    public void RemoveTrayIcon()
    {
        if (!_iconAdded) return;
        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _id
        };
        Shell_NotifyIcon(NIM_DELETE, ref nid);
        _iconAdded = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        const int scMask = 0xFFF0;

        if (msg == WM_SYSCOMMAND)
        {
            int cmd = wParam.ToInt32() & scMask;
            if (cmd == SC_MINIMIZE)
            {
                _ = _window.DispatcherQueue.TryEnqueue(() => HideRequested?.Invoke());
                return IntPtr.Zero;
            }
            if (cmd == SC_CLOSE)
            {
                _ = _window.DispatcherQueue.TryEnqueue(() => CloseRequested?.Invoke());
                return IntPtr.Zero;
            }
        }
        else if (msg == WM_TRAYICON)
        {
            var lParamVal = lParam.ToInt32();
            if (lParamVal == WM_LBUTTONUP || lParamVal == WM_LBUTTONDBLCLK)
            {
                _ = _window.DispatcherQueue.TryEnqueue(() => ShowRequested?.Invoke());
                return IntPtr.Zero;
            }
            if (lParamVal == WM_RBUTTONUP)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }
        else if (msg == WM_COMMAND)
        {
            int cmd = wParam.ToInt32() & 0xFFFF;
            if (cmd == ID_OPEN)
            {
                _ = _window.DispatcherQueue.TryEnqueue(() => ShowRequested?.Invoke());
                return IntPtr.Zero;
            }
            if (cmd == ID_CLOSE)
            {
                _ = _window.DispatcherQueue.TryEnqueue(() => ExitRequested?.Invoke());
                return IntPtr.Zero;
            }
        }

        return CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        GetCursorPos(out var pt);
        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING, ID_OPEN, "Open");
        AppendMenu(hMenu, MF_STRING, ID_CLOSE, "Close");
        SetForegroundWindow(_hwnd);
        TrackPopupMenu(hMenu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.x, pt.y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(hMenu);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public void Dispose()
    {
        if (!_disposed)
        {
            RemoveTrayIcon();
            if (_originalWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, GWL_WNDPROC, _originalWndProc);
            }
            _disposed = true;
        }
    }
}
