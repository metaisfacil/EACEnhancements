using System;
using System.Collections.Generic;
using System.IO;

namespace AudioDataPlugIn
{
    internal static class WorkflowFolderPathTests
    {
        private static int Main()
        {
            Dictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "albumartist", "GULLET" },
                { "albumtitle", "Hide & Sick" },
                { "year", "2004" },
                { "comment", "cpcs-004" }
            };
            AssertEqual(
                WorkflowFolderPath.Resolve(
                    "%albumartist% - %albumtitle% (((%year%))) [FLAC] {{{%comment%}}}",
                    metadata),
                "GULLET - Hide & Sick (2004) [FLAC] {cpcs-004}");

            metadata["year"] = String.Empty;
            metadata["comment"] = String.Empty;
            AssertEqual(
                WorkflowFolderPath.Resolve(
                    "%albumartist% - %albumtitle% (((%year%))) [FLAC] {{{%comment%}}}",
                    metadata),
                "GULLET - Hide & Sick [FLAC]");

            string root = Path.GetFullPath("C:\\EAC");
            AssertEqual(
                WorkflowFolderPath.ResolveDestination(
                    root,
                    "%albumartist% - %albumtitle%",
                    metadata,
                    false),
                root.TrimEnd('\\'));
            AssertEqual(
                WorkflowFolderPath.ResolveDestination(
                    root,
                    "%albumartist% - %albumtitle%",
                    metadata,
                    true),
                Path.Combine(root, "GULLET - Hide & Sick"));
            metadata["year"] = "2004";
            AssertEqual(
                WorkflowFolderPath.Resolve(
                    "%albumartist%/%year%/%albumtitle%",
                    metadata),
                "GULLET\\2004\\Hide & Sick");
            AssertEqual(
                WorkflowFolderPath.ResolveDestination(
                    root,
                    "%albumartist%\\%year%/%albumtitle%",
                    metadata,
                    true),
                Path.Combine(root, "GULLET", "2004", "Hide & Sick"));

            metadata["albumtitle"] = "Hide / Sick\\Deluxe";
            AssertEqual(
                WorkflowFolderPath.Resolve(
                    "%albumartist%/%year%/%albumtitle%",
                    metadata),
                "GULLET\\2004\\Hide _ Sick_Deluxe");

            Console.WriteLine("Workflow folder path tests passed.");
            return 0;
        }

        private static void AssertEqual(string actual, string expected)
        {
            if (!String.Equals(actual, expected, StringComparison.Ordinal))
                throw new Exception("Expected '" + expected + "' but got '" + actual + "'.");
        }
    }
}
