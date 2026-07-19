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
        private readonly List<EacSetupAuditIssue> issues = new List<EacSetupAuditIssue>();

        internal IList<EacSetupAuditIssue> Issues
        {
            get { return issues.AsReadOnly(); }
        }

        internal bool IsCompliant
        {
            get { return issues.Count == 0; }
        }

        internal void Add(string section, string setting, string current, string required)
        {
            issues.Add(new EacSetupAuditIssue(section, setting, current, required));
        }
    }

    internal static class EacSetupAudit
    {
        private const string EacRoot = @"Software\AWSoftware\EACU";

        internal static EacSetupAuditResult Run(IntPtr mainWindow)
        {
            EacSetupAuditResult result = new EacSetupAuditResult();
            using (RegistryKey extraction = Registry.CurrentUser.OpenSubKey(EacRoot + @"\Extraction Options"))
            using (RegistryKey startup = Registry.CurrentUser.OpenSubKey(EacRoot + @"\StartUp Options"))
            using (RegistryKey compression = Registry.CurrentUser.OpenSubKey(EacRoot + @"\Compression Options"))
            {
                CheckEnabled(result, extraction, "EAC Options > Extraction", "Fill up missing offset samples with silence", "FillUpMissingSamples", true);
                CheckEnabled(result, extraction, "EAC Options > Extraction", "Synchronize between tracks", "SyncTrackJunctions", true);
                CheckEnabled(result, extraction, "EAC Options > Extraction", "Delete leading and trailing silent blocks", "RemoveSilence", false);
                CheckInteger(result, extraction, "EAC Options > Extraction", "Error recovery quality", "NumberReads", 5, "High");

                CheckEnabled(result, extraction, "EAC Options > General", "Automatically access online metadata for unknown CDs", "RetrieveCDDBOnUnknownCD", true);
                CheckEnabled(result, startup, "EAC Options > General", "Create log files in English", "CreateEnglishLogFile", true);

                CheckEnabled(result, extraction, "EAC Options > Tools", "Automatically write status report after extraction", "AutoSaveStatus", true);
                CheckEnabled(result, extraction, "EAC Options > Tools", "Append checksum to status report", "AddChecksumLogFile", true);
                CheckEnabled(result, extraction, "EAC Options > Tools", "Start external compressors queued in the background", "BackgroundExternalCompression", false);
                CheckEnabled(result, startup, "EAC Options > Tools", "Beginner mode", "EasyGUI", false);

                CheckEnabled(result, extraction, "EAC Options > Normalize", "Normalize", "Normalize", false);
                CheckEnabled(result, extraction, "EAC Options > Filename", "Use Various Artist naming scheme", "UseVariousFileNamingConvention", true);

                CheckCompression(result, compression);
            }

            CheckSelectedDrive(result, mainWindow);
            return result;
        }

        private static void CheckCompression(EacSetupAuditResult result, RegistryKey key)
        {
            const string section = "Compression Options > External Compression";
            CheckEnabled(result, key, section, "Use external program for compression", "UseExternalEncoder", true);
            CheckInteger(result, key, section, "Parameter passing scheme", "ExternalEncoderType", 20, "User Defined Encoder");

            string extension = ReadString(key, "ExternalEncoderExtension");
            if (!String.Equals((extension ?? String.Empty).Trim().TrimStart('.'), "flac", StringComparison.OrdinalIgnoreCase))
                result.Add(section, "File extension", DisplayString(extension), ".flac");

            string encoder = ReadString(key, "ExternalEncoderProgram");
            if (!HasExecutableExtension(encoder))
                result.Add(section, "External compressor", DisplayString(encoder), "A command-line compressor ending in .exe");

            CheckEnabled(result, key, section, "Delete WAV after compression", "ExternalEncoderDeleteSource", true);
            CheckEnabled(result, key, section, "Use CRC check", "ExternalEncoderCreateCRC", true);
            CheckEnabled(result, key, section, "Add ID3 tag", "ExternalEncoderID3Tag", false);
            CheckEnabled(result, key, section, "Check external program return code", "ExternalEncoderCheckReturnCode", true);

            const string tagSection = "Compression Options > ID3 Tag";
            CheckEnabled(result, key, tagSection, "ID3 v1.1 tags", "UseID3V11", false);
            CheckEnabled(result, key, tagSection, "ID3 v2 tags", "UseID3V2", false);
            CheckEnabled(result, key, tagSection, "Write ID3 v1 tags", "WriteV1Tags", false);
            CheckEnabled(result, key, tagSection, "Add cover to ID3 tag", "AddCoverToID3V2", false);
            CheckEnabled(result, key, tagSection, "Write cover image into extraction folder", "WriteCoverToFolder", true);
        }

        private static void CheckSelectedDrive(EacSetupAuditResult result, IntPtr mainWindow)
        {
            string displayedDrive = FindSelectedDriveText(mainWindow);
            using (RegistryKey drives = Registry.CurrentUser.OpenSubKey(EacRoot + @"\Drive Options"))
            {
                string driveKeyName = MatchDriveKey(drives, displayedDrive);
                if (driveKeyName == null)
                {
                    result.Add(
                        "Drive Options",
                        "Selected drive",
                        String.IsNullOrWhiteSpace(displayedDrive) ? "Could not identify the selected drive" : displayedDrive,
                        "A selected drive with saved Drive Options");
                    return;
                }

                using (RegistryKey drive = drives.OpenSubKey(driveKeyName))
                {
                    string section = "Drive Options (" + NormalizeWhitespace(driveKeyName) + ")";
                    CheckInteger(result, drive, section + " > Extraction Method", "Extraction mode", "ExtractionMode", 5, "Secure mode");
                    CheckInteger(result, drive, section + " > Extraction Method", "Accurate Stream and drive-cache features", "SecureMode", 3, "Both enabled");
                    CheckEnabled(result, drive, section + " > Extraction Method", "Use C2 error information", "UseC2Correction", false);

                    int? command = ReadInteger(drive, "ExtractionCommandSet");
                    if (!command.HasValue || command.Value == 0)
                        result.Add(section + " > Drive", "Read command", DisplayInteger(command), "Autodetected for this drive");

                    CheckInteger(result, drive, section + " > Offset/Speed", "Speed selection", "SpeedSelection", -1, "Current");
                    CheckEnabled(result, drive, section + " > Offset/Speed", "Allow speed reduction during extraction", "SpeedReduction", true);
                    CheckEnabled(result, drive, section + " > Offset/Speed", "Use AccurateRip with this drive", "UseAccurateRip", true);
                    CheckIntegerRange(
                        result,
                        drive,
                        section + " > Gap Detection",
                        "Gap retrieval method",
                        "GapDetectionMode",
                        0,
                        2,
                        "Detection Method A, B, or C (start with A)");
                    CheckIntegerRange(
                        result,
                        drive,
                        section + " > Gap Detection",
                        "Detection accuracy",
                        "GapDetectionAccuracy",
                        1,
                        2,
                        "Secure, or Accurate if Secure stalls");
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
            bool expected)
        {
            int? value = ReadInteger(key, valueName);
            bool? actual = value.HasValue ? (bool?)(value.Value != 0) : null;
            if (!actual.HasValue || actual.Value != expected)
                result.Add(section, label, DisplayBoolean(actual), expected ? "Enabled" : "Disabled");
        }

        private static void CheckInteger(
            EacSetupAuditResult result,
            RegistryKey key,
            string section,
            string label,
            string valueName,
            int expected,
            string expectedDisplay)
        {
            int? value = ReadInteger(key, valueName);
            if (!value.HasValue || value.Value != expected)
                result.Add(section, label, DisplayInteger(value), expectedDisplay);
        }

        private static void CheckIntegerRange(
            EacSetupAuditResult result,
            RegistryKey key,
            string section,
            string label,
            string valueName,
            int minimum,
            int maximum,
            string expectedDisplay)
        {
            int? value = ReadInteger(key, valueName);
            if (!value.HasValue || value.Value < minimum || value.Value > maximum)
                result.Add(section, label, DisplayInteger(value), expectedDisplay);
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

        private static string DisplayString(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "Not configured" : value;
        }

        internal static bool HasExecutableExtension(string value)
        {
            return !String.IsNullOrWhiteSpace(value) &&
                value.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
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
            Text = "100% Log Setup Check";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            MinimumSize = new Size(680, 420);
            ClientSize = new Size(760, 540);
            Font = SystemFonts.MessageBoxFont;

            Label heading = new Label
            {
                AutoSize = false,
                Location = new Point(16, 16),
                Size = new Size(728, 42),
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Text = result.IsCompliant
                    ? "All required settings checked by EAC Enhancements are configured correctly."
                    : "The following required settings do not match the 100% log guide:"
            };

            TextBox report = new TextBox
            {
                Location = new Point(16, 64),
                Size = new Size(728, 424),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = true,
                TabIndex = 1,
                Text = FormatReport(result)
            };

            Button close = new Button
            {
                Location = new Point(669, 500),
                Size = new Size(75, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Text = "Close",
                DialogResult = DialogResult.OK,
                TabIndex = 0,
                UseVisualStyleBackColor = true
            };

            Controls.Add(heading);
            Controls.Add(report);
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
            ActiveControl = close;
        }

        private static string FormatReport(EacSetupAuditResult result)
        {
            if (result.IsCompliant)
                return "No problems were found.";

            StringBuilder text = new StringBuilder();
            string section = null;
            foreach (EacSetupAuditIssue issue in result.Issues)
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
                text.AppendLine("    Required: " + issue.Required);
            }
            text.AppendLine();
            text.Append("No settings were changed.");
            return text.ToString();
        }
    }
}
