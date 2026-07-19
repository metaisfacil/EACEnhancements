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

            IntPtr mainWindow = arguments.Length == 0
                ? IntPtr.Zero
                : Process.GetProcessById(Int32.Parse(arguments[0])).MainWindowHandle;
            EacSetupAuditResult result = EacSetupAudit.Run(mainWindow);
            foreach (EacSetupAuditIssue issue in result.Issues)
            {
                Console.WriteLine(
                    issue.Section + " | " + issue.Setting +
                    " | current=" + issue.Current +
                    " | required=" + issue.Required);
            }

            if (mainWindow != IntPtr.Zero)
                return 0;

            // With no EAC window supplied, drive detection must fail cleanly.
            foreach (EacSetupAuditIssue issue in result.Issues)
            {
                if (issue.Section == "Drive Options" && issue.Setting == "Selected drive")
                    return 0;
            }

            Console.Error.WriteLine("Expected the no-window drive diagnostic.");
            return 1;
        }
    }
}
