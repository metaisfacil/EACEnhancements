using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AudioDataPlugIn
{
    internal sealed class EacVersionLayout
    {
        internal readonly string Name;
        internal readonly string Sha256;
        internal readonly int ImageSize;
        internal readonly int CommandCompletionRva;
        internal readonly int RipDialogHwndRva;
        internal readonly uint PostMessageWThunkVa;
        internal readonly uint MainWindowGlobalVa;
        internal readonly uint ChainFlagVa;
        internal readonly uint TrackSelectionArrayVa;
        internal readonly uint FirstTocTrackNumberVa;
        internal readonly uint FirstTocTrackFlagsVa;
        internal readonly uint FirstTocTrackStartLowVa;
        internal readonly uint FirstTocTrackStartHighVa;
        internal readonly uint RangeStartLowVa;
        internal readonly uint RangeStartHighVa;
        internal readonly uint RangeEndLowVa;
        internal readonly uint RangeEndHighVa;
        internal readonly uint RangeDialogAcceptedFlagVa;
        internal readonly uint RangeDialogHookVa;
        internal readonly uint RangeDialogResumeVa;
        internal readonly uint RangeDialogBypassVa;
        internal readonly uint RangeSaveHookVa;
        internal readonly uint RangeSaveResumeVa;
        internal readonly uint RangeSaveBypassVa;
        internal readonly uint RangeOutputPathVa;
        internal readonly uint RangeSaveAcceptedHookVa;
        internal readonly uint RangeSaveAcceptedResumeVa;
        internal readonly uint CueSaveHookVa;
        internal readonly uint CueSaveDefaultVa;
        internal readonly uint CueSavePromptVa;
        internal readonly uint WaveformSaveHookVa;
        internal readonly uint WaveformSaveDefaultVa;
        internal readonly uint WaveformSaveResumeVa;
        internal readonly uint RipCompleteHookVa;
        internal readonly uint RipCompleteResumeVa;
        internal readonly uint CopyStatusCompleteFlagVa;
        internal readonly uint DispatchHookVa;
        internal readonly uint DispatchOldHandlerVa;
        internal readonly uint DispatchNextHandlerVa;
        internal readonly uint DispatchReturnVa;
        internal readonly uint GapsHookVa;
        internal readonly uint GapsResumeVa;
        internal readonly uint CueChainHookVa;
        internal readonly uint CueChainResumeVa;
        internal readonly uint OutputPathModeVa;
        internal readonly uint StandardDirectoryPathVa;
        internal readonly uint ActualPathVa;
        internal readonly uint LiveSettingsRefreshVa;
        internal readonly uint PluginHandlerContextVa;
        internal readonly uint PluginGetNumPluginsPointerVa;
        internal readonly uint PluginSetCurrentPluginPointerVa;
        internal readonly uint PluginGetPluginNamePointerVa;
        internal readonly uint PluginGetPluginGuidPointerVa;
        internal readonly byte[] ExpectedDispatch;
        internal readonly byte[] ExpectedOldHandler;
        internal readonly byte[] ExpectedGapsEndpoint;
        internal readonly byte[] ExpectedCueEndpoint;
        internal readonly byte[] ExpectedCueSaveDecision;
        internal readonly byte[] ExpectedWaveformDecision;
        internal readonly byte[] ExpectedRipComplete;
        internal readonly byte[] ExpectedRangeDialogHook;
        internal readonly byte[] ExpectedRangeSaveHook;
        internal readonly byte[] ExpectedRangeSaveAcceptedHook;

        private static readonly byte[] CommandCompletionPrologue =
            Hex("55 89 E5 83 EC 04");

        private EacVersionLayout(
            string name,
            string sha256,
            int imageSize,
            int commandCompletionRva,
            int ripDialogHwndRva,
            uint postMessageWThunkVa,
            uint mainWindowGlobalVa,
            uint chainFlagVa,
            uint trackSelectionArrayVa,
            uint firstTocTrackNumberVa,
            uint firstTocTrackFlagsVa,
            uint firstTocTrackStartLowVa,
            uint firstTocTrackStartHighVa,
            uint rangeStartLowVa,
            uint rangeStartHighVa,
            uint rangeEndLowVa,
            uint rangeEndHighVa,
            uint rangeDialogAcceptedFlagVa,
            uint rangeDialogHookVa,
            uint rangeDialogResumeVa,
            uint rangeDialogBypassVa,
            uint rangeSaveHookVa,
            uint rangeSaveResumeVa,
            uint rangeSaveBypassVa,
            uint rangeOutputPathVa,
            uint rangeSaveAcceptedHookVa,
            uint rangeSaveAcceptedResumeVa,
            uint cueSaveHookVa,
            uint cueSaveDefaultVa,
            uint cueSavePromptVa,
            uint waveformSaveHookVa,
            uint waveformSaveDefaultVa,
            uint waveformSaveResumeVa,
            uint ripCompleteHookVa,
            uint ripCompleteResumeVa,
            uint copyStatusCompleteFlagVa,
            uint dispatchHookVa,
            uint dispatchOldHandlerVa,
            uint dispatchNextHandlerVa,
            uint dispatchReturnVa,
            uint gapsHookVa,
            uint gapsResumeVa,
            uint cueChainHookVa,
            uint cueChainResumeVa,
            uint outputPathModeVa,
            uint standardDirectoryPathVa,
            uint actualPathVa,
            uint liveSettingsRefreshVa,
            uint pluginHandlerContextVa,
            uint pluginGetNumPluginsPointerVa,
            uint pluginSetCurrentPluginPointerVa,
            uint pluginGetPluginNamePointerVa,
            uint pluginGetPluginGuidPointerVa,
            string expectedOldHandler,
            string expectedGapsEndpoint,
            string expectedCueEndpoint,
            string expectedCueSaveDecision,
            string expectedWaveformDecision,
            string expectedRipComplete)
        {
            Name = name;
            Sha256 = sha256;
            ImageSize = imageSize;
            CommandCompletionRva = commandCompletionRva;
            RipDialogHwndRva = ripDialogHwndRva;
            PostMessageWThunkVa = postMessageWThunkVa;
            MainWindowGlobalVa = mainWindowGlobalVa;
            ChainFlagVa = chainFlagVa;
            TrackSelectionArrayVa = trackSelectionArrayVa;
            FirstTocTrackNumberVa = firstTocTrackNumberVa;
            FirstTocTrackFlagsVa = firstTocTrackFlagsVa;
            FirstTocTrackStartLowVa = firstTocTrackStartLowVa;
            FirstTocTrackStartHighVa = firstTocTrackStartHighVa;
            RangeStartLowVa = rangeStartLowVa;
            RangeStartHighVa = rangeStartHighVa;
            RangeEndLowVa = rangeEndLowVa;
            RangeEndHighVa = rangeEndHighVa;
            RangeDialogAcceptedFlagVa = rangeDialogAcceptedFlagVa;
            RangeDialogHookVa = rangeDialogHookVa;
            RangeDialogResumeVa = rangeDialogResumeVa;
            RangeDialogBypassVa = rangeDialogBypassVa;
            RangeSaveHookVa = rangeSaveHookVa;
            RangeSaveResumeVa = rangeSaveResumeVa;
            RangeSaveBypassVa = rangeSaveBypassVa;
            RangeOutputPathVa = rangeOutputPathVa;
            RangeSaveAcceptedHookVa = rangeSaveAcceptedHookVa;
            RangeSaveAcceptedResumeVa = rangeSaveAcceptedResumeVa;
            CueSaveHookVa = cueSaveHookVa;
            CueSaveDefaultVa = cueSaveDefaultVa;
            CueSavePromptVa = cueSavePromptVa;
            WaveformSaveHookVa = waveformSaveHookVa;
            WaveformSaveDefaultVa = waveformSaveDefaultVa;
            WaveformSaveResumeVa = waveformSaveResumeVa;
            RipCompleteHookVa = ripCompleteHookVa;
            RipCompleteResumeVa = ripCompleteResumeVa;
            CopyStatusCompleteFlagVa = copyStatusCompleteFlagVa;
            DispatchHookVa = dispatchHookVa;
            DispatchOldHandlerVa = dispatchOldHandlerVa;
            DispatchNextHandlerVa = dispatchNextHandlerVa;
            DispatchReturnVa = dispatchReturnVa;
            GapsHookVa = gapsHookVa;
            GapsResumeVa = gapsResumeVa;
            CueChainHookVa = cueChainHookVa;
            CueChainResumeVa = cueChainResumeVa;
            OutputPathModeVa = outputPathModeVa;
            StandardDirectoryPathVa = standardDirectoryPathVa;
            ActualPathVa = actualPathVa;
            LiveSettingsRefreshVa = liveSettingsRefreshVa;
            PluginHandlerContextVa = pluginHandlerContextVa;
            PluginGetNumPluginsPointerVa = pluginGetNumPluginsPointerVa;
            PluginSetCurrentPluginPointerVa = pluginSetCurrentPluginPointerVa;
            PluginGetPluginNamePointerVa = pluginGetPluginNamePointerVa;
            PluginGetPluginGuidPointerVa = pluginGetPluginGuidPointerVa;
            ExpectedDispatch = Hex("3D 10 03 00 00 75 23");
            ExpectedOldHandler = Hex(expectedOldHandler);
            ExpectedGapsEndpoint = Hex(expectedGapsEndpoint);
            ExpectedCueEndpoint = Hex(expectedCueEndpoint);
            ExpectedCueSaveDecision = Hex(expectedCueSaveDecision);
            ExpectedWaveformDecision = Hex(expectedWaveformDecision);
            ExpectedRipComplete = Hex(expectedRipComplete);
            ExpectedRangeDialogHook = Hex("C6 05 " + LittleEndian(rangeDialogAcceptedFlagVa) + " 00");
            ExpectedRangeSaveHook = Hex("68 FF 0F 00 00");
            ExpectedRangeSaveAcceptedHook = Hex("68 FF 0F 00 00");
        }

        internal static EacVersionLayout Detect(ProcessModule module, IntPtr imageBase)
        {
            EacVersionLayout[] layouts = { Eac18, Eac16 };
            string hash = TryHash(module.FileName);
            foreach (EacVersionLayout candidate in layouts)
            {
                if (hash != null && String.Equals(hash, candidate.Sha256, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            // Preserve compatibility with renamed or minimally pre-patched builds.
            // The image size separates 1.6 and 1.8, while the function prologue
            // prevents an unknown executable with a coincidental size from matching.
            foreach (EacVersionLayout candidate in layouts)
            {
                if (module.ModuleMemorySize == candidate.ImageSize &&
                    BytesEqual(Add(imageBase, candidate.CommandCompletionRva), CommandCompletionPrologue))
                    return candidate;
            }

            throw new NotSupportedException(
                "Unsupported EAC executable (SHA-256 " + (hash ?? "unavailable") +
                ", image size 0x" + module.ModuleMemorySize.ToString("X") + ").");
        }

        private static string TryHash(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                using (SHA256 sha256 = SHA256.Create())
                    return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", String.Empty);
            }
            catch
            {
                return null;
            }
        }

        private static bool BytesEqual(IntPtr address, byte[] expected)
        {
            try
            {
                byte[] actual = new byte[expected.Length];
                Marshal.Copy(address, actual, 0, actual.Length);
                for (int i = 0; i < actual.Length; i++)
                {
                    if (actual[i] != expected[i])
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IntPtr Add(IntPtr address, int offset)
        {
            return new IntPtr(address.ToInt64() + offset);
        }

        private static byte[] Hex(string text)
        {
            string[] parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            byte[] bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                bytes[i] = Convert.ToByte(parts[i], 16);
            return bytes;
        }

        private static string LittleEndian(uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        private static readonly EacVersionLayout Eac18 = new EacVersionLayout(
            "EAC 1.8",
            "A169A5CAD41CC341F2B108D8A38EAF957C817DF1F971AC13AA1FB731F957A204",
            0x009E2000,
            0x00042D70,
            0x007780C4,
            0x0066246C,
            0x007F8FC8,
            0x00D260F9,
            0x007F9014,
            0x007EEE0A,
            0x007EEE09,
            0x007EEE0E,
            0x007EEE12,
            0x007F8FDC,
            0x007F8FE0,
            0x007F8FE4,
            0x007F8FE8,
            0x00B55140,
            0x00409DF6,
            0x00409DFD,
            0x00409E25,
            0x00409E8F,
            0x00409E94,
            0x00409EDF,
            0x00C9B4F4,
            0x00409EDF,
            0x00409EE4,
            0x0040B255,
            0x0040B25E,
            0x0040B2DA,
            0x00623BE0,
            0x00623C68,
            0x00623BE9,
            0x0062231F,
            0x00622326,
            0x00C9FCFC,
            0x00408527,
            0x0040852E,
            0x00408551,
            0x0040F5F9,
            0x0040AEEE,
            0x0040AF0C,
            0x0040B71C,
            0x0040B733,
            0x009940A4,
            0x009948AC,
            0x009A1534,
            // Reloads EAC's live settings from the registry. 0x005D0840 is
            // the inverse save routine and must never be used as a refresh.
            0x005DFC30,
            0x009B52DC,
            0x007D01F8,
            0x007D01FC,
            0x007D0200,
            0x007D0204,
            "C6 05 F9 60 D2 00 01 6A 00 68 3C 02 00 00 68 11 01 00 00 FF 35 C8 8F 7F 00 E8 20 9F 25 00",
            "C6 05 F9 60 D2 00 00 6A 00 68 02 03 00 00 68 11 01 00 00 FF 35 C8 8F 7F 00 E8 60 75 25 00",
            "6A 00 68 1B 02 00 00 68 11 01 00 00 FF 35 C8 8F 7F 00 E8 39 6D 25 00",
            "83 3D A4 40 99 00 00 75 7C",
            "83 3D A4 40 99 00 01 75 7F",
            "C6 05 FC FC C9 00 01");

        private static readonly EacVersionLayout Eac16 = new EacVersionLayout(
            "EAC 1.6",
            "BE902E222421B4685B665F4F6C505C5FBC7ADD756D9141E722482C0D25E9A32D",
            0x00740000,
            0x0003FB10,
            0x004D88EC,
            0x0065F006,
            0x00774FB8,
            0x00A8691F,
            0x00775004,
            0x0076ADFA,
            0x0076ADF9,
            0x0076ADFE,
            0x0076AE02,
            0x00774FCC,
            0x00774FD0,
            0x00774FD4,
            0x00774FD8,
            0x008B5968,
            0x00409C12,
            0x00409C19,
            0x00409C41,
            0x00409CAB,
            0x00409CB0,
            0x00409CFB,
            0x009FBD1C,
            0x00409CFB,
            0x00409D00,
            0x0040B071,
            0x0040B07A,
            0x0040B0F6,
            0x00620510,
            0x00620598,
            0x00620519,
            0x0061EC4F,
            0x0061EC56,
            0x00A00524,
            0x00408345,
            0x0040834C,
            0x0040836F,
            0x0040F415,
            0x0040AD0A,
            0x0040AD28,
            0x0040B538,
            0x0040B54F,
            0x00830F14,
            0x0083171C,
            0x0083E3A4,
            // Reloads EAC's live settings from the registry. 0x005CC8C0 is
            // the inverse save routine and must never be used as a refresh.
            0x005DBCB0,
            0x0085214C,
            0x0074C1E8,
            0x0074C1EC,
            0x0074C1F0,
            0x0074C1F4,
            "C6 05 1F 69 A8 00 01 6A 00 68 3C 02 00 00 68 11 01 00 00 FF 35 B8 4F 77 00 E8 9C 6C 25 00",
            "C6 05 1F 69 A8 00 00 6A 00 68 02 03 00 00 68 11 01 00 00 FF 35 B8 4F 77 00 E8 DE 42 25 00",
            "6A 00 68 1B 02 00 00 68 11 01 00 00 FF 35 B8 4F 77 00 E8 B7 3A 25 00",
            "83 3D 14 0F 83 00 00 75 7C",
            "83 3D 14 0F 83 00 01 75 7F",
            "C6 05 24 05 A0 00 01");
    }
}
