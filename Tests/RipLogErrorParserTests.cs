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
        TestCleanHtoaWorkflow();
        TestMismatchedHtoaCrcs();
        TestIncompleteHtoaCrcPair();
        TestIncompleteHtoaSecondReportIsNotComplete();
        TestTracksAbsentFromAccurateRipAreNotMismatches();
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

    private static void TestTracksAbsentFromAccurateRipAreNotMismatches()
    {
        string log =
            "Track  4\r\n" +
            "  Test CRC B6704598\r\n" +
            "  Copy CRC B6704598\r\n" +
            "  Track not present in AccurateRip database\r\n" +
            "  Copy OK\r\n" +
            "Track  5\r\n" +
            "  Test CRC DA0BCBA1\r\n" +
            "  Copy CRC DA0BCBA1\r\n" +
            "  Track not present in AccurateRip database\r\n" +
            "  Copy OK\r\n" +
            "3 track(s) accurately ripped\r\n" +
            "2 track(s) not present in the AccurateRip database\r\n" +
            "Some tracks could not be verified as accurate\r\n" +
            "No errors occurred\r\n" +
            "End of status report\r\n";
        AssertEqual(RipLogErrorParser.Parse(log, 0));
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

    private static void TestCleanHtoaWorkflow()
    {
        string log = HtoaReport("4:34", "255229CD", null) +
            "------------------------------------------------------------\r\n" +
            HtoaReport("4:36", "255229CD", null);
        AssertEqual(RipLogErrorParser.ParseHtoaWorkflow(log, 0));
        if (!RipLogErrorParser.IsHtoaWorkflowComplete(log))
        {
            failures++;
            Console.Error.WriteLine("Two complete HTOA reports were not recognized as complete.");
        }
    }

    private static void TestMismatchedHtoaCrcs()
    {
        string log = HtoaReport("4:34", "255229CD", "Read error") +
            "------------------------------------------------------------\r\n" +
            HtoaReport("4:36", "DEADBEEF", "Sync error");
        AssertEqual(
            RipLogErrorParser.ParseHtoaWorkflow(log, 0),
            "Read error \u2014 selected range",
            "Sync error \u2014 selected range",
            "Mismatched Test/Copy CRC \u2014 selected range");
    }

    private static void TestIncompleteHtoaCrcPair()
    {
        string log = HtoaReport("4:34", "255229CD", null) +
            "------------------------------------------------------------\r\n" +
            HtoaReport("4:36", null, null);
        AssertEqual(
            RipLogErrorParser.ParseHtoaWorkflow(log, 0),
            "Missing HTOA Test/Copy CRC \u2014 selected range");
    }

    private static void TestIncompleteHtoaSecondReportIsNotComplete()
    {
        string log = HtoaReport("4:34", "255229CD", null) +
            "------------------------------------------------------------\r\n" +
            "Exact Audio Copy V1.8 from 15. July 2024\r\n" +
            "Range status and errors\r\nSelected range (Sectors 0-45149)\r\n";
        if (RipLogErrorParser.IsHtoaWorkflowComplete(log))
        {
            failures++;
            Console.Error.WriteLine("An incomplete second HTOA report was considered complete.");
        }
    }

    private static string HtoaReport(string time, string copyCrc, string error)
    {
        return
            "Exact Audio Copy V1.8 from 15. July 2024\r\n" +
            "EAC extraction logfile from 19. July 2026, " + time + "\r\n" +
            "Range status and errors\r\n" +
            "Selected range   (Sectors 0-45149)\r\n" +
            (error == null ? String.Empty : error + "\r\n") +
            (copyCrc == null ? String.Empty : "Copy CRC " + copyCrc + "\r\n") +
            (error == null ? "No errors occurred\r\n" : "There were errors\r\n") +
            "AccurateRip summary\r\n" +
            "No tracks could be verified as accurate\r\n" +
            "End of status report\r\n";
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
