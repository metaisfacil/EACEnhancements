using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AudioDataPlugIn
{
    internal static partial class EnhancementRuntime
    {
        internal static string CurrentPluginSettingsVersion()
        {
            Version version = typeof(AudioDataTransfer).Assembly.GetName().Version;
            return version.ToString(version.Revision > 0 ? 4 : 3);
        }

        internal static bool MigrateSettingsFile(string iniPath)
        {
            if (!File.Exists(iniPath))
                return false;

            byte[] original = File.ReadAllBytes(iniPath);
            Encoding originalEncoding;
            string originalText = DecodeSettings(original, out originalEncoding);
            Dictionary<string, Dictionary<string, string>> before =
                ValidateSettingsText(originalText);

            string directory = Path.GetDirectoryName(Path.GetFullPath(iniPath));
            string stagedPath = Path.Combine(directory,
                ".EACEnhancements-" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                File.WriteAllBytes(stagedPath, original);
                MigrateSettingsFileInPlace(stagedPath);

                Encoding stagedEncoding;
                string migratedText = DecodeSettings(
                    File.ReadAllBytes(stagedPath), out stagedEncoding);
                Dictionary<string, Dictionary<string, string>> after =
                    ValidateSettingsText(migratedText);
                VerifyMigratedSettings(before, after);
                string finalText = StampSettingsVersion(
                    migratedText, CurrentPluginSettingsVersion());
                Dictionary<string, Dictionary<string, string>> stamped =
                    ValidateSettingsText(finalText);
                VerifyVersionStamp(after, stamped);
                byte[] finalBytes = EncodeSettings(finalText, originalEncoding);
                if (BytesEqual(original, finalBytes))
                    return false;

                File.WriteAllBytes(stagedPath, finalBytes);
                File.Replace(stagedPath, iniPath, null);
                return true;
            }
            finally
            {
                if (File.Exists(stagedPath))
                    File.Delete(stagedPath);
            }
        }

        private static Dictionary<string, Dictionary<string, string>>
            ValidateSettingsText(string text)
        {
            if (text.IndexOf('\0') >= 0)
                throw new InvalidDataException("The settings file contains a null character.");
            Dictionary<string, Dictionary<string, string>> sections =
                new Dictionary<string, Dictionary<string, string>>(
                    StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> current = null;
            using (StringReader reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#')
                        continue;
                    if (trimmed[0] == '[')
                    {
                        if (trimmed.Length < 3 || trimmed[trimmed.Length - 1] != ']')
                            throw new InvalidDataException("The settings file has an invalid section header.");
                        string section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        if (section.Length == 0 || sections.ContainsKey(section))
                            throw new InvalidDataException("The settings file has an empty or repeated section.");
                        current = new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase);
                        sections.Add(section, current);
                        continue;
                    }
                    int equals = trimmed.IndexOf('=');
                    if (current == null || equals <= 0)
                        throw new InvalidDataException("The settings file has a setting outside a section or without '='.");
                    string key = trimmed.Substring(0, equals).Trim();
                    if (key.Length == 0 || current.ContainsKey(key))
                        throw new InvalidDataException("The settings file has an empty or repeated key.");
                    string value = trimmed.Substring(equals + 1);
                    if (IniKeySections.ContainsKey(key) && value.Length >= 32767)
                        throw new InvalidDataException(
                            "The " + key + " setting is too long to migrate safely.");
                    current.Add(key, value);
                }
            }

            string root = GetValidatedSetting(sections, "Root");
            if (!String.IsNullOrWhiteSpace(root))
                NormalizeRootFolder(root);
            string template = GetValidatedSetting(sections, "FolderTemplate");
            if (template != null)
                NormalizeFolderTemplate(template);

            string[] booleanKeys =
            {
                "CreateWorkflowFolders", "ShowWorkflowSetupAlert",
                "ShowRipErrorAlert", AdditionalWorkflowsIniKey,
                IncreaseExternalCompressorArgumentsLimitIniKey,
                "EnableLogging", "WorkflowOnlyFolderTemplates"
            };
            foreach (string key in booleanKeys)
            {
                string value = GetValidatedSetting(sections, key);
                if (String.IsNullOrWhiteSpace(value))
                    continue;
                switch (value.Trim().ToLowerInvariant())
                {
                    case "0": case "1": case "true": case "false":
                    case "yes": case "no": case "on": case "off":
                        break;
                    default:
                        throw new InvalidDataException("The " + key + " setting is not a valid boolean.");
                }
            }

            Dictionary<string, string> header;
            string savedVersion;
            if (sections.TryGetValue("EACEnhancements", out header) &&
                header.TryGetValue("Version", out savedVersion) &&
                !String.IsNullOrWhiteSpace(savedVersion))
            {
                Version parsed;
                if (!Version.TryParse(savedVersion.Trim(), out parsed))
                    throw new InvalidDataException("The recorded settings version is invalid.");
                Version normalized = new Version(parsed.Major, parsed.Minor,
                    Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
                if (normalized.CompareTo(
                    typeof(AudioDataTransfer).Assembly.GetName().Version) > 0)
                    throw new InvalidDataException(
                        "The settings file was written by a newer EAC Enhancements version.");
            }
            return sections;
        }

        private static void VerifyMigratedSettings(
            IDictionary<string, Dictionary<string, string>> before,
            IDictionary<string, Dictionary<string, string>> after)
        {
            foreach (KeyValuePair<string, Dictionary<string, string>> section in before)
            {
                foreach (KeyValuePair<string, string> setting in section.Value)
                {
                    string destinationSection = null;
                    bool moved = String.Equals(section.Key,
                        LegacySettingsSection, StringComparison.OrdinalIgnoreCase) &&
                        IniKeySections.TryGetValue(setting.Key, out destinationSection);
                    if (moved)
                    {
                        Dictionary<string, string> oldSection;
                        if (after.TryGetValue(LegacySettingsSection, out oldSection) &&
                            oldSection.ContainsKey(setting.Key))
                            throw new InvalidDataException(
                                "The legacy " + setting.Key + " setting was not removed.");
                        Dictionary<string, string> originalDestination;
                        string expected = setting.Value;
                        string newerValue;
                        if (before.TryGetValue(destinationSection, out originalDestination) &&
                            originalDestination.TryGetValue(setting.Key, out newerValue))
                            expected = newerValue;
                        Dictionary<string, string> migratedDestination;
                        string actual;
                        if (!after.TryGetValue(destinationSection, out migratedDestination) ||
                            !migratedDestination.TryGetValue(setting.Key, out actual) ||
                            !String.Equals(actual, expected, StringComparison.Ordinal))
                            throw new InvalidDataException(
                                "The " + setting.Key + " setting changed during migration.");
                    }
                    else
                    {
                        Dictionary<string, string> migratedSection;
                        string actual;
                        if (!after.TryGetValue(section.Key, out migratedSection) ||
                            !migratedSection.TryGetValue(setting.Key, out actual) ||
                            !String.Equals(actual, setting.Value, StringComparison.Ordinal))
                            throw new InvalidDataException(
                                "The " + setting.Key + " setting changed during migration.");
                    }
                }
            }
        }

        private static void VerifyVersionStamp(
            IDictionary<string, Dictionary<string, string>> before,
            IDictionary<string, Dictionary<string, string>> after)
        {
            foreach (KeyValuePair<string, Dictionary<string, string>> section in before)
            {
                foreach (KeyValuePair<string, string> setting in section.Value)
                {
                    if (String.Equals(section.Key, "EACEnhancements",
                        StringComparison.OrdinalIgnoreCase) &&
                        String.Equals(setting.Key, "Version",
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    Dictionary<string, string> destination;
                    string value;
                    if (!after.TryGetValue(section.Key, out destination) ||
                        !destination.TryGetValue(setting.Key, out value) ||
                        !String.Equals(value, setting.Value, StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "The " + setting.Key + " setting changed while recording the plugin version.");
                }
            }
            Dictionary<string, string> header;
            string version;
            if (!after.TryGetValue("EACEnhancements", out header) ||
                !header.TryGetValue("Version", out version) ||
                !String.Equals(version, CurrentPluginSettingsVersion(),
                    StringComparison.Ordinal))
                throw new InvalidDataException("The plugin version was not recorded correctly.");
        }

        private static string GetValidatedSetting(
            IDictionary<string, Dictionary<string, string>> sections,
            string key)
        {
            string section;
            if (!IniKeySections.TryGetValue(key, out section))
                return null;
            Dictionary<string, string> values;
            string value;
            if (sections.TryGetValue(section, out values) &&
                values.TryGetValue(key, out value))
                return value;
            if (sections.TryGetValue(LegacySettingsSection, out values) &&
                values.TryGetValue(key, out value))
                return value;
            return null;
        }

        private static string StampSettingsVersion(string text, string version)
        {
            string newline = text.Contains("\r\n") ? "\r\n" :
                text.Contains("\n") ? "\n" : "\r\n";
            List<string> lines = new List<string>(Regex.Split(text, "\r\n|\n|\r"));
            int start = -1;
            int end = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("[", StringComparison.Ordinal) ||
                    !line.EndsWith("]", StringComparison.Ordinal))
                    continue;
                if (start >= 0)
                {
                    end = i;
                    break;
                }
                if (String.Equals(line, "[EACEnhancements]",
                    StringComparison.OrdinalIgnoreCase))
                    start = i;
            }
            string versionLine = "Version=" + version;
            if (start == 0 && lines.Count > 1 &&
                String.Equals(lines[1].Trim(), versionLine,
                    StringComparison.OrdinalIgnoreCase))
                return text;

            List<string> header = new List<string>();
            if (start >= 0)
            {
                header.Add(lines[start]);
                for (int i = start + 1; i < end; i++)
                {
                    string line = lines[i].Trim();
                    int equals = line.IndexOf('=');
                    if (equals < 0 || !String.Equals(
                        line.Substring(0, equals).Trim(), "Version",
                        StringComparison.OrdinalIgnoreCase))
                        header.Add(lines[i]);
                }
                lines.RemoveRange(start, end - start);
            }
            else
            {
                header.Add("[EACEnhancements]");
            }
            header.Insert(1, versionLine);
            while (header.Count > 2 && header[header.Count - 1].Length == 0)
                header.RemoveAt(header.Count - 1);
            while (lines.Count > 0 && lines[0].Length == 0)
                lines.RemoveAt(0);
            header.Add(String.Empty);
            header.AddRange(lines);
            return String.Join(newline, header.ToArray());
        }

        private static Encoding DetectSettingsEncoding(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(true);
            try
            {
                new UTF8Encoding(false, true).GetString(bytes);
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Default;
            }
        }

        private static string DecodeSettings(byte[] bytes, out Encoding encoding)
        {
            encoding = DetectSettingsEncoding(bytes);
            int preambleLength = encoding.GetPreamble().Length;
            return encoding.GetString(bytes, preambleLength,
                bytes.Length - preambleLength);
        }

        private static byte[] EncodeSettings(string text, Encoding encoding)
        {
            byte[] preamble = encoding.GetPreamble();
            byte[] content = encoding.GetBytes(text);
            byte[] bytes = new byte[preamble.Length + content.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(content, 0, bytes, preamble.Length, content.Length);
            return bytes;
        }

        private static bool BytesEqual(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
                return false;
            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] != second[i])
                    return false;
            }
            return true;
        }
    }
}
