using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AudioDataPlugIn
{
    internal static class MusicBrainzToc
    {
        private const int Pregap = 150;
        private const int DataTrackGap = 11400;

        // TOC conversion and trailing data-track detection follow
        // https://eac-log-lookup.blogspot.com/ (kolen, MIT license).
        internal static string BuildSubmissionUrl(IList<CdTocEntry> entries)
        {
            if (entries == null || entries.Count == 0 || entries.Count > 99)
                throw new InvalidOperationException("There is no valid CD TOC to submit.");

            int audioTrackCount = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                CdTocEntry entry = entries[i];
                if (entry == null || entry.TrackNumber != i + 1 ||
                    entry.StartSector < 0 ||
                    entry.NextStartSector <= entry.StartSector ||
                    entry.NextStartSector > UInt32.MaxValue - Pregap)
                {
                    throw new InvalidOperationException("The CD TOC is not valid for MusicBrainz submission.");
                }

                if (i == 0)
                    continue;

                long gap = entry.StartSector - entries[i - 1].NextStartSector;
                if (i == entries.Count - 1 && gap == DataTrackGap)
                    audioTrackCount--;
                else if (gap != 0)
                    throw new InvalidOperationException(
                        "This CD has a non-standard track layout that cannot be submitted to MusicBrainz reliably.");
            }

            // EAC stores the exclusive end sector, so it already includes the
            // +1 applied to the log's inclusive end sector by the lookup page.
            StringBuilder url = new StringBuilder("https://musicbrainz.org/cdtoc/attach?toc=1%20");
            url.Append(audioTrackCount.ToString(CultureInfo.InvariantCulture));
            url.Append("%20").Append(
                (entries[audioTrackCount - 1].NextStartSector + Pregap)
                    .ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < audioTrackCount; i++)
            {
                url.Append("%20").Append(
                    (entries[i].StartSector + Pregap).ToString(CultureInfo.InvariantCulture));
            }
            return url.ToString();
        }
    }
}
