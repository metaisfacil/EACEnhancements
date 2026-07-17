using System;
using System.IO;

namespace AudioDataPlugIn
{
    internal static class NestedOutputLocationTests
    {
        private static int Main()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "EACEnhancements-NestedOutput-" + Guid.NewGuid().ToString("N"));
            string destination = Path.Combine(root, "Artist", "2004", "Album", "Disc 1");
            string logPath = Path.Combine(destination, "Album.log");
            try
            {
                Directory.CreateDirectory(destination);
                File.WriteAllText(logPath, "Exact Audio Copy extraction logfile");
                File.SetLastWriteTimeUtc(logPath, DateTime.UtcNow.AddSeconds(1.0));

                FileInfo found = EnhancementRuntime.FindMostRecentRipLog(
                    DateTime.UtcNow.AddMinutes(-1.0),
                    destination);
                if (found == null || !String.Equals(
                    found.FullName,
                    logPath,
                    StringComparison.OrdinalIgnoreCase))
                    throw new Exception("The log in the resolved nested output folder was not found.");
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }

            Console.WriteLine("Nested output location tests passed.");
            return 0;
        }
    }
}
