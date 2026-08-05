using System;
using System.Collections.Generic;

namespace AudioDataPlugIn
{
    internal static class CdTocComparisonTests
    {
        private static int Main()
        {
            AssertLogParsing();
            AssertExactMatch();
            AssertShiftThreshold();
            AssertDriftThreshold();
            AssertPostGapHandling();
            AssertTrackCountMismatch();
            AssertDataTrackDetection();
            AssertTwoLogComparison();
            AssertMultiDiscOrderMapping();
            Console.WriteLine("CD TOC comparison tests passed.");
            return 0;
        }

        private static void AssertLogParsing()
        {
            IList<CdTocEntry> entries = Entries(
                Entry(1, 0, 15847),
                Entry(2, 15847, 30859));
            string table = CdTocFormatter.Format(entries, new CdTocLabels());
            string error;
            IList<IList<CdTocEntry>> parsed = CdTocLogParser.ParseTables(
                "Exact Audio Copy V1.8\r\n\r\n" + table,
                out error);
            Assert(parsed.Count == 1, "EAC TOC was not parsed: " + error);
            Assert(parsed[0].Count == 2, "Parsed EAC TOC track count");
            Assert(parsed[0][1].StartSector == 15847, "Parsed start sector");
            Assert(parsed[0][1].NextStartSector == 30859, "Parsed end sector");

            parsed = CdTocLogParser.ParseTables(table + "\r\n" + table, out error);
            Assert(parsed.Count == 2, "Stacked log TOCs were not separated.");

            string invalid = table.Replace("  3:31.22 ", "  3:31.23 ");
            parsed = CdTocLogParser.ParseTables(invalid, out error);
            Assert(parsed.Count == 0 && error.IndexOf("disagree", StringComparison.Ordinal) >= 0,
                "Inconsistent MSF and sector values were accepted.");
        }

        private static void AssertExactMatch()
        {
            IList<CdTocEntry> toc = Entries(
                Entry(1, 0, 1000),
                Entry(2, 1000, 2000));
            CdTocComparisonResult result = CdTocComparer.Compare(toc, toc);
            Assert(result.IsMatch && result.IsExact, "Identical TOCs did not match exactly.");
            Assert(result.Reason.IndexOf("exactly", StringComparison.OrdinalIgnoreCase) >= 0,
                "Exact-match reason");
        }

        private static void AssertShiftThreshold()
        {
            IList<CdTocEntry> source = Entries(
                Entry(1, 0, 1000),
                Entry(2, 1000, 2000));
            CdTocComparisonResult within = CdTocComparer.Compare(
                source,
                Entries(Entry(1, 49, 1049), Entry(2, 1049, 2049)));
            Assert(within.IsMatch && !within.IsExact, "A 49-sector shift was rejected.");
            Assert(ContainsDetail(within, "49 sectors"), "Shift detail was not reported.");

            CdTocComparisonResult boundary = CdTocComparer.Compare(
                source,
                Entries(Entry(1, 50, 1050), Entry(2, 1050, 2050)));
            Assert(!boundary.IsMatch, "A 50-sector shift was accepted.");
            Assert(boundary.Reason.IndexOf("below 50", StringComparison.Ordinal) >= 0,
                "Shift-threshold reason");
        }

        private static void AssertDriftThreshold()
        {
            IList<CdTocEntry> source = Entries(
                Entry(1, 0, 1000),
                Entry(2, 1000, 2000));
            CdTocComparisonResult within = CdTocComparer.Compare(
                source,
                Entries(Entry(1, 0, 1000), Entry(2, 1000, 2009)));
            Assert(within.IsMatch, "Nine sectors of drift were rejected.");
            Assert(ContainsDetail(within, "9 sectors of drift"), "Drift detail was not reported.");

            CdTocComparisonResult boundary = CdTocComparer.Compare(
                source,
                Entries(Entry(1, 0, 1000), Entry(2, 1000, 2010)));
            Assert(!boundary.IsMatch, "Ten sectors of drift were accepted.");
            Assert(boundary.Reason.IndexOf("below 10", StringComparison.Ordinal) >= 0,
                "Drift-threshold reason");
        }

        private static void AssertPostGapHandling()
        {
            IList<CdTocEntry> source = Entries(
                Entry(1, 0, 1000),
                Entry(2, 1000, 2000));
            CdTocComparisonResult result = CdTocComparer.Compare(
                source,
                Entries(Entry(1, 0, 1000), Entry(2, 1000, 2150)));
            Assert(result.IsMatch, "The script's 150-sector post-gap exception was not applied.");
            Assert(ContainsDetail(result, "post-gap"), "Post-gap detail was not reported.");
        }

