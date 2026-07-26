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
                { "comment", "cpcs-004" },
                { "label", "Some Bizzare" },
                { "barcode", "012345678905" },
                { "catalognumber", "CPCS-004" }
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
                WorkflowFolderPath.Resolve(
                    "%albumartist%/%label% - %barcode% - %catalognumber%",
                    metadata),
                "GULLET\\Some Bizzare - 012345678905 - CPCS-004");
            AssertEqual(
                WorkflowFolderPath.Resolve(
                    "%albumartist% - %albumtitle% (((%year%))) [FLAC] {{{%catalognumber%}}}",
                    metadata),
                "GULLET - Hide & Sick (2004) [FLAC] {CPCS-004}");

            metadata["catalognumber"] = String.Empty;
            AssertEqual(
                WorkflowFolderPath.Resolve(
                    "%albumartist% - %albumtitle% (((%year%))) [FLAC] {{{%catalognumber%}}}",
                    metadata),
                "GULLET - Hide & Sick (2004) [FLAC]");
            metadata["catalognumber"] = "CPCS-004";
            AssertEqual(
                WorkflowFolderPath.Resolve(
                    "%albumartist% - %albumtitle% (((%catalognumber%)))",
                    metadata),
                "GULLET - Hide & Sick (CPCS-004)");

            metadata["catalognumber"] = String.Empty;
            AssertEqual(
                WorkflowFolderPath.Resolve(
                    "%albumartist% - %albumtitle% (((%catalognumber%)))",
                    metadata),
                "GULLET - Hide & Sick");
            metadata["catalognumber"] = "CPCS-004";
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

            Dictionary<char, string> characterReplacements = new Dictionary<char, string>
            {
                { ':', "：" },
                { '/', "／" },
                { '\\', "＼" },
                { '"', "''" },
                { '&', " and " },
                { '#', String.Empty }
            };
            AssertEqual(
                WorkflowFolderPath.Resolve(
                    "%albumartist%/%year%/%albumtitle%",
                    metadata,
                    characterReplacements),
                "GULLET\\2004\\Hide ／ Sick＼Deluxe");

            metadata["albumtitle"] = "A: \"B\" & C#";
            AssertEqual(
                WorkflowFolderPath.Resolve(
                    "Literal & %albumtitle%",
                    metadata,
                    characterReplacements),
                "Literal & A： ''B''  and  C");

            AssertEqual(
                WorkflowFolderPath.ResolveAbsoluteDestinationTemplate(
                    "C:\\Command Line Rips\\%albumartist%\\" +
                    "%albumtitle% {{{%catalognumber%}}}",
                    metadata,
                    characterReplacements),
                Path.Combine(
                    "C:\\Command Line Rips",
                    "GULLET",
                    "A" + characterReplacements[':'] +
                    " ''B''  and  C {CPCS-004}"));

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
