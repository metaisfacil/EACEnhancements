using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AudioDataPlugIn
{
    internal static class TracklistParserTests
    {
        private const uint ParseTracklistCommand = 0xA329;
        private const uint DisplayCdTocCommand = 0xA31C;

        [STAThread]
        private static int Main()
        {
            try
            {
                AssertUpstreamTrackNumbers();
                AssertUpstreamTrailingWhitespace();
                AssertUpstreamNumbersInTitles();
                AssertUpstreamIgnoresUnnumberedLines();
                AssertUpstreamLongTimes();
                AssertUpstreamFullwidthNumbers();
                AssertUpstreamFieldSelection();
                AssertDefaultFormat();
                AssertSeparators();
                AssertArtistCleanup();
                AssertJavaScriptWhitespace();
                AssertCustomDelimiters();
                AssertRegexCharacterSets();
                AssertTocPlan();
                AssertSeedText();
                AssertMenu();
                AssertCommandIdUnique();
                AssertEacCommits();
                AssertDiscNumberedFromTen();
                AssertReorderedTrackList();
                AssertLayouts();
                AssertDialog();
                AssertOptionsRoundTrip();
                Console.WriteLine("Tracklist parser tests passed.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static TracklistParserOptions Options(
            bool numbers = false,
            bool vinyl = false,
            bool artists = false,
            bool useNames = false,
            bool useArtists = false)
        {
            return new TracklistParserOptions
            {
                HasTrackNumbers = numbers,
                HasVinylNumbers = vinyl,
                HasTrackArtists = artists,
                UseTrackNames = useNames,
                UseTrackArtists = useArtists
            };
        }

        private static void AssertUpstreamTrackNumbers()
        {
            IList<ParsedTrack> tracks = TracklistParser.Parse(
                "a1  Kermis         02:04\n" +
                "a2.  Glitch        02:51\n" +
                "a3.Afrik Slang     02:11\n" +
                "4 Rot Beat         01:07\n" +
                "5. Pruik           02:21\n" +
                "6.In Je Graff      03:21\n" +
                "\uFF17 \uFF28\uFF49\uFF4C\uFF4C\uFF57\uFF4F\uFF4F\uFF44   \uFF10\uFF12\uFF1A\uFF13\uFF14",
                Options(numbers: true, vinyl: true, useNames: true),
                -1);
            string[,] expected =
            {
                { "a1", "Kermis" }, { "a2", "Glitch" }, { "a3", "Afrik Slang" }, { "4", "Rot Beat" },
                { "5", "Pruik" }, { "6", "In Je Graff" },
                { "7", "\uFF28\uFF49\uFF4C\uFF4C\uFF57\uFF4F\uFF4F\uFF44" }
            };
            AssertEqual(7, tracks.Count, "vinyl track count");
            for (int i = 0; i < 7; i++)
            {
                AssertEqual(i + 1, tracks[i].Position, "vinyl position");
                AssertEqual(expected[i, 0], tracks[i].Number, "vinyl number");
                AssertEqual(expected[i, 1], tracks[i].Name, "vinyl name");
            }
        }

        private static void AssertUpstreamTrailingWhitespace()
        {
            IList<ParsedTrack> tracks = TracklistParser.Parse(
                "1. Forgotten Child    3:39    \n" +
                "2. Dirty Looks  4:34   \n" +
                "  3. Private Life  3:29  \n" +
                "4.  Never Can Wait  3:24 ",
                Options(numbers: true, useNames: true),
                -1);
            string[] expected = { "Forgotten Child", "Dirty Looks", "Private Life", "Never Can Wait" };
            for (int i = 0; i < 4; i++)
                AssertEqual(expected[i], tracks[i].Name, "MBS-1284 name");
        }

        private static void AssertUpstreamNumbersInTitles()
        {
            IList<ParsedTrack> tracks = TracklistParser.Parse(
                "1. Criminology 2.5\n2. Love On A .45",
                Options(numbers: true, useNames: true),
                -1);
            AssertEqual("Criminology 2.5", tracks[0].Name, "MBS-2511");
            AssertEqual("Love On A .45", tracks[1].Name, "MBS-2902");
        }

        private static void AssertUpstreamIgnoresUnnumberedLines()
        {
            IList<ParsedTrack> tracks = TracklistParser.Parse(
                "    1 Freeman Hardy & Willis Acid\n\n" +
                "      Written-By \u2013 James*, Jenkinson* \n\n" +
                "    5:42\n" +
                "    2 Orange Romeda\n\n" +
                "    Written-By \u2013 Eoin*, Sandison* \n\n" +
                "    4:51 \n  ",
                Options(numbers: true, useNames: true),
                -1);
            AssertEqual(2, tracks.Count, "MBS-2540 count");
            AssertEqual("Freeman Hardy & Willis Acid", tracks[0].Name, "MBS-2540 first");
            AssertEqual(1, tracks[0].Position, "MBS-2540 first position");
            AssertEqual("Orange Romeda", tracks[1].Name, "MBS-2540 second");
            AssertEqual(2, tracks[1].Position, "MBS-2540 second position");
        }

        private static void AssertUpstreamLongTimes()
        {
            ParsedTrack track = TracklistParser.ParseLine(
                "1. Love On A .45  05:22:31",
                Options(numbers: true, useNames: true));
            AssertEqual("Love On A .45", track.Name, "MBS-3353 name");
        }

        private static void AssertUpstreamFullwidthNumbers()
        {
            ParsedTrack track = TracklistParser.ParseLine(
                "\uFF11 \uFF26\uFF4F\uFF4F \uFF12\uFF1A\uFF13\uFF14",
                Options(numbers: true, useNames: true));
            AssertEqual("1", track.Number, "fullwidth number");
            AssertEqual("\uFF26\uFF4F\uFF4F", track.Name, "fullwidth title");
        }

        private static void AssertUpstreamFieldSelection()
        {
            const string line = "A1. FOO! - BAR! (2:55)";
            ParsedTrack track = TracklistParser.ParseLine(line,
                Options(numbers: true, vinyl: true, artists: true));
            AssertEqual("A1", track.Number, "numbers only");
            AssertEqual(null, track.Name, "numbers only name");
            AssertEqual(null, track.Artist, "numbers only artist");

            track = TracklistParser.ParseLine(line,
                Options(numbers: true, vinyl: true, artists: true, useNames: true));
            AssertEqual("A1", track.Number, "titles only number");
            AssertEqual("FOO!", track.Name, "titles only");
            AssertEqual(null, track.Artist, "titles only artist");

            track = TracklistParser.ParseLine(line,
                Options(numbers: true, vinyl: true, artists: true, useArtists: true));
            AssertEqual(null, track.Name, "artists only name");
            AssertEqual("BAR!", track.Artist, "artists only");
        }

        private static void AssertDefaultFormat()
        {
            TracklistParserOptions options = new TracklistParserOptions();
            IList<ParsedTrack> tracks = TracklistParser.Parse(
                "1. Love Me Do - The Beatles (2:23)\r\n2. P.S. I Love You - The Beatles (2:03)\r\n",
                options,
                -1);
            AssertEqual(2, tracks.Count, "default count");
            AssertEqual("1", tracks[0].Number, "default number");
            AssertEqual("Love Me Do", tracks[0].Name, "default title");
            AssertEqual("The Beatles", tracks[0].Artist, "default artist");
            AssertEqual("P.S. I Love You", tracks[1].Name, "default second title");
            AssertEqual("7", TracklistParser.ParseLine("007 X",
                Options(numbers: true)).Number, "leading zeros");
            AssertEqual("0", TracklistParser.ParseLine("00 X",
                Options(numbers: true)).Number, "all zeros");
            AssertEqual("1-02", TracklistParser.ParseLine("1-02. X",
                Options(numbers: true)).Number, "disc-track number");
            AssertEqual("3", TracklistParser.ParseLine("M.3 X",
                Options(numbers: true)).Number, "MBS-3398 number");

            // A number with no title keeps its place, so later lines stay on their tracks.
            tracks = TracklistParser.Parse("1. A\n2.\n3. C", Options(numbers: true, useNames: true), -1);
            AssertEqual(3, tracks.Count, "a number with no title is a track");
            AssertEqual(String.Empty, tracks[1].Name, "a number with no title has an empty title");
            AssertEqual("C", tracks[2].Name, "later lines keep their place");

            // Track lengths are removed from the line, and aren't a track by themselves.
            AssertEqual("Song", TracklistParser.ParseLine("Song ?:??", Options(useNames: true)).Name,
                "unknown length is removed");
            AssertEqual("Song", TracklistParser.ParseLine("Song (1:02:03)", Options(useNames: true)).Name,
                "long length is removed");
            AssertEqual(0, TracklistParser.Parse("5:42\n?:??", Options(useNames: true), -1).Count,
                "a line with only a length is not a track");
        }

        private static void AssertSeparators()
        {
            TracklistParserOptions options = Options(artists: true, useNames: true, useArtists: true);
            foreach (string dash in new[] { "-", "\u2012", "\u2013", "\u2014", "\u2015" })
            {
                ParsedTrack track = TracklistParser.ParseLine("Title " + dash + " Artist", options);
                AssertEqual("Title", track.Name, "dash title " + dash);
                AssertEqual("Artist", track.Artist, "dash artist " + dash);
            }
            ParsedTrack tabbed = TracklistParser.ParseLine("Title\tArtist", options);
            AssertEqual("Title", tabbed.Name, "tab title");
            AssertEqual("Artist", tabbed.Artist, "tab artist");
            ParsedTrack inner = TracklistParser.ParseLine("Pt. 1 - Pt. 2 - Artist", options);
            AssertEqual("Pt. 1 - Pt. 2", inner.Name, "title keeps an inner separator");
            AssertEqual("Artist", inner.Artist, "last name is the artist");
            AssertEqual("No-Spaces", TracklistParser.ParseLine("No-Spaces", options).Name, "tight hyphen");
            AssertEqual(String.Empty, TracklistParser.ParseLine("Solo", options).Artist, "no artist");
        }

        private static void AssertArtistCleanup()
        {
            TracklistParserOptions options = Options(artists: true, useNames: true, useArtists: true);
            ParsedTrack track = TracklistParser.ParseLine("Beatles, The - Rolling Stones, THE", options);
            AssertEqual("The Beatles", track.Name, "The in title position");
            AssertEqual("The Rolling Stones", track.Artist, "/i on The");
            AssertEqual("A, B", TracklistParser.ParseLine("A , B", options).Name, "space before comma");
            // Without an artist column MusicBrainz leaves the title alone.
            AssertEqual("Beatles, The", TracklistParser.ParseLine("Beatles, The",
                Options(useNames: true)).Name, "title without artists");
            // JavaScript's /i does not fold U+212A or U+017F onto ASCII.
            AssertEqual(null, TracklistParser.ParseLine("\u212A1 X",
                Options(numbers: true, vinyl: true)).Number, "Kelvin is not vinyl");
        }

        private static void AssertJavaScriptWhitespace()
        {
            TracklistParserOptions options = Options(useNames: true);
            AssertEqual("A B", TracklistParser.ParseLine("\uFEFF A\u3000\u00A0B\uFEFF", options).Name,
                "U+FEFF is whitespace");
            AssertEqual("\u0085A\u0085", TracklistParser.ParseLine("\u0085A\u0085", options).Name,
                "U+0085 is not whitespace");
            AssertEqual("\u180EA", TracklistParser.ParseLine("\u180EA", options).Name,
                "U+180E is not whitespace");
            AssertEqual(1, TracklistParser.Parse("A\n\uFEFF\n\u3000\r\n \n", options, -1).Count,
                "blank lines");
        }

        private static void AssertCustomDelimiters()
        {
            TracklistParserOptions options = Options(artists: true, useNames: true, useArtists: true);
            options.UseCustomDelimiter = true;

            options.CustomDelimiter = @"\s+by\s+";
            ParsedTrack track = TracklistParser.ParseLine("Hey Jude\u3000by\u3000The Beatles", options);
            AssertEqual("Hey Jude", track.Name, "delimiter \\s is Unicode");
            AssertEqual("The Beatles", track.Artist, "delimiter artist");

            options.CustomDelimiter = " / ";
            track = TracklistParser.ParseLine("A - B / C", options);
            AssertEqual("A - B", track.Name, "custom replaces the default");
            AssertEqual("C", track.Artist, "custom artist");

            // MusicBrainz falls back to the default separators and shows an error.
            options.CustomDelimiter = "(";
            if (TracklistParser.IsValidCustomDelimiter("("))
                throw new Exception("An unbalanced delimiter was accepted.");
            track = TracklistParser.ParseLine("A - B", options);
            AssertEqual("B", track.Artist, "invalid delimiter fallback");

            options.CustomDelimiter = String.Empty;
            track = TracklistParser.ParseLine("A - B", options);
            AssertEqual("A - B", track.Name, "empty delimiter");
            AssertEqual(String.Empty, track.Artist, "empty delimiter artist");

            foreach (string invalid in new[] { "(?i)a", "(?>a)", "(?#c)", "[", "\\", "*", "(?<n" })
            {
                if (TracklistParser.IsValidCustomDelimiter(invalid))
                    throw new Exception("JavaScript rejects delimiter: " + invalid);
            }
            foreach (string valid in new[] { "x)|(y", ")(", @"\A", @"\z", @"\p", "a{", "[]", "[^]", @"\1", @"\8", "]" })
            {
                if (!TracklistParser.IsValidCustomDelimiter(valid))
                    throw new Exception("JavaScript accepts delimiter: " + valid);
            }

            // .NET would read these as anchors or as class subtraction.
            AssertTranslated(@"\A", "A", true);
            AssertTranslated(@"\A", "", false);
            AssertTranslated(@"\z", "z", true);
            AssertTranslated("[a-z-[aeiou]]", "b]", true);
            AssertTranslated("[a-z-[aeiou]]", "b", false);
            AssertTranslated(@"[a\-z]", "-", true);
            AssertTranslated(@"[a\-z]", "m", false);
            AssertTranslated(@"[\w-.]", ".", true);
            AssertTranslated("[]", "a", false);
            AssertTranslated("[^]", "\n", true);
            AssertTranslated(".", "\u2028", false);
            AssertTranslated("a$", "a\n", false);
            AssertTranslated(@"\bb", "\u00E9b", true);
            AssertTranslated(@"\d", "\uFF11", false);
            AssertTranslated(@"\w", "\u00E9", false);
            AssertTranslated(@"(a)|\1b", "b", true);
            AssertTranslated(@"\101", "A", true);
            AssertTranslated(@"\8", "8", true);
            AssertTranslated(@"\x2", "x2", true);
        }

        private static void AssertTranslated(string pattern, string input, bool expected)
        {
            bool actual = new Regex(
                TracklistParser.TranslateJavaScriptRegex(pattern),
                RegexOptions.CultureInvariant).IsMatch(input);
            if (actual != expected)
            {
                throw new Exception(
                    "JavaScript regex /" + pattern + "/ on <" + input + "> should be " + expected + ".");
            }
        }

        private static void AssertRegexCharacterSets()
        {
            AssertCharacterSet(@"\s", TracklistParser.IsJavaScriptWhiteSpace);
            AssertCharacterSet(@"[\s]", TracklistParser.IsJavaScriptWhiteSpace);
            AssertCharacterSet(@"\S", c => !TracklistParser.IsJavaScriptWhiteSpace(c));
            AssertCharacterSet(@"[\S]", c => !TracklistParser.IsJavaScriptWhiteSpace(c));
            AssertCharacterSet(@"\d", IsDigit);
            AssertCharacterSet(@"[\D]", c => !IsDigit(c));
            AssertCharacterSet(@"\w", IsWord);
            AssertCharacterSet(@"[\W]", c => !IsWord(c));
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static bool IsWord(char c)
        {
            return IsDigit(c) || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        }

        private static void AssertCharacterSet(string pattern, Func<char, bool> expected)
        {
            Regex regex = new Regex(
                "^" + TracklistParser.TranslateJavaScriptRegex(pattern) + "\\z",
                RegexOptions.CultureInvariant);
            for (int value = 0; value <= 0xFFFF; value++)
            {
                char c = (char)value;
                if (regex.IsMatch(c.ToString()) != expected(c))
                    throw new Exception("/" + pattern + "/ is wrong for U+" + value.ToString("X4") + ".");
            }
        }

        private static IList<TracklistTrack> CurrentTracks(int count)
        {
            List<TracklistTrack> tracks = new List<TracklistTrack>();
            for (int i = 1; i <= count; i++)
                tracks.Add(new TracklistTrack { Title = "Old " + i, Artist = "Old Artist " + i });
            return tracks;
        }

        private static void AssertTocPlan()
        {
            TracklistParserOptions options = new TracklistParserOptions();
            TracklistPlan plan = TracklistParser.PlanTocUpdate(
                "1. New A - Artist A\n2. New B\n3. New C - Artist C\n4. Dropped - Dropped",
                options,
                CurrentTracks(3));
            AssertEqual(3, plan.Parsed.Count, "lines past the TOC are dropped");
            AssertEqual(0, plan.ClearedCount, "nothing cleared");
            AssertEqual("New A", plan.Result[0].Title, "plan title");
            AssertEqual("Artist A", plan.Result[0].Artist, "plan artist");
            AssertEqual("New B", plan.Result[1].Title, "plan second title");
            AssertEqual("Old Artist 2", plan.Result[1].Artist, "a line without an artist keeps it");

            // An unparseable line still counts toward the TOC limit, as on MusicBrainz.
            plan = TracklistParser.PlanTocUpdate(
                "1. A\nnot numbered\n3. Beyond the limit",
                new TracklistParserOptions { HasTrackArtists = false },
                CurrentTracks(2));
            AssertEqual(1, plan.Parsed.Count, "slice happens before parsing");
            AssertEqual(1, plan.ClearedCount, "the remaining track is cleared");
            AssertEqual(String.Empty, plan.Result[1].Title, "remaining title cleared");
            AssertEqual(String.Empty, plan.Result[1].Artist, "remaining artist cleared");

            // MusicBrainz blanks a track with no line even when titles aren't in use.
            plan = TracklistParser.PlanTocUpdate(
                "1. Ignored - Artist A",
                new TracklistParserOptions { UseTrackNames = false },
                CurrentTracks(2));
            AssertEqual("Old 1", plan.Result[0].Title, "unused title is kept");
            AssertEqual("Artist A", plan.Result[0].Artist, "artist applied without titles");
            AssertEqual(String.Empty, plan.Result[1].Title, "unused title still cleared");

            plan = TracklistParser.PlanTocUpdate(
                "1. New - Ignored",
                new TracklistParserOptions { UseTrackArtists = false },
                CurrentTracks(1));
            AssertEqual("New", plan.Result[0].Title, "title applied without artists");
            AssertEqual("Old Artist 1", plan.Result[0].Artist, "unused artist is kept");

            plan = TracklistParser.PlanTocUpdate("no numbers here", options, CurrentTracks(2));
            if (!plan.IsError || plan.Result.Count != 0)
                throw new Exception("Unparseable text must report an error and change nothing.");

            plan = TracklistParser.PlanTocUpdate(" \r\n ", options, CurrentTracks(2));
            if (plan.IsError || plan.ClearedCount != 2)
                throw new Exception("Blank text clears the disc, as it does on MusicBrainz.");

            string longTitle = new String('x', 254) + "\uD83C\uDFB5";
            plan = TracklistParser.PlanTocUpdate(
                "1. " + longTitle,
                new TracklistParserOptions { HasTrackArtists = false },
                CurrentTracks(1));
            AssertEqual(new String('x', 254), plan.Result[0].Title, "field limit keeps surrogates whole");
            AssertEqual(255, TracklistParser.LimitTrackText(new String('x', 300)).Length, "field limit");
        }

        private static void AssertSeedText()
        {
            AssertEqual("?:??", TracklistParser.FormatTrackLength(0), "zero length");
            AssertEqual("500 ms", TracklistParser.FormatTrackLength(500), "sub-second length");
            AssertEqual("0:01", TracklistParser.FormatTrackLength(1499), "rounding down");
            AssertEqual("0:02", TracklistParser.FormatTrackLength(1500), "rounding up");
            AssertEqual("1:00", TracklistParser.FormatTrackLength(59500), "rounding to a minute");
            AssertEqual("1:01:05", TracklistParser.FormatTrackLength(3665000), "hours");

            List<TracklistTrack> tracks = new List<TracklistTrack>
            {
                new TracklistTrack { Number = "1", Title = "Love Me Do", Artist = "The Beatles", FormattedLength = "2:23" },
                new TracklistTrack { Number = "2", Title = "No Artist", FormattedLength = String.Empty }
            };
            TracklistParserOptions options = new TracklistParserOptions();
            string text = TracklistParser.FormatTracklist(tracks, options);
            AssertEqual("1. Love Me Do - The Beatles (2:23)\r\n2. No Artist (?:??)\r\n", text, "seed text");
            AssertEqual(
                "Love Me Do (2:23)\r\nNo Artist (?:??)\r\n",
                TracklistParser.FormatTracklist(tracks, new TracklistParserOptions
                {
                    HasTrackNumbers = false,
                    HasTrackArtists = false
                }),
                "seed text without numbers or artists");

            IList<ParsedTrack> parsed = TracklistParser.Parse(text, options, -1);
            AssertEqual("Love Me Do", parsed[0].Name, "seed round trip title");
            AssertEqual("The Beatles", parsed[0].Artist, "seed round trip artist");
            AssertEqual("No Artist", parsed[1].Name, "seed round trip second title");

            // The appended time stops a title's ending from being read as a track length.
            string timeLikeTitle = TracklistParser.FormatTracklist(
                new List<TracklistTrack>
                {
                    new TracklistTrack { Number = "1", Title = "Live at 12.30", FormattedLength = "3:10" },
                    new TracklistTrack { Number = "2", Title = "Live at 12.30" }
                },
                options);
            parsed = TracklistParser.Parse(timeLikeTitle, options, -1);
            AssertEqual("Live at 12.30", parsed[0].Name, "seed protects a title with a known length");
            AssertEqual("Live at 12.30", parsed[1].Name, "seed protects a title with an unknown length");
        }

        private static void AssertMenu()
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
                throw new Exception("Could not create the Tools-menu fixture.");
            try
            {
                if (!NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING,
                        new UIntPtr(42u), "Other command") ||
                    !NativeMethods.AppendMenuW(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null) ||
                    !NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING,
                        new UIntPtr(DisplayCdTocCommand), "Display CD TOC... (EAC Enhancements)") ||
                    !NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING,
                        new UIntPtr(43u), "Later command"))
                {
                    throw new Exception("Could not populate the Tools-menu fixture.");
                }

                if (!EnhancementRuntime.EnsureParseTracklistMenuItem(menu) ||
                    EnhancementRuntime.EnsureParseTracklistMenuItem(menu))
                {
                    throw new Exception("Parse Tracklist was missing or duplicated.");
                }
                if (NativeMethods.GetMenuItemCount(menu) != 5 ||
                    NativeMethods.GetMenuItemID(menu, 2) != DisplayCdTocCommand ||
                    NativeMethods.GetMenuItemID(menu, 3) != ParseTracklistCommand ||
                    NativeMethods.GetMenuItemID(menu, 4) != 43u)
                {
                    throw new Exception("Parse Tracklist must appear immediately below Display CD TOC.");
                }

                StringBuilder text = new StringBuilder(256);
                NativeMethods.GetMenuStringW(menu, 3, text, text.Capacity, NativeMethods.MF_BYPOSITION);
                AssertEqual("Parse Tracklist... (EAC Enhancements)", text.ToString(), "menu label");
                // No disc is loaded in the test process, matching Display CD TOC.
                if ((NativeMethods.GetMenuState(menu, ParseTracklistCommand, NativeMethods.MF_BYCOMMAND) &
                     NativeMethods.MF_GRAYED) == 0)
                {
                    throw new Exception("Parse Tracklist must be disabled without a CD.");
                }
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        private static void AssertCommandIdUnique()
        {
            foreach (FieldInfo field in typeof(EnhancementRuntime).GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.IsLiteral && field.FieldType == typeof(uint) &&
                    field.Name != "ParseTracklistCommand" &&
                    (uint)field.GetRawConstantValue() == ParseTracklistCommand)
                {
                    throw new Exception("Parse Tracklist conflicts with " + field.Name + ".");
                }
            }
        }

        private static void AssertEacCommits()
        {
            // Register common controls before creating the native list view.
            using (ListView registration = new ListView())
            using (EacHost host = new EacHost())
            {
                registration.CreateControl();
                host.Tracks = CreateTrackList(host.Handle);
                string[,] initial =
                {
                    { "Old 1", "Old Artist 1" },
                    { "Unchanged", "Same Artist" },
                    { "Old 3", "Old Artist 3" },
                    { "Old 4", "Old Artist 4" }
                };
                for (int row = 0; row < 4; row++)
                    AddRow(host.Tracks, row, initial[row, 0], initial[row, 1]);

                int[] order = { 0, 1, 2, 3 };
                List<CdTocEntry> toc = new List<CdTocEntry>();
                for (int row = 0; row < 4; row++)
                    toc.Add(new CdTocEntry { TrackNumber = row + 1, StartSector = row * 100, NextStartSector = (row + 1) * 100 });
                TracklistParseOutcome outcome = EnhancementRuntime.ApplyTracklist(
                    host.Handle,
                    host.Tracks,
                    "A1. New 1 - New Artist (1:23)\r\n2. Unchanged - Same Artist\r\n3. New 3",
                    new TracklistParserOptions { HasVinylNumbers = true },
                    toc,
                    order,
                    "Track");

                AssertEqual(false, outcome.IsError, "apply error");
                AssertEqual(3, outcome.ParsedCount, "apply parsed count");
                AssertEqual(1, outcome.ClearedCount, "apply cleared count");

                AssertEqual("New 1", host.Committed(0, 1), "title commit");
                AssertEqual("New Artist", host.Committed(0, 2), "artist commit");
                AssertEqual(null, host.Committed(1, 1), "unchanged title is not committed");
                AssertEqual(null, host.Committed(1, 2), "unchanged artist is not committed");
                AssertEqual("New 3", host.Committed(2, 1), "third title commit");
                AssertEqual(null, host.Committed(2, 2), "a line without an artist keeps it");

                // A blank artist goes through EAC, which applies its disc-level value and
                // must not then be overwritten. EAC ignores a blank title, so a cleared
                // track takes the default title of a disc without metadata.
                AssertEqual(String.Empty, host.Committed(3, 2), "blank artist commit");
                AssertEqual(EacHost.DiscArtist, ReadCell(host.Tracks, 3, 2), "EAC's artist reset is kept");
                AssertEqual("Track04", host.Committed(3, 1), "cleared title commit");
                AssertEqual("Track04", ReadCell(host.Tracks, 3, 1), "cleared title display");

                AssertEqual("New 1", ReadCell(host.Tracks, 0, 1), "title display");
                AssertEqual("New Artist", ReadCell(host.Tracks, 0, 2), "artist display");
                AssertEqual("Old Artist 3", ReadCell(host.Tracks, 2, 2), "kept artist display");

                AssertEqual("1", outcome.Tracks[0].Number, "the result uses the TOC number, not the line's");
                AssertEqual("New 1", outcome.Tracks[0].Title, "result title");
                AssertEqual(EacHost.DiscArtist, outcome.Tracks[3].Artist, "result reads back from EAC");

                host.Commits.Clear();
                outcome = EnhancementRuntime.ApplyTracklist(
                    host.Handle,
                    host.Tracks,
                    "not numbered",
                    new TracklistParserOptions(),
                    toc,
                    order,
                    "Track");
                if (!outcome.IsError || host.Commits.Count != 0)
                    throw new Exception("An unparseable tracklist changed the disc.");
                AssertEqual("New 1", ReadCell(host.Tracks, 0, 1), "unparseable text preserves titles");

                // Long text is limited to what EAC's fields can hold.
                outcome = EnhancementRuntime.ApplyTracklist(
                    host.Handle,
                    host.Tracks,
                    "1. " + new String('x', 400),
                    new TracklistParserOptions { HasTrackArtists = false },
                    toc,
                    order,
                    "Track");
                AssertEqual(255, host.Committed(0, 1).Length, "commit length limit");

                // A number with no title is a parsed line whose title is empty.
                host.Commits.Clear();
                EnhancementRuntime.ApplyTracklist(
                    host.Handle,
                    host.Tracks,
                    "1.",
                    new TracklistParserOptions { HasTrackArtists = false },
                    toc,
                    order,
                    "Track");
                AssertEqual("Track01", host.Committed(0, 1), "empty parsed title");
                AssertEqual("Track09", EnhancementRuntime.FormatDefaultTrackTitle("Track", 9), "default title padding");
                AssertEqual("Track12", EnhancementRuntime.FormatDefaultTrackTitle("Track", 12), "default title number");

                // A disc whose tracks don't start at 1 shows and defaults to its own numbers.
                foreach (CdTocEntry entry in toc)
                    entry.TrackNumber += 10;
                outcome = EnhancementRuntime.ApplyTracklist(
                    host.Handle,
                    host.Tracks,
                    "1.",
                    new TracklistParserOptions { HasTrackArtists = false },
                    toc,
                    order,
                    "Track");
                AssertEqual("11", outcome.Tracks[0].Number, "TOC track number");
                AssertEqual("Track11", host.Committed(0, 1), "default title uses the TOC number");
            }
        }

        // The TOC of a real disc whose subchannel track numbers run from 10 to 15.
        private static void AssertDiscNumberedFromTen()
        {
            long[] starts = { 0, 22650, 58955, 64962, 94015, 119800, 132165 };
            List<CdTocEntry> toc = new List<CdTocEntry>();
            for (int i = 0; i < 6; i++)
                toc.Add(new CdTocEntry { TrackNumber = 10 + i, StartSector = starts[i], NextStartSector = starts[i + 1] });
            int[] order = { 0, 1, 2, 3, 4, 5 };

            using (ListView registration = new ListView())
            using (EacHost host = new EacHost())
            {
                registration.CreateControl();
                host.Tracks = CreateTrackList(host.Handle);
                for (int row = 0; row < 6; row++)
                    AddRow(host.Tracks, row, "Old " + (row + 1), "Artist");

                string seed = TracklistParser.FormatTracklist(
                    EnhancementRuntime.ReadTrackList(host.Tracks, toc, order),
                    new TracklistParserOptions());
                AssertEqual(
                    "10. Old 1 - Artist (5:02)\r\n" +
                    "11. Old 2 - Artist (8:04)\r\n" +
                    "12. Old 3 - Artist (1:20)\r\n" +
                    "13. Old 4 - Artist (6:27)\r\n" +
                    "14. Old 5 - Artist (5:44)\r\n" +
                    "15. Old 6 - Artist (2:45)\r\n",
                    seed,
                    "seed text uses the disc's numbers");

                // Parsing the untouched seed text changes nothing.
                EnhancementRuntime.ApplyTracklist(
                    host.Handle, host.Tracks, seed, new TracklistParserOptions(), toc, order, "Track");
                AssertEqual(0, host.Commits.Count, "untouched seed text");

                // Lines apply in order, whether or not their numbers match the disc's.
                foreach (string first in new[] { "1", "10", "37" })
                {
                    TracklistParseOutcome outcome = EnhancementRuntime.ApplyTracklist(
                        host.Handle,
                        host.Tracks,
                        first + ". New " + first + "\r\n" + first + ". Second " + first,
                        new TracklistParserOptions(),
                        toc,
                        order,
                        "Track");
                    AssertEqual("New " + first, ReadCell(host.Tracks, 0, 1), "first line, numbered from " + first);
                    AssertEqual("Second " + first, ReadCell(host.Tracks, 1, 1), "second line, numbered from " + first);
                    AssertEqual("Track12", ReadCell(host.Tracks, 2, 1), "reset title uses the disc's number");
                    AssertEqual("Track15", ReadCell(host.Tracks, 5, 1), "last reset title");
                    AssertEqual("10", outcome.Tracks[0].Number, "results use the disc's first number");
                    AssertEqual("15", outcome.Tracks[5].Number, "results use the disc's last number");
                }
            }
        }

        private static void AssertReorderedTrackList()
        {
            List<CdTocEntry> toc = new List<CdTocEntry>();
            for (int i = 0; i < 4; i++)
                toc.Add(new CdTocEntry { TrackNumber = i + 1, StartSector = i * 7500, NextStartSector = (i + 1) * 7500 });
            // Row 0 shows the third track on the disc, row 1 the first, and so on.
            int[] order = { 2, 0, 3, 1 };

            using (ListView registration = new ListView())
            using (EacHost host = new EacHost())
            {
                registration.CreateControl();
                host.Tracks = CreateTrackList(host.Handle);
                AddRow(host.Tracks, 0, "Title C", "Artist");
                AddRow(host.Tracks, 1, "Title A", "Artist");
                AddRow(host.Tracks, 2, "Title D", "Artist");
                AddRow(host.Tracks, 3, "Title B", "Artist");

                AssertEqual(
                    "1. Title A - Artist (1:40)\r\n" +
                    "2. Title B - Artist (1:40)\r\n" +
                    "3. Title C - Artist (1:40)\r\n" +
                    "4. Title D - Artist (1:40)\r\n",
                    TracklistParser.FormatTracklist(
                        EnhancementRuntime.ReadTrackList(host.Tracks, toc, order),
                        new TracklistParserOptions()),
                    "seed text follows the disc, not the display");

                TracklistParseOutcome outcome = EnhancementRuntime.ApplyTracklist(
                    host.Handle,
                    host.Tracks,
                    "1. New A\r\n2. New B\r\n3. New C",
                    new TracklistParserOptions(),
                    toc,
                    order,
                    "Track");
                AssertEqual("New C", ReadCell(host.Tracks, 0, 1), "third line on row 0");
                AssertEqual("New A", ReadCell(host.Tracks, 1, 1), "first line on row 1");
                AssertEqual("Track04", ReadCell(host.Tracks, 2, 1), "the fourth track is reset");
                AssertEqual("New B", ReadCell(host.Tracks, 3, 1), "second line on row 3");
                AssertEqual("New A", outcome.Tracks[0].Title, "results follow the disc");
                AssertEqual("Track04", outcome.Tracks[3].Title, "results end with the last track");
            }

            EnhancementRuntime.CheckTrackListOrder(new int[0]);
            foreach (int[] invalid in new[] { new[] { 0, 0 }, new[] { 0, 2 }, new[] { -1, 0 } })
            {
                try
                {
                    EnhancementRuntime.CheckTrackListOrder(invalid);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                throw new Exception("An invalid track list order was accepted.");
            }
        }

        private static void AssertLayouts()
        {
            // Read from each version's LVN_ENDLABELEDITW handler in Ghidra.
            foreach (EacVersionLayout layout in EacVersionLayout.KnownLayouts)
            {
                uint expected = layout.Name == "EAC 1.8" ? 0x007F91B8u :
                    layout.Name == "EAC 1.6" ? 0x007751A8u : 0u;
                if (layout.TrackRowMapVa != expected)
                    throw new Exception("Unverified track row map for " + layout.Name + ".");
            }
        }

        private static void AssertDialog()
        {
            TracklistParserOptions options = new TracklistParserOptions();
            string received = null;
            TracklistParseOutcome next = new TracklistParseOutcome();
            Exception failure = null;
            using (TracklistParserDialog dialog = new TracklistParserDialog(
                options,
                "1. Seed (1:00)\r\n",
                delegate(string text)
                {
                    received = text;
                    if (failure != null)
                        throw failure;
                    return next;
                }))
            {
                dialog.CreateControl();
                dialog.PerformLayout();
                AssertEqual("1. Seed (1:00)\r\n", dialog.TracklistText, "dialog seed text");

                CheckBox hasNumbers = Find<CheckBox>(dialog, "hasTrackNumbers");
                CheckBox vinyl = Find<CheckBox>(dialog, "hasVinylNumbers");
                CheckBox hasArtists = Find<CheckBox>(dialog, "hasTrackArtists");
                CheckBox useArtists = Find<CheckBox>(dialog, "useTrackArtists");
                CheckBox custom = Find<CheckBox>(dialog, "useCustomDelimiter");
                TextBox delimiter = Find<TextBox>(dialog, "customDelimiter");
                Label delimiterError = Find<Label>(dialog, "delimiterError");
                Label parseError = Find<Label>(dialog, "parseError");
                Label status = Find<Label>(dialog, "status");
                ListView results = Find<ListView>(dialog, "results");

                // MusicBrainz's defaults and option labels.
                AssertEqual("Lines start with a track number", hasNumbers.Text, "numbers label");
                foreach (string removed in new[] { "useTrackNumbers", "useTrackLengths" })
                {
                    if (dialog.Controls.Find(removed, true).Length != 0)
                        throw new Exception("EAC can't change this, so there is no option for it: " + removed);
                }
                AssertEqual("Enable vinyl track numbers", vinyl.Text, "vinyl label");
                AssertEqual("Lines contain track artists", hasArtists.Text, "artists label");
                AssertEqual("Use track artists", useArtists.Text, "use artists label");
                AssertEqual("Use custom artist delimiter", custom.Text, "delimiter label");
                AssertEqual("Use track titles", Find<CheckBox>(dialog, "useTrackNames").Text, "titles label");
                AssertEqual(true, hasNumbers.Checked, "numbers default");
                AssertEqual(false, vinyl.Checked, "vinyl default");
                AssertEqual(false, custom.Checked, "delimiter default");

                // MusicBrainz's enable bindings; dependent options keep their state.
                hasNumbers.Checked = false;
                AssertEqual(false, vinyl.Enabled, "vinyl disabled");
                AssertEqual(false, options.HasTrackNumbers, "numbers option");
                hasNumbers.Checked = true;
                AssertEqual(true, vinyl.Enabled, "vinyl enabled");
                hasArtists.Checked = false;
                AssertEqual(false, useArtists.Enabled, "use artists disabled");
                hasArtists.Checked = true;

                custom.Checked = true;
                delimiter.Text = "(";
                AssertEqual(true, options.UseCustomDelimiter, "delimiter option");
                AssertEqual("(", options.CustomDelimiter, "delimiter text option");
                AssertEqual("Invalid regular expression.", delimiterError.Text, "delimiter error text");
                if (TracklistParser.IsValidCustomDelimiter(options.CustomDelimiter))
                    throw new Exception("Fixture delimiter should be invalid.");
                delimiter.Text = " / ";
                custom.Checked = false;

                next = new TracklistParseOutcome { IsError = true };
                dialog.TracklistText = "garbage";
                dialog.ParseTracks();
                AssertEqual("garbage", received, "dialog passes the text");
                AssertEqual("Unable to parse the tracklist you entered.", parseError.Text, "parse error text");
                AssertEqual(0, results.Items.Count, "error shows no results");

                failure = new InvalidOperationException("EAC has no loaded audio-CD TOC.");
                dialog.ParseTracks();
                AssertEqual("EAC has no loaded audio-CD TOC.", parseError.Text, "failure text");
                failure = null;

                next = new TracklistParseOutcome { ParsedCount = 1, ClearedCount = 1 };
                next.Tracks.Add(new TracklistTrack { Number = "1", Title = "T", Artist = "A" });
                next.Tracks.Add(new TracklistTrack { Number = "2" });
                dialog.ParseTracks();
                AssertEqual(2, results.Items.Count, "result rows");
                AssertEqual(3, results.Columns.Count, "result columns");
                AssertEqual("Track", results.Columns[0].Text, "result track column");
                AssertEqual("1", results.Items[0].SubItems[0].Text, "result number");
                AssertEqual("2", results.Items[1].SubItems[0].Text, "second result number");
                AssertEqual("T", results.Items[0].SubItems[1].Text, "result title");
                AssertEqual("Unable to parse the tracklist you entered.", parseError.Text, "error text is restored");
                if (!status.Text.Contains("1 of 2") || !status.Text.Contains("1 track had no line"))
                    throw new Exception("The status must report cleared tracks: " + status.Text);
            }
        }

        private static T Find<T>(Control root, string name) where T : Control
        {
            Control[] found = root.Controls.Find(name, true);
            if (found.Length != 1 || !(found[0] is T))
                throw new Exception("Dialog control not found: " + name);
            return (T)found[0];
        }

        private static void AssertOptionsRoundTrip()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "EACEnhancements-TrackParser-" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                // MusicBrainz's defaults apply until something has been saved.
                TracklistParserOptions defaults = EnhancementRuntime.LoadTracklistParserOptions(path);
                AssertEqual(true, defaults.HasTrackNumbers, "default numbers");
                AssertEqual(true, defaults.HasTrackArtists, "default artists");
                AssertEqual(false, defaults.HasVinylNumbers, "default vinyl");
                AssertEqual(false, defaults.UseCustomDelimiter, "default custom");
                AssertEqual(String.Empty, defaults.CustomDelimiter, "default delimiter");
                AssertEqual(true, defaults.UseTrackNames, "default use names");
                AssertEqual(true, defaults.UseTrackArtists, "default use artists");

                File.WriteAllText(path, "[Output]\r\nRoot=C:\\\r\n", Encoding.Unicode);
                foreach (string delimiter in new[] { " / ", "\"quoted\"", "\uFF0F", "a=b;c%20", "'x'", String.Empty })
                {
                    EnhancementRuntime.SaveTracklistParserOptions(path, new TracklistParserOptions
                    {
                        HasTrackNumbers = false,
                        HasTrackArtists = false,
                        HasVinylNumbers = true,
                        UseCustomDelimiter = true,
                        CustomDelimiter = delimiter,
                        UseTrackNames = false,
                        UseTrackArtists = false
                    });
                    TracklistParserOptions loaded = EnhancementRuntime.LoadTracklistParserOptions(path);
                    AssertEqual(delimiter, loaded.CustomDelimiter, "delimiter round trip");
                    AssertEqual(false, loaded.HasTrackNumbers, "numbers round trip");
                    AssertEqual(false, loaded.HasTrackArtists, "artists round trip");
                    AssertEqual(true, loaded.HasVinylNumbers, "vinyl round trip");
                    AssertEqual(true, loaded.UseCustomDelimiter, "custom round trip");
                    AssertEqual(false, loaded.UseTrackNames, "use names round trip");
                    AssertEqual(false, loaded.UseTrackArtists, "use artists round trip");
                }
                string contents = File.ReadAllText(path);
                if (!contents.Contains("[TrackParser]") || !contents.Contains("Root=C:\\"))
                    throw new Exception("The options must use their own section and keep other settings.");
                EnhancementRuntime.MigrateSettingsFile(path);
                AssertEqual(true, EnhancementRuntime.LoadTracklistParserOptions(path).HasVinylNumbers,
                    "options survive settings validation");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static IntPtr CreateTrackList(IntPtr parent)
        {
            IntPtr list = NativeMethods.CreateWindowExW(0, "SysListView32", "", 0x50000001,
                0, 0, 400, 100, parent, new IntPtr(14), IntPtr.Zero, IntPtr.Zero);
            if (list == IntPtr.Zero)
                throw new Exception("Could not create the track fixture.");
            // LVCOLUMNW on x86; only the width member is used.
            IntPtr column = Marshal.AllocHGlobal(24);
            try
            {
                for (int offset = 0; offset < 24; offset += 4)
                    Marshal.WriteInt32(column, offset, 0);
                Marshal.WriteInt32(column, 0, 2);
                Marshal.WriteInt32(column, 8, 100);
                for (int index = 0; index < 4; index++)
                {
                    if (NativeMethods.SendMessageW(list, 0x1061, new IntPtr(index), column).ToInt32() != index)
                        throw new Exception("Could not create a track column.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(column);
            }
            return list;
        }

        private static void AddRow(IntPtr list, int row, string title, string artist)
        {
            NativeMethods.LVITEM item = new NativeMethods.LVITEM();
            item.Item = row;
            if (NativeMethods.SendMessageListViewItemW(list, 0x104D, IntPtr.Zero, ref item).ToInt32() != row)
                throw new Exception("Could not create a track row.");
            SetCell(list, row, 1, title);
            SetCell(list, row, 2, artist);
        }

        internal static void SetCell(IntPtr list, int row, int subItem, string value)
        {
            NativeMethods.LVITEM item = new NativeMethods.LVITEM();
            item.SubItem = subItem;
            item.Text = Marshal.StringToHGlobalUni(value);
            try
            {
                if (NativeMethods.SendMessageListViewItemW(list, NativeMethods.LVM_SETITEMTEXTW,
                    new IntPtr(row), ref item) == IntPtr.Zero)
                {
                    throw new Exception("Could not populate a track row.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(item.Text);
            }
        }

        private static string ReadCell(IntPtr list, int row, int subItem)
        {
            NativeMethods.LVITEM item = new NativeMethods.LVITEM();
            item.SubItem = subItem;
            item.TextMaximum = 512;
            item.Text = Marshal.AllocHGlobal(1024);
            try
            {
                NativeMethods.SendMessageListViewItemW(list, NativeMethods.LVM_GETITEMTEXTW,
                    new IntPtr(row), ref item);
                return Marshal.PtrToStringUni(item.Text);
            }
            finally
            {
                Marshal.FreeHGlobal(item.Text);
            }
        }

        private static void AssertEqual(string expected, string actual, string name)
        {
            if (!String.Equals(expected, actual, StringComparison.Ordinal))
                throw new Exception(name + " mismatch.\r\nExpected: [" + expected + "]\r\nActual: [" + actual + "]");
        }

        private static void AssertEqual(int expected, int actual, string name)
        {
            if (expected != actual)
                throw new Exception(name + " mismatch. Expected " + expected + ", actual " + actual + ".");
        }

        private static void AssertEqual(bool expected, bool actual, string name)
        {
            if (expected != actual)
                throw new Exception(name + " mismatch. Expected " + expected + ", actual " + actual + ".");
        }

        // Models the main window's track-list LVN_ENDLABELEDITW handler as read in Ghidra:
        // an empty title is ignored, an empty artist takes the disc-level value, and
        // anything else is stored and shown.
        private sealed class EacHost : Form
        {
            internal const string DiscArtist = "Disc Artist";
            internal IntPtr Tracks;
            internal readonly Dictionary<string, string> Commits = new Dictionary<string, string>();

            internal string Committed(int row, int subItem)
            {
                string value;
                return Commits.TryGetValue(row + ":" + subItem, out value) ? value : null;
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == NativeMethods.WM_NOTIFY && message.LParam != IntPtr.Zero)
                {
                    NativeMethods.NMHDR header = (NativeMethods.NMHDR)Marshal.PtrToStructure(
                        message.LParam, typeof(NativeMethods.NMHDR));
                    if (header.Code == NativeMethods.LVN_ENDLABELEDITW)
                    {
                        NativeMethods.NMLVDISPINFO edit = (NativeMethods.NMLVDISPINFO)Marshal.PtrToStructure(
                            message.LParam, typeof(NativeMethods.NMLVDISPINFO));
                        string text = Marshal.PtrToStringUni(edit.Item.Text);
                        if (text.Length == 0 && edit.Item.SubItem == 1)
                            throw new Exception("EAC ignores an empty title, so it must not be sent.");
                        Commits[edit.Item.Item + ":" + edit.Item.SubItem] = text;
                        SetCell(Tracks, edit.Item.Item, edit.Item.SubItem,
                            text.Length == 0 ? DiscArtist : text);
                        message.Result = new IntPtr(1);
                        return;
                    }
                }
                base.WndProc(ref message);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(IntPtr menu);
    }
}
