using System;
using System.IO;

namespace AudioDataPlugIn
{
    internal static class EacVersionGateTests
    {
        private static int Main()
        {
            string iniPath = EnhancementRuntime.GetSettingsFilePath();
            bool hadOriginal = File.Exists(iniPath);
            byte[] original = hadOriginal ? File.ReadAllBytes(iniPath) : null;
            try
            {
                if (hadOriginal)
                    File.Delete(iniPath);

                AssertInitializationRejected();
                if (File.Exists(iniPath))
                    throw new Exception("Unsupported-host initialization created an INI file.");
                if (EnhancementRuntime.HookStatus.IndexOf(
                    "inactive: Unsupported EAC executable",
                    StringComparison.Ordinal) < 0)
                    throw new Exception("The unsupported host was not marked inactive.");

                // A second entry point must receive the cached rejection rather
                // than observing a partially initialized runtime.
                AssertInitializationRejected();
            }
            finally
            {
                if (File.Exists(iniPath))
                    File.Delete(iniPath);
                if (hadOriginal)
                    File.WriteAllBytes(iniPath, original);
            }

            Console.WriteLine("Unsupported EAC initialization rejection test passed.");
            return 0;
        }

        private static void AssertInitializationRejected()
        {
            try
            {
                EnhancementRuntime.Initialize();
            }
            catch (NotSupportedException error)
            {
                if (error.Message.IndexOf(
                    "Unsupported EAC executable",
                    StringComparison.Ordinal) >= 0)
                    return;
                throw new Exception("The version gate returned an unclear error.");
            }
            throw new Exception("A non-EAC executable passed the EAC 1.6/1.8 version gate.");
        }
    }
}
