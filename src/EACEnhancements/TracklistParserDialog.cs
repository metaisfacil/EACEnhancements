using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AudioDataPlugIn
{
    internal sealed class TracklistParseOutcome
    {
        internal bool IsError;
        internal int ParsedCount;
        internal int ClearedCount;
        // The disc's tracks after the parse.
        internal IList<TracklistTrack> Tracks = new List<TracklistTrack>();
    }

    internal sealed class TracklistParserDialog : Form
    {
        internal const string ParseErrorText = "Unable to parse the tracklist you entered.";
        internal const string InvalidDelimiterText = "Invalid regular expression.";

        private const string ParserHelpText =
            "If you have a tracklist you can copy and paste, you can input it here.\r\n\r\n" +
            "By default, the parser expects the following format:\r\n" +
            "    1. Love Me Do - The Beatles (2:23)\r\n" +
            "    2. P.S. I Love You - The Beatles (2:03)\r\n\r\n" +
            "You can use the checkboxes under the parser to deactivate some sections. For example, " +
            "you can deactivate \u201CLines contain track artists\u201D if your data has no track " +
            "artists. A track length at the end of a line is ignored, as EAC reads lengths from " +
            "the CD.\r\n\r\n" +
            "If you select \u201CEnable vinyl track numbers\u201D, the parser will also accept " +
            "strings such as \u201CA1\u201D as track numbers.";
        private const string DelimiterHelpText =
            "Enter the delimiter that separates the track name from the artist name " +
            "(valid regular expressions are accepted).";

        private readonly TracklistParserOptions options;
        private readonly Func<string, TracklistParseOutcome> parse;
        private readonly TextBox tracklist;
        private readonly Label parserHelp;
        private readonly Label parseError;
        private readonly CheckBox hasTrackNumbers;
        private readonly CheckBox hasVinylNumbers;
        private readonly CheckBox hasTrackArtists;
        private readonly CheckBox useTrackArtists;
        private readonly CheckBox useCustomDelimiter;
        private readonly CheckBox useTrackNames;
        private readonly Panel delimiterPanel;
        private readonly TextBox customDelimiter;
        private readonly Label delimiterError;
        private readonly Label delimiterHelp;
        private readonly Label status;
        private readonly ListView results;
        private readonly Button parseButton;

        internal TracklistParserDialog(
            TracklistParserOptions options,
            string initialText,
            Func<string, TracklistParseOutcome> parse)
        {
            this.options = options;
            this.parse = parse;

            Text = "Parse Tracklist";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(700, 660);
            MinimumSize = new Size(560, 560);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 8
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            FlowLayoutPanel heading = new FlowLayoutPanel
            {
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 3),
                WrapContents = false
            };
            heading.Controls.Add(new Label
            {
                AutoSize = true,
                Margin = Padding.Empty,
                Text = "Enter a tracklist below:"
            });
            heading.Controls.Add(CreateHelpLink("parserHelpLink", delegate
            {
                parserHelp.Visible = !parserHelp.Visible;
            }));
            layout.Controls.Add(heading, 0, 0);

            parserHelp = new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
                Name = "parserHelp",
                Text = ParserHelpText,
                Visible = false
            };
            layout.Controls.Add(parserHelp, 0, 1);

            tracklist = new TextBox
            {
                AcceptsReturn = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                MaxLength = 1024 * 1024,
                Multiline = true,
                Name = "tracklist",
                ScrollBars = ScrollBars.Both,
                Text = initialText ?? String.Empty,
                WordWrap = false
            };
            tracklist.KeyDown += HandleTracklistKeyDown;
            layout.Controls.Add(tracklist, 0, 2);

            parseError = new Label
            {
                AutoSize = true,
                ForeColor = Color.Firebrick,
                Margin = new Padding(0, 4, 0, 0),
                Name = "parseError",
                Text = ParseErrorText,
                Visible = false
            };
            layout.Controls.Add(parseError, 0, 3);

            hasTrackNumbers = CreateOption("hasTrackNumbers", "Lines start with a track number", options.HasTrackNumbers, 0);
            hasVinylNumbers = CreateOption("hasVinylNumbers", "Enable vinyl track numbers", options.HasVinylNumbers, 20);
            hasTrackArtists = CreateOption("hasTrackArtists", "Lines contain track artists", options.HasTrackArtists, 0);
            useTrackArtists = CreateOption("useTrackArtists", "Use track artists", options.UseTrackArtists, 0);
            useCustomDelimiter = CreateOption("useCustomDelimiter", "Use custom artist delimiter", options.UseCustomDelimiter, 0);
            useTrackNames = CreateOption("useTrackNames", "Use track titles", options.UseTrackNames, 0);

            customDelimiter = new TextBox
            {
                Margin = new Padding(4, 0, 0, 0),
                Name = "customDelimiter",
                Text = options.CustomDelimiter ?? String.Empty,
                Width = 150
            };
            customDelimiter.TextChanged += delegate { SynchronizeOptions(); };
            delimiterError = new Label
            {
                AutoSize = true,
                ForeColor = Color.Firebrick,
                Margin = new Padding(0, 3, 0, 0),
                Name = "delimiterError",
                Text = InvalidDelimiterText,
                Visible = false
            };
            delimiterHelp = new Label
            {
                AutoSize = true,
                Font = new Font(Font, FontStyle.Italic),
                Margin = new Padding(0, 3, 0, 0),
                MaximumSize = new Size(300, 0),
                Name = "delimiterHelp",
                Text = DelimiterHelpText,
                Visible = false
            };
            delimiterPanel = CreateDelimiterPanel();

            TableLayoutPanel optionGrid = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                RowCount = 5
            };
            optionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            optionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            optionGrid.Controls.Add(hasTrackNumbers, 0, 0);
            optionGrid.Controls.Add(hasVinylNumbers, 0, 1);
            optionGrid.Controls.Add(hasTrackArtists, 0, 2);
            optionGrid.Controls.Add(useTrackArtists, 1, 2);
            optionGrid.Controls.Add(useCustomDelimiter, 0, 3);
            optionGrid.Controls.Add(useTrackNames, 1, 3);
            optionGrid.Controls.Add(delimiterPanel, 0, 4);

            GroupBox optionGroup = new GroupBox
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 0),
                Text = "Options"
            };
            optionGroup.Controls.Add(optionGrid);
            layout.Controls.Add(optionGroup, 0, 4);

            status = new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 3),
                Name = "status",
                Text = "Parsed tracks will appear here."
            };
            layout.Controls.Add(status, 0, 5);

            results = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Margin = Padding.Empty,
                MultiSelect = false,
                Name = "results",
                View = View.Details
            };
            results.Columns.Add("Track", 56);
            results.Columns.Add("Title", 280);
            results.Columns.Add("Artist", 170);
            layout.Controls.Add(results, 0, 6);

            Button close = new Button
            {
                Anchor = AnchorStyles.Left,
                DialogResult = DialogResult.Cancel,
                Margin = Padding.Empty,
                MinimumSize = new Size(88, 28),
                Name = "close",
                Text = "Close",
                UseVisualStyleBackColor = true
            };
            parseButton = new Button
            {
                Anchor = AnchorStyles.Right,
                AutoSize = true,
                Margin = Padding.Empty,
                MinimumSize = new Size(100, 28),
                Name = "parseTracks",
                Text = "Parse tracks",
                UseVisualStyleBackColor = true
            };
            parseButton.Click += delegate { ParseTracks(); };

            TableLayoutPanel buttons = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 0),
                RowCount = 1
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttons.Controls.Add(close, 0, 0);
            buttons.Controls.Add(parseButton, 1, 0);
            layout.Controls.Add(buttons, 0, 7);

            Controls.Add(layout);
            CancelButton = close;

            layout.Resize += delegate
            {
                parserHelp.MaximumSize = new Size(Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal), 0);
            };
            results.Resize += delegate { FitResultColumns(); };
            Shown += delegate
            {
                // Pasting a new tracklist is the usual next step.
                tracklist.SelectAll();
                tracklist.Focus();
            };

            hasTrackNumbers.CheckedChanged += delegate { SynchronizeOptions(); };
            hasVinylNumbers.CheckedChanged += delegate { SynchronizeOptions(); };
            hasTrackArtists.CheckedChanged += delegate { SynchronizeOptions(); };
            useTrackArtists.CheckedChanged += delegate { SynchronizeOptions(); };
            useCustomDelimiter.CheckedChanged += delegate { SynchronizeOptions(); };
            useTrackNames.CheckedChanged += delegate { SynchronizeOptions(); };
            SynchronizeOptions();
        }

        internal string TracklistText
        {
            get { return tracklist.Text; }
            set { tracklist.Text = value; }
        }

        internal void ParseTracks()
        {
            SynchronizeOptions();
            parseError.Text = ParseErrorText;
            TracklistParseOutcome outcome;
            try
            {
                outcome = parse(tracklist.Text);
            }
            catch (RegexMatchTimeoutException)
            {
                parseError.Text = "The custom delimiter took too long to evaluate.";
                parseError.Visible = true;
                return;
            }
            catch (InvalidOperationException error)
            {
                // For example, the CD was ejected while the dialog was open.
                parseError.Text = error.Message;
                parseError.Visible = true;
                return;
            }

            parseError.Visible = outcome.IsError;
            if (outcome.IsError)
                return;

            results.BeginUpdate();
            results.Items.Clear();
            for (int i = 0; i < outcome.Tracks.Count; i++)
            {
                TracklistTrack track = outcome.Tracks[i];
                results.Items.Add(new ListViewItem(new[] { track.Number, track.Title, track.Artist }));
            }
            results.EndUpdate();

            string summary = "Applied " + outcome.ParsedCount + " of " + outcome.Tracks.Count +
                " tracks to the current CD.";
            if (outcome.ClearedCount == 1)
                summary += " 1 track had no line and was reset to EAC's defaults.";
            else if (outcome.ClearedCount > 1)
                summary += " " + outcome.ClearedCount + " tracks had no line and were reset to EAC's defaults.";
            status.Text = summary;
        }

        private void SynchronizeOptions()
        {
            options.HasTrackNumbers = hasTrackNumbers.Checked;
            options.HasVinylNumbers = hasVinylNumbers.Checked;
            options.HasTrackArtists = hasTrackArtists.Checked;
            options.UseTrackArtists = useTrackArtists.Checked;
            options.UseCustomDelimiter = useCustomDelimiter.Checked;
            options.CustomDelimiter = customDelimiter.Text;
            options.UseTrackNames = useTrackNames.Checked;

            hasVinylNumbers.Enabled = options.HasTrackNumbers;
            useTrackArtists.Enabled = options.HasTrackArtists;
            delimiterPanel.Visible = options.UseCustomDelimiter;
            delimiterError.Visible = options.UseCustomDelimiter &&
                !TracklistParser.IsValidCustomDelimiter(options.CustomDelimiter);
        }

        private void HandleTracklistKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                tracklist.SelectAll();
                e.SuppressKeyPress = true;
            }
        }

        private void FitResultColumns()
        {
            int remaining = results.ClientSize.Width - results.Columns[0].Width;
            if (remaining < 120)
                return;
            results.Columns[1].Width = remaining * 3 / 5;
            results.Columns[2].Width = remaining - results.Columns[1].Width;
        }

        private Panel CreateDelimiterPanel()
        {
            FlowLayoutPanel field = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty,
                WrapContents = false
            };
            field.Controls.Add(new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0),
                Text = "Custom delimiter:"
            });
            field.Controls.Add(customDelimiter);
            field.Controls.Add(CreateHelpLink("delimiterHelpLink", delegate
            {
                delimiterHelp.Visible = !delimiterHelp.Visible;
            }));

            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                Margin = new Padding(20, 2, 0, 2),
                Name = "delimiterPanel",
                WrapContents = false
            };
            panel.Controls.Add(field);
            panel.Controls.Add(delimiterError);
            panel.Controls.Add(delimiterHelp);
            return panel;
        }

        private static CheckBox CreateOption(string name, string text, bool value, int indent)
        {
            return new CheckBox
            {
                AutoSize = true,
                Checked = value,
                Margin = new Padding(indent, 2, 0, 2),
                Name = name,
                Text = text,
                UseVisualStyleBackColor = true
            };
        }

        private static LinkLabel CreateHelpLink(string name, Action toggle)
        {
            LinkLabel link = new LinkLabel
            {
                AutoSize = true,
                Margin = new Padding(6, 0, 0, 0),
                Name = name,
                Text = "help"
            };
            link.LinkClicked += delegate { toggle(); };
            return link;
        }
    }

    internal static partial class EnhancementRuntime
    {
        private const int DefaultTrackTitleStringId = 80647;

        private static void ShowTracklistParser(IntPtr mainWindow)
        {
            TracklistParserOptions options = LoadTracklistParserOptions(GetSettingsFilePath());
            try
            {
                IntPtr trackList = NativeMethods.GetDlgItem(mainWindow, TrackListControlId);
                if (trackList == IntPtr.Zero)
                    throw new InvalidOperationException("EAC's track list was not found.");

                string initialText = TracklistParser.FormatTracklist(
                    ReadTrackList(trackList, ReadCurrentCdToc(), ReadTrackListOrder(trackList)),
                    options);
                string defaultTitlePrefix = LoadDefaultTrackTitlePrefix(mainWindow);
                using (TracklistParserDialog dialog = new TracklistParserDialog(
                    options,
                    initialText,
                    delegate(string text)
                    {
                        return ApplyTracklist(
                            mainWindow,
                            trackList,
                            text,
                            options,
                            ReadCurrentCdToc(),
                            ReadTrackListOrder(trackList),
                            defaultTitlePrefix);
                    }))
                {
                    dialog.ShowDialog(new WindowHandleOwner(mainWindow));
                }
            }
            catch (Exception error)
            {
                Log("Parse tracklist failed: " + error);
                MessageBox.Show(
                    "EAC Enhancements could not parse the tracklist.\r\n\r\n" + error.Message,
                    "EAC Enhancements",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SaveTracklistParserOptions(GetSettingsFilePath(), options);
            }
        }

        // Returns the disc's tracks in TOC order, whatever order the rows are displayed in.
        internal static IList<TracklistTrack> ReadTrackList(
            IntPtr trackList,
            IList<CdTocEntry> toc,
            IList<int> rowOrder)
        {
            TracklistTrack[] tracks = new TracklistTrack[rowOrder.Count];
            for (int row = 0; row < rowOrder.Count; row++)
            {
                int index = rowOrder[row];
                TracklistTrack track = new TracklistTrack();
                track.Row = row;
                track.Title = ReadListViewText(trackList, row, TrackTitleSubItem);
                track.Artist = ReadListViewText(trackList, row, TrackArtistSubItem);
                track.Number = GetTocTrackNumber(toc, index).ToString(CultureInfo.InvariantCulture);
                if (index < toc.Count)
                {
                    track.FormattedLength = TracklistParser.FormatTrackLength(
                        (toc[index].NextStartSector - toc[index].StartSector) * 1000 / 75);
                }
                tracks[index] = track;
            }
            return tracks;
        }

        // EAC lets the track list be dragged and sorted, and keeps a map from each row to
        // its TOC index (written by 1.6: 0x00404833, 0x005F05AC; 1.8: 0x005F44EC, verified
        // in Ghidra). It's the identity until then, and resets on a refresh.
        private static int[] ReadTrackListOrder(IntPtr trackList)
        {
            int count = NativeMethods.SendMessageW(
                trackList,
                NativeMethods.LVM_GETITEMCOUNT,
                IntPtr.Zero,
                IntPtr.Zero).ToInt32();
            if (count > 99)
                throw new InvalidOperationException("EAC's track list has too many rows.");
            int[] order = new int[count];
            for (int row = 0; row < count; row++)
            {
                order[row] = Marshal.ReadInt32(
                    AddressFromStaticVa(layout.TrackRowMapVa + (uint)row * 4));
            }
            return CheckTrackListOrder(order);
        }

        internal static int[] CheckTrackListOrder(int[] order)
        {
            bool[] seen = new bool[order.Length];
            foreach (int index in order)
            {
                if (index < 0 || index >= order.Length || seen[index])
                    throw new InvalidOperationException("EAC's track list order is not valid.");
                seen[index] = true;
            }
            return order;
        }

        internal static TracklistParseOutcome ApplyTracklist(
            IntPtr mainWindow,
            IntPtr trackList,
            string text,
            TracklistParserOptions options,
            IList<CdTocEntry> toc,
            IList<int> rowOrder,
            string defaultTitlePrefix)
        {
            IList<TracklistTrack> current = ReadTrackList(trackList, toc, rowOrder);
            if (current.Count == 0)
                throw new InvalidOperationException("EAC has no track list loaded.");

            TracklistPlan plan = TracklistParser.PlanTocUpdate(text, options, current);
            TracklistParseOutcome outcome = new TracklistParseOutcome();
            outcome.IsError = plan.IsError;
            if (plan.IsError)
                return outcome;

            for (int index = 0; index < current.Count; index++)
            {
                int row = current[index].Row;
                TracklistTrack result = plan.Result[index];
                // EAC has no empty title: its label-edit handler ignores one (1.6:
                // 0x004060A0; 1.8: 0x00406190, verified in Ghidra), and a disc without
                // metadata gets its default title instead.
                string title = result.Title.Length > 0
                    ? result.Title
                    : FormatDefaultTrackTitle(defaultTitlePrefix, GetTocTrackNumber(toc, index));
                if (!String.Equals(current[index].Title, title, StringComparison.Ordinal))
                    WriteTrackText(mainWindow, trackList, row, TrackTitleSubItem, title);
                if (!String.Equals(current[index].Artist, result.Artist, StringComparison.Ordinal))
                {
                    if (result.Artist.Length > 0)
                        WriteTrackText(mainWindow, trackList, row, TrackArtistSubItem, result.Artist);
                    else
                        ResetTrackArtist(mainWindow, trackList, row);
                }
            }
            NativeMethods.InvalidateRect(trackList, IntPtr.Zero, false);

            outcome.ParsedCount = plan.Parsed.Count;
            outcome.ClearedCount = plan.ClearedCount;
            // Read back rather than reporting the plan: EAC decides what an empty artist
            // becomes.
            outcome.Tracks = ReadTrackList(trackList, toc, rowOrder);
            Log("Applied a parsed tracklist: " + outcome.ParsedCount + " of " + current.Count +
                " tracks, " + outcome.ClearedCount + " cleared.");
            return outcome;
        }

        // A disc's track numbers come from its subchannel data and needn't start at 1.
        private static int GetTocTrackNumber(IList<CdTocEntry> toc, int index)
        {
            return index < toc.Count ? toc[index].TrackNumber : index + 1;
        }

        // Matches the title UpdateTrackList gives a disc without metadata (1.8:
        // 0x00645469, verified in Ghidra), such as "Track04".
        internal static string FormatDefaultTrackTitle(string prefix, int trackNumber)
        {
            return prefix + trackNumber.ToString("00", CultureInfo.InvariantCulture);
        }

        private static string LoadDefaultTrackTitlePrefix(IntPtr mainWindow)
        {
            Dictionary<int, string> strings = FindCurrentLanguageStrings(mainWindow);
            string prefix;
            return strings != null && strings.TryGetValue(DefaultTrackTitleStringId, out prefix)
                ? prefix
                : "Track";
        }

        private static void ResetTrackArtist(IntPtr mainWindow, IntPtr trackList, int row)
        {
            IntPtr text = Marshal.StringToHGlobalUni(String.Empty);
            try
            {
                NativeMethods.LVITEM item = new NativeMethods.LVITEM();
                item.Mask = NativeMethods.LVIF_TEXT;
                item.Item = row;
                item.SubItem = TrackArtistSubItem;
                item.Text = text;
                NotifyTrackTextEdited(mainWindow, trackList, item);
            }
            finally
            {
                Marshal.FreeHGlobal(text);
            }
        }

        internal static TracklistParserOptions LoadTracklistParserOptions(string iniPath)
        {
            TracklistParserOptions options = new TracklistParserOptions();
            try
            {
                options.HasTrackNumbers = ReadTrackParserOption(iniPath, "HasTrackNumbers", options.HasTrackNumbers);
                options.HasVinylNumbers = ReadTrackParserOption(iniPath, "HasVinylNumbers", options.HasVinylNumbers);
                options.HasTrackArtists = ReadTrackParserOption(iniPath, "HasTrackArtists", options.HasTrackArtists);
                options.UseCustomDelimiter = ReadTrackParserOption(iniPath, "UseCustomDelimiter", options.UseCustomDelimiter);
                options.CustomDelimiter = Uri.UnescapeDataString(
                    ReadIniValue(iniPath, "CustomDelimiter", String.Empty));
                options.UseTrackArtists = ReadTrackParserOption(iniPath, "UseTrackArtists", options.UseTrackArtists);
                options.UseTrackNames = ReadTrackParserOption(iniPath, "UseTrackNames", options.UseTrackNames);
            }
            catch (Exception error)
            {
                Log("Could not read the tracklist parser options: " + error.Message);
            }
            return options;
        }

        private static bool ReadTrackParserOption(string iniPath, string key, bool fallback)
        {
            return ParseIniBoolean(ReadIniValue(iniPath, key, null), fallback);
        }

        internal static void SaveTracklistParserOptions(string iniPath, TracklistParserOptions options)
        {
            if (settingsMigrationFailed)
                return;
            // The profile APIs strip a value's surrounding spaces and quotes, either of
            // which can be significant in a delimiter, so it's stored percent-encoded.
            if (!WriteIniValue(iniPath, "HasTrackNumbers", options.HasTrackNumbers ? "1" : "0") ||
                !WriteIniValue(iniPath, "HasVinylNumbers", options.HasVinylNumbers ? "1" : "0") ||
                !WriteIniValue(iniPath, "HasTrackArtists", options.HasTrackArtists ? "1" : "0") ||
                !WriteIniValue(iniPath, "UseCustomDelimiter", options.UseCustomDelimiter ? "1" : "0") ||
                !WriteIniValue(iniPath, "CustomDelimiter",
                    Uri.EscapeDataString(options.CustomDelimiter ?? String.Empty).Replace("'", "%27")) ||
                !WriteIniValue(iniPath, "UseTrackArtists", options.UseTrackArtists ? "1" : "0") ||
                !WriteIniValue(iniPath, "UseTrackNames", options.UseTrackNames ? "1" : "0"))
            {
                Log("Could not save the tracklist parser options; Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }
        }
    }
}
