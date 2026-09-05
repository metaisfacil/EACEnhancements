using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace AudioDataPlugIn
{
    internal static class MusicBrainzTocTests
    {
        private static int Main()
        {
            // Audio and CD-Extra examples from MusicBrainz's Disc ID Calculation
            // documentation: https://musicbrainz.org/doc/Disc_ID_Calculation
            AssertUrl(
                "1%206%2095462%20150%2015363%2032314%2046592%2063414%2080489",
                CreateEntries(new long[] { 0, 15213, 32164, 46442, 63264, 80339, 95312 }));

            List<CdTocEntry> extra = CreateEntries(
                new long[] { 0, 13959, 33436, 52927, 65631, 77742, 99024, 125824, 188333 });
            extra[6].NextStartSector = 114424;
            AssertUrl(
                "1%207%20114574%20150%2014109%2033586%2053077%2065781%2077892%2099174",
                extra);
            extra.RemoveAt(7);
            AssertUrl(
                "1%207%20114574%20150%2014109%2033586%2053077%2065781%2077892%2099174",
                extra);

            // Preserve a nonzero first-track start (hidden track audio), and
            // produce the same URL regardless of the Windows number format.
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
                AssertUrl("1%201%2020150%201650", CreateEntries(new long[] { 1500, 20000 }));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }

            AssertInvalid(null);
            AssertInvalid(new List<CdTocEntry>());
            AssertInvalid(new List<CdTocEntry> { null });
            AssertInvalid(CreateEntries(new long[] { -1, 75 }));
            AssertInvalid(CreateEntries(new long[] { 75, 75 }));
            AssertInvalid(CreateEntries(new long[] { 0, Int64.MaxValue }));
            List<CdTocEntry> invalid = CreateEntries(new long[] { 0, 75, 150 });
            invalid[1].TrackNumber = 3;
            AssertInvalid(invalid);
            invalid[1].TrackNumber = 2;
            invalid[1].StartSector = 76;
            AssertInvalid(invalid);
            invalid[1].StartSector = 74;
            AssertInvalid(invalid);

            // A session gap is only recognized before the final track.
            invalid = CreateEntries(new long[] { 0, 12000, 13000, 14000 });
            invalid[0].NextStartSector = 600;
            AssertInvalid(invalid);
            Console.WriteLine("MusicBrainz TOC tests passed.");
            return 0;
        }

        private static List<CdTocEntry> CreateEntries(long[] offsets)
        {
            List<CdTocEntry> entries = new List<CdTocEntry>();
            for (int i = 0; i < offsets.Length - 1; i++)
            {
                entries.Add(new CdTocEntry
                {
                    TrackNumber = i + 1,
                    StartSector = offsets[i],
                    NextStartSector = offsets[i + 1]
                });
            }
            return entries;
        }

        private static void AssertUrl(string toc, IList<CdTocEntry> entries)
        {
            string expected = "https://musicbrainz.org/cdtoc/attach?toc=" + toc;
            string actual = MusicBrainzToc.BuildSubmissionUrl(entries);
            if (expected != actual)
                throw new Exception("Submission URL mismatch: " + actual + " (expected " + expected + ")");
        }

        private static void AssertInvalid(IList<CdTocEntry> entries)
        {
            try
            {
                MusicBrainzToc.BuildSubmissionUrl(entries);
            }
            catch (InvalidOperationException)
            {
                return;
            }
            throw new Exception("Invalid TOC was accepted for MusicBrainz submission.");
        }
    }
}
