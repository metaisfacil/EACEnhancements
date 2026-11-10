using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioDataPlugIn
{
    // EAC formats CUEs in ANSI byte buffers (some only 256 bytes). Merely
    // changing WideCharToMultiByte to UTF-8 can overflow those buffers or cut
    // a FILE name short. Give its formatter short ASCII placeholders instead,
    // and expand the original Unicode strings at the final stream write.
    // EAC still owns every FILE/TRACK/FLAGS/INDEX/PREGAP decision and file I/O.
    internal sealed class CueSheetTextEncoding
    {
        // Menu writers also convert the extension through a 31-byte buffer.
        private readonly string prefix = "EACU" + Guid.NewGuid().ToString("N").Substring(0, 16);
        private readonly Dictionary<string, string> values =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal string Capture(string value)
        {
            value = (value ?? String.Empty).Replace('"', '\'')
                .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            if (value.Length == 0)
                return String.Empty;
            // REM's native formatter decides whether to quote a value by
            // looking for a space in its ANSI representation.
            string marker = prefix + values.Count.ToString("X4") +
                (value.IndexOf(' ') >= 0 ? " X" : "X");
            values.Add(marker, value);
            return marker;
        }

        internal byte[] Expand(byte[] nativeText)
        {
            string text = Encoding.Default.GetString(nativeText);
            // Expand in one pass: a value containing another marker must stay
            // literal. Never interpret inserted metadata as template text.
            StringBuilder result = new StringBuilder();
            int start = 0;
            while (start < text.Length)
            {
                int next = text.IndexOf(prefix, start, StringComparison.Ordinal);
                if (next < 0)
                {
                    result.Append(text, start, text.Length - start);
                    break;
                }
                result.Append(text, start, next - start);
                int end = next + prefix.Length + 4;
                if (end < text.Length && text[end] == ' ')
                    end++;
                string value;
                if (end < text.Length && text[end] == 'X' &&
                    values.TryGetValue(text.Substring(next, end - next + 1), out value))
                {
                    result.Append(value);
                    start = end + 1;
                }
                else
                {
                    throw new InvalidOperationException("Incomplete CUE text placeholder.");
                }
            }
            return new UTF8Encoding(false, true).GetBytes(result.ToString());
        }
    }

    internal sealed class CueSheetHookLayout
    {
        internal readonly uint ImageWriter;
        internal readonly uint TextWriter;
        internal readonly uint[] MenuWriters;
        internal readonly uint Convert;
        internal readonly uint Write;
        internal readonly uint[] ConversionCalls;
        internal readonly uint[] WriteCalls;
        internal static readonly byte[] ImagePrologue =
            { 0x55, 0x89, 0xE5, 0x81, 0xEC, 0x38, 0x0E, 0, 0 };
        internal static readonly byte[] TextPrologue =
            { 0x55, 0x89, 0xE5, 0x83, 0xEC, 0x38 };
        internal static readonly byte[][] MenuPrologues =
        {
            new byte[] { 0x55, 0x89, 0xE5, 0x89, 0x84, 0x24, 0, 0xF0, 0xFF, 0xFF },
            new byte[] { 0x55, 0x89, 0xE5, 0x81, 0xEC, 0x48, 0x0F, 0, 0 },
            new byte[] { 0x55, 0x89, 0xE5, 0x81, 0xEC, 0x68, 0x0F, 0, 0 }
        };

        internal CueSheetHookLayout(string version)
        {
            // Ghidra: WriteCueSheetMetadata_RE, WriteCueSheetRemComment_RE,
            // WriteCueSheetTrackDirectives_RE, WriteCueSheetTextToStream_GLANCE,
            // WriteCueSheetFile_RE, BuildCueSheetContent_RE, CreateCueSheet_Noncompliant.
            if (version == "EAC 1.8")
            {
                ImageWriter = 0x004F3B90;
                TextWriter = 0x005A72F0;
                Convert = 0x00425080;
                Write = 0x0065E72D;
            }
            else if (version == "EAC 1.6")
            {
                ImageWriter = 0x004EFC10;
                TextWriter = 0x005A3370;
                Convert = 0x00424080;
                Write = 0x0065B3DD;
            }
            else
                throw new NotSupportedException("No CUE encoding layout for " + version + ".");

            // The three multiple-file menu writers are separate native routines;
            // they do not call either the image writer or the cue editor's save.
            MenuWriters = new uint[] { ImageWriter + 0x1D00, ImageWriter + 0x3740, ImageWriter + 0x52A0 };
            List<uint> conversions = new List<uint>();
            AddOffsets(conversions, ImageWriter, new uint[]
                { 0x3D5, 0x580, 0x71E, 0xA29, 0xD13, 0xD35, 0x106B });
            AddOffsets(conversions, ImageWriter - 0x1B0, new uint[] { 0x31 });
            AddOffsets(conversions, ImageWriter - 0x24820, new uint[]
                { 0x1A8, 0x350, 0x64C, 0xE2D, 0x1055 });
            AddOffsets(conversions, MenuWriters[0], new uint[]
                { 0x2AD, 0x458, 0x5F6, 0x932, 0x968, 0xBEC, 0xC0E, 0xF43 });
            AddOffsets(conversions, MenuWriters[1], new uint[]
                { 0x2A5, 0x450, 0x5EE, 0x92A, 0x960, 0xBE4, 0xC06, 0xF3B });
            AddOffsets(conversions, MenuWriters[2], new uint[]
                { 0x2A5, 0x450, 0x5EE, 0x92E, 0x964, 0xBF2, 0xC14, 0xF47, 0x1915, 0x1937, 0x1C74 });
            ConversionCalls = conversions.ToArray();
            List<uint> writes = new List<uint>();
            AddOffsets(writes, ImageWriter, new uint[]
            {
                0x382, 0x3A4, 0x537, 0x559, 0x6D5, 0x6F7, 0x882, 0x8A4,
                0x9B9, 0x9DB, 0xAA5, 0xAC7, 0xC9D, 0xCBF, 0xE97, 0xEB9,
                0x1009, 0x102B, 0x11B9, 0x11DB, 0x1278, 0x129A,
                0x1453, 0x1475, 0x1544, 0x1566, 0x16B1, 0x16D3,
                0x17AD, 0x17CF, 0x195F, 0x1981
            });
            AddOffsets(writes, ImageWriter - 0x1B0, new uint[]
                { 0xD5, 0xEF, 0x105, 0x121, 0x147, 0x15D, 0x17B });
            AddOffsets(writes, TextWriter, new uint[] { 0x76 });
            AddOffsets(writes, MenuWriters[0], new uint[]
            {
                0x25A, 0x27C, 0x40F, 0x431, 0x5AD, 0x5CF, 0x75A, 0x77C,
                0x887, 0x8A9, 0x9F0, 0xA12, 0xB76, 0xB98, 0xD6F, 0xD91,
                0xEE1, 0xF03, 0x1091, 0x10B3, 0x1150, 0x1172, 0x132B, 0x134D,
                0x13ED, 0x140F, 0x1574, 0x1596, 0x15D4, 0x15F6, 0x17D6, 0x17F8
            });
            AddOffsets(writes, MenuWriters[1], new uint[]
            {
                0x252, 0x274, 0x407, 0x429, 0x5A5, 0x5C7, 0x752, 0x774,
                0x87F, 0x8A1, 0x9E8, 0xA0A, 0xB6E, 0xB90, 0xD67, 0xD89,
                0xED9, 0xEFB, 0x1089, 0x10AB, 0x1148, 0x116A, 0x1323, 0x1345,
                0x1395, 0x13B7, 0x1455, 0x1477, 0x1510, 0x1532, 0x163D, 0x165F,
                0x18D8, 0x18FA
            });
            AddOffsets(writes, MenuWriters[2], new uint[]
            {
                0x252, 0x274, 0x407, 0x429, 0x5A5, 0x5C7, 0x752, 0x774,
                0x883, 0x8A5, 0x9EC, 0xA0E, 0xB7C, 0xB9E, 0xD73, 0xD95,
                0xEE5, 0xF07, 0x1095, 0x10B7, 0x1154, 0x1176, 0x132F, 0x1351,
                0x13F1, 0x1413, 0x1451, 0x1473, 0x1656, 0x1678, 0x1891, 0x18B3,
                0x1A97, 0x1AB9, 0x1C09, 0x1C2B, 0x1DC1, 0x1DE3, 0x1E92, 0x1EB4,
                0x20A3, 0x20C5, 0x21C2, 0x21E4
            });
            WriteCalls = writes.ToArray();
        }

        private static void AddOffsets(List<uint> result, uint address, uint[] offsets)
        {
            foreach (uint offset in offsets)
                result.Add(address + offset);
        }
    }

    internal static partial class EnhancementRuntime
    {
        private const string UseUtf8CueSheetsIniKey = "UseUtf8CueSheets";

        private static bool IsUtf8CueSheetsEnabled()
        {
            try
            {
                return ParseIniBoolean(ReadIniValue(
                    GetSettingsFilePath(), UseUtf8CueSheetsIniKey, "1"), true);
            }
            catch (Exception error)
            {
                Log("Could not read the UTF-8 CUE sheet option; defaulting to enabled: " + error.Message);
                return true;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CueImageWriterDelegate(
            uint relative, IntPtr metadata, uint binary, IntPtr filename,
            uint filenameBound, IntPtr unused, uint unusedBound,
            IntPtr path, uint pathBound);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CueTextWriterDelegate(IntPtr path, uint pathBound);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CueMenuWriterDelegate(
            IntPtr metadata, IntPtr extension, uint extensionBound, uint binary,
            IntPtr unused, uint unusedBound, IntPtr path, uint pathBound);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate byte CueConvertDelegate(
            int capacity, IntPtr destination, uint sourceBound, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CueWriteDelegate(
            uint count, IntPtr source, uint sourceBound, IntPtr stream);

        private static CueImageWriterDelegate originalCueImageWriter;
        private static CueTextWriterDelegate originalCueTextWriter;
        private static CueConvertDelegate originalCueConvert;
        private static CueWriteDelegate originalCueWrite;
        private static readonly CueMenuWriterDelegate[] originalCueMenuWriters = new CueMenuWriterDelegate[3];
        private static readonly CueMenuWriterDelegate[] cueMenuWriterHooks =
            { WriteUnicodeMenuCue0, WriteUnicodeMenuCue1, WriteUnicodeMenuCue2 };
        private static readonly CueImageWriterDelegate cueImageWriterHook = WriteUnicodeImageCue;
        private static readonly CueTextWriterDelegate cueTextWriterHook = WriteUnicodeTextCue;
        private static readonly CueConvertDelegate cueConvertHook = CaptureCueUnicode;
        private static readonly CueWriteDelegate cueWriteHook = WriteCueUtf8;
        [ThreadStatic]
        private static CueEncodingSession cueEncodingSession;

        private sealed class CueEncodingSession
        {
            internal readonly CueSheetTextEncoding Text = new CueSheetTextEncoding();
            internal bool Started;
            internal bool Failed;
        }

        private static void InstallCueSheetEncodingHooks()
        {
            CueSheetHookLayout cue = new CueSheetHookLayout(layout.Name);
            Dictionary<uint, byte[]> originals = new Dictionary<uint, byte[]>();
            RequireBytes(cue.ImageWriter, CueSheetHookLayout.ImagePrologue, "CUE image writer");
            RequireBytes(cue.TextWriter, CueSheetHookLayout.TextPrologue, "CUE text writer");
            originals.Add(cue.ImageWriter, CueSheetHookLayout.ImagePrologue);
            originals.Add(cue.TextWriter, CueSheetHookLayout.TextPrologue);
            for (int i = 0; i < cue.MenuWriters.Length; i++)
            {
                RequireBytes(cue.MenuWriters[i], CueSheetHookLayout.MenuPrologues[i], "CUE menu writer " + i);
                originals.Add(cue.MenuWriters[i], CueSheetHookLayout.MenuPrologues[i]);
            }
            foreach (uint call in cue.ConversionCalls)
            {
                byte[] expected = CueCallBytes(call, cue.Convert);
                RequireBytes(call, expected, "CUE Unicode conversion call");
                originals.Add(call, expected);
            }
            foreach (uint call in cue.WriteCalls)
            {
                byte[] expected = CueCallBytes(call, cue.Write);
                RequireBytes(call, expected, "CUE stream write call");
                originals.Add(call, expected);
            }
            originalCueImageWriter = (CueImageWriterDelegate)Marshal.GetDelegateForFunctionPointer(
                CreateAlbumMetadataStoreTrampoline(cue.ImageWriter, CueSheetHookLayout.ImagePrologue),
                typeof(CueImageWriterDelegate));
            originalCueTextWriter = (CueTextWriterDelegate)Marshal.GetDelegateForFunctionPointer(
                CreateAlbumMetadataStoreTrampoline(cue.TextWriter, CueSheetHookLayout.TextPrologue),
                typeof(CueTextWriterDelegate));
            originalCueConvert = (CueConvertDelegate)Marshal.GetDelegateForFunctionPointer(
                AddressFromStaticVa(cue.Convert), typeof(CueConvertDelegate));
            originalCueWrite = (CueWriteDelegate)Marshal.GetDelegateForFunctionPointer(
                AddressFromStaticVa(cue.Write), typeof(CueWriteDelegate));
            for (int i = 0; i < cue.MenuWriters.Length; i++)
            {
                originalCueMenuWriters[i] = (CueMenuWriterDelegate)Marshal.GetDelegateForFunctionPointer(
                    CreateCueMenuWriterBridge(CreateAlbumMetadataStoreTrampoline(
                        cue.MenuWriters[i], CueSheetHookLayout.MenuPrologues[i])),
                    typeof(CueMenuWriterDelegate));
            }

            List<uint> patched = new List<uint>();
            try
            {
                foreach (uint call in cue.ConversionCalls)
                {
                    patched.Add(call);
                    WriteMemoryPatch(call, CueCallBytes(RuntimeVa(call),
                        Pointer32(Marshal.GetFunctionPointerForDelegate(cueConvertHook))));
                }
                foreach (uint call in cue.WriteCalls)
                {
                    patched.Add(call);
                    WriteMemoryPatch(call, CueCallBytes(RuntimeVa(call),
                        Pointer32(Marshal.GetFunctionPointerForDelegate(cueWriteHook))));
                }
                patched.Add(cue.ImageWriter);
                WriteJumpPatch(cue.ImageWriter,
                    Pointer32(Marshal.GetFunctionPointerForDelegate(cueImageWriterHook)),
                    CueSheetHookLayout.ImagePrologue.Length);
                patched.Add(cue.TextWriter);
                WriteJumpPatch(cue.TextWriter,
                    Pointer32(Marshal.GetFunctionPointerForDelegate(cueTextWriterHook)),
                    CueSheetHookLayout.TextPrologue.Length);
                for (int i = 0; i < cue.MenuWriters.Length; i++)
                {
                    patched.Add(cue.MenuWriters[i]);
                    WriteJumpPatch(cue.MenuWriters[i],
                        Pointer32(Marshal.GetFunctionPointerForDelegate(cueMenuWriterHooks[i])),
                        CueSheetHookLayout.MenuPrologues[i].Length);
                }
            }
            catch (Exception installationError)
            {
                bool restored = true;
                for (int i = patched.Count - 1; i >= 0; i--)
                {
                    try { WriteMemoryPatch(patched[i], originals[patched[i]]); }
                    catch (Exception rollbackError)
                    {
                        restored = false;
                        Log("Could not restore CUE hook at 0x" + patched[i].ToString("X8") +
                            ": " + rollbackError);
                    }
                }
                throw new InvalidOperationException("CUE encoding hooks could not be installed; " +
                    (restored ? "all changes were restored." : "some changes could not be restored."),
                    installationError);
            }
            Log("UTF-8 CUE sheet output enabled for " + layout.Name + ".");
        }

        internal static byte[] CueCallBytes(uint call, uint target)
        {
            byte[] bytes = new byte[5];
            bytes[0] = 0xE8;
            Buffer.BlockCopy(BitConverter.GetBytes(unchecked(target - call - 5)), 0, bytes, 1, 4);
            return bytes;
        }

        internal static IntPtr CreateCueMenuWriterBridge(IntPtr nativeWriter)
        {
            // Despite RET 32, the three Modula-2 menu writers do not preserve
            // EBX/ESI/EDI: their scratch locals reuse their register-save slots.
            // Ghidra + native execution confirm ESI can return as 0xFF/0x1FF.
            // CLR's interop stub keeps its Thread pointer in ESI. Own a separate
            // set of saves around the call before returning to managed code.
            IntPtr bridge = NativeMethods.VirtualAlloc(IntPtr.Zero, new UIntPtr(64),
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE,
                NativeMethods.PAGE_EXECUTE_READWRITE);
            if (bridge == IntPtr.Zero)
                throw new InvalidOperationException("Could not allocate the CUE menu calling-convention bridge.");
            X86CodeBuilder code = new X86CodeBuilder(bridge);
            code.Emit(new byte[] { 0x55, 0x89, 0xE5, 0x53, 0x56, 0x57 });
            for (int offset = 0x24; offset >= 8; offset -= 4)
                code.Emit(new byte[] { 0xFF, 0x75, (byte)offset });
            code.EmitCall(Pointer32(nativeWriter));
            code.Emit(new byte[] { 0x5F, 0x5E, 0x5B, 0xC9, 0xC2, 0x20, 0 });
            byte[] bytes = code.ToArray();
            Marshal.Copy(bytes, 0, bridge, bytes.Length);
            NativeMethods.FlushInstructionCache(NativeMethods.GetCurrentProcess(), bridge,
                new UIntPtr((uint)bytes.Length));
            return bridge;
        }

        private static void WriteUnicodeImageCue(
            uint relative, IntPtr metadata, uint binary, IntPtr filename,
            uint filenameBound, IntPtr unused, uint unusedBound, IntPtr path, uint pathBound)
        {
            CueEncodingSession previous = cueEncodingSession;
            try
            {
                cueEncodingSession = IsUtf8CueSheetsEnabled() ? new CueEncodingSession() : null;
                originalCueImageWriter(relative, metadata, binary, filename,
                    filenameBound, unused, unusedBound, path, pathBound);
            }
            catch (Exception error) { Log("Unicode image CUE generation failed: " + error); }
            finally { cueEncodingSession = previous; }
        }

        private static void WriteUnicodeTextCue(IntPtr path, uint pathBound)
        {
            CueEncodingSession previous = cueEncodingSession;
            try
            {
                cueEncodingSession = IsUtf8CueSheetsEnabled() ? new CueEncodingSession() : null;
                originalCueTextWriter(path, pathBound);
            }
            catch (Exception error) { Log("Unicode track CUE generation failed: " + error); }
            finally { cueEncodingSession = previous; }
        }

        private static void WriteUnicodeMenuCue0(IntPtr metadata, IntPtr extension, uint extensionBound,
            uint binary, IntPtr unused, uint unusedBound, IntPtr path, uint pathBound)
        {
            WriteUnicodeMenuCue(0, metadata, extension, extensionBound, binary, unused, unusedBound, path, pathBound);
        }

        private static void WriteUnicodeMenuCue1(IntPtr metadata, IntPtr extension, uint extensionBound,
            uint binary, IntPtr unused, uint unusedBound, IntPtr path, uint pathBound)
        {
            WriteUnicodeMenuCue(1, metadata, extension, extensionBound, binary, unused, unusedBound, path, pathBound);
        }

        private static void WriteUnicodeMenuCue2(IntPtr metadata, IntPtr extension, uint extensionBound,
            uint binary, IntPtr unused, uint unusedBound, IntPtr path, uint pathBound)
        {
            WriteUnicodeMenuCue(2, metadata, extension, extensionBound, binary, unused, unusedBound, path, pathBound);
        }

        private static void WriteUnicodeMenuCue(int kind, IntPtr metadata, IntPtr extension, uint extensionBound,
            uint binary, IntPtr unused, uint unusedBound, IntPtr path, uint pathBound)
        {
            CueEncodingSession previous = cueEncodingSession;
            try
            {
                cueEncodingSession = IsUtf8CueSheetsEnabled() ? new CueEncodingSession() : null;
                originalCueMenuWriters[kind](metadata, extension, extensionBound, binary,
                    unused, unusedBound, path, pathBound);
            }
            catch (Exception error) { Log("Unicode menu CUE generation failed (writer " + kind + "): " + error); }
            finally { cueEncodingSession = previous; }
        }

        private static byte CaptureCueUnicode(
            int capacity, IntPtr destination, uint sourceBound, IntPtr source)
        {
            CueEncodingSession session = cueEncodingSession;
            if (session == null)
                return originalCueConvert(capacity, destination, sourceBound, source);
            try
            {
                string marker = session.Text.Capture(Marshal.PtrToStringUni(source));
                byte[] bytes = Encoding.ASCII.GetBytes(marker + "\0");
                if (bytes.Length > capacity)
                    throw new InvalidOperationException("CUE placeholder exceeds native buffer.");
                Marshal.Copy(bytes, 0, destination, bytes.Length);
                return 1;
            }
            catch (Exception error)
            {
                session.Failed = true;
                Marshal.WriteByte(destination, 0);
                Log("Could not preserve Unicode CUE text: " + error);
                return 0;
            }
        }

        private static void WriteCueUtf8(uint count, IntPtr source, uint sourceBound, IntPtr stream)
        {
            CueEncodingSession session = cueEncodingSession;
            if (session == null)
            {
                originalCueWrite(count, source, sourceBound, stream);
                return;
            }
            try
            {
                if (session.Failed)
                {
                    Marshal.WriteInt32(stream, 5); // EAC stream I/O error; never write placeholders.
                    return;
                }
                if (count == 0)
                    return;
                byte[] bytes = new byte[checked((int)count)];
                Marshal.Copy(source, bytes, 0, bytes.Length);
                byte[] utf8 = session.Text.Expand(bytes);
                if (!session.Started)
                {
                    WriteCueBytes(new byte[] { 0xEF, 0xBB, 0xBF }, stream);
                    session.Started = true;
                }
                WriteCueBytes(utf8, stream);
            }
            catch (Exception error)
            {
                session.Failed = true;
                Marshal.WriteInt32(stream, 5);
                Log("Could not write UTF-8 CUE text: " + error);
            }
        }

        private static void WriteCueBytes(byte[] bytes, IntPtr stream)
        {
            GCHandle pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                originalCueWrite((uint)bytes.Length, pinned.AddrOfPinnedObject(),
                    (uint)bytes.Length, stream);
            }
            finally { pinned.Free(); }
        }
    }
}
