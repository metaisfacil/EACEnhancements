using System;
using System.Runtime.InteropServices;

namespace AudioDataPlugIn
{
    internal static class ResponsiveRipSafetyTests
    {
        private static int Main()
        {
            AssertSafe(NativeMethods.WM_PAINT, true);
            AssertSafe(NativeMethods.WM_TIMER, true);
            AssertSafe(NativeMethods.WM_COMMAND, false);
            AssertSafe(NativeMethods.WM_CLOSE, false);
            AssertSafe(NativeMethods.WM_KEYDOWN, false);
            AssertSafe(NativeMethods.WM_LBUTTONUP, false);

            // The rip session policy is unchanged by the scan-dialog argument.
            IntPtr dialog = new IntPtr(0x1234);
            IntPtr other = new IntPtr(0x5678);
            AssertDispatch(NativeMethods.WM_PAINT, other, true, dialog, true);
            AssertDispatch(NativeMethods.WM_TIMER, other, true, dialog, true);
            AssertDispatch(NativeMethods.WM_COMMAND, dialog, true, dialog, false);
            AssertDispatch(NativeMethods.WM_NCLBUTTONDOWN, dialog, true, dialog, false);

            // The cue sheet UPC/ISRC scan assist services paint anywhere and
            // client-area mouse input aimed at the scan dialog itself.  Never
            // WM_TIMER (EAC's main-window timer polls the drive whenever no rip
            // is running), never non-client presses or keys (they start modal
            // loops that dispatch that timer), never other windows.
            AssertDispatch(NativeMethods.WM_PAINT, other, false, dialog, true);
            AssertDispatch(NativeMethods.WM_TIMER, other, false, dialog, false);
            AssertDispatch(NativeMethods.WM_TIMER, dialog, false, dialog, false);
            AssertDispatch(NativeMethods.WM_LBUTTONUP, dialog, false, dialog, true);
            AssertDispatch(NativeMethods.WM_MOUSEMOVE, dialog, false, dialog, true);
            AssertDispatch(NativeMethods.WM_NCMOUSEMOVE, dialog, false, dialog, true);
            AssertDispatch(NativeMethods.WM_MOUSELEAVE, dialog, false, dialog, true);
            AssertDispatch(NativeMethods.WM_NCLBUTTONDOWN, dialog, false, dialog, false);
            AssertDispatch(NativeMethods.WM_NCRBUTTONDOWN, dialog, false, dialog, false);
            AssertDispatch(NativeMethods.WM_COMMAND, dialog, false, dialog, false);
            AssertDispatch(NativeMethods.WM_SYSCOMMAND, dialog, false, dialog, false);
            AssertDispatch(NativeMethods.WM_CLOSE, dialog, false, dialog, false);
            AssertDispatch(NativeMethods.WM_KEYDOWN, dialog, false, dialog, false);
            AssertDispatch(NativeMethods.WM_SYSKEYDOWN, dialog, false, dialog, false);
            AssertDispatch(0x0466, dialog, false, dialog, false);
            AssertDispatch(NativeMethods.WM_LBUTTONUP, other, false, dialog, false);
            AssertDispatch(NativeMethods.WM_LBUTTONUP, IntPtr.Zero, false, dialog, false);
            AssertDispatch(NativeMethods.WM_LBUTTONUP, dialog, false, IntPtr.Zero, false);
            AssertDispatch(NativeMethods.WM_PAINT, dialog, false, IntPtr.Zero, true);

            // A caption press is allowed only with the modal-loop guard in
            // place, only on the dialog itself, and only for HTCAPTION.
            IntPtr caption = new IntPtr(NativeMethods.HTCAPTION);
            IntPtr sysMenu = new IntPtr(3);
            AssertDrag(dialog, caption, dialog, true, true);
            AssertDrag(dialog, caption, dialog, false, false);
            AssertDrag(dialog, sysMenu, dialog, true, false);
            AssertDrag(other, caption, dialog, true, false);
            AssertDrag(dialog, caption, IntPtr.Zero, true, false);

            // Inside that nested loop: paint anywhere, mouse and plain keys for
            // the dialog tree, nothing else (WM_TIMER and posted messages for
            // other windows are held and replayed afterwards).
            AssertNested(NativeMethods.WM_PAINT, other, dialog, true);
            AssertNested(NativeMethods.WM_NULL, other, dialog, true);
            AssertNested(NativeMethods.WM_MOUSEMOVE, dialog, dialog, true);
            AssertNested(NativeMethods.WM_LBUTTONUP, dialog, dialog, true);
            AssertNested(NativeMethods.WM_KEYDOWN, dialog, dialog, true);
            AssertNested(NativeMethods.WM_TIMER, other, dialog, false);
            AssertNested(NativeMethods.WM_TIMER, dialog, dialog, false);
            AssertNested(NativeMethods.WM_COMMAND, other, dialog, false);
            AssertNested(NativeMethods.WM_COMMAND, dialog, dialog, false);
            AssertNested(NativeMethods.WM_SYSKEYDOWN, dialog, dialog, false);
            AssertNested(NativeMethods.WM_MOUSEMOVE, other, dialog, false);
            AssertNested(NativeMethods.WM_MOUSEMOVE, dialog, IntPtr.Zero, false);

            AssertSubChannelQuery(0x02, 10, 0x42, 0x40, 0x02, true);
            AssertSubChannelQuery(0x02, 10, 0x42, 0x40, 0x03, true);
            AssertSubChannelQuery(0x02, 10, 0x42, 0x40, 0x01, false);
            AssertSubChannelQuery(0x02, 10, 0x42, 0x00, 0x02, false);
            AssertSubChannelQuery(0x02, 10, 0xBE, 0x40, 0x02, false);
            AssertSubChannelQuery(0x02, 12, 0x42, 0x40, 0x02, false);
            AssertSubChannelQuery(0x00, 10, 0x42, 0x40, 0x02, false);
            if (EnhancementRuntime.IsSubChannelCodeQuery(IntPtr.Zero))
                throw new Exception("A null SRB was classified as a sub-channel query.");

            Console.WriteLine("Responsive rip reentrancy safety tests passed.");
            return 0;
        }

