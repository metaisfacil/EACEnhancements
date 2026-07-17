using System;
using System.Collections.Generic;
using AudioDataPlugIn;

internal static class RipLogErrorParserTests
{
    private static int failures;

    private static void Main()
    {
        TestTrackAttribution();
        TestSelectedRange();
        TestCallbackOnlySuspiciousPosition();
        TestAppendedLogUsesOnlyLatestReport();
        TestIncompleteAppendedReportIsNotComplete();
        TestCleanLog();
        if (failures != 0)
            Environment.Exit(1);
        Console.WriteLine("RipLogErrorParser tests passed.");
    }

    private static void TestTrackAttribution()
    {
        string log =
            "Track 1\r\n" +
            "  Read error\r\n" +
            "  Read error\r\n" +
            "  Suspicious position 0:01:23\r\n" +
            "  Test CRC AAAAAAAA\r\n" +
            "  Copy CRC BBBBBBBB\r\n" +
            "Track 4\r\n" +
            "  Sync error\r\n" +
            "  Cannot be verified as accurate\r\n" +
            "AccurateRip summary\r\n" +
            "Track 7 not accurately ripped\r\n" +
            "End of status report\r\n";
        AssertEqual(
            RipLogErrorParser.Parse(log, 1),
            "Read error (2) \u2014 track 1",
            "Suspicious position \u2014 track 1",
            "Mismatched Test/Copy CRC \u2014 track 1",
            "Sync error \u2014 track 4",
            "AccurateRip verification mismatch (2) \u2014 tracks 4, 7");
    }

    private static void TestSelectedRange()
    {
        AssertEqual(
            RipLogErrorParser.Parse(
                "Range status and errors\r\nSelected range (Sectors 0-100)\r\nSuspicious position 0:00:00\r\n",
                0),
            "Suspicious position \u2014 selected range");
    }

    private static void TestCallbackOnlySuspiciousPosition()
    {
        AssertEqual(
            RipLogErrorParser.Parse("End of status report\r\n", 1),
            "Suspicious position \u2014 1 occurrence not attributable to a track");
    }

    private static void TestCleanLog()
    {
        AssertEqual(RipLogErrorParser.Parse("No errors occurred\r\nEnd of status report\r\n", 0));
    }

    private static void TestAppendedLogUsesOnlyLatestReport()
    {
        string oldReport =
            "Exact Audio Copy V1.8 from 15. July 2024\r\n" +
            "Track 2\r\n  Read error\r\nThere were errors\r\nEnd of status report\r\n";
        string newReport =
            "------------------------------------------------------------\r\n" +
            "Exact Audio Copy V1.8 from 15. July 2024\r\n" +
            "Track 6\r\n  Sync error\r\nThere were errors\r\nEnd of status report\r\n";
        AssertEqual(
            RipLogErrorParser.Parse(oldReport + newReport, 0),
            "Sync error \u2014 track 6");
    }

    private static void TestIncompleteAppendedReportIsNotComplete()
    {
        string oldReport =
            "Exact Audio Copy V1.8 from 15. July 2024\r\n" +
            "No errors occurred\r\nEnd of status report\r\n";
        string partialAppend =
            "------------------------------------------------------------\r\n" +
            "Exact Audio Copy V1.8 from 15. July 2024\r\nTrack 3\r\n";
        if (RipLogErrorParser.IsLatestReportComplete(oldReport + partialAppend))
        {
            failures++;
            Console.Error.WriteLine("An incomplete appended report was incorrectly considered complete.");
        }
    }

    private static void AssertEqual(List<string> actual, params string[] expected)
    {
        if (actual.Count == expected.Length)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (!String.Equals(actual[i], expected[i], StringComparison.Ordinal))
                    break;
                if (i == expected.Length - 1)
                    return;
            }
            if (expected.Length == 0)
                return;
        }
        failures++;
        Console.Error.WriteLine("Expected: " + String.Join(" | ", expected));
        Console.Error.WriteLine("Actual:   " + String.Join(" | ", actual.ToArray()));
    }
}
