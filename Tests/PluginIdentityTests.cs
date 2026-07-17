using System;
using HelperFunctionsLib;
using MetadataPlugIn;

namespace AudioDataPlugIn
{
    internal static class PluginIdentityTests
    {
        private static int Main()
        {
            Version version = typeof(AudioDataTransfer).Assembly.GetName().Version;
            string expected = "EAC Enhancements V" +
                version.Major + "." + version.Minor + "." + version.Build;
            if (version.Revision > 0)
                expected += "." + version.Revision;

            if (!String.Equals(AudioDataTransfer.PluginDisplayName, expected, StringComparison.Ordinal))
                throw new Exception("Plugin display name does not report the loaded assembly version.");
            if (!typeof(IAudioDataTransfer).IsAssignableFrom(typeof(AudioDataTransfer)))
                throw new Exception("AudioDataTransfer does not implement EAC's audio-transfer contract.");
            if (!typeof(IMetadataRetriever).IsAssignableFrom(typeof(MetadataRetriever)))
                throw new Exception("MetadataRetriever does not implement EAC's metadata contract.");

            Console.WriteLine("Plugin identity and EAC contract tests passed: " + expected);
            return 0;
        }
    }
}