        private static void AssertSafe(uint message, bool expected)
        {
            bool actual = EnhancementRuntime.IsSafeAssistedMessage(message);
            if (actual != expected)
            {
                throw new Exception(
                    "Unexpected assisted-pump policy for message 0x" +
                    message.ToString("X") + ".");
            }
        }

        private static void AssertDispatch(
            uint message,
            IntPtr hwnd,
            bool ripSession,
            IntPtr scanDialog,
            bool expected)
        {
            bool actual = EnhancementRuntime.ShouldDispatchAssistedMessage(
                message,
                hwnd,
                IntPtr.Zero,
                ripSession,
                scanDialog,
                true);
            if (actual != expected)
            {
                throw new Exception(
                    "Unexpected assisted dispatch policy for message 0x" +
                    message.ToString("X") + " to 0x" + hwnd.ToInt64().ToString("X") +
                    " (ripSession=" + ripSession + ", dialog 0x" +
                    scanDialog.ToInt64().ToString("X") + ").");
            }
        }

        private static void AssertDrag(
            IntPtr hwnd,
            IntPtr hitTest,
            IntPtr scanDialog,
            bool guarded,
            bool expected)
        {
            bool actual = EnhancementRuntime.ShouldDispatchAssistedMessage(
                NativeMethods.WM_NCLBUTTONDOWN,
                hwnd,
                hitTest,
                false,
                scanDialog,
                guarded);
            if (actual != expected)
            {
                throw new Exception(
                    "Unexpected caption-drag policy for hit test " + hitTest +
                    " to 0x" + hwnd.ToInt64().ToString("X") + " (guarded=" +
                    guarded + ", dialog 0x" + scanDialog.ToInt64().ToString("X") + ").");
            }
        }

        private static void AssertNested(
            uint message,
            IntPtr hwnd,
            IntPtr scanDialog,
            bool expected)
        {
            bool actual = EnhancementRuntime.IsSafeInsideScanDialogModalLoop(
                message,
                hwnd,
                scanDialog);
            if (actual != expected)
            {
                throw new Exception(
                    "Unexpected modal-loop guard policy for message 0x" +
                    message.ToString("X") + " to 0x" + hwnd.ToInt64().ToString("X") +
                    " with dialog 0x" + scanDialog.ToInt64().ToString("X") + ".");
            }
        }

        // Builds an ASPI SRB_ExecSCSICmd image the way EAC's UPC/ISRC readers do
        // and checks whether the completion hook would assist its wait.
        private static void AssertSubChannelQuery(
            byte srbCommand,
            byte cdbLength,
            byte opcode,
            byte subQ,
            byte format,
            bool expected)
        {
            IntPtr srb = Marshal.AllocHGlobal(0x50);
            try
            {
                for (int i = 0; i < 0x50; i++)
                    Marshal.WriteByte(srb, i, 0);
                Marshal.WriteByte(srb, 0x00, srbCommand);
                Marshal.WriteByte(srb, 0x15, cdbLength);
                Marshal.WriteByte(srb, 0x30, opcode);
                Marshal.WriteByte(srb, 0x32, subQ);
                Marshal.WriteByte(srb, 0x33, format);
                Marshal.WriteByte(srb, 0x38, 0x18);
                bool actual = EnhancementRuntime.IsSubChannelCodeQuery(srb);
                if (actual != expected)
                {
                    throw new Exception(
                        "Unexpected sub-channel query classification for CDB " +
                        opcode.ToString("X2") + "/" + subQ.ToString("X2") + "/" +
                        format.ToString("X2") + ".");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(srb);
            }
        }
    }
}
