using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AudioDataPlugIn
{
    internal sealed class CdTocComparisonResult
    {
        internal bool IsMatch;
        internal bool IsExact;
        internal int TrackCount;
        internal string Reason = String.Empty;
        internal readonly List<string> Details = new List<string>();
    }

    internal sealed class CdTocReleaseComparisonResult
    {
        internal bool IsMatch;
        internal bool IsExact;
        internal string Reason = String.Empty;
        internal readonly List<string> Details = new List<string>();
    }

    internal static class CdTocLogParser
    {
        private const string MsfPattern =
            @"(?:(?:-?\d+):)?-?\d+:-?\d+[\.:]-?\d+";

        private static readonly Regex[] TocRowPatterns =
        {
            // EAC and XLD.
            new Regex(
                @"^\s*(?<track>\d+)\s+\|\s+(?<start>" + MsfPattern +
                @")\s+\|\s+(?<length>" + MsfPattern +
                @")\s+\|\s+(?<startSector>-?\d+)\s+\|\s+(?<endSector>-?\d+)\s*$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant),
            // EZ CD Audio Converter.
            new Regex(
                @"^\s*\[X\]\s+(?<track>\d+)\s+(?<start>" + MsfPattern +
                @")\s+(?<length>" + MsfPattern +
                @")\s+(?<startSector>-?\d+)\s+(?<endSector>-?\d+)\b.*$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant),
            // whipper.
            new Regex(
                @"^\s+(?<track>\d+):\s*\r?\n\s{4,}Start:\s*(?<start>" + MsfPattern +
                @")\b\s*\r?\n\s{4,}Length:\s*(?<length>" + MsfPattern +
                @")\b\s*\r?\n\s{4,}Start sector:\s*(?<startSector>-?\d+)\b\s*\r?\n" +
                @"\s{4,}End sector:\s*(?<endSector>-?\d+)\b\s*",
                RegexOptions.Multiline | RegexOptions.CultureInvariant),
            // Rip.
            new Regex(
                @"^\s*(?<track>\d+)\s+\|\s+(?<start>" + MsfPattern +
                @")\s+\|\s+" + MsfPattern +
                @"\s+\|\s+(?<length>" + MsfPattern +
                @")\s+\|\s+(?<startSector>-?\d+)\s+\|\s+(?<endSector>-?\d+)" +
                @"\s+\|\s+\d+\s*$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
        };

        private static readonly Regex PercentPeakPattern = new Regex(
            @"^\s*(?:Peak level|Пиковый уровень|Ïèêîâûé óðîâåíü|峰值电平|ピークレベル|Spitzenpegel|" +
            @"Pauze lengte|Livello di picco|Peak-nivå|Nivel Pico|Пиково ниво|Poziom wysterowania|" +
            @"Vršni nivo|[Šš]pičková úroveň|Nível de Pico)\s+(?<peak>\d+(?:\.\d+)?)\s*%\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex FractionPeakPattern = new Regex(
            @"^\s*Peak(?: level)?\s*:\s*(?<peak>\d+(?:\.\d+)?)\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static IList<IList<CdTocEntry>> ParseTables(string logText, out string error)
        {
            error = String.Empty;
            if (String.IsNullOrEmpty(logText))
            {
                error = "The selected file is empty.";
                return new List<IList<CdTocEntry>>();
            }

            List<IndexedTocEntry> matches = new List<IndexedTocEntry>();
            foreach (Regex pattern in TocRowPatterns)
            {
                foreach (Match match in pattern.Matches(logText))
                {
                    CdTocEntry entry;
                    string entryError;
                    if (!TryParseEntry(match, out entry, out entryError))
                    {
                        error = entryError;
                        return new List<IList<CdTocEntry>>();
                    }
                    matches.Add(new IndexedTocEntry(match.Index, match.Length, entry));
                }
            }
            matches.Sort(delegate(IndexedTocEntry left, IndexedTocEntry right)
            {
                return left.Index.CompareTo(right.Index);
            });

            // A row cannot legitimately match two supported formats, but keep
            // overlapping regex results from becoming duplicate tracks.
            for (int i = matches.Count - 1; i > 0; i--)
            {
                IndexedTocEntry current = matches[i];
                IndexedTocEntry previous = matches[i - 1];
                if (current.Index < previous.Index + previous.Length)
                    matches.RemoveAt(i);
            }

            List<IList<CdTocEntry>> tables = new List<IList<CdTocEntry>>();
            List<CdTocEntry> table = null;
            foreach (IndexedTocEntry indexed in matches)
            {
                CdTocEntry entry = indexed.Entry;
                if (table == null || entry.TrackNumber == 1)
                {
                    if (table != null && table.Count > 0)
                        tables.Add(table);
                    table = new List<CdTocEntry>();
                }
                if (table.Count == 0 && entry.TrackNumber != 1)
                    continue;
                if (entry.TrackNumber != table.Count + 1)
                {
                    error = "The selected log's TOC has missing, duplicate, or out-of-order track numbers.";
                    return new List<IList<CdTocEntry>>();
                }
                if (table.Count > 0 && entry.StartSector <= table[table.Count - 1].NextStartSector - 1)
                {
                    error = "The selected log's TOC contains overlapping tracks.";
                    return new List<IList<CdTocEntry>>();
                }
                table.Add(entry);
            }
            if (table != null && table.Count > 0)
                tables.Add(table);
            if (tables.Count == 0)
                error = "No supported CD TOC table was found in the selected log.";
            else
                AttachPeakLevels(logText, tables);
            return tables;
        }

        private static void AttachPeakLevels(
            string logText,
            IList<IList<CdTocEntry>> tables)
        {
            List<ParsedPeak> peaks = new List<ParsedPeak>();
            AddPeakMatches(logText, PercentPeakPattern, 10.0, 3, peaks);
            AddPeakMatches(logText, FractionPeakPattern, 1000.0, 6, peaks);
            peaks.Sort(delegate(ParsedPeak left, ParsedPeak right)
            {
                return left.Index.CompareTo(right.Index);
            });

            int expectedPeakCount = 0;
            int[] audioTrackCounts = new int[tables.Count];
            for (int i = 0; i < tables.Count; i++)
            {
                int dataTracks = CdTocComparer.GetDataTrackCount(tables[i]);
                if (dataTracks < 0)
                    return;
                audioTrackCounts[i] = tables[i].Count - dataTracks;
                expectedPeakCount += audioTrackCounts[i];
            }
            // Only attach peaks when the file reports one for every audio
            // track. Partial peak data must cause comparison to be skipped.
            if (peaks.Count != expectedPeakCount)
                return;

            int peakIndex = 0;
            for (int tableIndex = 0; tableIndex < tables.Count; tableIndex++)
            {
                for (int trackIndex = 0;
                    trackIndex < audioTrackCounts[tableIndex];
                    trackIndex++)
                {
                    ParsedPeak peak = peaks[peakIndex++];
                    CdTocEntry entry = tables[tableIndex][trackIndex];
                    entry.HasPeak = true;
                    entry.Peak = peak.Value;
                    entry.PeakPrecision = peak.Precision;
                }
            }
        }

        private static void AddPeakMatches(
            string logText,
            Regex pattern,
            double scale,
            int precision,
            IList<ParsedPeak> peaks)
        {
            foreach (Match match in pattern.Matches(logText))
            {
                double value;
                if (Double.TryParse(
                    match.Groups["peak"].Value,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out value))
                {
                    peaks.Add(new ParsedPeak(
                        match.Index,
                        Math.Max(0.0, Math.Min(1000.0, value * scale)),
                        precision));
                }
            }
        }

        private static bool TryParseEntry(
            Match match,
            out CdTocEntry entry,
            out string error)
        {
            entry = null;
            error = String.Empty;
            int trackNumber;
            long startSector;
            long endSector;
            long startFromMsf;
            long lengthFromMsf;
            if (!Int32.TryParse(match.Groups["track"].Value, out trackNumber) ||
                !Int64.TryParse(
                    match.Groups["startSector"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out startSector) ||
                !Int64.TryParse(
                    match.Groups["endSector"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out endSector) ||
                !TryMsfToSector(match.Groups["start"].Value, out startFromMsf) ||
                !TryMsfToSector(match.Groups["length"].Value, out lengthFromMsf))
            {
                error = "The selected log contains a malformed CD TOC row.";
                return false;
            }
            if (trackNumber < 1 || trackNumber > 99 || endSector < startSector)
            {
                error = "The selected log contains an invalid CD TOC row for track " +
                    trackNumber + ".";
                return false;
            }
            if (startFromMsf != startSector || lengthFromMsf != endSector + 1 - startSector)
            {
                error = "The time and sector values disagree in the selected log's TOC row for track " +
                    trackNumber + ".";
                return false;
            }
            entry = new CdTocEntry
            {
                TrackNumber = trackNumber,
                StartSector = startSector,
                NextStartSector = endSector + 1
            };
            return true;
        }

        private static bool TryMsfToSector(string value, out long sectors)
        {
            sectors = 0;
            Match match = Regex.Match(
                value ?? String.Empty,
                @"^\s*(?:(?<hours>-?\d+):)?(?<minutes>-?\d+):(?<seconds>-?\d+)[\.:](?<frames>-?\d+)\s*$",
                RegexOptions.CultureInvariant);
            long hours;
            long minutes;
            long seconds;
            long frames;
            if (!match.Success ||
                !Int64.TryParse(
                    match.Groups["hours"].Success ? match.Groups["hours"].Value : "0",
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out hours) ||
                !Int64.TryParse(match.Groups["minutes"].Value, out minutes) ||
                !Int64.TryParse(match.Groups["seconds"].Value, out seconds) ||
                !Int64.TryParse(match.Groups["frames"].Value, out frames))
            {
                return false;
            }
            try
            {
                sectors = checked((((hours * 60) + minutes) * 60 + seconds) * 75 + frames);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private sealed class IndexedTocEntry
        {
            internal readonly int Index;
            internal readonly int Length;
            internal readonly CdTocEntry Entry;

            internal IndexedTocEntry(int index, int length, CdTocEntry entry)
            {
                Index = index;
                Length = length;
                Entry = entry;
            }
        }

        private sealed class ParsedPeak
        {
            internal readonly int Index;
            internal readonly double Value;
            internal readonly int Precision;

            internal ParsedPeak(int index, double value, int precision)
            {
                Index = index;
                Value = value;
                Precision = precision;
            }
        }
    }

    internal static class CdTocComparer
    {
        private const long MaximumShift = 50;
        private const long MaximumDrift = 10;

        internal static CdTocComparisonResult Compare(
            IList<CdTocEntry> displayed,
            IList<CdTocEntry> logged)
        {
            return Compare(displayed, logged, false);
        }

        internal static CdTocComparisonResult Compare(
            IList<CdTocEntry> displayed,
            IList<CdTocEntry> logged,
            bool comparePeaksWhenComplete)
        {
            if (displayed == null)
                throw new ArgumentNullException("displayed");
            if (logged == null)
                throw new ArgumentNullException("logged");

            CdTocComparisonResult result = new CdTocComparisonResult();
            int displayedDataTracks = GetDataTrackCount(displayed);
            int loggedDataTracks = GetDataTrackCount(logged);
            int displayedLength = displayed.Count - Math.Max(displayedDataTracks, 0);
            int loggedLength = logged.Count - Math.Max(loggedDataTracks, 0);
            if (displayedDataTracks < 0)
                result.Details.Add("The displayed TOC has an unknown layout type.");
            if (loggedDataTracks < 0)
                result.Details.Add("The selected log's TOC has an unknown layout type.");
            if (displayedLength != loggedLength)
            {
                result.Reason = "Audio-track counts differ (" + displayedLength +
                    " displayed, " + loggedLength + " in the selected log).";
                return result;
            }
            if (displayedLength == 0)
            {
                result.Reason = "Neither TOC contains an audio track to compare.";
                return result;
            }
            result.TrackCount = displayedLength;

            int hiddenTrackCount = 0;
            if (displayed[0].StartSector > 150) hiddenTrackCount++;
            if (logged[0].StartSector > 150) hiddenTrackCount++;
            if (hiddenTrackCount > 0)
            {
                result.Details.Add(
                    (hiddenTrackCount == 1 ? "One TOC possibly contains" : "Both TOCs possibly contain") +
                    " a leading hidden track because the TOC starts above sector 150.");
            }

            List<long> shifts = new List<long>(displayedLength);
            bool exact = displayed.Count == logged.Count;
            for (int i = 0; i < displayedLength; i++)
            {
                long displayedEnd = displayed[i].NextStartSector - 1;
                long loggedEnd = logged[i].NextStartSector - 1;
                shifts.Add(loggedEnd - displayedEnd);
                bool offsetsDiffer = displayed[i].StartSector != logged[i].StartSector;
                bool lengthsDiffer =
                    displayed[i].NextStartSector - displayed[i].StartSector !=
                    logged[i].NextStartSector - logged[i].StartSector;
                if (offsetsDiffer || lengthsDiffer)
                {
                    exact = false;
                    string difference = offsetsDiffer && lengthsDiffer
                        ? "offsets and lengths differ"
                        : offsetsDiffer ? "offsets differ" : "lengths differ";
                    result.Details.Add("Track " + (i + 1) + ": " + difference + ".");
                }
            }
            if (exact)
            {
                for (int i = displayedLength; i < displayed.Count; i++)
                {
                    if (displayed[i].StartSector != logged[i].StartSector ||
                        displayed[i].NextStartSector != logged[i].NextStartSector)
                    {
                        exact = false;
                        break;
                    }
                }
            }

            long referenceShift = 0;
            if (shifts.Count > 1)
            {
                long largestBeforeLast = MaximumAbsolute(shifts, shifts.Count - 1);
                for (int i = 0; i < shifts.Count; i++)
                {
                    if (Math.Abs(shifts[i]) == largestBeforeLast)
                    {
                        referenceShift = shifts[i];
                        break;
                    }
                }
            }
            long lastShift = shifts[shifts.Count - 1];
            bool hasPostGap = lastShift == referenceShift + 150 ||
                lastShift == referenceShift - 150;
            int shiftCount = hasPostGap ? shifts.Count - 1 : shifts.Count;
            long tocShift = MaximumAbsolute(shifts, shiftCount);
            long tocDrift = Drift(shifts, shiftCount);
            if (tocShift >= MaximumShift)
            {
                result.Reason = "TOC shift is " + tocShift +
                    " sectors; it must be below " + MaximumShift + " sectors.";
                return result;
            }
            if (tocDrift >= MaximumDrift)
            {
                result.Reason = "TOC drift is " + tocDrift +
                    " sectors; it must be below " + MaximumDrift + " sectors.";
                return result;
            }

            if (tocDrift > 0)
            {
                result.Details.Insert(0, "The TOCs are shifted by up to " + tocShift +
                    " sectors with " + tocDrift + " sectors of drift.");
            }
            else if (tocShift > 0)
            {
                result.Details.Insert(0, "The TOCs are shifted by " + tocShift + " sectors.");
            }
            if (hasPostGap)
                result.Details.Add("The selected log differs by a 150-sector post-gap.");

            if (comparePeaksWhenComplete &&
                HaveCompletePeakLevels(displayed, logged, displayedLength))
            {
                for (int i = 0; i < displayedLength; i++)
                {
                    CdTocEntry displayedEntry = displayed[i];
                    CdTocEntry loggedEntry = logged[i];
                    if (!PeaksMatch(displayedEntry, loggedEntry))
                    {
                        result.Reason = "Track " + (i + 1) +
                            " peak difference is above 0.001 (" +
                            FormatPeak(displayedEntry) + " vs. " +
                            FormatPeak(loggedEntry) + ").";
                        return result;
                    }
                    if (displayedEntry.PeakPrecision == loggedEntry.PeakPrecision &&
                        displayedEntry.Peak != loggedEntry.Peak)
                    {
                        exact = false;
                        result.Details.Add("Track " + (i + 1) +
                            ": peak levels differ (" +
                            FormatPeak(displayedEntry) + " vs. " +
                            FormatPeak(loggedEntry) + ").");
                    }
                }
            }

            result.IsMatch = true;
            result.IsExact = exact;
            result.Reason = exact
                ? "The TOCs match exactly."
                : "The TOCs match under the Similar CD Detector thresholds.";
            return result;
        }

        private static bool HaveCompletePeakLevels(
            IList<CdTocEntry> displayed,
            IList<CdTocEntry> logged,
            int audioTrackCount)
        {
            for (int i = 0; i < audioTrackCount; i++)
            {
                if (!displayed[i].HasPeak || !logged[i].HasPeak)
                    return false;
            }
            return true;
        }

        private static bool PeaksMatch(CdTocEntry first, CdTocEntry second)
        {
            double[] progressiveOffsets = { -0.031, 0.901 };
            foreach (double offset in progressiveOffsets)
            {
                if (Math.Abs(
                    NormalizePeak(first, offset) -
                    NormalizePeak(second, offset)) < 1.0)
                {
                    return true;
                }
            }
            return false;
        }

        private static double NormalizePeak(CdTocEntry entry, double offset)
        {
            double peak = entry.PeakPrecision <= 3
                ? Math.Floor(entry.Peak) + offset
                : entry.Peak;
            return Math.Max(0.0, Math.Min(1000.0, peak));
        }

        private static string FormatPeak(CdTocEntry entry)
        {
            return (entry.Peak / 10.0).ToString(
                "0.######",
                CultureInfo.InvariantCulture) + "%";
        }

        internal static int GetDataTrackCount(IList<CdTocEntry> entries)
        {
            for (int i = 0; i < entries.Count - 1; i++)
            {
                long gap = entries[i + 1].StartSector - entries[i].NextStartSector;
                if (gap != 0)
                    return gap == 11400 ? entries.Count - i - 1 : -1;
            }
            return 0;
        }

        private static long MaximumAbsolute(IList<long> values, int count)
        {
            long maximum = 0;
            for (int i = 0; i < count; i++)
                maximum = Math.Max(maximum, Math.Abs(values[i]));
            return maximum;
        }

        private static long Drift(IList<long> values, int count)
        {
            if (count == 0)
                return 0;
            long minimum = values[0];
            long maximum = values[0];
            for (int i = 1; i < count; i++)
            {
                minimum = Math.Min(minimum, values[i]);
                maximum = Math.Max(maximum, values[i]);
            }
            return maximum - minimum;
        }
    }

    internal static class CdTocReleaseComparer
    {
        internal static CdTocReleaseComparisonResult Compare(
            IList<IList<CdTocEntry>> firstLog,
            IList<IList<CdTocEntry>> secondLog)
        {
            return Compare(firstLog, secondLog, false);
        }

        internal static CdTocReleaseComparisonResult Compare(
            IList<IList<CdTocEntry>> firstLog,
            IList<IList<CdTocEntry>> secondLog,
            bool comparePeaksWhenComplete)
        {
            if (firstLog == null)
                throw new ArgumentNullException("firstLog");
            if (secondLog == null)
                throw new ArgumentNullException("secondLog");

            CdTocReleaseComparisonResult releaseResult =
                new CdTocReleaseComparisonResult();
            if (firstLog.Count != secondLog.Count)
            {
                releaseResult.Reason = "The logs contain different numbers of TOC tables (" +
                    firstLog.Count + " and " + secondLog.Count + ").";
                return releaseResult;
            }
            if (firstLog.Count == 0)
            {
                releaseResult.Reason = "Neither log contains a TOC table to compare.";
                return releaseResult;
            }

            bool[] usedSecondTables = new bool[secondLog.Count];
            int[] mapping = new int[firstLog.Count];
            CdTocComparisonResult[] comparisons =
                new CdTocComparisonResult[firstLog.Count];
            if (!TryMapTables(
                firstLog,
                secondLog,
                0,
                usedSecondTables,
                mapping,
                comparisons,
                comparePeaksWhenComplete))
            {
                releaseResult.Reason = firstLog.Count == 1
                    ? CdTocComparer.Compare(
                        firstLog[0],
                        secondLog[0],
                        comparePeaksWhenComplete).Reason
                    : "No one-to-one matching arrangement was found for the logs' TOC tables.";
                // Pair labels are useful when several stacked/multi-disc TOCs
                // were tried, but merely repeat the reason for a single pair.
                if (firstLog.Count > 1)
                    AppendFailureDetails(
                        releaseResult,
                        firstLog,
                        secondLog,
                        comparePeaksWhenComplete);
                return releaseResult;
            }

            bool exact = true;
            for (int i = 0; i < comparisons.Length; i++)
            {
                CdTocComparisonResult comparison = comparisons[i];
                if (!comparison.IsExact)
                    exact = false;
                if (comparisons.Length > 1)
                {
                    releaseResult.Details.Add(
                        "First log table " + (i + 1) + " matches second log table " +
                        (mapping[i] + 1) + ": " + comparison.Reason);
                }
                foreach (string detail in comparison.Details)
                {
                    releaseResult.Details.Add(
                        comparisons.Length > 1
                            ? "Tables " + (i + 1) + " and " + (mapping[i] + 1) +
                                ": " + detail
                            : detail);
                }
            }
            releaseResult.IsMatch = true;
            releaseResult.IsExact = exact;
            releaseResult.Reason = comparisons.Length == 1
                ? comparisons[0].Reason
                : exact
                    ? "All " + comparisons.Length + " TOC tables match exactly."
                    : "All " + comparisons.Length +
                        " TOC tables match under the Similar CD Detector thresholds.";
            return releaseResult;
        }

        private static bool TryMapTables(
            IList<IList<CdTocEntry>> firstLog,
            IList<IList<CdTocEntry>> secondLog,
            int firstIndex,
            bool[] usedSecondTables,
            int[] mapping,
            CdTocComparisonResult[] comparisons,
            bool comparePeaksWhenComplete)
        {
            if (firstIndex == firstLog.Count)
                return true;

            // Prefer exact matches so ambiguous multi-disc logs produce the
            // most useful mapping, then try threshold matches.
            for (int exactPass = 1; exactPass >= 0; exactPass--)
            {
                for (int secondIndex = 0; secondIndex < secondLog.Count; secondIndex++)
                {
                    if (usedSecondTables[secondIndex])
                        continue;
                    CdTocComparisonResult comparison = CdTocComparer.Compare(
                        firstLog[firstIndex],
                        secondLog[secondIndex],
                        comparePeaksWhenComplete);
                    if (!comparison.IsMatch || comparison.IsExact != (exactPass == 1))
                        continue;
                    usedSecondTables[secondIndex] = true;
                    mapping[firstIndex] = secondIndex;
                    comparisons[firstIndex] = comparison;
                    if (TryMapTables(
                        firstLog,
                        secondLog,
                        firstIndex + 1,
                        usedSecondTables,
                        mapping,
                        comparisons,
                        comparePeaksWhenComplete))
                    {
                        return true;
                    }
                    usedSecondTables[secondIndex] = false;
                    comparisons[firstIndex] = null;
                }
            }
            return false;
        }

        private static void AppendFailureDetails(
            CdTocReleaseComparisonResult releaseResult,
            IList<IList<CdTocEntry>> firstLog,
            IList<IList<CdTocEntry>> secondLog,
            bool comparePeaksWhenComplete)
        {
            const int MaximumDetails = 12;
            for (int firstIndex = 0;
                firstIndex < firstLog.Count && releaseResult.Details.Count < MaximumDetails;
                firstIndex++)
            {
                for (int secondIndex = 0;
                    secondIndex < secondLog.Count &&
                        releaseResult.Details.Count < MaximumDetails;
                    secondIndex++)
                {
                    CdTocComparisonResult comparison = CdTocComparer.Compare(
                        firstLog[firstIndex],
                        secondLog[secondIndex],
                        comparePeaksWhenComplete);
                    releaseResult.Details.Add(
                        "First log table " + (firstIndex + 1) +
                        " vs. second log table " + (secondIndex + 1) +
                        ": " + comparison.Reason);
                }
            }
        }
    }
}
