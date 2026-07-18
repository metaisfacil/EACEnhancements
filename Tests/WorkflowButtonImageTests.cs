using System;
using System.Drawing;

namespace AudioDataPlugIn
{
    internal static class WorkflowButtonImageTests
    {
        private static int Main()
        {
            try
            {
                using (Bitmap enabled = EnhancementRuntime.LoadWorkflowButtonImage(true))
                using (Bitmap disabled = EnhancementRuntime.LoadWorkflowButtonImage(false))
                {
                    AssertImage(enabled, "enabled");
                    AssertImage(disabled, "disabled");
                    if (enabled.GetPixel(0, 0).ToArgb() == disabled.GetPixel(0, 0).ToArgb() &&
                        enabled.GetPixel(24, 24).ToArgb() == disabled.GetPixel(24, 24).ToArgb())
                    {
                        throw new InvalidOperationException(
                            "The enabled and disabled workflow-button resources appear identical.");
                    }
                }
                if (EnhancementRuntime.WorkflowButtonTooltipText !=
                    "Test & Copy + Cue (100% Log)")
                {
                    throw new InvalidOperationException(
                        "The workflow-button tooltip text is incorrect.");
                }
                AssertClickNotificationFiltering();
                Console.WriteLine("Workflow button image tests passed.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void AssertImage(Bitmap image, string state)
        {
            if (image.Width != 48 || image.Height != 48)
                throw new InvalidOperationException(
                    "The " + state + " workflow-button image is not 48x48 pixels.");
        }

        private static void AssertClickNotificationFiltering()
        {
            IntPtr button = new IntPtr(0x1234);
            if (!EnhancementRuntime.IsWorkflowButtonClickNotification(
                NativeMethods.WM_COMMAND,
                new IntPtr(EnhancementRuntime.WorkflowButtonControlId),
                button,
                button))
            {
                throw new InvalidOperationException(
                    "A workflow-button click was not recognized.");
            }

            int[] nonClickNotifications = { 2, 3 }; // STN_ENABLE, STN_DISABLE
            foreach (int notification in nonClickNotifications)
            {
                IntPtr wParam = new IntPtr(
                    (notification << 16) | EnhancementRuntime.WorkflowButtonControlId);
                if (EnhancementRuntime.IsWorkflowButtonClickNotification(
                    NativeMethods.WM_COMMAND,
                    wParam,
                    button,
                    button))
                {
                    throw new InvalidOperationException(
                        "A workflow-button state notification was mistaken for a click.");
                }
            }

            if (EnhancementRuntime.IsWorkflowButtonClickNotification(
                NativeMethods.WM_COMMAND,
                new IntPtr(EnhancementRuntime.WorkflowButtonControlId),
                IntPtr.Zero,
                button))
            {
                throw new InvalidOperationException(
                    "A menu command was mistaken for a workflow-button click.");
            }
        }
    }
}
