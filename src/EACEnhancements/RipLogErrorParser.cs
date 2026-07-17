using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AudioDataPlugIn
{
    internal static class RipLogErrorParser
    {
        internal static List<string> Parse(string logText, int suspiciousCallbackCount)
        {
            ErrorCollection errors = new ErrorCollection();
            string text = SelectLatestReport(logText);
            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int? currentTrack = null;
            bool selectedRange = false;
            string testCrc = null;
            int suspiciousLogCount = 0;

            foreach (string line in lines)
            {
                Match trackHeader = Regex.Match(line, "^\\s*Track\\s+(\\d+)\\s*$", RegexOptions.IgnoreCase);
                if (trackHeader.Success)
                {
                    currentTrack = Int32.Parse(trackHeader.Groups[1].Value);
                    selectedRange = false;
                    testCrc = null;
                    continue;
                }

                if (Regex.IsMatch(line, "^\\s*(?:Range status and errors|Selected range\\b)", RegexOptions.IgnoreCase))
                {
                    currentTrack = null;
                    selectedRange = true;
                    testCrc = null;
                }
                else if (Regex.IsMatch(line, "^\\s*(?:AccurateRip summary|End of status report)\\s*$", RegexOptions.IgnoreCase))
                {
                    currentTrack = null;
                    selectedRange = false;
                    testCrc = null;
                }

                AddLineError(errors, line, "^\\s*Read error\\b", "Read error", currentTrack, selectedRange);
                AddLineError(errors, line, "^\\s*Sync error\\b", "Sync error", currentTrack, selectedRange);
                if (Regex.IsMatch(line, "^\\s*Suspicious position\\b", RegexOptions.IgnoreCase))
                {
                    errors.Add("Suspicious position", currentTrack, selectedRange);
                    suspiciousLogCount++;
                }
                AddLineError(errors, line, "^\\s*Timing problem\\b", "Timing problem", currentTrack, selectedRange);
                AddLineError(errors, line, "^\\s*Missing samples\\b", "Missing samples", currentTrack, selectedRange);
                AddLineError(errors, line, "^\\s*Too many samples\\b", "Too many samples", currentTrack, selectedRange);
                AddLineError(errors, line, "^\\s*(?:File )?write error\\b", "File write error", currentTrack, selectedRange);
                AddLineError(errors, line, "^\\s*(?:Copy|Extraction) aborted\\b", "Extraction aborted", currentTrack, selectedRange);
                AddLineError(errors, line, "^\\s*(?:External compressor|Encoder).*(?:error|failed)\\b", "Encoder error", currentTrack, selectedRange);

                Match test = Regex.Match(line, "^\\s*Test CRC\\s+([0-9A-F]+)\\s*$", RegexOptions.IgnoreCase);
                if (test.Success)
                {
                    testCrc = test.Groups[1].Value;
                }
                else
                {
                    Match copy = Regex.Match(line, "^\\s*Copy CRC\\s+([0-9A-F]+)\\s*$", RegexOptions.IgnoreCase);
                    if (copy.Success && testCrc != null)
                    {
                        if (!testCrc.Equals(copy.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                            errors.Add("Mismatched Test/Copy CRC", currentTrack, selectedRange);
                        testCrc = null;
                    }
                }

                if (Regex.IsMatch(
                    line,
                    "(?:not accurately ripped|could not be verified as accurate|cannot be verified as accurate)",
                    RegexOptions.IgnoreCase))
                {
                    Match explicitTrack = Regex.Match(line, "^\\s*Track\\s+(\\d+)\\b", RegexOptions.IgnoreCase);
                    int? mismatchTrack = explicitTrack.Success
                        ? (int?)Int32.Parse(explicitTrack.Groups[1].Value)
                        : currentTrack;
                    errors.Add("AccurateRip verification mismatch", mismatchTrack, selectedRange && !mismatchTrack.HasValue);
                }
            }

            for (int i = suspiciousLogCount; i < suspiciousCallbackCount; i++)
                errors.Add("Suspicious position", null, false);

            if (errors.Count == 0 && Regex.IsMatch(
                text,
                "^\\s*There were errors\\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                errors.Add("EAC reported unspecified extraction errors", null, false);
            }

            return errors.Format();
        }

        internal static bool IsLatestReportComplete(string logText)
        {
            return SelectLatestReport(logText).IndexOf(
                "End of status report",
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string SelectLatestReport(string logText)
        {
            string text = logText ?? String.Empty;
            if (text.Length == 0)
                return text;

            int reportStart = -1;
            MatchCollection headers = Regex.Matches(
                text,
                "^\\s*Exact Audio Copy V[^\\r\\n]*$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (headers.Count > 0)
                reportStart = headers[headers.Count - 1].Index;

            // EAC separates appended reports with a long hyphen-only line.
            // Honor a trailing delimiter even while the next report header is
            // only partially written, so an older completed report cannot win.
            MatchCollection delimiters = Regex.Matches(
                text,
                "^\\s*-{20,}\\s*$",
                RegexOptions.Multiline);
            if (delimiters.Count > 0)
            {
                Match delimiter = delimiters[delimiters.Count - 1];
                int afterDelimiter = delimiter.Index + delimiter.Length;
                if (afterDelimiter > reportStart)
                    reportStart = afterDelimiter;
            }

            return reportStart > 0 ? text.Substring(reportStart) : text;
        }

        private static void AddLineError(
            ErrorCollection errors,
            string line,
            string pattern,
            string label,
            int? track,
            bool selectedRange)
        {
            if (Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase))
                errors.Add(label, track, selectedRange);
        }

        private sealed class ErrorCollection
        {
            private readonly List<ErrorEntry> ordered = new List<ErrorEntry>();
            private readonly Dictionary<string, ErrorEntry> byLabel =
                new Dictionary<string, ErrorEntry>(StringComparer.OrdinalIgnoreCase);

            internal int Count
            {
                get { return ordered.Count; }
            }

            internal void Add(string label, int? track, bool selectedRange)
            {
                ErrorEntry entry;
                if (!byLabel.TryGetValue(label, out entry))
                {
                    entry = new ErrorEntry(label);
                    byLabel.Add(label, entry);
                    ordered.Add(entry);
                }
                entry.Add(track, selectedRange);
            }

            internal List<string> Format()
            {
                List<string> result = new List<string>();
                foreach (ErrorEntry entry in ordered)
                    result.Add(entry.Format());
                return result;
            }
        }

        private sealed class ErrorEntry
        {
            private readonly string label;
            private readonly SortedSet<int> tracks = new SortedSet<int>();
            private int count;
            private int unattributedCount;
            private bool includesSelectedRange;

            internal ErrorEntry(string label)
            {
                this.label = label;
            }

            internal void Add(int? track, bool selectedRange)
            {
                count++;
                if (track.HasValue)
                    tracks.Add(track.Value);
                else if (selectedRange)
                    includesSelectedRange = true;
                else
                    unattributedCount++;
            }

            internal string Format()
            {
                StringBuilder text = new StringBuilder(label);
                if (count != 1)
                    text.Append(" (").Append(count).Append(')');

                List<string> locations = new List<string>();
                if (tracks.Count == 1)
                    locations.Add("track " + JoinTracks());
                else if (tracks.Count > 1)
                    locations.Add("tracks " + JoinTracks());
                if (includesSelectedRange)
                    locations.Add("selected range");
                if (unattributedCount > 0)
                {
                    locations.Add(unattributedCount == 1
                        ? "1 occurrence not attributable to a track"
                        : unattributedCount + " occurrences not attributable to a track");
                }
                if (locations.Count > 0)
                    text.Append(" — ").Append(String.Join("; ", locations.ToArray()));
                return text.ToString();
            }

            private string JoinTracks()
            {
                string[] values = new string[tracks.Count];
                int index = 0;
                foreach (int track in tracks)
                    values[index++] = track.ToString();
                return String.Join(", ", values);
            }
        }
    }
}
