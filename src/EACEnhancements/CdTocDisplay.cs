using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AudioDataPlugIn
{
    internal sealed class CdTocEntry
    {
        internal int TrackNumber;
        internal long StartSector;
        internal long NextStartSector;
        internal bool HasPeak;
        // Matches the userscript's normalized 0..1000 representation.
        internal double Peak;
        internal int PeakPrecision;
    }

    internal sealed class CdTocLabels
    {
        internal string Title = "TOC of the extracted CD";
        internal string Track = "Track";
        internal string Start = "Start";
        internal string Length = "Length";
        internal string StartSector = "Start sector";
        internal string EndSector = "End sector";
    }

    internal static class CdTocFormatter
    {
        private static readonly int[] DefaultColumnWidths = { 4, 10, 10, 8, 8 };

        internal static string Format(IList<CdTocEntry> entries, CdTocLabels labels)
        {
            if (entries == null)
                throw new ArgumentNullException("entries");
            if (labels == null)
                throw new ArgumentNullException("labels");

            string[] captions =
            {
                labels.Track,
                labels.Start,
                labels.Length,
                labels.StartSector,
                labels.EndSector
            };
            int[] widths = new int[captions.Length];
            int separatorLength = 0;
            for (int i = 0; i < captions.Length; i++)
            {
                captions[i] = captions[i] ?? String.Empty;
                widths[i] = Math.Max(DefaultColumnWidths[i], captions[i].Length + 2);
                separatorLength += widths[i] + 1;
            }

            StringBuilder text = new StringBuilder();
            text.Append(labels.Title ?? String.Empty).Append("\r\n\r\n    ");
            for (int i = 0; i < captions.Length; i++)
            {
                text.Append(Center(captions[i], widths[i]));
                if (i < captions.Length - 1)
                    text.Append('|');
            }
            text.Append("\r\n    ").Append('-', separatorLength - 1).Append("\r\n");

            for (int i = 0; i < entries.Count; i++)
            {
                CdTocEntry entry = entries[i];
                long length = entry.NextStartSector - entry.StartSector;
                if (entry.TrackNumber < 1 || entry.TrackNumber > 99 ||
                    entry.StartSector < 0 || length <= 0)
                {
                    throw new InvalidOperationException("EAC's current CD TOC is not valid.");
                }

                string track = entry.TrackNumber.ToString(CultureInfo.InvariantCulture);
                // EAC's integer conversion path manually gives single-digit track
                // numbers one leading space before centering the column.
                if (track.Length == 1)
                    track = " " + track;
                text.Append("    ")
                    .Append(Center(track, widths[0])).Append('|')
                    .Append(Center(FormatMsf(entry.StartSector), widths[1])).Append('|')
                    .Append(Center(FormatMsf(length), widths[2])).Append('|')
                    .Append(Center(
                        AlignRight(
                            entry.StartSector.ToString(CultureInfo.InvariantCulture),
                            6),
                        widths[3] - 1)).Append(" |")
                    .Append(Center(
                        AlignRight(
                            (entry.NextStartSector - 1).ToString(CultureInfo.InvariantCulture),
                            6),
                        widths[4] - 1)).Append(" \r\n");
            }
            text.Append("\r\n");
            return text.ToString();
        }

        internal static string FormatMsf(long sectors)
        {
            if (sectors < 0)
                throw new ArgumentOutOfRangeException("sectors");
            long minutes = sectors / (75 * 60);
            long seconds = (sectors / 75) % 60;
            long frames = sectors % 75;
            return String.Format(
                CultureInfo.InvariantCulture,
                "{0,2}:{1:00}.{2:00}",
                minutes,
                seconds,
                frames);
        }

        private static string Center(string value, int width)
        {
            if (value.Length >= width)
                return value;
            int padding = width - value.Length;
            int trailing = padding / 2;
            int leading = padding - trailing;
            return new String(' ', leading) + value + new String(' ', trailing);
        }

        private static string AlignRight(string value, int width)
        {
            return value.Length >= width
                ? value
                : new String(' ', width - value.Length) + value;
        }
    }

    internal static partial class EnhancementRuntime
    {
        private const int TocRecordStride = 0x22;
        private const int TocStartSectorOffset = 4;
        private const int TocNextStartSectorOffset = 0x14;
        private const string CurrentDiscPeakComparisonNotice =
            "Peak values are not factored into this comparison because they " +
            "cannot be derived without completing a rip.";
        private static Form cdTocWindow;
        private static RichTextBox cdTocTextBox;
        private static IList<CdTocEntry> displayedCdTocEntries;
        private static IntPtr cdTocOwnerWindow;

        internal static bool HasCurrentCdToc()
        {
            try
            {
                int count = Marshal.ReadInt32(
                    AddressFromStaticVa(layout.TrackSelectionArrayVa - 0x10));
                if (count < 1 || count > 99)
                    return false;
                int firstTrackNumber = Marshal.ReadInt32(
                    AddressFromStaticVa(layout.FirstTocTrackNumberVa));
                return firstTrackNumber >= 1 && firstTrackNumber <= 99;
            }
            catch
            {
                return false;
            }
        }

        internal static IList<CdTocEntry> ReadCurrentCdToc()
        {
            int count = Marshal.ReadInt32(
                AddressFromStaticVa(layout.TrackSelectionArrayVa - 0x10));
            if (count < 1 || count > 99)
                throw new InvalidOperationException("EAC has no loaded audio-CD TOC.");

            List<CdTocEntry> entries = new List<CdTocEntry>(count);
            for (int i = 0; i < count; i++)
            {
                IntPtr track = Add(
                    AddressFromStaticVa(layout.FirstTocTrackNumberVa),
                    i * TocRecordStride);
                CdTocEntry entry = new CdTocEntry();
                entry.TrackNumber = Marshal.ReadInt32(track);
                entry.StartSector = ReadTocInt64(track, TocStartSectorOffset);
                entry.NextStartSector = ReadTocInt64(track, TocNextStartSectorOffset);
                if (entry.TrackNumber < 1 || entry.TrackNumber > 99 ||
                    entry.StartSector < 0 ||
                    entry.NextStartSector <= entry.StartSector)
                {
                    throw new InvalidOperationException("EAC's current CD TOC is not valid.");
                }
                entries.Add(entry);
            }
            return entries;
        }

        private static long ReadTocInt64(IntPtr address, int offset)
        {
            uint low = unchecked((uint)Marshal.ReadInt32(address, offset));
            uint high = unchecked((uint)Marshal.ReadInt32(address, offset + 4));
            return unchecked((long)(((ulong)high << 32) | low));
        }

        private static void ShowCurrentCdToc(IntPtr mainWindow)
        {
            try
            {
                IList<CdTocEntry> entries = ReadCurrentCdToc();
                string toc = CdTocFormatter.Format(entries, LoadCurrentTocLabels(mainWindow));
                displayedCdTocEntries = entries;
                cdTocOwnerWindow = mainWindow;
                if (cdTocWindow != null && !cdTocWindow.IsDisposed)
                {
                    cdTocTextBox.Text = toc;
                    cdTocWindow.Show();
                    cdTocWindow.Activate();
                    cdTocTextBox.Select(0, 0);
                    return;
                }

                cdTocTextBox = new RichTextBox();
                cdTocTextBox.AcceptsTab = true;
                cdTocTextBox.BackColor = SystemColors.Window;
                cdTocTextBox.BorderStyle = BorderStyle.None;
                cdTocTextBox.DetectUrls = false;
                cdTocTextBox.Dock = DockStyle.Fill;
                cdTocTextBox.Font = new Font("Consolas", 9.0F, FontStyle.Regular);
                cdTocTextBox.ReadOnly = true;
                cdTocTextBox.ScrollBars = RichTextBoxScrollBars.Both;
                cdTocTextBox.WordWrap = false;
                cdTocTextBox.Text = toc;

                Button copyButton = new Button();
                copyButton.AutoSize = true;
                copyButton.Text = "Copy TOC";
                copyButton.UseVisualStyleBackColor = true;
                copyButton.Click += delegate { CopyDisplayedCdToc(); };

                Button compareButton = new Button();
                compareButton.AutoSize = true;
                compareButton.Text = "Compare to Log...";
                compareButton.UseVisualStyleBackColor = true;
                compareButton.Click += delegate { CompareDisplayedCdTocToLog(); };

                Button compareTwoButton = new Button();
                compareTwoButton.AutoSize = true;
                compareTwoButton.Text = "Compare Two Logs...";
                compareTwoButton.UseVisualStyleBackColor = true;
                compareTwoButton.Click += delegate { CompareTwoLogs(); };

                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.AutoSize = true;
                buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                buttons.Dock = DockStyle.Bottom;
                buttons.FlowDirection = FlowDirection.LeftToRight;
                buttons.Padding = new Padding(8, 7, 8, 7);
                buttons.WrapContents = false;
                buttons.Controls.Add(copyButton);
                buttons.Controls.Add(compareButton);
                buttons.Controls.Add(compareTwoButton);

                cdTocWindow = new Form();
                cdTocWindow.ClientSize = new Size(600, 420);
                cdTocWindow.Controls.Add(cdTocTextBox);
                cdTocWindow.Controls.Add(buttons);
                cdTocWindow.MinimizeBox = false;
                cdTocWindow.MinimumSize = new Size(420, 240);
                cdTocWindow.ShowIcon = false;
                cdTocWindow.StartPosition = FormStartPosition.CenterParent;
                cdTocWindow.Text = "CD Table-of-Contents (TOC)";
                cdTocWindow.FormClosed += delegate
                {
                    if (cdTocTextBox != null)
                    {
                        cdTocTextBox.Dispose();
                        cdTocTextBox = null;
                    }
                    displayedCdTocEntries = null;
                    cdTocOwnerWindow = IntPtr.Zero;
                    cdTocWindow = null;
                };
                cdTocWindow.Show(new EacWindowOwner(mainWindow));
                cdTocTextBox.Select(0, 0);
            }
            catch (Exception error)
            {
                Log("Display CD TOC failed: " + error);
                MessageBox.Show(
                    "EAC Enhancements could not display the current CD TOC.\r\n\r\n" +
                    error.Message,
                    "EAC Enhancements",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void CopyDisplayedCdToc()
        {
            try
            {
                if (cdTocTextBox == null || String.IsNullOrEmpty(cdTocTextBox.Text))
                    throw new InvalidOperationException("There is no displayed CD TOC to copy.");
                Clipboard.SetText(cdTocTextBox.Text);
            }
            catch (Exception error)
            {
                Log("Copy CD TOC failed: " + error);
                MessageBox.Show(
                    "EAC Enhancements could not copy the CD TOC to the clipboard.\r\n\r\n" +
                    error.Message,
                    "EAC Enhancements",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void CompareDisplayedCdTocToLog()
        {
            try
            {
                if (displayedCdTocEntries == null || displayedCdTocEntries.Count == 0)
                    throw new InvalidOperationException("There is no displayed CD TOC to compare.");

                string path;
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.CheckFileExists = true;
                    dialog.DefaultExt = "log";
                    dialog.Filter =
                        "Rip log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*";
                    dialog.FilterIndex = 1;
                    dialog.Multiselect = false;
                    dialog.RestoreDirectory = true;
                    dialog.Title = "Select a rip log to compare";
                    if (dialog.ShowDialog(new EacWindowOwner(cdTocOwnerWindow)) != DialogResult.OK)
                        return;
                    path = dialog.FileName;
                }

                string parseError;
                IList<IList<CdTocEntry>> tables = CdTocLogParser.ParseTables(
                    File.ReadAllText(path, Encoding.Default),
                    out parseError);
                if (tables.Count == 0)
                {
                    ShowTocComparisonMessage(
                        false,
                        "The TOCs could not be compared.\r\n\r\n" + parseError +
                        "\r\n\r\nLog: " + path);
                    return;
                }

                List<CdTocComparisonResult> results = new List<CdTocComparisonResult>();
                CdTocComparisonResult match = null;
                int matchedTable = -1;
                for (int i = 0; i < tables.Count; i++)
                {
                    CdTocComparisonResult result = CdTocComparer.Compare(
                        displayedCdTocEntries,
                        tables[i]);
                    results.Add(result);
                    if (result.IsMatch &&
                        (match == null || (!match.IsExact && result.IsExact)))
                    {
                        match = result;
                        matchedTable = i;
                    }
                }

                if (match != null)
                {
                    StringBuilder message = new StringBuilder();
                    message.Append(match.Reason);
                    if (tables.Count > 1)
                    {
                        message.Append("\r\n\r\nMatched TOC table ")
                            .Append(matchedTable + 1).Append(" of ").Append(tables.Count).Append('.');
                    }
                    AppendComparisonDetails(message, match);
                    message.Append("\r\n\r\n").Append(CurrentDiscPeakComparisonNotice);
                    message.Append("\r\n\r\nLog: ").Append(path);
                    ShowTocComparisonMessage(true, message.ToString());
                    return;
                }

                StringBuilder failure = new StringBuilder();
                failure.Append("The TOCs do not match.");
                if (results.Count == 1)
                {
                    failure.Append("\r\n\r\nReason: ").Append(results[0].Reason);
                    AppendComparisonDetails(failure, results[0]);
                }
                else
                {
                    failure.Append(" No matching TOC was found among ")
                        .Append(results.Count).Append(" tables in the selected log.");
                    for (int i = 0; i < results.Count; i++)
                    {
                        failure.Append("\r\n\r\nTable ").Append(i + 1)
                            .Append(": ").Append(results[i].Reason);
                    }
                }
                failure.Append("\r\n\r\n").Append(CurrentDiscPeakComparisonNotice);
                failure.Append("\r\n\r\nLog: ").Append(path);
                ShowTocComparisonMessage(false, failure.ToString());
            }
            catch (Exception error)
            {
                Log("Compare CD TOC failed: " + error);
                ShowTocComparisonMessage(
                    false,
                    "The TOCs could not be compared.\r\n\r\n" + error.Message);
            }
        }

        private static void CompareTwoLogs()
        {
            try
            {
                string[] paths;
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.CheckFileExists = true;
                    dialog.DefaultExt = "log";
                    dialog.Filter =
                        "Rip log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*";
                    dialog.FilterIndex = 1;
                    dialog.Multiselect = true;
                    dialog.RestoreDirectory = true;
                    dialog.Title = "Select two rip logs to compare";
                    if (dialog.ShowDialog(new EacWindowOwner(cdTocOwnerWindow)) != DialogResult.OK)
                        return;
                    paths = dialog.FileNames;
                }
                if (paths.Length == 1)
                {
                    using (OpenFileDialog secondDialog = new OpenFileDialog())
                    {
                        secondDialog.CheckFileExists = true;
                        secondDialog.DefaultExt = "log";
                        secondDialog.Filter =
                            "Rip log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*";
                        secondDialog.FilterIndex = 1;
                        secondDialog.InitialDirectory = Path.GetDirectoryName(paths[0]);
                        secondDialog.Multiselect = false;
                        secondDialog.RestoreDirectory = true;
                        secondDialog.Title = "Select the second rip log to compare";
                        if (secondDialog.ShowDialog(
                            new EacWindowOwner(cdTocOwnerWindow)) != DialogResult.OK)
                        {
                            return;
                        }
                        paths = new[] { paths[0], secondDialog.FileName };
                    }
                }
                if (paths.Length != 2)
                {
                    ShowTocComparisonMessage(
                        false,
                        "Select exactly two separate rip log files to compare.");
                    return;
                }
                if (String.Equals(paths[0], paths[1], StringComparison.OrdinalIgnoreCase))
                {
                    ShowTocComparisonMessage(
                        false,
                        "Select two different rip log files to compare.");
                    return;
                }

                IList<IList<CdTocEntry>> firstTables = ReadLogTocTables(paths[0]);
                IList<IList<CdTocEntry>> secondTables = ReadLogTocTables(paths[1]);
                CdTocReleaseComparisonResult result = CdTocReleaseComparer.Compare(
                    firstTables,
                    secondTables,
                    true);
                StringBuilder message = new StringBuilder();
                message.Append(result.IsMatch
                    ? result.Reason
                    : "The logs do not match.\r\n\r\nReason: " + result.Reason);
                if (!String.IsNullOrEmpty(result.PeakSummary))
                    message.Append("\r\n").Append(result.PeakSummary);
                AppendComparisonDetails(message, result);
                message.Append("\r\n\r\nFirst log: ").Append(paths[0])
                    .Append("\r\nSecond log: ").Append(paths[1]);
                ShowTocComparisonMessage(result.IsMatch, message.ToString());
            }
            catch (Exception error)
            {
                Log("Compare two log TOCs failed: " + error);
                ShowTocComparisonMessage(
                    false,
                    "The logs' TOCs could not be compared.\r\n\r\n" + error.Message);
            }
        }

        private static IList<IList<CdTocEntry>> ReadLogTocTables(string path)
        {
            string parseError;
            IList<IList<CdTocEntry>> tables = CdTocLogParser.ParseTables(
                File.ReadAllText(path, Encoding.Default),
                out parseError);
            if (tables.Count == 0)
            {
                throw new InvalidDataException(
                    Path.GetFileName(path) + ": " + parseError);
            }
            return tables;
        }

        private static void AppendComparisonDetails(
            StringBuilder message,
            CdTocComparisonResult result)
        {
            if (result.Details.Count == 0)
                return;
            message.Append("\r\n\r\nDetails:");
            foreach (string detail in result.Details)
                message.Append("\r\n- ").Append(detail);
        }

        private static void AppendComparisonDetails(
            StringBuilder message,
            CdTocReleaseComparisonResult result)
        {
            if (result.Details.Count == 0)
                return;
            message.Append("\r\n\r\nDetails:");
            foreach (string detail in result.Details)
                message.Append("\r\n- ").Append(detail);
        }

        private static void ShowTocComparisonMessage(bool match, string message)
        {
            MessageBox.Show(
                new EacWindowOwner(cdTocWindow != null ? cdTocWindow.Handle : cdTocOwnerWindow),
                message,
                "CD TOC Comparison",
                MessageBoxButtons.OK,
                match ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private static CdTocLabels LoadCurrentTocLabels(IntPtr mainWindow)
        {
            CdTocLabels fallback = new CdTocLabels();
            try
            {
                string toolsCaption = GetToolsMenuCaption(mainWindow);
                string languageDirectory = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Languages");
                if (!Directory.Exists(languageDirectory))
                    return fallback;

                foreach (string path in Directory.GetFiles(languageDirectory, "*.txt"))
                {
                    Dictionary<int, string> strings = ReadLanguageStrings(path);
                    string candidateTools;
                    if (!strings.TryGetValue(240000, out candidateTools) ||
                        !String.Equals(
                            NormalizeMenuCaption(candidateTools),
                            NormalizeMenuCaption(toolsCaption),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string value;
                    CdTocLabels labels = new CdTocLabels();
                    if (strings.TryGetValue(1289, out value)) labels.Title = value;
                    if (strings.TryGetValue(1290, out value)) labels.Track = value;
                    if (strings.TryGetValue(1291, out value)) labels.Start = value;
                    if (strings.TryGetValue(1292, out value)) labels.Length = value;
                    if (strings.TryGetValue(1293, out value)) labels.StartSector = value;
                    if (strings.TryGetValue(1294, out value)) labels.EndSector = value;
                    return labels;
                }
            }
            catch (Exception error)
            {
                Log("Could not load EAC's localized TOC captions: " + error.Message);
            }
            return fallback;
        }

        private static string GetToolsMenuCaption(IntPtr mainWindow)
        {
            IntPtr menu = NativeMethods.GetMenu(mainWindow);
            IntPtr toolsMenu = FindToolsMenu(menu);
            int count = NativeMethods.GetMenuItemCount(menu);
            StringBuilder caption = new StringBuilder(256);
            for (int position = 0; position < count; position++)
            {
                if (NativeMethods.GetSubMenu(menu, position) != toolsMenu)
                    continue;
                NativeMethods.GetMenuStringW(
                    menu,
                    (uint)position,
                    caption,
                    caption.Capacity,
                    NativeMethods.MF_BYPOSITION);
                return caption.ToString();
            }
            return "Tools";
        }

        internal static Dictionary<int, string> ReadLanguageStrings(string path)
        {
            Dictionary<int, string> values = new Dictionary<int, string>();
            Regex assignment = new Regex(
                "^\\s*(\\d+)\\s*=\\s*\"(.*)\"\\s*$",
                RegexOptions.CultureInvariant);
            foreach (string line in File.ReadAllLines(path, Encoding.Default))
            {
                Match match = assignment.Match(line);
                if (!match.Success)
                    continue;
                int id;
                if (Int32.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out id))
                {
                    values[id] = match.Groups[2].Value.Replace("\"\"", "\"");
                }
            }
            return values;
        }

        private static string NormalizeMenuCaption(string value)
        {
            return (value ?? String.Empty)
                .Replace("&", String.Empty)
                .Split('\t')[0]
                .Trim();
        }

        private sealed class EacWindowOwner : IWin32Window
        {
            private readonly IntPtr handle;

            internal EacWindowOwner(IntPtr handle)
            {
                this.handle = handle;
            }

            public IntPtr Handle
            {
                get { return handle; }
            }
        }
    }
}
