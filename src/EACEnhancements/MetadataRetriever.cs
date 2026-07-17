using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HelperFunctionsLib;

namespace MetadataPlugIn
{
    // PluginHandler looks up this exact full type name rather than scanning every
    // IMetadataRetriever implementation in an assembly.
    [Guid(AudioDataPlugIn.EnhancementRuntime.CommandLineMetadataProviderGuid)]
    [ClassInterface(ClassInterfaceType.None)]
    [ComSourceInterfaces(typeof(IMetadataRetriever))]
    public sealed class MetadataRetriever : IMetadataRetriever
    {
        public MetadataRetriever()
        {
            try
            {
                AudioDataPlugIn.EnhancementRuntime.Initialize();
                AudioDataPlugIn.EnhancementRuntime.Log("Command-line metadata provider loaded.");
            }
            catch (Exception error)
            {
                AudioDataPlugIn.EnhancementRuntime.Log(
                    "Metadata provider constructor failed: " + error);
            }
        }

        public bool GetCDInformation(CCDMetadata data, bool cdinfo, bool cover, bool lyrics)
        {
            return AudioDataPlugIn.EnhancementRuntime.ProvideCommandLineMetadata(
                data, cdinfo, cover, lyrics);
        }

        public void ShowOptions()
        {
            MessageBox.Show(
                "This metadata provider is used automatically by --eace-metadata. " +
                "It does not have configurable options.",
                "EAC Enhancements",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        public string GetPluginName()
        {
            return "EAC Enhancements Command-Line Metadata";
        }

        public string GetPluginGuid()
        {
            return AudioDataPlugIn.EnhancementRuntime.CommandLineMetadataProviderGuid;
        }

        public Array GetPluginLogo()
        {
            // Valid 1x1 transparent PNG; EAC expects an image array here.
            return Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/" +
                "e9Zq7QAAAABJRU5ErkJggg==");
        }

        public bool SupportsMetadataRetrieval() { return true; }
        public bool SupportsCoverRetrieval() { return true; }
        public bool SupportsLyricsRetrieval() { return true; }
        public bool SupportsMetadataSubmission() { return false; }
        public bool SubmitCDInformation(IMetadataLookup data) { return false; }
    }
}
