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
