using System;
using System.Diagnostics;

namespace AudioDataPlugIn
{
    internal static class EacSetupAuditSmoke
    {
        private static int Main(string[] arguments)
        {
            if (!EacSetupAudit.HasExecutableExtension(@"C:\Program Files\FLAC\flac.exe") ||
                !EacSetupAudit.HasExecutableExtension(@"C:\Encoders\CUSTOM.EXE  ") ||
                EacSetupAudit.HasExecutableExtension(@"C:\Encoders\flac") ||
                EacSetupAudit.HasExecutableExtension(@"C:\Encoders\flac.exe.bak") ||
                EacSetupAudit.HasExecutableExtension(null) ||
                EacSetupAudit.HasExecutableExtension(String.Empty))
            {
                Console.Error.WriteLine(
                    "The command-line compressor audit did not enforce a case-insensitive .exe suffix.");
                return 1;
            }

            if (!EacSetupAudit.IsOrpheusAcceptedExtension("flac") ||
                !EacSetupAudit.IsOrpheusAcceptedExtension(".WAV") ||
                !EacSetupAudit.IsOrpheusAcceptedExtension(" ape ") ||
                EacSetupAudit.IsOrpheusAcceptedExtension("mp3") ||
                EacSetupAudit.IsOrpheusAcceptedExtension(null))
            {
                Console.Error.WriteLine("The Orpheus extension audit accepted an unverifiable format.");
                return 1;
            }

            if (EacSetupAudit.DisplayReadCommand(null) != "Not configured" ||
                EacSetupAudit.DisplayReadCommand(0) != "Not autodetected" ||
                EacSetupAudit.DisplayReadCommand(7) != "Command set 7" ||
                EacSetupAudit.DisplayGapDetectionAccuracy(null) != "Not configured" ||
                EacSetupAudit.DisplayGapDetectionAccuracy(0) != "Inaccurate" ||
                EacSetupAudit.DisplayGapDetectionAccuracy(1) != "Accurate" ||
                EacSetupAudit.DisplayGapDetectionAccuracy(2) != "Secure" ||
                EacSetupAudit.DisplayGapDetectionAccuracy(3) != "Unknown value (3)")
            {
                Console.Error.WriteLine("The drive-option audit did not format enum values clearly.");
                return 1;
            }

            IntPtr mainWindow = arguments.Length == 0
                ? IntPtr.Zero
                : Process.GetProcessById(Int32.Parse(arguments[0])).MainWindowHandle;
            EacSetupAuditResult result = EacSetupAudit.Run(mainWindow);
            foreach (EacSetupAuditIssue issue in result.LogScoreIssues)
            {
                Console.WriteLine(
                    issue.Section + " | " + issue.Setting +
                    " | current=" + issue.Current +
                    " | required=" + issue.Required);
            }
            foreach (EacSetupAuditIssue issue in result.Recommendations)
            {
                Console.WriteLine(
                    issue.Section + " | " + issue.Setting +
                    " | current=" + issue.Current +
                    " | recommended=" + issue.Required);
            }

            if (mainWindow != IntPtr.Zero)
                return 0;

            // With no EAC window supplied, drive detection must fail cleanly.
            foreach (EacSetupAuditIssue issue in result.LogScoreIssues)
            {
                if (issue.Section == "Drive Options" && issue.Setting == "Selected drive")
                    return 0;
            }

            Console.Error.WriteLine("Expected the no-window drive diagnostic.");
            return 1;
        }
    }
}
