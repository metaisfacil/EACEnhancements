using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AudioDataPlugIn
{
    internal static partial class EnhancementRuntime
    {
        private const string WorkflowButtonEnabledResource =
            "AudioDataPlugIn.Resources.100log_active.bmp";
        private const string WorkflowButtonDisabledResource =
            "AudioDataPlugIn.Resources.100log_disabled.bmp";
        private static readonly int[] WorkflowSideButtonIds = { 502, 770, 466, 574 };
        private const uint WorkflowButtonHoverTimerId = 0xA319;

        internal static Bitmap LoadWorkflowButtonImage(bool enabled)
        {
            string name = enabled
                ? WorkflowButtonEnabledResource
                : WorkflowButtonDisabledResource;
            Assembly assembly = typeof(AudioDataTransfer).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        "The embedded workflow-button image was not found: " + name + ".");
                using (Bitmap embedded = new Bitmap(stream))
                    return new Bitmap(embedded);
            }
        }

        internal static bool IsWorkflowButtonClickNotification(
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            IntPtr button)
        {
            int command = (int)wParam.ToInt64() & 0xFFFF;
            int notification = ((int)wParam.ToInt64() >> 16) & 0xFFFF;
            return message == NativeMethods.WM_COMMAND &&
                button != IntPtr.Zero &&
                lParam == button &&
                command == WorkflowButtonControlId &&
                notification == 0; // STN_CLICKED
        }

        private static bool RequestWorkflowButtonInstallation(IntPtr mainWindow)
        {
            lock (WorkflowButtonLock)
            {
                if (workflowButton != IntPtr.Zero && NativeMethods.IsWindow(workflowButton))
                    return true;
            }

            if (Interlocked.CompareExchange(ref workflowButtonInstallRequested, 1, 0) == 0)
            {
                if (!NativeMethods.PostMessageW(
                    mainWindow,
                    NativeMethods.WM_COMMAND,
                    new IntPtr((int)InstallWorkflowButtonCommand),
                    IntPtr.Zero))
                {
                    Interlocked.Exchange(ref workflowButtonInstallRequested, 0);
                    throw new InvalidOperationException(
                        "PostMessageW failed while requesting the workflow button with Win32 error " +
                        Marshal.GetLastWin32Error() + ".");
                }
            }
            return false;
        }

        private static void InstallWorkflowButtonOnUiThread(IntPtr mainWindow)
        {
            lock (WorkflowButtonLock)
            {
                if (workflowButton != IntPtr.Zero && NativeMethods.IsWindow(workflowButton))
                    return;

                IntPtr[] sideButtons = new IntPtr[WorkflowSideButtonIds.Length];
                NativeMethods.POINT[] positions = new NativeMethods.POINT[sideButtons.Length];
                NativeMethods.RECT[] rectangles = new NativeMethods.RECT[sideButtons.Length];
                for (int i = 0; i < sideButtons.Length; i++)
                {
                    sideButtons[i] = NativeMethods.GetDlgItem(mainWindow, WorkflowSideButtonIds[i]);
                    if (sideButtons[i] == IntPtr.Zero ||
                        !NativeMethods.GetWindowRect(sideButtons[i], out rectangles[i]))
                    {
                        throw new InvalidOperationException(
                            "EAC side button " + WorkflowSideButtonIds[i] + " was not available.");
                    }
                    positions[i].X = rectangles[i].Left;
                    positions[i].Y = rectangles[i].Top;
                    if (!NativeMethods.ScreenToClient(mainWindow, ref positions[i]))
                        throw new InvalidOperationException(
                            "The position of EAC side button " + WorkflowSideButtonIds[i] +
                            " could not be converted to client coordinates.");
                }

                int width = rectangles[0].Right - rectangles[0].Left;
                int height = rectangles[0].Bottom - rectangles[0].Top;
                int slotHeight = positions[1].Y - positions[0].Y;
                if (slotHeight <= 0)
                    slotHeight = height + 5;

                workflowButtonEnabledImage = LoadWorkflowButtonImage(true);
                workflowButtonDisabledImage = LoadWorkflowButtonImage(false);
                if (workflowButtonEnabledImage.Width > width ||
                    workflowButtonEnabledImage.Height > height ||
                    workflowButtonDisabledImage.Width != workflowButtonEnabledImage.Width ||
                    workflowButtonDisabledImage.Height != workflowButtonEnabledImage.Height)
                {
                    DisposeWorkflowButtonImages();
                    throw new InvalidOperationException(
                        "The workflow-button images do not fit EAC's side-button slot.");
                }
                workflowButtonEnabledImage.MakeTransparent(
                    workflowButtonEnabledImage.GetPixel(0, 0));
                workflowButtonDisabledImage.MakeTransparent(
                    workflowButtonDisabledImage.GetPixel(0, 0));

                // Match EAC's native mybutton style. Owner drawing keeps the
                // supplied disabled artwork intact while allowing the same
                // hover and pressed bevels as the surrounding buttons.
                const uint style = 0x5400000B; // WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | BS_OWNERDRAW
                IntPtr button = NativeMethods.CreateWindowExW(
                    0,
                    "BUTTON",
                    WorkflowButtonTooltipText,
                    style,
                    positions[0].X,
                    positions[0].Y,
                    width,
                    height,
                    mainWindow,
                    new IntPtr(WorkflowButtonControlId),
                    IntPtr.Zero,
                    IntPtr.Zero);
                if (button == IntPtr.Zero)
                {
                    DisposeWorkflowButtonImages();
                    throw new InvalidOperationException(
                        "CreateWindowExW failed for the workflow button with Win32 error " +
                        Marshal.GetLastWin32Error() + ".");
                }

                workflowButtonSubclassDelegate = WorkflowButtonSubclass;
                IntPtr subclassProcedure = Marshal.GetFunctionPointerForDelegate(
                    workflowButtonSubclassDelegate);
                if (!NativeMethods.SetWindowSubclass(
                    button,
                    subclassProcedure,
                    new UIntPtr(246194963u),
                    UIntPtr.Zero))
                {
                    NativeMethods.DestroyWindow(button);
                    workflowButtonSubclassDelegate = null;
                    DisposeWorkflowButtonImages();
                    throw new InvalidOperationException(
                        "SetWindowSubclass failed for the workflow button with Win32 error " +
                        Marshal.GetLastWin32Error() + ".");
                }

                try
                {
                    InstallWorkflowButtonTooltip(mainWindow, button);
                }
                catch
                {
                    NativeMethods.DestroyWindow(button);
                    workflowButtonSubclassDelegate = null;
                    DisposeWorkflowButtonImages();
                    throw;
                }

                workflowButton = button;
                NativeMethods.EnableWindow(button, false);

                for (int i = 0; i < sideButtons.Length; i++)
                {
                    if (!NativeMethods.SetWindowPos(
                        sideButtons[i],
                        IntPtr.Zero,
                        positions[i].X,
                        positions[i].Y + slotHeight,
                        rectangles[i].Right - rectangles[i].Left,
                        rectangles[i].Bottom - rectangles[i].Top,
                        0x0014)) // SWP_NOZORDER | SWP_NOACTIVATE
                    {
                        Log("EAC side button " + WorkflowSideButtonIds[i] +
                            " could not be moved for the 100% log button.");
                    }
                }
                ApplyWorkflowButtonState();
                Log("Installed the 100% log side button above EAC's extraction buttons.");
            }
        }

        private static void RequestWorkflowButtonState(IntPtr mainWindow, bool enabled)
        {
            int value = enabled ? 1 : 0;
            if (Interlocked.Exchange(ref workflowButtonRequestedEnabled, value) == value)
                return;
            NativeMethods.PostMessageW(
                mainWindow,
                NativeMethods.WM_COMMAND,
                new IntPtr((int)RefreshWorkflowButtonCommand),
                IntPtr.Zero);
        }

        private static void ApplyWorkflowButtonState()
        {
            lock (WorkflowButtonLock)
            {
                if (workflowButton == IntPtr.Zero || !NativeMethods.IsWindow(workflowButton))
                    return;

                bool enabled = workflowButtonRequestedEnabled > 0;
                NativeMethods.EnableWindow(workflowButton, enabled);
                NativeMethods.InvalidateRect(workflowButton, IntPtr.Zero, true);
            }
        }

        private static IntPtr WorkflowButtonSubclass(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData)
        {
            try
            {
                if (message == NativeMethods.WM_MOUSEMOVE)
                {
                    if (!workflowButtonHovered)
                    {
                        workflowButtonHovered = true;
                        NativeMethods.TRACKMOUSEEVENT tracking =
                            new NativeMethods.TRACKMOUSEEVENT();
                        tracking.Size =
                            (uint)Marshal.SizeOf(typeof(NativeMethods.TRACKMOUSEEVENT));
                        tracking.Flags = 0x00000002; // TME_LEAVE
                        tracking.TrackWindow = hwnd;
                        NativeMethods.TrackMouseEvent(ref tracking);
                        NativeMethods.SetTimer(
                            hwnd,
                            new UIntPtr(WorkflowButtonHoverTimerId),
                            30,
                            IntPtr.Zero);
                        NativeMethods.InvalidateRect(hwnd, IntPtr.Zero, false);
                        NativeMethods.UpdateWindow(hwnd);
                    }
                    ShowWorkflowButtonTooltip();
                }
                else if (message == NativeMethods.WM_MOUSELEAVE)
                {
                    ClearWorkflowButtonHover(hwnd);
                }
                else if (message == NativeMethods.WM_TIMER &&
                    wParam.ToInt64() == WorkflowButtonHoverTimerId)
                {
                    NativeMethods.POINT cursor;
                    NativeMethods.RECT bounds;
                    if (!NativeMethods.GetCursorPos(out cursor) ||
                        !NativeMethods.GetWindowRect(hwnd, out bounds) ||
                        cursor.X < bounds.Left || cursor.X >= bounds.Right ||
                        cursor.Y < bounds.Top || cursor.Y >= bounds.Bottom)
                    {
                        ClearWorkflowButtonHover(hwnd);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("100% log button hover tracking failed: " + ex.Message);
            }
            return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
        }

        private static void ClearWorkflowButtonHover(IntPtr hwnd)
        {
            NativeMethods.KillTimer(hwnd, new UIntPtr(WorkflowButtonHoverTimerId));
            HideWorkflowButtonTooltip();
            if (!workflowButtonHovered)
                return;

            workflowButtonHovered = false;
            NativeMethods.InvalidateRect(hwnd, IntPtr.Zero, false);
            NativeMethods.UpdateWindow(hwnd);
        }

        private static bool DrawWorkflowButton(IntPtr drawItemPointer)
        {
            if (drawItemPointer == IntPtr.Zero)
                return false;

            NativeMethods.DRAWITEMSTRUCT item =
                (NativeMethods.DRAWITEMSTRUCT)Marshal.PtrToStructure(
                    drawItemPointer,
                    typeof(NativeMethods.DRAWITEMSTRUCT));
            if (item.ItemWindow != workflowButton || item.DeviceContext == IntPtr.Zero)
                return false;

            const uint OdsSelected = 0x0001;
            const uint OdsDisabled = 0x0004;
            bool pressed = (item.ItemState & OdsSelected) != 0;
            bool enabled = (item.ItemState & OdsDisabled) == 0;
            Bitmap image = enabled
                ? workflowButtonEnabledImage
                : workflowButtonDisabledImage;
            if (image == null)
                return false;

            int width = item.ItemRectangle.Right - item.ItemRectangle.Left;
            int height = item.ItemRectangle.Bottom - item.ItemRectangle.Top;
            Rectangle bounds = new Rectangle(0, 0, width, height);
            using (Graphics graphics = Graphics.FromHdc(item.DeviceContext))
            {
                graphics.Clear(SystemColors.Control);
                if (pressed)
                    ControlPaint.DrawBorder3D(graphics, bounds, Border3DStyle.SunkenInner);
                else if (enabled && workflowButtonHovered)
                    ControlPaint.DrawBorder3D(graphics, bounds, Border3DStyle.RaisedInner);

                int offset = pressed ? 1 : 0;
                int x = (width - image.Width) / 2 + offset;
                int y = (height - image.Height) / 2 + offset;
                graphics.DrawImageUnscaled(image, x, y);
            }
            return true;
        }

        private static void DisposeWorkflowButtonImages()
        {
            if (workflowButtonEnabledImage != null)
            {
                workflowButtonEnabledImage.Dispose();
                workflowButtonEnabledImage = null;
            }
            if (workflowButtonDisabledImage != null)
            {
                workflowButtonDisabledImage.Dispose();
                workflowButtonDisabledImage = null;
            }
        }

        private static void InstallWorkflowButtonTooltip(IntPtr mainWindow, IntPtr button)
        {
            // Ghidra shows that EAC's mytooltip is a desktop child with this
            // exact classic border/style combination. Use a separate window
            // so EAC's native tooltip manager cannot clear or hide our text,
            // then reproduce its paint procedure pixel for pixel below.
            IntPtr tooltip = NativeMethods.CreateWindowExW(
                0x00000080, // WS_EX_TOOLWINDOW
                "STATIC",
                String.Empty,
                0x4C800000, // WS_CHILD | WS_CLIPSIBLINGS | WS_CLIPCHILDREN | WS_BORDER
                0,
                0,
                0,
                0,
                NativeMethods.GetDesktopWindow(),
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);
            if (tooltip == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "CreateWindowExW failed for the workflow-button tooltip with Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }

            workflowButtonTooltipSubclassDelegate = WorkflowButtonTooltipSubclass;
            IntPtr subclassProcedure = Marshal.GetFunctionPointerForDelegate(
                workflowButtonTooltipSubclassDelegate);
            if (!NativeMethods.SetWindowSubclass(
                tooltip,
                subclassProcedure,
                new UIntPtr(246194964u),
                UIntPtr.Zero))
            {
                NativeMethods.DestroyWindow(tooltip);
                workflowButtonTooltipSubclassDelegate = null;
                throw new InvalidOperationException(
                    "SetWindowSubclass failed for the workflow-button tooltip with Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }

            NativeMethods.RECT measured = new NativeMethods.RECT();
            IntPtr deviceContext = NativeMethods.GetDC(tooltip);
            if (deviceContext == IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(tooltip);
                workflowButtonTooltipSubclassDelegate = null;
                throw new InvalidOperationException("The workflow tooltip could not be measured.");
            }
            NativeMethods.SelectObject(deviceContext, NativeMethods.GetStockObject(17));
            NativeMethods.DrawTextW(
                deviceContext,
                WorkflowButtonTooltipText,
                WorkflowButtonTooltipText.Length,
                ref measured,
                0x00000C00); // DT_CALCRECT | DT_NOPREFIX
            NativeMethods.ReleaseDC(tooltip, deviceContext);
            NativeMethods.SetWindowPos(
                tooltip,
                IntPtr.Zero,
                0,
                0,
                measured.Right + 8,
                measured.Bottom + 8,
                0x0036); // SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED
            NativeMethods.InvalidateRect(tooltip, IntPtr.Zero, true);
            NativeMethods.UpdateWindow(tooltip);

            workflowButtonTooltip = tooltip;
        }

        private static IntPtr WorkflowButtonTooltipSubclass(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData)
        {
            if (message == NativeMethods.WM_ERASEBKGND)
                return new IntPtr(1);
            if (message == NativeMethods.WM_PAINT)
            {
                NativeMethods.PAINTSTRUCT paint;
                IntPtr deviceContext = NativeMethods.BeginPaint(hwnd, out paint);
                try
                {
                    NativeMethods.RECT client;
                    NativeMethods.GetClientRect(hwnd, out client);
                    NativeMethods.FillRect(
                        deviceContext,
                        ref client,
                        NativeMethods.GetSysColorBrush(24)); // COLOR_INFOBK
                    NativeMethods.SelectObject(
                        deviceContext,
                        NativeMethods.GetStockObject(17));
                    NativeMethods.SetTextColor(
                        deviceContext,
                        NativeMethods.GetSysColor(23)); // COLOR_INFOTEXT
                    NativeMethods.SetBkMode(deviceContext, 1); // TRANSPARENT
                    NativeMethods.RECT textRectangle = client;
                    textRectangle.Left += 3;
                    textRectangle.Top += 3;
                    NativeMethods.DrawTextW(
                        deviceContext,
                        WorkflowButtonTooltipText,
                        WorkflowButtonTooltipText.Length,
                        ref textRectangle,
                        0x00000800); // DT_NOPREFIX
                }
                catch (Exception ex)
                {
                    Log("100% log tooltip painting failed: " + ex.Message);
                }
                finally
                {
                    if (deviceContext != IntPtr.Zero)
                        NativeMethods.EndPaint(hwnd, ref paint);
                }
                return IntPtr.Zero;
            }
            if (message == NativeMethods.WM_MOUSEMOVE ||
                message == NativeMethods.WM_LBUTTONUP)
            {
                NativeMethods.ShowWindow(hwnd, 0); // SW_HIDE
                return IntPtr.Zero;
            }
            return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
        }

        private static void ShowWorkflowButtonTooltip()
        {
            IntPtr tooltip = workflowButtonTooltip;
            if (tooltip == IntPtr.Zero || !NativeMethods.IsWindow(tooltip))
                return;

            NativeMethods.POINT cursor;
            if (!NativeMethods.GetCursorPos(out cursor))
                return;

            NativeMethods.SetWindowPos(
                tooltip,
                IntPtr.Zero,
                cursor.X,
                cursor.Y + 20,
                0,
                0,
                0x0071); // EAC: NOSIZE | NOACTIVATE | FRAMECHANGED | SHOWWINDOW
            NativeMethods.UpdateWindow(tooltip);
        }

        private static void HideWorkflowButtonTooltip()
        {
            IntPtr tooltip = workflowButtonTooltip;
            if (tooltip != IntPtr.Zero && NativeMethods.IsWindow(tooltip))
                NativeMethods.ShowWindow(tooltip, 0); // SW_HIDE
        }
    }
}
