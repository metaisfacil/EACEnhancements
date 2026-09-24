using System;
using System.IO;
using System.Text;

namespace AudioDataPlugIn
{
    internal static class SettingsMigrationTests
    {
        private const string Missing = "__missing__";

        private static int Main()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "EACEnhancements-SettingsMigration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                AssertLegacySettingsMove(Path.Combine(directory, "legacy.ini"));
                AssertEmptyLegacySectionRemoved(Path.Combine(directory, "empty.ini"));
                AssertLongValuePreserved(Path.Combine(directory, "long.ini"));
                AssertCurrentSettingsUntouched(Path.Combine(directory, "current.ini"));
                AssertVersionAddedToCurrentSettings(Path.Combine(directory, "versionless.ini"));
                AssertFailedMigrationLeavesFileUntouched(Path.Combine(directory, "invalid.ini"));
                AssertNewerVersionLeavesFileUntouched(Path.Combine(directory, "newer.ini"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
            Console.WriteLine("Settings migration tests passed.");
            return 0;
        }

        private static void AssertLegacySettingsMove(string path)
        {
            File.WriteAllText(path,
                "[OutputTemplate]\r\n" +
                "Root=C:\\Legacy\\\r\n" +
                "FolderTemplate=%albumartist% - Señor Álbum\r\n" +
                "CreateWorkflowFolders=0\r\n" +
                "ShowWorkflowSetupAlert=0\r\n" +
                "ShowRipErrorAlert=1\r\n" +
                "ShowAdditionalWorkflows=1\r\n" +
                "IncreaseExternalCompressorArgumentsLimit=0\r\n" +
                "EnableLogging=1\r\n" +
                "WorkflowOnlyFolderTemplates=1\r\n" +
                "; keep this legacy note\r\n" +
                "Custom=keep\r\n\r\n" +
                "[Workflows]\r\nShowRipErrorAlert=0\r\n\r\n" +
                "[EACEnhancements]\r\nVersion = 0.18.0\r\n",
                Encoding.Unicode);
            if (!EnhancementRuntime.MigrateSettingsFile(path))
                throw new Exception("Legacy settings were not migrated.");
            AssertVersionHeader(path);
            if (!File.ReadAllText(path, Encoding.Unicode).Contains("; keep this legacy note"))
                throw new Exception("Migration removed an unrelated INI comment.");

            AssertValue(path, "Output", "Root", "C:\\Legacy\\");
            AssertValue(path, "Output", "FolderTemplate", "%albumartist% - Señor Álbum");
            AssertValue(path, "Output", "CreateWorkflowFolders", "0");
            AssertValue(path, "Workflows", "ShowWorkflowSetupAlert", "0");
            AssertValue(path, "Workflows", "ShowRipErrorAlert", "0");
            AssertValue(path, "Workflows", "ShowAdditionalWorkflows", "1");
            AssertValue(path, "Extraction", "IncreaseExternalCompressorArgumentsLimit", "0");
            AssertValue(path, "Diagnostics", "EnableLogging", "1");
            AssertValue(path, "Internal", "WorkflowOnlyFolderTemplates", "1");
            AssertValue(path, "OutputTemplate", "Custom", "keep");
            AssertValue(path, "OutputTemplate", "Root", Missing);
            AssertValue(path, "OutputTemplate", "ShowRipErrorAlert", Missing);

            string migrated = File.ReadAllText(path, Encoding.Unicode);
            if (EnhancementRuntime.MigrateSettingsFile(path) ||
                File.ReadAllText(path, Encoding.Unicode) != migrated)
                throw new Exception("Settings migration was not idempotent.");
        }

        private static void AssertEmptyLegacySectionRemoved(string path)
        {
            File.WriteAllText(path,
                "[OutputTemplate]\r\nEnableLogging=1\r\n", Encoding.Unicode);
            if (!EnhancementRuntime.MigrateSettingsFile(path))
                throw new Exception("The legacy logging setting was not migrated.");
            AssertValue(path, "Diagnostics", "EnableLogging", "1");
            if (File.ReadAllText(path, Encoding.Unicode).Contains("[OutputTemplate]"))
                throw new Exception("An empty legacy section was left behind.");

            File.WriteAllText(path, "[OutputTemplate]\r\n", Encoding.Unicode);
            if (!EnhancementRuntime.MigrateSettingsFile(path) ||
                File.ReadAllText(path, Encoding.Unicode).Contains("[OutputTemplate]"))
                throw new Exception("A previously emptied legacy section was not removed.");
        }

        private static void AssertLongValuePreserved(string path)
        {
            string value = new String('x', 5000);
            File.WriteAllText(path,
                "[OutputTemplate]\r\nFolderTemplate=" + value + "\r\n",
                Encoding.Unicode);
            if (!EnhancementRuntime.MigrateSettingsFile(path))
                throw new Exception("A long folder template was not migrated.");
            AssertValue(path, "Output", "FolderTemplate", value);
        }

        private static void AssertCurrentSettingsUntouched(string path)
        {
            string current = "[EACEnhancements]\r\nVersion=" +
                EnhancementRuntime.CurrentPluginSettingsVersion() +
                "\r\n\r\n[Diagnostics]\r\nEnableLogging=0\r\n";
            File.WriteAllText(path, current, Encoding.Unicode);
            if (EnhancementRuntime.MigrateSettingsFile(path) ||
                File.ReadAllText(path, Encoding.Unicode) != current)
                throw new Exception("A current settings file was changed.");
        }

        private static void AssertVersionAddedToCurrentSettings(string path)
        {
            File.WriteAllText(path,
                "[Diagnostics]\r\nEnableLogging=0\r\n", Encoding.Unicode);
            if (!EnhancementRuntime.MigrateSettingsFile(path))
                throw new Exception("A versionless settings file was not stamped.");
            AssertVersionHeader(path);
            AssertValue(path, "Diagnostics", "EnableLogging", "0");
        }

        private static void AssertFailedMigrationLeavesFileUntouched(string path)
        {
            File.WriteAllText(path,
                "[OutputTemplate]\r\nFolderTemplate=" +
                new String('x', 70000) + "\r\n", Encoding.Unicode);
            byte[] original = File.ReadAllBytes(path);
            try
            {
                EnhancementRuntime.MigrateSettingsFile(path);
                throw new Exception("An oversized legacy setting was migrated without an error.");
            }
            catch (InvalidDataException)
            {
            }
            byte[] after = File.ReadAllBytes(path);
            if (after.Length != original.Length)
                throw new Exception("A failed migration changed the file length.");
            for (int i = 0; i < original.Length; i++)
            {
                if (original[i] != after[i])
                    throw new Exception("A failed migration changed the original file.");
            }
            if (Directory.GetFiles(Path.GetDirectoryName(path),
                ".EACEnhancements-*.ini").Length != 0)
                throw new Exception("A failed migration left a temporary INI behind.");
        }

        private static void AssertNewerVersionLeavesFileUntouched(string path)
        {
            string contents = "[EACEnhancements]\r\nVersion=999.0.0\r\n\r\n" +
                "[Diagnostics]\r\nEnableLogging=0\r\n";
            File.WriteAllText(path, contents, Encoding.Unicode);
            try
            {
                EnhancementRuntime.MigrateSettingsFile(path);
                throw new Exception("A newer settings version was downgraded.");
            }
            catch (InvalidDataException)
            {
            }
            if (File.ReadAllText(path, Encoding.Unicode) != contents)
                throw new Exception("A newer settings file was changed.");
        }

        private static void AssertVersionHeader(string path)
        {
            string expected = "[EACEnhancements]\r\nVersion=" +
                EnhancementRuntime.CurrentPluginSettingsVersion() + "\r\n";
            if (!File.ReadAllText(path, Encoding.Unicode).StartsWith(
                expected, StringComparison.Ordinal))
                throw new Exception("The migrated INI does not start with the current plugin version.");
        }

        private static void AssertValue(string path, string section,
            string key, string expected)
        {
            StringBuilder value = new StringBuilder(8192);
            NativeMethods.GetPrivateProfileStringW(
                section, key, Missing, value, value.Capacity, path);
            if (value.ToString() != expected)
                throw new Exception(section + "/" + key + " expected '" + expected +
                    "' but was '" + value + "'.");
        }
    }
}
