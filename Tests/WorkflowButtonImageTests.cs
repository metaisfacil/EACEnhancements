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
                AssertWorkflowInvocationGate();
                AssertWorkflowSetupConfirmationPolicy();
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

        private static void AssertWorkflowInvocationGate()
        {
            if (!EnhancementRuntime.IsGapDetectionTocReady(1) ||
                !EnhancementRuntime.IsGapDetectionTocReady(99))
            {
                throw new InvalidOperationException(
                    "A valid EAC audio-CD TOC was rejected.");
            }

            int[] unavailableTocStates = { Int32.MinValue, -1, 0 };
            foreach (int firstTrackNumber in unavailableTocStates)
            {
                if (EnhancementRuntime.IsGapDetectionTocReady(firstTrackNumber))
                {
                    throw new InvalidOperationException(
                        "An unavailable EAC gap-detection TOC passed the workflow gate.");
                }
            }

            if (!EnhancementRuntime.IsReferenceRipCommandStateEnabled(0))
                throw new InvalidOperationException(
                    "An enabled EAC reference-rip command was rejected.");

            uint[] blockedStates =
            {
                NativeMethods.MF_DISABLED,
                NativeMethods.MF_GRAYED,
                NativeMethods.MF_DISABLED | NativeMethods.MF_GRAYED,
                UInt32.MaxValue
            };
            foreach (uint state in blockedStates)
            {
                if (EnhancementRuntime.IsReferenceRipCommandStateEnabled(state))
                {
                    throw new InvalidOperationException(
                        "A disabled or unavailable EAC reference-rip command passed " +
                        "the workflow invocation gate.");
                }
            }
        }

        private static void AssertWorkflowSetupConfirmationPolicy()
        {
            string expectedWarning = String.Join("\r\n", new[]
            {
                "Warning!",
                String.Empty,
                "Although you are trying to use the 100% log rip workflow, your EAC does not appear to be configured to use it correctly.",
                "EAC must be set up with the correct configuration in order to produce rips which adhere to best practices. " +
                    "If you continue anyway, your rips may not qualify as 'perfect' in certain communities.",
                String.Empty,
                "It is strongly advised you first open Action > EAC Enhancement Options... > Check 100% Log Setup... and change your settings accordingly.",
                String.Empty,
                "Are you sure you want to proceed?"
            });
            if (EnhancementRuntime.WorkflowSetupWarningText != expectedWarning)
                throw new InvalidOperationException("The 100% log setup warning text is incorrect.");

            EacSetupAuditResult compliant = new EacSetupAuditResult();
            if (EnhancementRuntime.WorkflowSetupNeedsConfirmation(compliant))
                throw new InvalidOperationException("A compliant EAC setup requires confirmation.");

            EacSetupAuditResult noncompliant = new EacSetupAuditResult();
            noncompliant.Add("Test", "Setting", "Off", "On");
            if (!EnhancementRuntime.WorkflowSetupNeedsConfirmation(noncompliant) ||
                !EnhancementRuntime.WorkflowSetupNeedsConfirmation(null))
            {
                throw new InvalidOperationException(
                    "An incomplete or unavailable EAC setup audit bypassed confirmation.");
            }
        }
    }
}
