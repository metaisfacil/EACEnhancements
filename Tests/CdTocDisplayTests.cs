using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioDataPlugIn
{
    internal static class CdTocDisplayTests
    {
        private static int Main()
        {
            AssertEnglishRipLogTable();
            AssertLocalizedColumnExpansion();
            AssertLanguageFileParsing();
            AssertToolsMenuResolution();
            Console.WriteLine("CD TOC display tests passed.");
            return 0;
        }

        private static void AssertEnglishRipLogTable()
        {
            List<CdTocEntry> entries = new List<CdTocEntry>
            {
                new CdTocEntry
                {
                    TrackNumber = 1,
                    StartSector = 0,
                    NextStartSector = 15847
                },
                new CdTocEntry
                {
                    TrackNumber = 2,
                    StartSector = 15847,
                    NextStartSector = 30859
                },
                new CdTocEntry
                {
                    TrackNumber = 8,
                    StartSector = 106207,
                    NextStartSector = 125619
                }
            };
            string expected =
                "TOC of the extracted CD\r\n" +
                "\r\n" +
                "     Track |   Start  |  Length  | Start sector | End sector \r\n" +
                "    ---------------------------------------------------------\r\n" +
                "        1  |  0:00.00 |  3:31.22 |         0    |    15846   \r\n" +
                "        2  |  3:31.22 |  3:20.12 |     15847    |    30858   \r\n" +
                "        8  | 23:36.07 |  4:18.62 |    106207    |   125618   \r\n" +
                "\r\n";
            AssertEqual(expected, CdTocFormatter.Format(entries, new CdTocLabels()),
                "English EAC table");
            AssertEqual(" 0:00.00", CdTocFormatter.FormatMsf(0), "zero MSF");
            AssertEqual("99:59.74", CdTocFormatter.FormatMsf(449999), "maximum MSF");
        }

        private static void AssertLocalizedColumnExpansion()
        {
            CdTocLabels labels = new CdTocLabels();
            labels.Track = "Very long track caption";
            string table = CdTocFormatter.Format(
                new List<CdTocEntry>
                {
                    new CdTocEntry
                    {
                        TrackNumber = 1,
                        StartSector = 0,
                        NextStartSector = 75
                    }
                },
                labels);
            string[] lines = table.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines[2].Length != lines[3].Length || lines[3].Trim().Trim('-').Length != 0)
                throw new Exception("Localized TOC columns did not expand EAC's separator.");
        }

        private static void AssertLanguageFileParsing()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "EACEnhancements-TocLanguage-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(
                    path,
                    "  1289 = \"Localized TOC\"\r\n" +
                    "  1290 = \"Localized Track\"\r\n" +
                    "240000 = \"&Utilities\"\r\n",
                    Encoding.Unicode);
                Dictionary<int, string> values = EnhancementRuntime.ReadLanguageStrings(path);
                AssertEqual("Localized TOC", values[1289], "localized title");
                AssertEqual("Localized Track", values[1290], "localized track");
                AssertEqual("&Utilities", values[240000], "localized Tools caption");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static void AssertToolsMenuResolution()
        {
            IntPtr root = CreateMenu();
            IntPtr eac = CreateMenu();
            IntPtr edit = CreateMenu();
            IntPtr action = CreateMenu();
            IntPtr database = CreateMenu();
            IntPtr tools = CreateMenu();
            try
            {
                if (root == IntPtr.Zero || eac == IntPtr.Zero || edit == IntPtr.Zero ||
                    action == IntPtr.Zero || database == IntPtr.Zero || tools == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not create the Tools-menu fixture.");
                }
                AppendPopup(root, eac, "&EAC");
                AppendPopup(root, edit, "&Edit");
                AppendPopup(root, action, "&Action");
                AppendPopup(root, database, "&Database");
                AppendPopup(root, tools, "&Tools");
                if (EnhancementRuntime.FindToolsMenu(root) != tools)
                    throw new Exception("EAC's Tools menu was not resolved.");
            }
            finally
            {
                DestroyMenu(root);
            }
        }

        private static void AppendPopup(IntPtr menu, IntPtr popup, string text)
        {
            if (!NativeMethods.AppendMenuW(
                menu,
                NativeMethods.MF_POPUP | NativeMethods.MF_STRING,
                new UIntPtr(unchecked((uint)popup.ToInt32())),
                text))
            {
                throw new InvalidOperationException("Could not populate the Tools-menu fixture.");
            }
        }

        private static void AssertEqual(string expected, string actual, string name)
        {
            if (!String.Equals(expected, actual, StringComparison.Ordinal))
                throw new Exception(name + " mismatch.\r\nExpected: [" + expected + "]\r\nActual: [" + actual + "]");
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateMenu();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(IntPtr menu);
    }
}
