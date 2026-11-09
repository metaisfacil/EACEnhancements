using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AudioDataPlugIn
{
    internal sealed class TracklistParserOptions
    {
        internal bool HasTrackNumbers = true;
        internal bool HasTrackArtists = true;
        internal bool HasVinylNumbers;
        internal string CustomDelimiter = String.Empty;
        internal bool UseCustomDelimiter;
        internal bool UseTrackNames = true;
        internal bool UseTrackArtists = true;
    }

    // A null field matches an undefined property on the MusicBrainz parser's
    // result object, which is distinct from a parsed empty string.
    internal sealed class ParsedTrack
    {
        internal int Position;
        internal string Number;
        internal string Name;
        internal string Artist;

        internal bool HasValue
        {
            get
            {
                return !String.IsNullOrEmpty(Number) || !String.IsNullOrEmpty(Name) ||
                    !String.IsNullOrEmpty(Artist);
            }
        }
    }

    internal sealed class TracklistTrack
    {
        internal int Row;
        internal string Number = String.Empty;
        internal string Title = String.Empty;
        internal string Artist = String.Empty;
        internal string FormattedLength = String.Empty;
    }

    internal sealed class TracklistPlan
    {
        internal bool IsError;
        internal IList<ParsedTrack> Parsed = new List<ParsedTrack>();
        // One entry per existing track, holding the title and artist it should end
        // up with. An empty string means the field should be cleared.
        internal IList<TracklistTrack> Result = new List<TracklistTrack>();

        internal int ClearedCount
        {
            get { return Math.Max(0, Result.Count - Parsed.Count); }
        }
    }

    // Port of the MusicBrainz release editor's track parser (trackParser.js in
    // metabrainz/musicbrainz-server, GPL-2.0-or-later). JavaScript and .NET regular
    // expressions disagree on \s, \d, \b, '.', '$' and /i, so these patterns spell
    // out the JavaScript meaning rather than relying on the .NET one.
    internal static class TracklistParser
    {
        // EAC's per-track title and artist fields hold 256 UTF-16 code units including
        // the terminator (LVN_ENDLABELEDITW handler; 1.6: 0x00405CD8; 1.8: 0x00405DC8,
        // verified in Ghidra).
        internal const int MaximumTrackTextLength = 255;

        private const string SpaceSet =
            "\\t\\n\\v\\f\\r \\u00A0\\u1680\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000\\uFEFF";
        private const string NotSpaceSet =
            "\\u0000-\\u0008\\u000E-\\u001F\\u0021-\\u009F\\u00A1-\\u167F\\u1681-\\u1FFF" +
            "\\u200B-\\u2027\\u202A-\\u202E\\u2030-\\u205E\\u2060-\\u2FFF\\u3001-\\uFEFE\\uFF00-\\uFFFF";
        private const string DigitSet = "0-9";
        private const string NotDigitSet = "\\u0000-\\u002F\\u003A-\\uFFFF";
        private const string WordSet = "0-9A-Za-z_";
        private const string NotWordSet =
            "\\u0000-\\u002F\\u003A-\\u0040\\u005B-\\u005E\\u0060\\u007B-\\uFFFF";
        private const string Space = "[" + SpaceSet + "]";
        private const string WordBoundary =
            "(?:(?<=[" + WordSet + "])(?![" + WordSet + "])|(?<![" + WordSet + "])(?=[" + WordSet + "]))";
        private const string NotWordBoundary =
            "(?:(?<=[" + WordSet + "])(?=[" + WordSet + "])|(?<![" + WordSet + "])(?![" + WordSet + "]))";
        private const string FullwidthDigits = "\\uFF10-\\uFF19";
        private const string NumberSuffix =
            "(?:\\.|\\uFF0E|" + Space + "?-|:|\\uFF1A|;|,|\\uFF0C|\\z)?";
        private const string VinylCharacters = "[" + FullwidthDigits + "0-9a-zA-Z]+";

        // These are all different types of dash.
        private const string DefaultSeparatorSource =
            "(" + Space + "+[-\\u2012\\u2013\\u2014\\u2015]" + Space + "+|" +
            Space + "*\\t" + Space + "*)";

        // Leading "M." is for Japanese releases. MBS-3398
        private static readonly Regex TrackNumber = new Regex(
            "^(?:M[.-])?([" + FullwidthDigits + "0-9]+(?:-[" + FullwidthDigits + "0-9]+)?)" + NumberSuffix,
            RegexOptions.CultureInvariant);
        private static readonly Regex VinylNumber = new Regex(
            "^(" + VinylCharacters + ")(?:/" + VinylCharacters + ")?" + NumberSuffix,
            RegexOptions.CultureInvariant);
        private static readonly Regex TrackTime = new Regex(
            "\\(?((?:[0-9" + FullwidthDigits + "]+[\\uFF1A\\uFF0C\\uFF0E':,.])?" +
            "[0-9" + FullwidthDigits + "?]+[\\uFF1A\\uFF0C\\uFF0E':,.]" +
            "[0-5\\uFF10-\\uFF15?][0-9" + FullwidthDigits + "?])\\)?\\z",
            RegexOptions.CultureInvariant);
        private static readonly Regex AsciiNumber = new Regex(
            "^[0-9]+\\z", RegexOptions.CultureInvariant);
        private static readonly Regex LeadingZeros = new Regex(
            "^0+([0-9]+)", RegexOptions.CultureInvariant);
        private static readonly Regex Whitespace = new Regex(
            Space + "+", RegexOptions.CultureInvariant);
        private static readonly Regex TrailingThe = new Regex(
            "(.*)," + Space + "[Tt][Hh][Ee]\\z", RegexOptions.CultureInvariant);
        private static readonly Regex SpaceBeforeComma = new Regex(
            Space + "*,", RegexOptions.CultureInvariant);
        // Must precede DefaultSeparators: static fields initialize in textual order.
        private static readonly TimeSpan SeparatorTimeout = TimeSpan.FromSeconds(2);
        private static readonly SeparatorPattern DefaultSeparators =
            new SeparatorPattern(DefaultSeparatorSource);

        private sealed class SeparatorPattern
        {
            internal readonly Regex Search;
            internal readonly Regex Sticky;
            internal readonly Regex Leading;
            internal readonly Regex Trailing;

            internal SeparatorPattern(string source)
            {
                Search = Compile(source);
                Sticky = Compile("\\G(?:" + source + ")");
                Leading = Compile("^" + source);
                Trailing = Compile(source + "\\z");
            }

            // A user's delimiter runs on EAC's UI thread, so a pathological pattern
            // must not be able to hang the application.
            private static Regex Compile(string pattern)
            {
                return new Regex(pattern, RegexOptions.CultureInvariant, SeparatorTimeout);
            }
        }

        internal static bool IsValidCustomDelimiter(string delimiter)
        {
            return TryCompileCustomDelimiter(delimiter) != null;
        }

        private static SeparatorPattern TryCompileCustomDelimiter(string delimiter)
        {
            try
            {
                return new SeparatorPattern(
                    TranslateJavaScriptRegex("(" + (delimiter ?? String.Empty) + ")"));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static SeparatorPattern ResolveSeparators(TracklistParserOptions options)
        {
            if (!options.UseCustomDelimiter)
                return DefaultSeparators;
            // An invalid delimiter is reported beside its field and otherwise falls
            // back to the default separators.
            return TryCompileCustomDelimiter(options.CustomDelimiter) ?? DefaultSeparators;
        }

        // Returns the parsed lines. maximumLines limits the input before parsing, as
        // MusicBrainz does for a medium with a CD TOC; pass -1 for no limit.
        internal static IList<ParsedTrack> Parse(
            string text,
            TracklistParserOptions options,
            int maximumLines)
        {
            SeparatorPattern separators = ResolveSeparators(options);
            List<ParsedTrack> tracks = new List<ParsedTrack>();
            int lineCount = 0;
            foreach (string line in (text ?? String.Empty).Split('\n'))
            {
                if (IsBlank(line))
                    continue;
                if (maximumLines >= 0 && lineCount >= maximumLines)
                    break;
                lineCount++;

                ParsedTrack track = ParseLine(line, options, separators);
                // We should've parsed at least some values, otherwise something
                // went wrong.
                if (!track.HasValue)
                    continue;
                track.Position = tracks.Count + 1;
                tracks.Add(track);
            }
            return tracks;
        }

        // An EAC disc always behaves like a MusicBrainz medium with a CD TOC: lines
        // map to tracks by position, there can be no more tracks than the TOC has,
        // and track lengths are never changed. Tracks left without a line are blanked.
        internal static TracklistPlan PlanTocUpdate(
            string text,
            TracklistParserOptions options,
            IList<TracklistTrack> current)
        {
            TracklistPlan plan = new TracklistPlan();
            plan.Parsed = Parse(text, options, current.Count);
            // MusicBrainz cannot reach this error on a CD TOC medium, and instead
            // blanks every track when nothing parsed. There's no undo in EAC, so
            // unparseable input is reported and leaves the disc alone.
            if (!IsBlank(text) && plan.Parsed.Count == 0)
            {
                plan.IsError = true;
                return plan;
            }

            for (int i = 0; i < current.Count; i++)
            {
                TracklistTrack result = new TracklistTrack();
                if (i < plan.Parsed.Count)
                {
                    ParsedTrack parsed = plan.Parsed[i];
                    result.Title = options.UseTrackNames
                        ? (parsed.Name ?? String.Empty)
                        : current[i].Title;
                    // A line with no artist keeps the track's existing credit.
                    result.Artist = options.UseTrackArtists && !String.IsNullOrEmpty(parsed.Artist)
                        ? parsed.Artist
                        : current[i].Artist;
                }
                result.Title = LimitTrackText(result.Title);
                result.Artist = LimitTrackText(result.Artist);
                plan.Result.Add(result);
            }
            return plan;
        }

        internal static string LimitTrackText(string value)
        {
            if (value.Length <= MaximumTrackTextLength)
                return value;
            int length = MaximumTrackTextLength;
            if (Char.IsHighSurrogate(value[length - 1]))
                length--;
            return value.Substring(0, length);
        }

        internal static ParsedTrack ParseLine(string line, TracklistParserOptions options)
        {
            return ParseLine(line, options, ResolveSeparators(options));
        }

        private static ParsedTrack ParseLine(
            string line,
            TracklistParserOptions options,
            SeparatorPattern separators)
        {
            ParsedTrack data = new ParsedTrack();

            // Trim only, keeping tabs and other space separators intact.
            line = Trim(line);
            if (line.Length == 0)
                return data;

            // Remove track times first, because they could be confused with track
            // numbers if the line only contains a time. Assume the track time is at
            // the end. EAC takes track lengths from the TOC, so the value is unused.
            Match match = TrackTime.Match(line);
            if (match.Success)
                line = line.Substring(0, line.Length - match.Length);

            if (options.HasTrackNumbers)
            {
                match = (options.HasVinylNumbers ? VinylNumber : TrackNumber).Match(line);

                // There should always be a track number if this option's set.
                if (!match.Success)
                    return new ParsedTrack();

                // EAC takes track numbers from the TOC, so this only matters for keeping
                // a line with no title from being dropped and moving later lines up.
                data.Number = FromFullwidthLatin(match.Groups[1].Value);
                if (AsciiNumber.IsMatch(data.Number))
                    data.Number = LeadingZeros.Replace(data.Number, "$1");
                line = line.Substring(match.Length);
            }

            if (!options.HasTrackArtists)
            {
                if (options.UseTrackNames)
                    data.Name = Clean(line);
                return data;
            }

            List<string> parts = SplitLikeJavaScript(line, separators.Sticky);
            List<string> names = new List<string>();
            foreach (string part in parts)
            {
                if (!IsSeparatorOrBlank(part, separators.Search))
                    names.Add(part);
            }

            // Only parse an artist if there's more than one name. Assume the artist
            // is the last name.
            if (names.Count > 1)
            {
                string artist = names[names.Count - 1];
                if (options.UseTrackArtists)
                    data.Artist = artist;
                if (options.UseTrackNames)
                {
                    // Use whatever's left as the name, including any separators.
                    string withoutArtist = String.Concat(
                        parts.GetRange(0, parts.LastIndexOf(artist)));
                    data.Name = separators.Trailing.Replace(
                        separators.Leading.Replace(withoutArtist, String.Empty, 1),
                        String.Empty,
                        1);
                }
            }
            else if (options.UseTrackNames)
            {
                data.Name = Clean(line);
            }

            // Either of these could be the artist name, so clean both.
            if (options.UseTrackNames)
                data.Name = CleanArtistName(data.Name ?? String.Empty);
            if (options.UseTrackArtists)
                data.Artist = CleanArtistName(data.Artist ?? String.Empty);
            return data;
        }

        // Regex.Split handles empty matches and the string's end differently from
        // String.prototype.split, so this follows the ECMAScript algorithm. A null
        // part is a capture group that did not participate, as undefined is there.
        private static List<string> SplitLikeJavaScript(string text, Regex sticky)
        {
            List<string> parts = new List<string>();
            if (text.Length == 0)
            {
                if (!sticky.Match(text).Success)
                    parts.Add(text);
                return parts;
            }

            int start = 0;
            int position = 0;
            while (position < text.Length)
            {
                Match match = sticky.Match(text, position);
                int end = match.Success ? Math.Min(match.Index + match.Length, text.Length) : start;
                if (!match.Success || end == start)
                {
                    position++;
                    continue;
                }
                parts.Add(text.Substring(start, position - start));
                for (int group = 1; group < match.Groups.Count; group++)
                    parts.Add(match.Groups[group].Success ? match.Groups[group].Value : null);
                start = end;
                position = start;
            }
            parts.Add(text.Substring(start));
            return parts;
        }

        private static bool IsSeparatorOrBlank(string part, Regex search)
        {
            return part == null || search.IsMatch(part) || IsBlank(part);
        }

        // The text the dialog opens with, in the format the parser expects. The time
        // keeps a title ending in something like "12.30" from being read as one.
        internal static string FormatTracklist(
            IList<TracklistTrack> tracks,
            TracklistParserOptions options)
        {
            StringBuilder text = new StringBuilder();
            foreach (TracklistTrack track in tracks)
            {
                if (options.HasTrackNumbers)
                    text.Append(track.Number).Append(". ");
                text.Append(track.Title);
                if (options.HasTrackArtists && !String.IsNullOrEmpty(track.Artist))
                    text.Append(" - ").Append(track.Artist);
                text.Append(" (")
                    .Append(String.IsNullOrEmpty(track.FormattedLength) ? "?:??" : track.FormattedLength)
                    .Append(")\r\n");
            }
            return text.ToString();
        }

        internal static string FormatTrackLength(long milliseconds)
        {
            if (milliseconds <= 0)
                return "?:??";
            if (milliseconds < 1000)
                return milliseconds.ToString(CultureInfo.InvariantCulture) + " ms";

            long seconds = (long)Math.Floor(milliseconds / 1000.0 + 0.5);
            long hours = seconds / 3600;
            long minutes = (seconds % 3600) / 60;
            seconds %= 60;
            return hours > 0
                ? String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, seconds)
                : String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, seconds);
        }

        // Converts the JavaScript regular expression syntax MusicBrainz accepts into
        // the .NET equivalent. Throws ArgumentException for syntax JavaScript rejects,
        // including .NET-only constructs. new Regex reports the remaining errors.
        internal static string TranslateJavaScriptRegex(string pattern)
        {
            StringBuilder result = new StringBuilder(pattern.Length + 16);
            int captureCount = CountCaptureGroups(pattern);
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                switch (c)
                {
                    case '\\':
                        i = TranslateEscape(pattern, i, result, false, captureCount);
                        break;
                    case '[':
                        i = TranslateCharacterClass(pattern, i, result, captureCount);
                        break;
                    case '(':
                        ValidateGroupPrefix(pattern, i);
                        result.Append(c);
                        i++;
                        break;
                    case '.':
                        result.Append("[^\\n\\r\\u2028\\u2029]");
                        i++;
                        break;
                    case '$':
                        result.Append("\\z");
                        i++;
                        break;
                    default:
                        result.Append(c);
                        i++;
                        break;
                }
            }
            return result.ToString();
        }

        private static void ValidateGroupPrefix(string pattern, int index)
        {
            if (index + 1 >= pattern.Length || pattern[index + 1] != '?')
                return;
            if (Regex.IsMatch(
                pattern.Substring(index + 2),
                "^(?:[:=!]|<[=!]|<[A-Za-z_$][0-9A-Za-z_$]*>)",
                RegexOptions.CultureInvariant))
            {
                return;
            }
            throw new ArgumentException("Unsupported regular expression group.");
        }

        private static int TranslateCharacterClass(
            string pattern,
            int index,
            StringBuilder result,
            int captureCount)
        {
            int i = index + 1;
            bool negated = i < pattern.Length && pattern[i] == '^';
            if (negated)
                i++;

            StringBuilder body = new StringBuilder();
            bool afterSet = false;
            while (true)
            {
                if (i >= pattern.Length)
                    throw new ArgumentException("Unterminated character class.");
                char c = pattern[i];
                if (c == ']')
                    break;
                if (c == '\\')
                {
                    afterSet = IsSetEscape(pattern, i);
                    i = TranslateEscape(pattern, i, body, true, captureCount);
                    continue;
                }
                // An unescaped '[' would begin .NET class subtraction, and a '-' beside
                // \d, \w or \s is literal in JavaScript but would form a range once
                // those are expanded.
                if (c == '[' || (c == '-' && (afterSet || IsSetEscape(pattern, i + 1))))
                    body.Append('\\');
                body.Append(c);
                afterSet = false;
                i++;
            }

            if (body.Length > 0)
                result.Append(negated ? "[^" : "[").Append(body).Append(']');
            else
                // JavaScript's [] matches nothing and [^] matches anything.
                result.Append(negated ? "[\\s\\S]" : "(?!)");
            return i + 1;
        }

        private static bool IsSetEscape(string pattern, int index)
        {
            return index + 1 < pattern.Length && pattern[index] == '\\' &&
                "dDwWsS".IndexOf(pattern[index + 1]) >= 0;
        }

        private static int TranslateEscape(
            string pattern,
            int index,
            StringBuilder result,
            bool inClass,
            int captureCount)
        {
            if (index + 1 >= pattern.Length)
                throw new ArgumentException("Regular expression ends with a backslash.");

            char c = pattern[index + 1];
            int next = index + 2;
            switch (c)
            {
                case 'd': AppendSet(result, DigitSet, false, inClass); return next;
                case 'D': AppendSet(result, DigitSet, true, inClass); return next;
                case 'w': AppendSet(result, WordSet, false, inClass); return next;
                case 'W': AppendSet(result, WordSet, true, inClass); return next;
                case 's': AppendSet(result, SpaceSet, false, inClass); return next;
                case 'S': AppendSet(result, SpaceSet, true, inClass); return next;
                case 'b':
                    result.Append(inClass ? "\\b" : WordBoundary);
                    return next;
                case 'B':
                    if (inClass)
                        result.Append('B');
                    else
                        result.Append(NotWordBoundary);
                    return next;
                case 'x':
                    return AppendHexEscape(pattern, next, 2, 'x', result);
                case 'u':
                    return AppendHexEscape(pattern, next, 4, 'u', result);
                case 'c':
                    if (next < pattern.Length && IsAsciiLetter(pattern[next]))
                    {
                        result.Append("\\c").Append(pattern[next]);
                        return next + 1;
                    }
                    // Without a control letter, the backslash is literal.
                    result.Append("\\\\c");
                    return next;
                case 'k':
                    if (pattern.IndexOf("(?<", StringComparison.Ordinal) >= 0 &&
                        Regex.IsMatch(pattern.Substring(next), "^<[A-Za-z_$][0-9A-Za-z_$]*>"))
                    {
                        result.Append("\\k");
                        return next;
                    }
                    result.Append('k');
                    return next;
                case 'f':
                case 'n':
                case 'r':
                case 't':
                case 'v':
                    result.Append('\\').Append(c);
                    return next;
            }

            if (c >= '0' && c <= '9')
                return TranslateNumericEscape(pattern, index + 1, result, inClass, captureCount);
            // JavaScript treats any other escape as the character itself, while .NET
            // gives letters such as \A, \z and \p their own meanings and rejects other
            // word characters. ASCII punctuation can stay escaped.
            if (c < 128 && !IsAsciiLetter(c) && c != '_')
                result.Append('\\');
            result.Append(c);
            return next;
        }

        private static int TranslateNumericEscape(
            string pattern,
            int index,
            StringBuilder result,
            bool inClass,
            int captureCount)
        {
            int end = index;
            int number = 0;
            while (end < pattern.Length && pattern[end] >= '0' && pattern[end] <= '9' && number <= 99999)
                number = number * 10 + (pattern[end++] - '0');

            // A JavaScript backreference to a group that hasn't captured matches the
            // empty string, where .NET would fail the match.
            if (!inClass && pattern[index] != '0' && number <= captureCount)
            {
                result.Append("(?(").Append(number).Append(")\\").Append(number).Append(')');
                return end;
            }

            // Otherwise it's a legacy octal escape, or \8 and \9 as themselves.
            if (pattern[index] >= '8')
            {
                result.Append(pattern[index]);
                return index + 1;
            }
            int maximumDigits = pattern[index] <= '3' ? 3 : 2;
            int value = 0;
            end = index;
            while (end < pattern.Length && end - index < maximumDigits &&
                pattern[end] >= '0' && pattern[end] <= '7')
            {
                value = value * 8 + (pattern[end++] - '0');
            }
            result.Append("\\x").Append(value.ToString("X2", CultureInfo.InvariantCulture));
            return end;
        }

        private static int CountCaptureGroups(string pattern)
        {
            int count = 0;
            bool inClass = false;
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '\\')
                    i++;
                else if (inClass)
                    inClass = c != ']';
                else if (c == '[')
                    inClass = true;
                else if (c == '(' && (i + 1 >= pattern.Length || pattern[i + 1] != '?' ||
                    (i + 3 < pattern.Length && pattern[i + 2] == '<' &&
                     pattern[i + 3] != '=' && pattern[i + 3] != '!')))
                    count++;
            }
            return count;
        }

        private static void AppendSet(StringBuilder result, string set, bool negated, bool inClass)
        {
            if (inClass)
            {
                if (!negated)
                    result.Append(set);
                else if (set == DigitSet)
                    result.Append(NotDigitSet);
                else if (set == WordSet)
                    result.Append(NotWordSet);
                else
                    result.Append(NotSpaceSet);
                return;
            }
            result.Append(negated ? "[^" : "[").Append(set).Append(']');
        }

        private static int AppendHexEscape(
            string pattern,
            int index,
            int digits,
            char prefix,
            StringBuilder result)
        {
            if (index + digits <= pattern.Length &&
                Regex.IsMatch(pattern.Substring(index, digits), "^[0-9A-Fa-f]+$"))
            {
                result.Append('\\').Append(prefix).Append(pattern, index, digits);
                return index + digits;
            }
            result.Append(prefix);
            return index;
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        internal static bool IsJavaScriptWhiteSpace(char c)
        {
            return (c >= '\u0009' && c <= '\u000D') || c == '\u0020' || c == '\u00A0' ||
                c == '\u1680' || (c >= '\u2000' && c <= '\u200A') || c == '\u2028' ||
                c == '\u2029' || c == '\u202F' || c == '\u205F' || c == '\u3000' ||
                c == '\uFEFF';
        }

        private static bool IsBlank(string value)
        {
            foreach (char c in value ?? String.Empty)
            {
                if (!IsJavaScriptWhiteSpace(c))
                    return false;
            }
            return true;
        }

        private static string Trim(string value)
        {
            int start = 0;
            int end = value.Length;
            while (start < end && IsJavaScriptWhiteSpace(value[start]))
                start++;
            while (end > start && IsJavaScriptWhiteSpace(value[end - 1]))
                end--;
            return value.Substring(start, end - start);
        }

        private static string Clean(string value)
        {
            return Whitespace.Replace(Trim(value ?? String.Empty), " ");
        }

        private static string CleanArtistName(string name)
        {
            // Artist, The -> The Artist
            string cleaned = TrailingThe.Replace(Clean(name), "The $1");
            return SpaceBeforeComma.Replace(cleaned, ",");
        }

        internal static string FromFullwidthLatin(string value)
        {
            StringBuilder result = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\u3000': result.Append(' '); break;
                    case '\uFF5F': result.Append('\u2985'); break;
                    case '\uFF60': result.Append('\u2986'); break;
                    case '\uFFE0': result.Append('\u00A2'); break;
                    case '\uFFE1': result.Append('\u00A3'); break;
                    case '\uFFE2': result.Append('\u00AC'); break;
                    case '\uFFE3': result.Append('\u00AF'); break;
                    case '\uFFE4': result.Append('\u00A6'); break;
                    case '\uFFE5': result.Append('\u00A5'); break;
                    case '\uFFE6': result.Append('\u20A9'); break;
                    default:
                        result.Append(c >= '\uFF01' && c <= '\uFF5E'
                            ? (char)((c + 0x20) & 0xFF)
                            : c);
                        break;
                }
            }
            return result.ToString();
        }
    }
}
