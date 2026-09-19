using System;

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

            // Every supported executable lists its own three cue sheet
            // "Analyzing" dialog procedures (gaps, second pass, UPC/ISRC).
            foreach (EacVersionLayout layout in EacVersionLayout.KnownLayouts)
            {
                uint[] procs = layout.AnalyzingDialogProcVas;
                if (procs == null || procs.Length != 3)
                    throw new Exception(layout.Name + " must list three Analyzing dialog procedures.");
                foreach (uint proc in procs)
                {
                    if (proc == 0 || !EnhancementRuntime.IsAnalyzingDialogProc(procs, proc))
                        throw new Exception(layout.Name + " has an unusable Analyzing dialog procedure.");
                }
                if (EnhancementRuntime.IsAnalyzingDialogProc(procs, 0) ||
                    EnhancementRuntime.IsAnalyzingDialogProc(procs, 0x00401000))
                    throw new Exception(layout.Name + " matched an unknown dialog procedure.");
            }
            foreach (uint proc in EacVersionLayout.KnownLayouts[0].AnalyzingDialogProcVas)
            {
                if (EnhancementRuntime.IsAnalyzingDialogProc(
                    EacVersionLayout.KnownLayouts[1].AnalyzingDialogProcVas, proc))
                    throw new Exception("Analyzing dialog procedures must differ between EAC versions.");
            }
            if (EnhancementRuntime.IsAnalyzingDialogProc(null, 0x004F1300))
                throw new Exception("A missing procedure list matched a dialog.");

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
    }
}
