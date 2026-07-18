using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioDataPlugIn
{
    internal static class NativeMethods
    {
        internal delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

        internal const uint MEM_COMMIT = 0x1000;
        internal const uint MEM_RESERVE = 0x2000;
        internal const uint PAGE_READWRITE = 0x04;
        internal const uint PAGE_EXECUTE_READWRITE = 0x40;
        internal const uint PM_REMOVE = 0x0001;
        internal const uint MF_BYCOMMAND = 0x00000000;
        internal const uint MF_BYPOSITION = 0x00000400;
        internal const uint MF_SEPARATOR = 0x00000800;
        internal const uint MF_STRING = 0x00000000;
        internal const uint MF_ENABLED = 0x00000000;
        internal const uint MF_GRAYED = 0x00000001;
        internal const uint MF_DISABLED = 0x00000002;
        internal const int WH_GETMESSAGE = 3;
        internal const int WH_CALLWNDPROC = 4;
        internal const uint WM_CLOSE = 0x0010;
        internal const uint WM_PAINT = 0x000F;
        internal const uint WM_ERASEBKGND = 0x0014;
        internal const uint WM_DRAWITEM = 0x002B;
        internal const uint WM_COMMAND = 0x0111;
        internal const uint WM_SYSCOMMAND = 0x0112;
        internal const uint WM_TIMER = 0x0113;
        internal const uint WM_KEYDOWN = 0x0100;
        internal const uint WM_LBUTTONUP = 0x0202;
        internal const uint WM_MOUSEMOVE = 0x0200;
        internal const uint WM_MOUSELEAVE = 0x02A3;
        internal const uint SC_CLOSE = 0xF060;
        internal const int IDCANCEL = 2;
        internal const uint VK_ESCAPE = 0x1B;
        internal const uint MB_OK = 0x00000000;
        internal const uint MB_ICONWARNING = 0x00000030;

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSG
        {
            internal IntPtr hwnd;
            internal uint message;
            internal IntPtr wParam;
            internal IntPtr lParam;
            internal uint time;
            internal POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CWPSTRUCT
        {
            internal IntPtr lParam;
            internal IntPtr wParam;
            internal uint message;
            internal IntPtr hwnd;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DRAWITEMSTRUCT
        {
            internal uint ControlType;
            internal uint ControlId;
            internal uint ItemId;
            internal uint ItemAction;
            internal uint ItemState;
            internal IntPtr ItemWindow;
            internal IntPtr DeviceContext;
            internal RECT ItemRectangle;
            internal UIntPtr ItemData;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TRACKMOUSEEVENT
        {
            internal uint Size;
            internal uint Flags;
            internal IntPtr TrackWindow;
            internal uint HoverTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PAINTSTRUCT
        {
            internal IntPtr DeviceContext;
            internal int Erase;
            internal RECT PaintRectangle;
            internal int Restore;
            internal int IncrementalUpdate;
            internal int Reserved1;
            internal int Reserved2;
            internal int Reserved3;
            internal int Reserved4;
            internal int Reserved5;
            internal int Reserved6;
            internal int Reserved7;
            internal int Reserved8;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr VirtualAlloc(
            IntPtr address,
            UIntPtr size,
            uint allocationType,
            uint protection);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool VirtualProtect(
            IntPtr address,
            UIntPtr size,
            uint newProtection,
            out uint oldProtection);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetActiveWindow();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint GetPrivateProfileStringW(
            string section,
            string key,
            string defaultValue,
            StringBuilder returnedString,
            int size,
            string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WritePrivateProfileStringW(
            string section,
            string key,
            string value,
            string fileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FlushInstructionCache(
            IntPtr process,
            IntPtr baseAddress,
            UIntPtr size);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowEnabled(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowExW(
            uint extendedStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDlgItem(IntPtr parent, int controlId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rectangle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr hwnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnableWindow(IntPtr hwnd, bool enable);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InvalidateRect(IntPtr hwnd, IntPtr rectangle, bool erase);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tracking);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern UIntPtr SetTimer(
            IntPtr hwnd,
            UIntPtr timerId,
            uint intervalMilliseconds,
            IntPtr timerProcedure);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool KillTimer(IntPtr hwnd, UIntPtr timerId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetLastActivePopup(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern int GetDlgCtrlID(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextW(
            IntPtr hwnd,
            StringBuilder text,
            int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        internal static extern IntPtr SendMessageTextW(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            StringBuilder lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumChildWindows(
            IntPtr parent,
            EnumChildProc callback,
            IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassNameW(
            IntPtr hwnd,
            StringBuilder className,
            int maximumCount);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(
            IntPtr hwnd,
            IntPtr processId);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetMenu(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern int GetMenuItemCount(IntPtr menu);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetSubMenu(IntPtr menu, int position);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetMenuStringW(
            IntPtr menu,
            uint item,
            StringBuilder text,
            int maximumCount,
            uint flags);

        [DllImport("user32.dll")]
        internal static extern uint GetMenuState(IntPtr menu, uint item, uint flags);

        [DllImport("user32.dll")]
        internal static extern uint EnableMenuItem(IntPtr menu, uint item, uint enable);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AppendMenuW(
            IntPtr menu,
            uint flags,
            UIntPtr newItem,
            string text);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DrawMenuBar(IntPtr hwnd);

        [DllImport(
            "user32.dll",
            EntryPoint = "SetWindowsHookExW",
            ExactSpelling = true,
            SetLastError = true)]
        internal static extern IntPtr SetWindowsHookExW(
            int hookId,
            IntPtr hookProcedure,
            IntPtr module,
            uint threadId);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(
            IntPtr hook,
            int code,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessageW(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT paint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT paint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr hwnd, out RECT rectangle);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr hwnd, IntPtr deviceContext);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetSysColorBrush(int colorIndex);

        [DllImport("user32.dll")]
        internal static extern uint GetSysColor(int colorIndex);

        [DllImport("user32.dll")]
        internal static extern int FillRect(
            IntPtr deviceContext,
            ref RECT rectangle,
            IntPtr brush);

        [DllImport("gdi32.dll")]
        internal static extern uint SetTextColor(IntPtr deviceContext, uint color);

        [DllImport("gdi32.dll")]
        internal static extern int SetBkMode(IntPtr deviceContext, int mode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int DrawTextW(
            IntPtr deviceContext,
            string text,
            int textLength,
            ref RECT rectangle,
            uint format);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr GetStockObject(int objectIndex);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessageW(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowSubclass(
            IntPtr hwnd,
            IntPtr subclassProcedure,
            UIntPtr subclassId,
            UIntPtr referenceData);

        [DllImport("comctl32.dll")]
        internal static extern IntPtr DefSubclassProc(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PeekMessageW(
            out MSG message,
            IntPtr hwnd,
            uint minimumMessage,
            uint maximumMessage,
            uint removeMessage);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsDialogMessageW(
            IntPtr dialog,
            ref MSG message);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TranslateMessage(ref MSG message);

        [DllImport("user32.dll")]
        internal static extern IntPtr DispatchMessageW(ref MSG message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int MessageBoxW(
            IntPtr hwnd,
            string text,
            string caption,
            uint type);

    }
}
