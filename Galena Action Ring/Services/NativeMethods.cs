using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Galena_Action_Ring;

internal static class NativeMethods
{
    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOPMOST = 0x8;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_CAPTION = unchecked((int)0x00C00000);
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_SYSMENU = 0x00080000;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_FRAMECHANGED = 0x0020;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint LWA_COLORKEY = 0x00000001;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWA_COLOR_WINDOW_BORDER = 34;
    private const int DWM_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    public const int HWND_BROADCAST = 0xFFFF;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateEllipticRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("Dwmapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(IntPtr hFile, IntPtr lpOverlapped);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(
        IntPtr HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(
        IntPtr HidDeviceObject, out IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll", SetLastError = true)]
    private static extern uint HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Caps);

    public static IntPtr OpenHidDeviceRead(string devicePath, out int win32Error)
    {
        win32Error = 0;
        var handle = CreateFileW(
            devicePath, GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE)
            win32Error = Marshal.GetLastWin32Error();
        return handle;
    }

    public static IntPtr OpenHidDeviceWrite(string devicePath, out int win32Error)
    {
        win32Error = 0;
        var handle = CreateFileW(
            devicePath, GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE)
            win32Error = Marshal.GetLastWin32Error();
        return handle;
    }

    public static IntPtr OpenHidDeviceReadWrite(string devicePath, out int win32Error)
    {
        win32Error = 0;
        var handle = CreateFileW(
            devicePath, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE)
            win32Error = Marshal.GetLastWin32Error();
        return handle;
    }

    public static void CloseHidDevice(IntPtr handle)
    {
        if (handle != IntPtr.Zero && handle != INVALID_HANDLE_VALUE)
            CloseHandle(handle);
    }

    public static bool IsValidHandle(IntPtr handle) =>
        handle != IntPtr.Zero && handle != INVALID_HANDLE_VALUE;

    public static ushort GetInputReportByteLength(IntPtr deviceHandle)
    {
        if (!HidD_GetPreparsedData(deviceHandle, out IntPtr preparsedData))
            return 0;
        try
        {
            HidP_GetCaps(preparsedData, out HIDP_CAPS caps);
            return caps.InputReportByteLength;
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    public static ushort GetFeatureReportByteLength(IntPtr deviceHandle)
    {
        if (!HidD_GetPreparsedData(deviceHandle, out IntPtr preparsedData))
            return 0;
        try
        {
            HidP_GetCaps(preparsedData, out HIDP_CAPS caps);
            return caps.FeatureReportByteLength;
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    public static bool ReadInputReport(IntPtr readHandle, byte[] buffer, uint bytesToRead, out uint bytesRead)
    {
        return ReadFile(readHandle, buffer, bytesToRead, out bytesRead, IntPtr.Zero);
    }

    public static bool SendHidFeatureReport(IntPtr writeHandle, byte reportId, byte[] payload)
    {
        ushort featureLen = GetFeatureReportByteLength(writeHandle);
        if (featureLen == 0) return false;

        var buf = new byte[featureLen];
        buf[0] = reportId;
        int copyLen = Math.Min(payload.Length, featureLen - 1);
        Array.Copy(payload, 0, buf, 1, copyLen);

        return HidD_SetFeature(writeHandle, buf, (uint)featureLen);
    }

    public static void CancelRead(IntPtr readHandle)
    {
        CancelIoEx(readHandle, IntPtr.Zero);
    }

    private static IntPtr GetHwnd(Window window) =>
        WindowNative.GetWindowHandle(window);

    public static void SetOverlayStyles(Window window)
    {
        var hwnd = GetHwnd(window);
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        exStyle = new IntPtr(exStyle.ToInt64() | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    public static void ApplyColorKey(Window window)
    {
        var hwnd = GetHwnd(window);

        var cornerPref = DWMWCP_DEFAULT;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        SetLayeredWindowAttributes(hwnd, 0x00FF00FF, 255, LWA_COLORKEY);

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    public static void RemoveWindowFrame(Window window)
    {
        var hwnd = GetHwnd(window);
        var style = GetWindowLongPtr(hwnd, GWL_STYLE);
        style = new IntPtr(style.ToInt64() & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU));
        SetWindowLongPtr(hwnd, GWL_STYLE, style);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    public static void ShowTopmost(Window window)
    {
        var hwnd = GetHwnd(window);
        ShowWindow(hwnd, SW_SHOWNA);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static void SetWindowPosition(Window window, int width = 400, int height = 400)
    {
        var hwnd = GetHwnd(window);
        var screenW = GetSystemMetrics(SM_CXSCREEN);
        var screenH = GetSystemMetrics(SM_CYSCREEN);
        var x = (screenW - width) / 2;
        var y = (screenH - height) / 2;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER);
    }

    public static void HideWindow(Window window)
    {
        var hwnd = GetHwnd(window);
        ShowWindow(hwnd, SW_HIDE);
    }

    public static void SetWindowSize(Window window, int width = 400, int height = 400)
    {
        var hwnd = GetHwnd(window);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, SWP_NOZORDER);
    }
}
