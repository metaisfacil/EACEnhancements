using System;
using Microsoft.Win32;

namespace AudioDataPlugIn
{
    internal static class WorkflowNamingSchemesTests
    {
        private static int Main()
        {
            const string template = "%albumartist% - %albumtitle% (((%year%))) [FLAC] {{{%catalognumber%}}}";
            const string tail = "%tracknr2% %title%";
            foreach (string folder in new[] {
                "%albumartist% - %albumtitle% (%year%) [FLAC] {%catalognumber%}",
                "%albumartist% - %albumtitle% [FLAC] {%catalognumber%}",
                "%albumartist% - %albumtitle% (%year%) [FLAC] ",
                "%albumartist% - %albumtitle% [FLAC] " })
            {
                Assert(tail, WorkflowNamingSchemes.RemoveLegacyFolderPrefix(
                    folder + "\\" + tail, template));
            }
            Assert(tail, WorkflowNamingSchemes.RemoveLegacyFolderPrefix(
                "%albumartist%/%year%/%albumtitle%/" + tail,
                "%albumartist%/%year%/%albumtitle%"));
            Assert(tail, WorkflowNamingSchemes.RemoveLegacyFolderPrefix(
                "%albumartist% [FLAC]\\" + tail,
                "%albumartist%\t(((%year%)))\t[FLAC]"));
            foreach (string custom in new[] {
                tail, "Custom\\" + tail, "%albumartist%\\%albumtitle%\\" + tail,
                "Archive\\%albumartist% - %albumtitle% (%year%) [FLAC] {%catalognumber%}\\" + tail,
                "%albumartist% - %albumtitle% (%year%) [MP3] {%catalognumber%}\\" + tail,
                "", null })
                Assert(custom, WorkflowNamingSchemes.RemoveLegacyFolderPrefix(custom, template));

            // Exercise the actual registry operations in an isolated test key:
            // custom directories, all four schemes, absent/empty values, and
            // repeated workflows must survive both success and cancellation.
            string path = @"Software\EACEnhancements.Tests\" + Guid.NewGuid().ToString("N");
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path))
                {
                    key.SetValue("FileNamingConvention", "Custom\\" + tail);
                    key.SetValue("FileNamingConvention2nd", "Alternate/Disc/%tracknr2% - %title%");
                    key.SetValue("VariousFileNamingConvention", "");
                    key.SetValue("Unrelated", "untouched");
                    for (int pass = 0; pass < 2; pass++)
                    {
                        WorkflowNamingSchemes snapshot = WorkflowNamingSchemes.Capture(key);
                        snapshot.ApplyTrackOnly(key);
                        Assert(tail, key.GetValue("FileNamingConvention") as string);
                        Assert("%tracknr2% - %title%", key.GetValue("FileNamingConvention2nd") as string);
                        Assert("%artist% - %title%", key.GetValue("VariousFileNamingConvention") as string);
                        Assert("%artist% - %title%", key.GetValue("VariousFileNamingConvention2nd") as string);
                        snapshot.Restore(key);
                        snapshot.Restore(key);
                        Assert("Custom\\" + tail, key.GetValue("FileNamingConvention") as string);
                        Assert("Alternate/Disc/%tracknr2% - %title%", key.GetValue("FileNamingConvention2nd") as string);
                        Assert("", key.GetValue("VariousFileNamingConvention") as string);
                        Assert(null, key.GetValue("VariousFileNamingConvention2nd") as string);
                    }
                    key.SetValue("FileNamingConvention",
                        "%albumartist% - %albumtitle% (%year%) [FLAC] {%catalognumber%}\\" + tail);
                    if (!WorkflowNamingSchemes.RemoveLegacyFolderPrefixes(key, template))
                        throw new Exception("Legacy naming settings were not migrated.");
                    if (WorkflowNamingSchemes.RemoveLegacyFolderPrefixes(key, template))
                        throw new Exception("Migration was not idempotent.");
                    Assert(tail, key.GetValue("FileNamingConvention") as string);
                    Assert("Alternate/Disc/%tracknr2% - %title%", key.GetValue("FileNamingConvention2nd") as string);
                    Assert("untouched", key.GetValue("Unrelated") as string);
                }
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(path, false);
            }
            Console.WriteLine("Workflow naming isolation tests passed.");
            return 0;
        }

        private static void Assert(string expected, string actual)
        {
            if (!String.Equals(expected, actual, StringComparison.Ordinal))
                throw new Exception("Expected '" + expected + "', got '" + actual + "'.");
        }
    }
}
