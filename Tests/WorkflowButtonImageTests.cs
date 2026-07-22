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
                AssertHtoaDetection();
                AssertHtoaTrackHighlighting();
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

        private static void AssertHtoaDetection()
        {
            if (!EnhancementRuntime.IsHtoaAvailable(1, 0, 8925, 0) ||
                !EnhancementRuntime.IsHtoaAvailable(1, 0, 0, 1))
            {
                throw new InvalidOperationException("A valid audio HTOA range was rejected.");
            }

            if (EnhancementRuntime.IsHtoaAvailable(0, 0, 8925, 0) ||
                EnhancementRuntime.IsHtoaAvailable(1, 0, 0, 0) ||
                EnhancementRuntime.IsHtoaAvailable(1, 4, 8925, 0))
            {
                throw new InvalidOperationException(
                    "An unavailable, zero-length, or data-track HTOA passed the workflow gate.");
            }
        }

        private static void AssertHtoaTrackHighlighting()
        {
            if (!EnhancementRuntime.ShouldShadeHtoaTrack(0, 0, true))
                throw new InvalidOperationException("The available HTOA track was not highlighted.");

            uint[] excludedStates = { 0x00000001, 0x00000040, 0x00001000 };
            foreach (uint state in excludedStates)
            {
                if (EnhancementRuntime.ShouldShadeHtoaTrack(0, state, true))
                {
                    throw new InvalidOperationException(
                        "The HTOA background replaced an interactive track-list state.");
                }
            }

            if (EnhancementRuntime.ShouldShadeHtoaTrack(1, 0, true) ||
                EnhancementRuntime.ShouldShadeHtoaTrack(0, 0, false) ||
                EnhancementRuntime.HtoaTrackBackgroundColor != 0x00E1E1E1)
            {
                throw new InvalidOperationException("The HTOA track highlighting policy is incorrect.");
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
                "Your EAC is not configured to produce 100% log rips.",
                "EAC must be set up with the correct configuration in order to produce rips which adhere to best practices. " +
                    "If you continue anyway, your rips may not qualify as 'perfect' in certain communities.",
                String.Empty,
                "It is strongly advised you first open Action > EAC Enhancement Options... > Check Rip Configuration... and review the suggested settings.",
                String.Empty,
                "Are you sure you want to proceed?"
            });
            if (EnhancementRuntime.WorkflowSetupWarningText != expectedWarning)
                throw new InvalidOperationException("The 100% log setup warning text is incorrect.");

            string expectedRecommendationWarning = String.Join("\r\n", new[]
            {
                "Warning!",
                String.Empty,
                "Although your EAC is configured to produce 100% log rips,\r\none or more other ripping settings do not follow recommended best practices. " +
                    "These settings cannot cause deductions in log checkers but continuing may still produce a subpar rip.",
                String.Empty,
                "It is strongly advised you first open Action > EAC Enhancement Options... > Check Rip Configuration... and review the suggested settings.",
                String.Empty,
                "Are you sure you want to proceed?"
            });
            if (EnhancementRuntime.WorkflowRecommendationWarningText != expectedRecommendationWarning)
                throw new InvalidOperationException("The rip recommendation warning text is incorrect.");

            EacSetupAuditResult compliant = new EacSetupAuditResult();
            if (EnhancementRuntime.WorkflowSetupNeedsConfirmation(compliant, true) ||
                EnhancementRuntime.GetWorkflowSetupWarningKind(compliant, true) !=
                    EnhancementRuntime.WorkflowSetupWarningKind.None)
                throw new InvalidOperationException("A compliant EAC setup requires confirmation.");

            EacSetupAuditResult recommendationOnly = new EacSetupAuditResult();
            recommendationOnly.AddRecommendation("Test", "Recommended setting", "Off", "On");
            if (!EnhancementRuntime.WorkflowSetupNeedsConfirmation(recommendationOnly, true) ||
                EnhancementRuntime.GetWorkflowSetupWarningKind(recommendationOnly, true) !=
                    EnhancementRuntime.WorkflowSetupWarningKind.Recommendations)
            {
                throw new InvalidOperationException(
                    "A recommendation-only setup did not select the recommendation warning.");
            }

            EacSetupAuditResult noncompliant = new EacSetupAuditResult();
            noncompliant.AddLogScoreIssue("Test", "Score setting", "Off", "On");
            noncompliant.AddRecommendation("Test", "Recommended setting", "Off", "On");
            if (!EnhancementRuntime.WorkflowSetupNeedsConfirmation(noncompliant, true) ||
                !EnhancementRuntime.WorkflowSetupNeedsConfirmation(null, true) ||
                EnhancementRuntime.GetWorkflowSetupWarningKind(noncompliant, true) !=
                    EnhancementRuntime.WorkflowSetupWarningKind.RequiredSettings ||
                EnhancementRuntime.GetWorkflowSetupWarningKind(null, true) !=
                    EnhancementRuntime.WorkflowSetupWarningKind.RequiredSettings)
            {
                throw new InvalidOperationException(
                    "An incomplete or unavailable EAC setup audit bypassed confirmation.");
            }

            EacSetupAuditResult checksumMissing = new EacSetupAuditResult();
            checksumMissing.AddLogScoreIssue(
                "EAC Options > Tools",
                "Append checksum to status report",
                "Disabled",
                "Enabled");
            if (!EnhancementRuntime.WorkflowSetupNeedsConfirmation(checksumMissing, true))
                throw new InvalidOperationException("A missing log checksum did not require confirmation.");

            if (EnhancementRuntime.WorkflowSetupNeedsConfirmation(noncompliant, false) ||
                EnhancementRuntime.WorkflowSetupNeedsConfirmation(recommendationOnly, false) ||
                EnhancementRuntime.WorkflowSetupNeedsConfirmation(null, false))
            {
                throw new InvalidOperationException(
                    "The disabled workflow setup alert still requires confirmation.");
            }
        }
    }
}
