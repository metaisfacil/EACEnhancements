using System;
using System.IO;
using System.Text;

namespace AudioDataPlugIn
{
    internal static class DefaultSettingsFileTests
    {
        private static int Main()
        {
            string iniPath = EnhancementRuntime.GetSettingsFilePath();
            string assemblyDirectory = Path.GetDirectoryName(
                typeof(AudioDataTransfer).Assembly.Location);
            AssertEqual(Path.GetDirectoryName(iniPath), assemblyDirectory, "INI directory");

            bool hadOriginal = File.Exists(iniPath);
            byte[] original = hadOriginal ? File.ReadAllBytes(iniPath) : null;
            try
            {
                if (hadOriginal)
                    File.Delete(iniPath);

                if (!EnhancementRuntime.EnsureDefaultSettingsFile())
                    throw new Exception("The missing default INI was not created.");
                if (!File.Exists(iniPath))
                    throw new Exception("The default INI does not exist after creation.");

                string contents = File.ReadAllText(iniPath, Encoding.Unicode);
                AssertContains(contents, "[OutputTemplate]");
                AssertContains(contents, "Root=");
                AssertContains(contents, "FolderTemplate=");
                AssertContains(contents, "ShowRipErrorAlert=1");
                AssertContains(contents, "CreateWorkflowFolders=1");
                AssertContains(contents, "EnableLogging=0");

                const string sentinel = "[OutputTemplate]\r\nEnableLogging=1\r\nCustom=keep\r\n";
                File.WriteAllText(iniPath, sentinel, Encoding.Unicode);
                if (EnhancementRuntime.EnsureDefaultSettingsFile())
                    throw new Exception("An existing INI was reported as newly created.");
                AssertEqual(
                    File.ReadAllText(iniPath, Encoding.Unicode),
                    sentinel,
                    "preserved existing INI");

                string message = EnhancementRuntime.FormatSettingsFileError(
                    iniPath,
                    "create",
                    new UnauthorizedAccessException("Access to the path is denied."));
                AssertContains(message, iniPath);
                AssertContains(message, "could not create");
                AssertContains(message, "Access to the path is denied.");
                AssertContains(message, "built-in defaults");
                AssertContains(message, "Modify permission");
                AssertContains(message, "administrator");
            }
            finally
            {
                if (File.Exists(iniPath))
                    File.Delete(iniPath);
                if (hadOriginal)
                    File.WriteAllBytes(iniPath, original);
            }

            Console.WriteLine("Default settings file tests passed.");
            return 0;
        }

        private static void AssertContains(string value, string expected)
        {
            if (value.IndexOf(expected, StringComparison.Ordinal) < 0)
                throw new Exception("Expected text is missing '" + expected + "'.");
        }

        private static void AssertEqual(string actual, string expected, string description)
        {
            if (!String.Equals(actual, expected, StringComparison.Ordinal))
                throw new Exception("Unexpected " + description + ".");
        }
    }
}
