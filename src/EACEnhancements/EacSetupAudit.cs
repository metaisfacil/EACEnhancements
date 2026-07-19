using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioDataPlugIn
{
    internal sealed class EacSetupAuditIssue
    {
        internal EacSetupAuditIssue(string section, string setting, string current, string required)
        {
            Section = section;
            Setting = setting;
            Current = current;
            Required = required;
        }

        internal string Section { get; private set; }
        internal string Setting { get; private set; }
        internal string Current { get; private set; }
        internal string Required { get; private set; }
    }

    internal sealed class EacSetupAuditResult
    {
        private readonly List<EacSetupAuditIssue> logScoreIssues =
            new List<EacSetupAuditIssue>();
        private readonly List<EacSetupAuditIssue> recommendations =
            new List<EacSetupAuditIssue>();

        internal IList<EacSetupAuditIssue> LogScoreIssues
        {
            get { return logScoreIssues.AsReadOnly(); }
        }

        internal IList<EacSetupAuditIssue> Recommendations
        {
            get { return recommendations.AsReadOnly(); }
        }

        internal bool Is100PercentLogCompliant
        {
            get { return logScoreIssues.Count == 0; }
        }

        internal void AddLogScoreIssue(
            string section,
            string setting,
            string current,
            string required)
        {
            logScoreIssues.Add(new EacSetupAuditIssue(section, setting, current, required));
        }

        internal void AddRecommendation(
            string section,
            string setting,
            string current,
            string required)
        {
            recommendations.Add(new EacSetupAuditIssue(section, setting, current, required));
        }
    }

    internal static class EacSetupAudit
    {
        private const string EacRoot = @"Software\AWSoftware\EACU";

        private enum IssueCategory
        {
            LogScore,
            Recommendation
        }

        internal static EacSetupAuditResult Run(IntPtr mainWindow)
        {
            // Base LogScore issues on the Orpheus/OPS deductions implemented by
            // Cambia, plus settings required for a complete, verifiable 100% log.
            // Recommendations remain useful guidance but do not gate the workflow.
            EacSetupAuditResult result = new EacSetupAuditResult();
            using (RegistryKey extraction = Registry.CurrentUser.OpenSubKey(EacRoot + @"\Extraction Options"))
            using (RegistryKey startup = Registry.CurrentUser.OpenSubKey(EacRoot + @"\StartUp Options"))
            using (RegistryKey compression = Registry.CurrentUser.OpenSubKey(EacRoot + @"\Compression Options"))
            {
                CheckEnabled(result, extraction, "EAC Options > Extraction", "Fill up missing offset samples with silence", "FillUpMissingSamples", true, IssueCategory.LogScore);
                CheckEnabled(result, extraction, "EAC Options > Extraction", "Synchronize between tracks", "SyncTrackJunctions", true, IssueCategory.Recommendation);
                CheckEnabled(result, extraction, "EAC Options > Extraction", "Delete leading and trailing silent blocks", "RemoveSilence", false, IssueCategory.LogScore);
                CheckInteger(result, extraction, "EAC Options > Extraction", "Error recovery quality", "NumberReads", 5, "High", IssueCategory.Recommendation);

                CheckEnabled(result, extraction, "EAC Options > General", "Automatically access online metadata for unknown CDs", "RetrieveCDDBOnUnknownCD", true, IssueCategory.Recommendation);
                CheckEnabled(result, startup, "EAC Options > General", "Create log files in English", "CreateEnglishLogFile", true, IssueCategory.Recommendation);

                CheckEnabled(result, extraction, "EAC Options > Tools", "Automatically write status report after extraction", "AutoSaveStatus", true, IssueCategory.LogScore);
                CheckEnabled(result, extraction, "EAC Options > Tools", "Append checksum to status report", "AddChecksumLogFile", true, IssueCategory.LogScore);
                CheckEnabled(result, extraction, "EAC Options > Tools", "Start external compressors queued in the background", "BackgroundExternalCompression", false, IssueCategory.Recommendation);
                CheckEnabled(result, startup, "EAC Options > Tools", "Beginner mode", "EasyGUI", false, IssueCategory.Recommendation);

                CheckEnabled(result, extraction, "EAC Options > Normalize", "Normalize", "Normalize", false, IssueCategory.LogScore);
                CheckEnabled(result, extraction, "EAC Options > Filename", "Use Various Artist naming scheme", "UseVariousFileNamingConvention", true, IssueCategory.Recommendation);

                CheckCompression(result, compression);
            }

            CheckSelectedDrive(result, mainWindow);
            return result;
        }

        private static void CheckCompression(EacSetupAuditResult result, RegistryKey key)
        {
            const string section = "Compression Options > External Compression";
            CheckEnabled(result, key, section, "Use external program for compression", "UseExternalEncoder", true, IssueCategory.Recommendation);
            CheckInteger(result, key, section, "Parameter passing scheme", "ExternalEncoderType", 20, "User Defined Encoder", IssueCategory.Recommendation);

            string extension = ReadString(key, "ExternalEncoderExtension");
            string normalizedExtension = (extension ?? String.Empty).Trim().TrimStart('.');
            if (!IsOrpheusAcceptedExtension(normalizedExtension))
            {
                result.AddLogScoreIssue(
                    section,
                    "File extension",
                    DisplayString(extension),
                    "A score-verifiable lossless extension: .flac, .wav, or .ape");
            }
            else if (!String.Equals(normalizedExtension, "flac", StringComparison.OrdinalIgnoreCase))
            {
                result.AddRecommendation(section, "File extension", DisplayString(extension), ".flac");
            }

            string encoder = ReadString(key, "ExternalEncoderProgram");
            if (!HasExecutableExtension(encoder))
                result.AddRecommendation(section, "External compressor", DisplayString(encoder), "A command-line compressor ending in .exe");

            CheckEnabled(result, key, section, "Delete WAV after compression", "ExternalEncoderDeleteSource", true, IssueCategory.Recommendation);
            CheckEnabled(result, key, section, "Use CRC check", "ExternalEncoderCreateCRC", true, IssueCategory.Recommendation);
            CheckEnabled(result, key, section, "Add ID3 tag", "ExternalEncoderID3Tag", false, IssueCategory.LogScore);
            CheckEnabled(result, key, section, "Check external program return code", "ExternalEncoderCheckReturnCode", true, IssueCategory.Recommendation);

            const string tagSection = "Compression Options > ID3 Tag";
            CheckEnabled(result, key, tagSection, "ID3 v1.1 tags", "UseID3V11", false, IssueCategory.Recommendation);
            CheckEnabled(result, key, tagSection, "ID3 v2 tags", "UseID3V2", false, IssueCategory.Recommendation);
            CheckEnabled(result, key, tagSection, "Write ID3 v1 tags", "WriteV1Tags", false, IssueCategory.Recommendation);
            CheckEnabled(result, key, tagSection, "Add cover to ID3 tag", "AddCoverToID3V2", false, IssueCategory.Recommendation);
            CheckEnabled(result, key, tagSection, "Write cover image into extraction folder", "WriteCoverToFolder", true, IssueCategory.Recommendation);
        }

        private static void CheckSelectedDrive(EacSetupAuditResult result, IntPtr mainWindow)
        {
            string displayedDrive = FindSelectedDriveText(mainWindow);
            using (RegistryKey drives = Registry.CurrentUser.OpenSubKey(EacRoot + @"\Drive Options"))
            {
                string driveKeyName = MatchDriveKey(drives, displayedDrive);
                if (driveKeyName == null)
                {
                    result.AddLogScoreIssue(
                        "Drive Options",
                        "Selected drive",
                        String.IsNullOrWhiteSpace(displayedDrive) ? "Could not identify the selected drive" : displayedDrive,
                        "A selected drive with saved Drive Options");
                    return;
                }

                using (RegistryKey drive = drives.OpenSubKey(driveKeyName))
                {
                    string section = "Drive Options (" + NormalizeWhitespace(driveKeyName) + ")";
                    CheckInteger(result, drive, section + " > Extraction Method", "Extraction mode", "ExtractionMode", 5, "Secure mode", IssueCategory.LogScore);
                    CheckInteger(result, drive, section + " > Extraction Method", "Accurate Stream and drive-cache features", "SecureMode", 3, "Both enabled", IssueCategory.LogScore);
                    CheckEnabled(result, drive, section + " > Extraction Method", "Use C2 error information", "UseC2Correction", false, IssueCategory.LogScore);

                    int? command = ReadInteger(drive, "ExtractionCommandSet");
                    if (!command.HasValue || command.Value == 0)
                        result.AddRecommendation(
                            section + " > Drive",
                            "Read command",
                            DisplayReadCommand(command),
                            "Autodetected for this drive");

                    CheckInteger(result, drive, section + " > Offset/Speed", "Speed selection", "SpeedSelection", -1, "Current", IssueCategory.Recommendation);
                    CheckEnabled(result, drive, section + " > Offset/Speed", "Allow speed reduction during extraction", "SpeedReduction", true, IssueCategory.Recommendation);
                    CheckEnabled(result, drive, section + " > Offset/Speed", "Use AccurateRip with this drive", "UseAccurateRip", true, IssueCategory.Recommendation);
                    CheckIntegerRange(
                        result,
                        drive,
                        section + " > Gap Detection",
                        "Gap retrieval method",
                        "GapDetectionMode",
                        0,
                        2,
                        "Detection Method A, B, or C (start with A)",
                        IssueCategory.Recommendation);
                    int? gapAccuracy = ReadInteger(drive, "GapDetectionAccuracy");
                    if (!gapAccuracy.HasValue || gapAccuracy.Value < 1 || gapAccuracy.Value > 2)
                    {
                        result.AddRecommendation(
                            section + " > Gap Detection",
                            "Detection accuracy",
                            DisplayGapDetectionAccuracy(gapAccuracy),
                            "Secure, or Accurate if Secure stalls");
                    }
                }
            }
        }

        private static string FindSelectedDriveText(IntPtr mainWindow)
        {
            const uint WmGetText = 0x000D;
            if (mainWindow == IntPtr.Zero || !NativeMethods.IsWindow(mainWindow))
                return null;

            string selected = null;
            NativeMethods.EnumChildProc callback = delegate(IntPtr hwnd, IntPtr ignored)
            {
                StringBuilder className = new StringBuilder(64);
                NativeMethods.GetClassNameW(hwnd, className, className.Capacity);
                string windowClass = className.ToString();
                bool isDriveSelectorClass =
                    windowClass.Equals("mycombo", StringComparison.OrdinalIgnoreCase) ||
                    windowClass.Equals("ComboBox", StringComparison.OrdinalIgnoreCase);
                if (!isDriveSelectorClass && NativeMethods.GetDlgCtrlID(hwnd) != 5)
                    return true;

                StringBuilder text = new StringBuilder(512);
                NativeMethods.SendMessageTextW(
                    hwnd,
                    WmGetText,
                    new IntPtr(text.Capacity),
                    text);
                string value = text.ToString();
                if (value.IndexOf("Adapter:", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    value.IndexOf("ID:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    selected = value;
                    return false;
                }
                return true;
            };
            NativeMethods.EnumChildWindows(mainWindow, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return selected;
        }

        private static string MatchDriveKey(RegistryKey drives, string displayedDrive)
        {
            if (drives == null || String.IsNullOrWhiteSpace(displayedDrive))
                return null;
            string normalizedDisplay = NormalizeWhitespace(displayedDrive);
            string best = null;
            foreach (string name in drives.GetSubKeyNames())
            {
                if (String.IsNullOrWhiteSpace(name))
                    continue;
                string normalizedName = NormalizeWhitespace(name);
                if (normalizedDisplay.StartsWith(normalizedName, StringComparison.OrdinalIgnoreCase) &&
                    (best == null || normalizedName.Length > NormalizeWhitespace(best).Length))
                    best = name;
            }
            return best;
        }

        private static void CheckEnabled(
            EacSetupAuditResult result,
            RegistryKey key,
            string section,
            string label,
            string valueName,
            bool expected,
            IssueCategory category)
        {
            int? value = ReadInteger(key, valueName);
            bool? actual = value.HasValue ? (bool?)(value.Value != 0) : null;
            if (!actual.HasValue || actual.Value != expected)
                AddIssue(result, category, section, label, DisplayBoolean(actual), expected ? "Enabled" : "Disabled");
        }

        private static void CheckInteger(
            EacSetupAuditResult result,
            RegistryKey key,
            string section,
            string label,
            string valueName,
            int expected,
            string expectedDisplay,
            IssueCategory category)
        {
            int? value = ReadInteger(key, valueName);
            if (!value.HasValue || value.Value != expected)
                AddIssue(result, category, section, label, DisplayInteger(value), expectedDisplay);
        }

        private static void CheckIntegerRange(
            EacSetupAuditResult result,
            RegistryKey key,
            string section,
            string label,
            string valueName,
            int minimum,
            int maximum,
            string expectedDisplay,
            IssueCategory category)
        {
            int? value = ReadInteger(key, valueName);
            if (!value.HasValue || value.Value < minimum || value.Value > maximum)
                AddIssue(result, category, section, label, DisplayInteger(value), expectedDisplay);
        }

        private static void AddIssue(
            EacSetupAuditResult result,
            IssueCategory category,
            string section,
            string setting,
            string current,
            string required)
        {
            if (category == IssueCategory.LogScore)
                result.AddLogScoreIssue(section, setting, current, required);
            else
                result.AddRecommendation(section, setting, current, required);
        }

        private static int? ReadInteger(RegistryKey key, string name)
        {
            if (key == null)
                return null;
            object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is int)
                return (int)value;
            byte[] bytes = value as byte[];
            if (bytes == null || bytes.Length == 0)
                return null;
            if (bytes.Length >= 4)
                return BitConverter.ToInt32(bytes, 0);
            if (bytes.Length >= 2)
                return BitConverter.ToInt16(bytes, 0);
            return (sbyte)bytes[0];
        }

        private static string ReadString(RegistryKey key, string name)
        {
            return key == null ? null : key.GetValue(name, null) as string;
        }

        private static string DisplayBoolean(bool? value)
        {
            return value.HasValue ? (value.Value ? "Enabled" : "Disabled") : "Not configured";
        }

        private static string DisplayInteger(int? value)
        {
            return value.HasValue ? value.Value.ToString() : "Not configured";
        }

        internal static string DisplayReadCommand(int? value)
        {
            if (!value.HasValue)
                return "Not configured";
            if (value.Value == 0)
                return "Not autodetected";
            return "Command set " + value.Value;
        }

        internal static string DisplayGapDetectionAccuracy(int? value)
        {
            if (!value.HasValue)
                return "Not configured";

            switch (value.Value)
            {
                case 0:
                    return "Inaccurate";
                case 1:
                    return "Accurate";
                case 2:
                    return "Secure";
                default:
                    return "Unknown value (" + value.Value + ")";
            }
        }

        private static string DisplayString(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "Not configured" : value;
        }

        internal static bool HasExecutableExtension(string value)
        {
            return !String.IsNullOrWhiteSpace(value) &&
                value.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsOrpheusAcceptedExtension(string value)
        {
            string extension = (value ?? String.Empty).Trim().TrimStart('.');
            return String.Equals(extension, "flac", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(extension, "wav", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(extension, "ape", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeWhitespace(string value)
        {
            return Regex.Replace((value ?? String.Empty).Trim(), "\\s+", " ");
        }
    }

    internal sealed class EacSetupAuditDialog : Form
    {
        internal EacSetupAuditDialog(EacSetupAuditResult result)
        {
            Text = "Rip Configuration Check";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            MinimumSize = new Size(680, 520);
            ClientSize = new Size(780, 640);
            Font = SystemFonts.MessageBoxFont;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 6
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

            Label logScoreHeading = new Label
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.TopLeft,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Text = result.Is100PercentLogCompliant
                    ? "All checked settings that influence 100% log score are configured correctly."
                    : "The following required settings do not match the 100% log guide:"
            };

            TextBox logScoreReport = CreateReportTextBox(
                FormatReport(
                    result.LogScoreIssues,
                    "No 100% log problems were found.",
                    "Required"),
                1);
            logScoreReport.Margin = new Padding(0, 0, 0, 12);

            Label recommendationHeading = new Label
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.TopLeft,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Text = "The following settings do not influence log score, but changing them is highly recommended:"
            };

            TextBox recommendationReport = CreateReportTextBox(
                FormatReport(
                    result.Recommendations,
                    "No additional recommendations were found.",
                    "Suggested"),
                2);
            recommendationReport.Margin = new Padding(0, 0, 0, 8);

            Label footer = new Label
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "No settings were changed."
            };

            Button close = new Button
            {
                Size = new Size(75, 28),
                Margin = Padding.Empty,
                Text = "Close",
                DialogResult = DialogResult.OK,
                TabIndex = 0,
                UseVisualStyleBackColor = true
            };

            FlowLayoutPanel buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            buttonRow.Controls.Add(close);

            layout.Controls.Add(logScoreHeading, 0, 0);
            layout.Controls.Add(logScoreReport, 0, 1);
            layout.Controls.Add(recommendationHeading, 0, 2);
            layout.Controls.Add(recommendationReport, 0, 3);
            layout.Controls.Add(footer, 0, 4);
            layout.Controls.Add(buttonRow, 0, 5);
            Controls.Add(layout);
            AcceptButton = close;
            CancelButton = close;
            ActiveControl = close;
        }

        private static TextBox CreateReportTextBox(string text, int tabIndex)
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = true,
                TabIndex = tabIndex,
                Text = text
            };
        }

        private static string FormatReport(
            IList<EacSetupAuditIssue> issues,
            string emptyMessage,
            string targetLabel)
        {
            if (issues.Count == 0)
                return emptyMessage;

            StringBuilder text = new StringBuilder();
            string section = null;
            foreach (EacSetupAuditIssue issue in issues)
            {
                if (!String.Equals(section, issue.Section, StringComparison.Ordinal))
                {
                    if (text.Length > 0)
                        text.AppendLine();
                    section = issue.Section;
                    text.AppendLine(section);
                }
                text.AppendLine("  " + issue.Setting);
                text.AppendLine("    Current:  " + issue.Current);
                text.AppendLine("    " + targetLabel + ": " + issue.Required);
            }
            return text.ToString().TrimEnd();
        }
    }
}
