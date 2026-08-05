using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AudioDataPlugIn
{
    internal static partial class EnhancementRuntime
    {
        private const int EacProfileNameCapacity = 64;
        private const int EacProfilesPathCapacity = 512;
        private const string EacDriveOptionsRegistryPath =
            @"Software\AWSoftware\EACU\Drive Options";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void LiveSettingsSaveDelegate();

        internal static EacVersionLayout CurrentEacLayout
        {
            get { return layout; }
        }

        internal static IntPtr ResolveEacAddress(uint staticVa)
        {
            return AddressFromStaticVa(staticVa);
        }

        internal static string TryGetActiveEacProfilePath()
        {
            try
            {
                if (layout == null || imageBase == IntPtr.Zero)
                    return null;

                string profileName = TryGetActiveEacProfileName();
                if (String.IsNullOrWhiteSpace(profileName))
                    return null;

                string profilesPath = ReadFixedUnicodeString(
                    AddressFromStaticVa(layout.ProfilesPathVa),
                    EacProfilesPathCapacity);
                foreach (string candidate in ProfilePathCandidates(profileName, profilesPath))
                {
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }

                Log(
                    "Active EAC profile '" + profileName +
                    "' could not be found in the configured profile locations.");
            }
            catch (Exception error)
            {
                Log("Active EAC profile could not be resolved: " + error.Message);
            }
            return null;
        }

        internal static void SaveActiveProfileSettingsToRegistry()
        {
            if (String.IsNullOrWhiteSpace(TryGetActiveEacProfileName()))
                return;

            List<DetectedReadCommand> detectedReadCommands =
                CaptureDetectedReadCommands();
            LiveSettingsSaveDelegate save =
                (LiveSettingsSaveDelegate)Marshal.GetDelegateForFunctionPointer(
                    AddressFromStaticVa(layout.LiveSettingsSaveVa),
                    typeof(LiveSettingsSaveDelegate));
            save();
            int restored = RestoreDetectedReadCommands(detectedReadCommands);
            Log(
                "Active EAC profile settings synchronized before updating live output settings; " +
                "preservedReadCommands=" + restored + ".");
        }

        private sealed class DetectedReadCommand
        {
            internal string DriveName;
            internal object Value;
            internal RegistryValueKind Kind;
            internal int Command;
        }

        private static List<DetectedReadCommand> CaptureDetectedReadCommands()
        {
            List<DetectedReadCommand> result = new List<DetectedReadCommand>();
            using (RegistryKey root = Registry.CurrentUser.OpenSubKey(
                EacDriveOptionsRegistryPath))
            {
                if (root == null)
                    return result;
                foreach (string driveName in root.GetSubKeyNames())
                {
                    using (RegistryKey drive = root.OpenSubKey(driveName))
                    {
                        if (drive == null)
                            continue;
                        object value = drive.GetValue(
                            "ExtractionCommandSet",
                            null,
                            RegistryValueOptions.DoNotExpandEnvironmentNames);
                        int? command = ReadRegistryInteger(value);
                        if (!command.HasValue || command.Value <= 0)
                            continue;
                        byte[] bytes = value as byte[];
                        result.Add(new DetectedReadCommand
                        {
                            DriveName = driveName,
                            Value = bytes == null ? value : (byte[])bytes.Clone(),
                            Kind = drive.GetValueKind("ExtractionCommandSet"),
                            Command = command.Value
                        });
                    }
                }
            }
            return result;
        }

        private static int RestoreDetectedReadCommands(
            IEnumerable<DetectedReadCommand> snapshots)
        {
            int restored = 0;
            using (RegistryKey root = Registry.CurrentUser.OpenSubKey(
                EacDriveOptionsRegistryPath,
                true))
            {
                if (root == null)
                    return restored;
                foreach (DetectedReadCommand snapshot in snapshots)
                {
                    using (RegistryKey drive = root.OpenSubKey(
                        snapshot.DriveName,
                        true))
                    {
                        if (drive == null)
                            continue;
                        int? current = ReadRegistryInteger(drive.GetValue(
                            "ExtractionCommandSet",
                            null,
                            RegistryValueOptions.DoNotExpandEnvironmentNames));
                        if (!ShouldRestoreDetectedReadCommand(
                            snapshot.Command,
                            current))
                        {
                            continue;
                        }
                        drive.SetValue(
                            "ExtractionCommandSet",
                            snapshot.Value,
                            snapshot.Kind);
                        restored++;
                    }
                }
            }
            return restored;
        }

        internal static bool ShouldRestoreDetectedReadCommand(
            int previous,
            int? current)
        {
            return previous > 0 && (!current.HasValue || current.Value == 0);
        }

        private static int? ReadRegistryInteger(object value)
        {
            byte[] bytes = value as byte[];
            if (bytes != null)
            {
                if (bytes.Length >= sizeof(int))
                    return BitConverter.ToInt32(bytes, 0);
                if (bytes.Length >= sizeof(short))
                    return (int)BitConverter.ToUInt16(bytes, 0);
                if (bytes.Length == 1)
                    return (int)bytes[0];
                return null;
            }
            if (value is int)
                return (int)value;
            if (value is long)
                return unchecked((int)(long)value);
            return null;
        }

        private static string TryGetActiveEacProfileName()
        {
            if (layout == null || imageBase == IntPtr.Zero)
                return null;
            return ReadFixedUnicodeString(
                AddressFromStaticVa(layout.ActiveProfileNameVa),
                EacProfileNameCapacity);
        }

        private static string ReadFixedUnicodeString(IntPtr address, int capacity)
        {
            string value = Marshal.PtrToStringUni(address, capacity) ?? String.Empty;
            int terminator = value.IndexOf('\0');
            return (terminator >= 0 ? value.Substring(0, terminator) : value).Trim();
        }

        private static IEnumerable<string> ProfilePathCandidates(
            string profileName,
            string profilesPath)
        {
            HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string name = Environment.ExpandEnvironmentVariables(profileName.Trim());
            string[] names = name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)
                ? new[] { name }
                : new[] { name, name + ".cfg" };

            foreach (string candidateName in names)
            {
                if (Path.IsPathRooted(candidateName) && candidates.Add(candidateName))
                    yield return candidateName;
            }

            List<string> directories = new List<string>();
            AddProfileDirectory(directories, profilesPath);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            AddProfileDirectory(directories, Path.Combine(appData, "EAC", "Profiles"));
            AddProfileDirectory(directories, Path.Combine(appData, "Exact Audio Copy", "Profiles"));
            AddProfileDirectory(directories, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles"));

            foreach (string directory in directories)
            {
                foreach (string candidateName in names)
                {
                    string fileName = Path.GetFileName(candidateName);
                    string candidate = Path.Combine(directory, fileName);
                    if (candidates.Add(candidate))
                        yield return candidate;
                }
            }
        }

        private static void AddProfileDirectory(List<string> directories, string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return;
            string expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            if (!directories.Contains(expanded))
                directories.Add(expanded);
        }
    }

    internal sealed class EacProfileSettings
    {
        private static readonly Regex ValueLine = new Regex(
            "^\"((?:\\\\.|[^\"])*)\"=(.*)$",
            RegexOptions.Compiled);
        private readonly Dictionary<string, Dictionary<string, object>> sections =
            new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        internal static EacProfileSettings Load(string path)
        {
            return Parse(File.ReadAllText(path));
        }

        internal static bool IsBinary(string path)
        {
            byte[] header = new byte[16];
            using (FileStream stream = File.OpenRead(path))
            {
                if (stream.Read(header, 0, header.Length) != header.Length)
                    return false;
            }
            return Encoding.Unicode.GetString(header) == "EACV1300";
        }

        internal static EacProfileSettings Parse(string text)
        {
            EacProfileSettings result = new EacProfileSettings();
            string currentSection = null;
            foreach (string logicalLine in LogicalLines(text ?? String.Empty))
            {
                string line = logicalLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) &&
                    line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = NormalizeSection(line.Substring(1, line.Length - 2));
                    continue;
                }
                if (currentSection == null || line.Length == 0 || line[0] == ';')
                    continue;

                Match match = ValueLine.Match(line);
                if (!match.Success)
                    continue;
                object value;
                if (!TryParseValue(match.Groups[2].Value.Trim(), out value))
                    continue;
                result.Set(currentSection, Unescape(match.Groups[1].Value), value);
            }
            return result;
        }

        internal bool TryGetValue(string section, string name, out object value)
        {
            Dictionary<string, object> values;
            if (sections.TryGetValue(section, out values) && values.TryGetValue(name, out value))
                return true;
            value = null;
            return false;
        }

        internal IEnumerable<string> GetSubKeyNames(string section)
        {
            string prefix = section.TrimEnd('\\') + "\\";
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in sections.Keys)
            {
                if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                string remainder = candidate.Substring(prefix.Length);
                int separator = remainder.IndexOf('\\');
                names.Add(separator < 0 ? remainder : remainder.Substring(0, separator));
            }
            return names;
        }

        private void Set(string section, string name, object value)
        {
            Dictionary<string, object> values;
            if (!sections.TryGetValue(section, out values))
            {
                values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                sections.Add(section, values);
            }
            values[name] = value;
        }

        private static IEnumerable<string> LogicalLines(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder current = new StringBuilder();
            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd();
                bool continued = line.EndsWith("\\", StringComparison.Ordinal);
                if (continued)
                    line = line.Substring(0, line.Length - 1);
                current.Append(line);
                if (continued)
                    continue;
                yield return current.ToString();
                current.Length = 0;
            }
            if (current.Length > 0)
                yield return current.ToString();
        }

        private static string NormalizeSection(string section)
        {
            string normalized = section.Trim();
            int software = normalized.IndexOf("\\Software\\AWSoftware\\", StringComparison.OrdinalIgnoreCase);
            if (software >= 0)
            {
                int rootEnd = normalized.IndexOf('\\', software + "\\Software\\AWSoftware\\".Length);
                return rootEnd < 0 ? String.Empty : normalized.Substring(rootEnd + 1);
            }
            return normalized.TrimStart('\\');
        }

        private static bool TryParseValue(string serialized, out object value)
        {
            if (serialized.Length >= 2 && serialized[0] == '"' && serialized[serialized.Length - 1] == '"')
            {
                value = Unescape(serialized.Substring(1, serialized.Length - 2));
                return true;
            }
            if (serialized.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
            {
                uint parsed;
                if (UInt32.TryParse(serialized.Substring(6), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    value = unchecked((int)parsed);
                    return true;
                }
            }
            int colon = serialized.IndexOf(':');
            string kind = colon < 0 ? String.Empty : serialized.Substring(0, colon);
            if (kind.Equals("hex", StringComparison.OrdinalIgnoreCase) ||
                kind.StartsWith("hex(", StringComparison.OrdinalIgnoreCase))
            {
                string[] bytes = serialized.Substring(colon + 1).Split(',');
                List<byte> parsed = new List<byte>();
                foreach (string item in bytes)
                {
                    byte octet;
                    if (!Byte.TryParse(item.Trim(), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out octet))
                    {
                        value = null;
                        return false;
                    }
                    parsed.Add(octet);
                }
                byte[] data = parsed.ToArray();
                if ((kind.Equals("hex(1)", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("hex(2)", StringComparison.OrdinalIgnoreCase)) &&
                    data.Length % sizeof(char) == 0)
                {
                    value = Encoding.Unicode.GetString(data).TrimEnd('\0');
                    return true;
                }
                value = data;
                return true;
            }
            value = null;
            return false;
        }

        private static string Unescape(string value)
        {
            return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }

    internal sealed class EacSettingsSource
    {
        private const string EacRoot = @"Software\AWSoftware\EACU";
        private readonly EacProfileSettings profile;
        private readonly EacLiveSettings live;

        private EacSettingsSource(EacProfileSettings profile, EacLiveSettings live)
        {
            this.profile = profile;
            this.live = live;
        }

        internal static EacSettingsSource Create(int? selectedDriveIndex)
        {
            string profilePath = EnhancementRuntime.TryGetActiveEacProfilePath();
            if (profilePath == null)
                return new EacSettingsSource(null, null);
            try
            {
                EacProfileSettings settings = EacProfileSettings.IsBinary(profilePath)
                    ? null
                    : EacProfileSettings.Load(profilePath);
                EnhancementRuntime.Log(
                    "Rip configuration audit is using EAC's live settings for active profile '" +
                    profilePath + "'.");
                return new EacSettingsSource(
                    settings,
                    EacLiveSettings.Create(selectedDriveIndex));
            }
            catch (Exception error)
            {
                EnhancementRuntime.Log("Active EAC profile could not be read for the setup audit: " + error.Message);
                return new EacSettingsSource(null, null);
            }
        }

        internal object GetValue(string section, string name)
        {
            object value;
            if (section.StartsWith("Drive Options\\", StringComparison.OrdinalIgnoreCase) &&
                name.Equals("ExtractionCommandSet", StringComparison.OrdinalIgnoreCase))
            {
                object liveValue = null;
                object profileValue = null;
                object registryValue;
                if (live != null)
                    live.TryGetValue(section, name, out liveValue);
                if (profile != null)
                    profile.TryGetValue(section, name, out profileValue);
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    EacRoot + "\\" + section))
                {
                    registryValue = key == null
                        ? null
                        : key.GetValue(
                            name,
                            null,
                            RegistryValueOptions.DoNotExpandEnvironmentNames);
                }
                object selected = SelectExtractionCommandSet(
                    liveValue,
                    profileValue,
                    registryValue);
                if (ReadInteger(liveValue) == 0 && ReadInteger(selected) > 0)
                {
                    EnhancementRuntime.Log(
                        "Rip configuration audit ignored transient live read command 0 " +
                        "for '" + section + "' in favor of persisted command " +
                        ReadInteger(selected) + ".");
                }
                return selected;
            }
            if (live != null && live.TryGetValue(section, name, out value))
                return value;
            if (profile != null && profile.TryGetValue(section, name, out value))
                return value;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(EacRoot + "\\" + section))
                return key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        internal static object SelectExtractionCommandSet(
            object liveValue,
            object profileValue,
            object registryValue)
        {
            if (ReadInteger(liveValue) > 0)
                return liveValue;
            if (ReadInteger(profileValue) > 0)
                return profileValue;
            if (ReadInteger(registryValue) > 0)
                return registryValue;
            return liveValue ?? profileValue ?? registryValue;
        }

        private static int? ReadInteger(object value)
        {
            byte[] bytes = value as byte[];
            if (bytes != null)
            {
                if (bytes.Length >= sizeof(int))
                    return BitConverter.ToInt32(bytes, 0);
                if (bytes.Length >= sizeof(short))
                    return (int)BitConverter.ToUInt16(bytes, 0);
                return bytes.Length == 1 ? (int?)bytes[0] : null;
            }
            if (value is int)
                return (int)value;
            if (value is long)
                return unchecked((int)(long)value);
            return null;
        }

        internal IEnumerable<string> GetSubKeyNames(string section)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profile != null)
            {
                foreach (string name in profile.GetSubKeyNames(section))
                    names.Add(name);
            }
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(EacRoot + "\\" + section))
            {
                if (key != null)
                {
                    foreach (string name in key.GetSubKeyNames())
                        names.Add(name);
                }
            }
            return names;
        }
    }

    internal sealed class EacLiveSettings
    {
        private enum ValueKind
        {
            Byte,
            Int16,
            Int32,
            Unicode,
            NonZeroInt16,
            NonZeroInt32,
            ZeroInt16,
            LowNibble,
            HighNibble
        }

        private sealed class Field
        {
            internal Field(uint address, ValueKind kind, int stride, int capacity)
            {
                Address = address;
                Kind = kind;
                Stride = stride;
                Capacity = capacity;
            }

            internal uint Address;
            internal ValueKind Kind;
            internal int Stride;
            internal int Capacity;
        }

        private readonly Dictionary<string, Field> fields;
        private readonly int? selectedDriveIndex;

        private EacLiveSettings(Dictionary<string, Field> fields, int? selectedDriveIndex)
        {
            this.fields = fields;
            this.selectedDriveIndex = selectedDriveIndex;
        }

        internal static EacLiveSettings Create(int? selectedDriveIndex)
        {
            if (EnhancementRuntime.CurrentEacLayout == null)
                return null;
            bool is18 = EnhancementRuntime.CurrentEacLayout.Name == "EAC 1.8";
            return new EacLiveSettings(CreateFields(is18), selectedDriveIndex);
        }

        internal bool TryGetValue(string section, string name, out object value)
        {
            value = null;
            bool drive = section.StartsWith("Drive Options\\", StringComparison.OrdinalIgnoreCase);
            int index = 0;
            if (drive)
            {
                if (!selectedDriveIndex.HasValue || selectedDriveIndex.Value < 0 || selectedDriveIndex.Value >= 32)
                    return false;
                index = selectedDriveIndex.Value;
            }

            Field field;
            string key = (drive ? "Drive Options" : section) + "\\" + name;
            if (!fields.TryGetValue(key, out field))
                return false;
            try
            {
                IntPtr address = EnhancementRuntime.ResolveEacAddress(
                    field.Address + unchecked((uint)(index * field.Stride)));
                int raw;
                switch (field.Kind)
                {
                    case ValueKind.Byte:
                        value = (int)Marshal.ReadByte(address);
                        break;
                    case ValueKind.Int16:
                        value = (int)(ushort)Marshal.ReadInt16(address);
                        break;
                    case ValueKind.Int32:
                        value = Marshal.ReadInt32(address);
                        break;
                    case ValueKind.Unicode:
                        value = ReadUnicode(address, field.Capacity);
                        break;
                    case ValueKind.NonZeroInt16:
                        value = Marshal.ReadInt16(address) == 0 ? 0 : 1;
                        break;
                    case ValueKind.NonZeroInt32:
                        value = Marshal.ReadInt32(address) == 0 ? 0 : 1;
                        break;
                    case ValueKind.ZeroInt16:
                        value = Marshal.ReadInt16(address) == 0 ? 1 : 0;
                        break;
                    case ValueKind.LowNibble:
                        raw = (ushort)Marshal.ReadInt16(address);
                        value = raw & 0xf;
                        break;
                    case ValueKind.HighNibble:
                        raw = (ushort)Marshal.ReadInt16(address);
                        value = raw >> 4;
                        break;
                }
                return true;
            }
            catch (Exception error)
            {
                EnhancementRuntime.Log("Could not read live EAC setting " + key + ": " + error.Message);
                return false;
            }
        }

        private static string ReadUnicode(IntPtr address, int capacity)
        {
            string value = Marshal.PtrToStringUni(address, capacity) ?? String.Empty;
            int terminator = value.IndexOf('\0');
            return terminator < 0 ? value : value.Substring(0, terminator);
        }

        private static Dictionary<string, Field> CreateFields(bool is18)
        {
            Dictionary<string, Field> result = new Dictionary<string, Field>(StringComparer.OrdinalIgnoreCase);
            AddCommonFields(result, is18);
            AddDriveFields(result, is18);
            return result;
        }

        private static void AddCommonFields(Dictionary<string, Field> fields, bool is18)
        {
            Add(fields, "Extraction Options", "FillUpMissingSamples", is18 ? 0x009B44DDu : 0x0085134Du, ValueKind.Byte);
            Add(fields, "Extraction Options", "SyncTrackJunctions", is18 ? 0x009B44DFu : 0x0085134Fu, ValueKind.Byte);
            Add(fields, "Extraction Options", "RemoveSilence", is18 ? 0x007F64AEu : 0x0077249Eu, ValueKind.Byte);
            Add(fields, "Extraction Options", "NumberReads", is18 ? 0x009994F4u : 0x00836364u, ValueKind.Int32);
            Add(fields, "Extraction Options", "RetrieveCDDBOnUnknownCD", is18 ? 0x007F9721u : 0x00775711u, ValueKind.Byte);
            Add(fields, "StartUp Options", "CreateEnglishLogFile", is18 ? 0x00791574u : 0x0070D564u, ValueKind.Byte);
            Add(fields, "Extraction Options", "AutoSaveStatus", is18 ? 0x007F7573u : 0x00773563u, ValueKind.Byte);
            Add(fields, "Extraction Options", "AddChecksumLogFile", is18 ? 0x009A173Du : 0x0083E5ADu, ValueKind.Byte);
            Add(fields, "Extraction Options", "BackgroundExternalCompression", is18 ? 0x007F7579u : 0x00773569u, ValueKind.Byte);
            Add(fields, "StartUp Options", "EasyGUI", is18 ? 0x009AFD4Du : 0x0084CBBDu, ValueKind.Byte);
            Add(fields, "Extraction Options", "Normalize", is18 ? 0x007F64A2u : 0x00772492u, ValueKind.Byte);

            Add(fields, "Compression Options", "UseExternalEncoder", is18 ? 0x009B0D6Du : 0x0084DBDDu, ValueKind.Byte);
            Add(fields, "Compression Options", "ExternalEncoderType", is18 ? 0x009B357Cu : 0x008503ECu, ValueKind.Int32);
            Add(fields, "Compression Options", "ExternalEncoderExtension", is18 ? 0x006869FCu : 0x0068382Cu, ValueKind.Unicode, 0, 8);
            Add(fields, "Compression Options", "ExternalEncoderProgram", is18 ? 0x009B1578u : 0x0084E3E8u, ValueKind.Unicode, 0, 512);
            Add(fields, "Compression Options", "ExternalEncoderDeleteSource", is18 ? 0x009B0D67u : 0x0084DBD7u, ValueKind.Byte);
            Add(fields, "Compression Options", "ExternalEncoderID3Tag", is18 ? 0x009B0D66u : 0x0084DBD6u, ValueKind.Byte);
            Add(fields, "Compression Options", "ExternalEncoderCheckReturnCode", is18 ? 0x00995A59u : 0x008328C9u, ValueKind.Byte);
            Add(fields, "Compression Options", "UseID3V11", is18 ? 0x007F4BE9u : 0x00770BD9u, ValueKind.Byte);
            Add(fields, "Compression Options", "UseID3V2", is18 ? 0x007F9747u : 0x00775737u, ValueKind.Byte);
            Add(fields, "Compression Options", "WriteV1Tags", is18 ? 0x009AFD62u : 0x0084CBD2u, ValueKind.Byte);
            Add(fields, "Compression Options", "AddCoverToID3V2", is18 ? 0x009AFD5Eu : 0x0084CBCEu, ValueKind.Byte);
            Add(fields, "Compression Options", "WriteCoverToFolder", is18 ? 0x009AFD60u : 0x0084CBD0u, ValueKind.Byte);
        }

        private static void AddDriveFields(Dictionary<string, Field> fields, bool is18)
        {
            uint compact = is18 ? 0x009A2D00u : 0x0083FB70u;
            uint extended = is18 ? 0x009A3200u : 0x00840070u;
            Add(fields, "Drive Options", "SecureMode", compact + 4, ValueKind.Int16, 0x29);
            Add(fields, "Drive Options", "SpeedSelection", compact + 8, ValueKind.Int32, 0x29);
            Add(fields, "Drive Options", "ExtractionMode", compact + 0x11, ValueKind.Int16, 0x29);
            Add(fields, "Drive Options", "SpeedReduction", compact + 0x13, ValueKind.ZeroInt16, 0x29);
            Add(fields, "Drive Options", "GapDetectionAccuracy", compact + 0x17, ValueKind.LowNibble, 0x29);
            Add(fields, "Drive Options", "GapDetectionMode", compact + 0x17, ValueKind.HighNibble, 0x29);
            Add(fields, "Drive Options", "ExtractionCommandSet", compact + 0x1D, ValueKind.Int16, 0x29);
            Add(fields, "Drive Options", "UseC2Correction", extended + 0x38, ValueKind.NonZeroInt32, 0x54);
            Add(fields, "Drive Options", "UseAccurateRip", extended + 0x3C, ValueKind.NonZeroInt16, 0x54);
        }

        private static void Add(
            Dictionary<string, Field> fields,
            string section,
            string name,
            uint address,
            ValueKind kind,
            int stride = 0,
            int capacity = 0)
        {
            fields.Add(section + "\\" + name, new Field(address, kind, stride, capacity));
        }
    }
}