        private static void AssertTrackCountMismatch()
        {
            CdTocComparisonResult result = CdTocComparer.Compare(
                Entries(Entry(1, 0, 1000)),
                Entries(Entry(1, 0, 1000), Entry(2, 1000, 2000)));
            Assert(!result.IsMatch, "Different audio-track counts matched.");
            Assert(result.Reason.IndexOf("counts differ", StringComparison.Ordinal) >= 0,
                "Track-count mismatch reason");
        }

        private static void AssertDataTrackDetection()
        {
            IList<CdTocEntry> mixedMode = Entries(
                Entry(1, 0, 1000),
                Entry(2, 12400, 15000));
            Assert(CdTocComparer.GetDataTrackCount(mixedMode) == 1,
                "The script's 11,400-sector mixed-mode gap was not detected.");
            IList<CdTocEntry> unknown = Entries(
                Entry(1, 0, 1000),
                Entry(2, 1100, 2000));
            Assert(CdTocComparer.GetDataTrackCount(unknown) == -1,
                "An unknown inter-track gap was treated as a known layout.");
        }

        private static void AssertTwoLogComparison()
        {
            IList<CdTocEntry> first = Entries(
                Entry(1, 0, 1000),
                Entry(2, 1000, 2000));
            CdTocReleaseComparisonResult exact = CdTocReleaseComparer.Compare(
                Tables(first),
                Tables(first));
            Assert(exact.IsMatch && exact.IsExact,
                "Two identical single-disc logs did not match exactly.");

            CdTocReleaseComparisonResult shifted = CdTocReleaseComparer.Compare(
                Tables(first),
                Tables(Entries(Entry(1, 49, 1049), Entry(2, 1049, 2049))));
            Assert(shifted.IsMatch && !shifted.IsExact,
                "Two logs within the shift threshold did not match.");

            CdTocReleaseComparisonResult mismatch = CdTocReleaseComparer.Compare(
                Tables(first),
                Tables(Entries(Entry(1, 55, 1055), Entry(2, 1055, 2055))));
            Assert(!mismatch.IsMatch && mismatch.Details.Count == 0,
                "A single-table mismatch repeated its reason as a pairwise detail.");

            CdTocReleaseComparisonResult countMismatch = CdTocReleaseComparer.Compare(
                Tables(first),
                new List<IList<CdTocEntry>>
                {
                    first,
                    Entries(Entry(1, 0, 500))
                });
            Assert(!countMismatch.IsMatch &&
                countMismatch.Reason.IndexOf("different numbers", StringComparison.Ordinal) >= 0,
                "Different stacked-log TOC counts matched.");
        }

        private static void AssertMultiDiscOrderMapping()
        {
            IList<CdTocEntry> discOne = Entries(
                Entry(1, 0, 1000),
                Entry(2, 1000, 2000));
            IList<CdTocEntry> discTwo = Entries(
                Entry(1, 0, 700),
                Entry(2, 700, 1400),
                Entry(3, 1400, 2100));
            CdTocReleaseComparisonResult reordered = CdTocReleaseComparer.Compare(
                new List<IList<CdTocEntry>> { discOne, discTwo },
                new List<IList<CdTocEntry>> { discTwo, discOne });
            Assert(reordered.IsMatch && reordered.IsExact,
                "Multi-disc log TOCs did not match in a different order.");
            Assert(ContainsDetail(reordered, "table 2"),
                "The multi-disc table mapping was not reported.");

            CdTocReleaseComparisonResult mismatch = CdTocReleaseComparer.Compare(
                new List<IList<CdTocEntry>> { discOne, discTwo },
                new List<IList<CdTocEntry>>
                {
                    Entries(Entry(1, 100, 1100), Entry(2, 1100, 2100)),
                    discOne
                });
            Assert(!mismatch.IsMatch && mismatch.Details.Count > 0,
                "A failed multi-disc mapping did not explain its pairwise failures.");
        }

        private static bool ContainsDetail(CdTocComparisonResult result, string value)
        {
            foreach (string detail in result.Details)
            {
                if (detail.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool ContainsDetail(CdTocReleaseComparisonResult result, string value)
        {
            foreach (string detail in result.Details)
            {
                if (detail.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static IList<IList<CdTocEntry>> Tables(params IList<CdTocEntry>[] tables)
        {
            return new List<IList<CdTocEntry>>(tables);
        }

        private static IList<CdTocEntry> Entries(params CdTocEntry[] entries)
        {
            return new List<CdTocEntry>(entries);
        }

        private static CdTocEntry Entry(int number, long start, long nextStart)
        {
            return new CdTocEntry
            {
                TrackNumber = number,
                StartSector = start,
                NextStartSector = nextStart
            };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }
}
