using System;
using System.IO;
using System.Text;

namespace AudioDataPlugIn
{
    internal static class EacProfileSettingsTests
    {
        private static int Main()
        {
            const string profile =
                "Windows Registry Editor Version 5.00\r\n\r\n" +
                "[HKEY_CURRENT_USER\\Software\\AWSoftware\\EACU\\Extraction Options]\r\n" +
                "\"FillUpMissingSamples\"=hex:ff\r\n" +
                "\"NumberReads\"=hex:05,00,00,00\r\n" +
                "\"DirectorySpecification\"=\"C:\\\\Rips\"\r\n\r\n" +
                "\"ExpandedPath\"=hex(2):43,00,3a,00,5c,00,52,00,69,00,70,00,73,00,00,00\r\n" +
                "[HKEY_CURRENT_USER\\Software\\AWSoftware\\EACU\\Drive Options\\TEST DRIVE]\r\n" +
                "\"ExtractionMode\"=dword:00000005\r\n" +
                "\"SecureMode\"=hex:03,00,\\\r\n" +
                "  00,00\r\n";

            EacProfileSettings settings = EacProfileSettings.Parse(profile);
            AssertBytes(settings, "Extraction Options", "FillUpMissingSamples", 0xff);
            AssertBytes(settings, "Extraction Options", "NumberReads", 5, 0, 0, 0);
            AssertValue(settings, "Extraction Options", "DirectorySpecification", @"C:\Rips");
            AssertValue(settings, "Extraction Options", "ExpandedPath", @"C:\Rips");
            AssertValue(settings, "Drive Options\\TEST DRIVE", "ExtractionMode", 5);
            AssertBytes(settings, "Drive Options\\TEST DRIVE", "SecureMode", 3, 0, 0, 0);

            if (!EnhancementRuntime.ShouldRestoreDetectedReadCommand(7, 0) ||
                !EnhancementRuntime.ShouldRestoreDetectedReadCommand(7, null) ||
                EnhancementRuntime.ShouldRestoreDetectedReadCommand(0, 0) ||
                EnhancementRuntime.ShouldRestoreDetectedReadCommand(7, 7) ||
                EnhancementRuntime.ShouldRestoreDetectedReadCommand(7, 1))
            {
                throw new InvalidOperationException(
                    "Detected read-command preservation did not reject a transient downgrade.");
            }

            AssertSelectedReadCommand(7, 0, null, new byte[] { 7, 0 });
            AssertSelectedReadCommand(8, 8, new byte[] { 7, 0 }, new byte[] { 7, 0 });
            AssertSelectedReadCommand(7, 0, new byte[] { 7, 0 }, new byte[] { 8, 0 });
            AssertSelectedReadCommand(0, 0, null, new byte[] { 0, 0 });

            bool foundDrive = false;
            foreach (string name in settings.GetSubKeyNames("Drive Options"))
                foundDrive |= String.Equals(name, "TEST DRIVE", StringComparison.OrdinalIgnoreCase);
            if (!foundDrive)
                throw new InvalidOperationException("Profile drive subkeys were not enumerated.");

            // Older EAC profiles use the EAC root instead of EACU. Both roots
            // must normalize to the section paths used by the current audit.
            EacProfileSettings legacy = EacProfileSettings.Parse(
                "[HKEY_CURRENT_USER\\Software\\AWSoftware\\EAC\\Compression Options]\n" +
                "\"ExternalEncoderExtension\"=\".flac\"\n");
            AssertValue(legacy, "Compression Options", "ExternalEncoderExtension", ".flac");

            string binaryPath = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(binaryPath, Encoding.Unicode.GetBytes("EACV1300"));
                if (!EacProfileSettings.IsBinary(binaryPath))
                    throw new InvalidOperationException("EACV1300 binary profiles were not detected.");
            }
            finally
            {
                File.Delete(binaryPath);
            }

            Console.WriteLine("EAC active-profile parsing tests passed.");
            return 0;
        }

        private static void AssertSelectedReadCommand(
            int expected,
            object live,
            object profile,
            object registry)
        {
            object selected = EacSettingsSource.SelectExtractionCommandSet(
                live,
                profile,
                registry);
            byte[] bytes = selected as byte[];
            int actual = bytes == null
                ? Convert.ToInt32(selected)
                : bytes.Length >= sizeof(int)
                    ? BitConverter.ToInt32(bytes, 0)
                    : BitConverter.ToUInt16(bytes, 0);
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    "Read-command selection returned " + actual +
                    " instead of " + expected + ".");
            }
        }

        private static void AssertValue(
            EacProfileSettings settings,
            string section,
            string name,
            object expected)
        {
            object actual;
            if (!settings.TryGetValue(section, name, out actual) || !Object.Equals(actual, expected))
                throw new InvalidOperationException(
                    section + "\\" + name + " was not parsed as '" + expected + "'.");
        }

        private static void AssertBytes(
            EacProfileSettings settings,
            string section,
            string name,
            params byte[] expected)
        {
            object value;
            byte[] actual;
            if (!settings.TryGetValue(section, name, out value) || (actual = value as byte[]) == null)
                throw new InvalidOperationException(section + "\\" + name + " was not parsed as bytes.");
            if (actual.Length != expected.Length)
                throw new InvalidOperationException(section + "\\" + name + " has the wrong byte length.");
            for (int index = 0; index < actual.Length; index++)
            {
                if (actual[index] != expected[index])
                    throw new InvalidOperationException(section + "\\" + name + " has incorrect byte data.");
            }
        }
    }
}
