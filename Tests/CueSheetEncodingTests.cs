using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioDataPlugIn
{
    internal static class CueSheetEncodingTests
    {
        private static readonly Type Runtime = typeof(EnhancementRuntime);
        private static MemoryStream output;
        private static bool nested;
        private static bool fail;
        private static bool failConversion;
        private static int legacyConversions;
        private static readonly string Filename = new string('\u65E5', 240) + "\U0001F3B5.flac";

        private static int Main()
        {
            string iniPath = EnhancementRuntime.GetSettingsFilePath();
            bool existed = File.Exists(iniPath);
            byte[] original = existed ? File.ReadAllBytes(iniPath) : null;
            try
            {
                File.WriteAllText(iniPath, "[Extraction]\r\nUseUtf8CueSheets=1\r\n", Encoding.Unicode);
                RunTests();
                TestSavedPreference(iniPath);
            }
            finally
            {
                if (existed) File.WriteAllBytes(iniPath, original);
                else File.Delete(iniPath);
            }
            return 0;
        }

        private static void RunTests()
        {
            TestUnicodeTemplates();
            TestLayouts();
            TestNativeMenuWriterRegisters();
            SetDelegate("originalCueConvert", "LegacyConvert");
            SetDelegate("originalCueWrite", "CollectWrite");
            SetDelegate("originalCueTextWriter", "NativeTextWriter");
            SetDelegate("originalCueImageWriter", "NativeImageWriter");
            SetMenuDelegates("NativeMenuWriter");
            TestWriterScope("WriteUnicodeTextCue", new object[] { IntPtr.Zero, 0u });
            TestWriterScope("WriteUnicodeImageCue", new object[] {
                0u, IntPtr.Zero, 0u, IntPtr.Zero, 0u, IntPtr.Zero, 0u, IntPtr.Zero, 0u });
            for (int i = 0; i < 3; i++)
                TestWriterScope("WriteUnicodeMenuCue" + i, new object[] {
                    IntPtr.Zero, IntPtr.Zero, 0u, 0u, IntPtr.Zero, 0u, IntPtr.Zero, 0u });
            TestUnrelatedWritesAndFailureCleanup();
            Console.WriteLine("CUE sheet Unicode encoding tests passed.");
        }

        private static void TestSavedPreference(string iniPath)
        {
            NativeMethods.WritePrivateProfileStringW("Extraction", "UseUtf8CueSheets", null, iniPath);
            Assert((bool)Invoke("IsUtf8CueSheetsEnabled"), "Missing preference defaults to enabled");
            Assert((bool)Invoke("WriteIniValue", iniPath, "UseUtf8CueSheets", "0"),
                "Disabled preference is saved");
            Assert(!((OutputTemplateSettings)Invoke("LoadOutputTemplateSettings")).UseUtf8CueSheets,
                "Saved preference is loaded into the settings dialog model");
            SetDelegate("originalCueTextWriter", "NativeAnsiTextWriter");
            SetDelegate("originalCueImageWriter", "NativeAnsiImageWriter");
            SetMenuDelegates("NativeAnsiMenuWriter");
            int before = legacyConversions;
            output = new MemoryStream();
            Invoke("WriteUnicodeTextCue", IntPtr.Zero, 0u);
            Equal("TITLE \"legacy\"\r\n", Decode(output.ToArray()),
                "Disabled track CUE uses native conversion and writes without a BOM");
            output = new MemoryStream();
            Invoke("WriteUnicodeImageCue", 0u, IntPtr.Zero, 0u, IntPtr.Zero,
                0u, IntPtr.Zero, 0u, IntPtr.Zero, 0u);
            Equal("TITLE \"legacy\"\r\n", Decode(output.ToArray()),
                "Disabled image CUE uses native conversion and writes without a BOM");
            for (int i = 0; i < 3; i++)
            {
                output = new MemoryStream();
                Invoke("WriteUnicodeMenuCue" + i, IntPtr.Zero, IntPtr.Zero, 0u,
                    0u, IntPtr.Zero, 0u, IntPtr.Zero, 0u);
                Equal("TITLE \"legacy\"\r\n", Decode(output.ToArray()),
                    "Disabled menu CUE uses native conversion and writes without a BOM");
            }
            Assert(legacyConversions == before + 5, "All disabled writers use the original converter");
            Assert((bool)Invoke("WriteIniValue", iniPath, "UseUtf8CueSheets", "1"),
                "Enabled preference is saved");
            Assert(((OutputTemplateSettings)Invoke("LoadOutputTemplateSettings")).UseUtf8CueSheets,
                "Enabled preference reloads correctly");
            SetDelegate("originalCueTextWriter", "NativeTextWriter");
            SetDelegate("originalCueImageWriter", "NativeImageWriter");
            SetMenuDelegates("NativeMenuWriter");
            legacyConversions = 0;
            TestWriterScope("WriteUnicodeTextCue", new object[] { IntPtr.Zero, 0u });
        }

        private static void NativeAnsiTextWriter(IntPtr path, uint bound)
        {
            Write("TITLE \"" + Capture("\u65E5\u672C\u8A9E") + "\"\r\n");
        }

        private static void NativeAnsiImageWriter(uint relative, IntPtr metadata, uint binary,
            IntPtr filename, uint filenameBound, IntPtr unused, uint unusedBound,
            IntPtr path, uint pathBound)
        {
            NativeAnsiTextWriter(path, pathBound);
        }

        private static void NativeMenuWriter(IntPtr metadata, IntPtr extension, uint extensionBound,
            uint binary, IntPtr unused, uint unusedBound, IntPtr path, uint pathBound)
        {
            // The menu's custom extension conversion has only 31 bytes available.
            Equal(".flac", DecodeForCurrentSession(Capture(".flac", 31)), "Small extension buffer");
            NativeTextWriter(path, pathBound);
        }

        private static string DecodeForCurrentSession(string marker)
        {
            object session = Runtime.GetField("cueEncodingSession", BindingFlags.Static |
                BindingFlags.NonPublic).GetValue(null);
            CueSheetTextEncoding text = (CueSheetTextEncoding)session.GetType().GetField(
                "Text", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
            return Decode(text.Expand(Encoding.ASCII.GetBytes(marker)));
        }

        private static void NativeAnsiMenuWriter(IntPtr metadata, IntPtr extension, uint extensionBound,
            uint binary, IntPtr unused, uint unusedBound, IntPtr path, uint pathBound)
        {
            NativeAnsiTextWriter(path, pathBound);
        }

        private static void TestUnicodeTemplates()
        {
            CueSheetTextEncoding encoding = new CueSheetTextEncoding();
            string genre = encoding.Capture("\u65E5\u672C Jazz");
            string title = encoding.Capture("Bj\u00F6rk \u041C\u0443\u0437\u044B\u043A\u0430 \U0001F3B5");
            string file = encoding.Capture(Filename);
            Assert(file.Length < 80, "Filename fits EAC's smallest formatter buffer.");
            string template = "REM GENRE \"" + genre + "\"\r\nTITLE \"" + title +
                "\"\r\nFILE \"" + file + "\" WAVE\r\n" +
                "  TRACK 01 AUDIO\r\n    FLAGS PRE DCP\r\n    INDEX 00 00:00:00\r\n" +
                "    INDEX 01 00:02:00\r\nFILE \"" + file + "\" WAVE\r\n";
            string actual = Decode(encoding.Expand(Encoding.ASCII.GetBytes(template)));
            Equal(template.Replace(genre, "\u65E5\u672C Jazz")
                .Replace(title, "Bj\u00F6rk \u041C\u0443\u0437\u044B\u043A\u0430 \U0001F3B5")
                .Replace(file, Filename), actual, "Unicode text and exact native track layout");
            Equal("", encoding.Capture(null), "Empty metadata");
            Equal("'quoted'  text ", Decode(encoding.Expand(Encoding.ASCII.GetBytes(
                encoding.Capture("\"quoted\"\r\ntext\t")))), "CUE quote/newline safety");

            // Metadata is literal, even if it happens to contain a valid marker.
            string literalMarker = encoding.Capture(title);
            Equal(title, Decode(encoding.Expand(Encoding.ASCII.GetBytes(literalMarker))),
                "Inserted text is never expanded again");
        }

        private static void TestLayouts()
        {
            foreach (EacVersionLayout eac in EacVersionLayout.KnownLayouts)
            {
                CueSheetHookLayout cue = new CueSheetHookLayout(eac.Name);
                Assert(cue.ConversionCalls.Length == 40, "Every native CUE conversion is hooked.");
                Assert(cue.WriteCalls.Length == 150, "Every native CUE write is hooked.");
                Assert(cue.MenuWriters.Length == 3, "All multiple-file menu writers are hooked.");
                foreach (uint address in cue.ConversionCalls)
                    AssertCall(address, cue.Convert);
                foreach (uint address in cue.WriteCalls)
                    AssertCall(address, cue.Write);
            }
            Assert(new CueSheetHookLayout("EAC 1.6").Convert !=
                new CueSheetHookLayout("EAC 1.8").Convert, "Version-specific native helpers");
        }

        private static void AssertCall(uint address, uint target)
        {
            byte[] call = EnhancementRuntime.CueCallBytes(address, target);
            Assert(call[0] == 0xE8 && unchecked(address + 5 +
                BitConverter.ToUInt32(call, 1)) == target, "Native rel32 CALL target");
        }

        private static void TestWriterScope(string method, object[] args)
        {
            output = new MemoryStream();
            nested = false;
            Invoke(method, args);
            AssertOutput(output.ToArray());
            Assert(legacyConversions == 0, "CUE text never passes through the ANSI converter.");
            Assert(Runtime.GetField("cueEncodingSession", BindingFlags.Static |
                BindingFlags.NonPublic).GetValue(null) == null, "Writer scope is cleared.");
        }

        private static void NativeTextWriter(IntPtr path, uint bound)
        {
            if (failConversion)
            {
                IntPtr source = Marshal.StringToHGlobalUni("Unicode CUE text");
                IntPtr destination = Marshal.AllocHGlobal(1);
                try
                {
                    Assert((byte)Invoke("CaptureCueUnicode", 1, destination, 255u, source) == 0,
                        "Failed conversion reports failure across the native ABI");
                    Write("TITLE \"placeholder\"\r\n");
                }
                finally
                {
                    Marshal.FreeHGlobal(source);
                    Marshal.FreeHGlobal(destination);
                }
                return;
            }
            string filename = Capture(Filename);
            string artist = Capture("\u65E5\u672C\u8A9E \U0001F3B5");
            if (fail)
                throw new InvalidOperationException("Simulated writer failure");
            if (!nested)
            {
                nested = true;
                MemoryStream outer = output;
                output = new MemoryStream();
                Invoke("WriteUnicodeTextCue", IntPtr.Zero, 0u);
                AssertOutput(output.ToArray());
                output = outer;
            }
            // EAC's REM helper emits several separate writes; the track writer
            // emits an entire sheet. Exercise both with the same stream callback.
            Write("REM GENRE ");
            Write("\"");
            Write(artist);
            Write("\"\r\nFILE \"" + filename + "\" WAVE\r\n");
            Write("  TRACK 01 AUDIO\r\n    INDEX 01 00:00:00\r\n");
        }

        private static void NativeImageWriter(uint relative, IntPtr metadata, uint binary,
            IntPtr filename, uint filenameBound, IntPtr unused, uint unusedBound,
            IntPtr path, uint pathBound)
        {
            NativeTextWriter(path, pathBound);
        }

        private static string Capture(string value, int capacity = 254)
        {
            IntPtr source = Marshal.StringToHGlobalUni(value);
            IntPtr destination = Marshal.AllocHGlobal(capacity + 1);
            try
            {
                byte success = (byte)Invoke("CaptureCueUnicode", capacity, destination, 255u, source);
                Assert(success == 1, "Native converter ABI success result");
                return Marshal.PtrToStringAnsi(destination);
            }
            finally
            {
                Marshal.FreeHGlobal(source);
                Marshal.FreeHGlobal(destination);
            }
        }

        private static void Write(string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            IntPtr buffer = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
            IntPtr stream = Marshal.AllocHGlobal(56);
            try
            {
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
                Invoke("WriteCueUtf8", (uint)bytes.Length, buffer, (uint)bytes.Length, stream);
                if (failConversion)
                    Assert(Marshal.ReadInt32(stream) == 5, "Failed output sets EAC's stream error");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                Marshal.FreeHGlobal(stream);
            }
        }

        private static void CollectWrite(uint count, IntPtr source, uint bound, IntPtr stream)
        {
            byte[] bytes = new byte[count];
            Marshal.Copy(source, bytes, 0, bytes.Length);
            output.Write(bytes, 0, bytes.Length);
        }

        private static byte LegacyConvert(int capacity, IntPtr destination, uint bound, IntPtr source)
        {
            legacyConversions++;
            Marshal.Copy(Encoding.ASCII.GetBytes("legacy\0"), 0, destination, 7);
            return 1;
        }

        private static void TestUnrelatedWritesAndFailureCleanup()
        {
            Equal("legacy", Capture("anything"), "Unrelated conversion uses EAC's original helper");
            output = new MemoryStream();
            Write("ANSI TOC/log data\r\n");
            Equal("ANSI TOC/log data\r\n", Decode(output.ToArray()), "Unrelated writes have no BOM");
            fail = true;
            Invoke("WriteUnicodeTextCue", IntPtr.Zero, 0u);
            fail = false;
            Assert(Runtime.GetField("cueEncodingSession", BindingFlags.Static |
                BindingFlags.NonPublic).GetValue(null) == null, "Failed writer clears its scope");
            output = new MemoryStream();
            failConversion = true;
            Invoke("WriteUnicodeTextCue", IntPtr.Zero, 0u);
            failConversion = false;
            Assert(output.Length == 0, "Conversion failure never writes placeholders to disk");
            Invoke("WriteUnicodeTextCue", IntPtr.Zero, 0u);
            AssertOutput(output.ToArray());
        }

        private static void AssertOutput(byte[] bytes)
        {
            Assert(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "Exactly one UTF-8 BOM precedes the sheet");
            string text = new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
            Equal("REM GENRE \"\u65E5\u672C\u8A9E \U0001F3B5\"\r\nFILE \"" + Filename +
                "\" WAVE\r\n  TRACK 01 AUDIO\r\n    INDEX 01 00:00:00\r\n", text,
                "UTF-8 stream bytes preserve filenames, metadata, times, and CRLF");
        }

        private static object Invoke(string method, params object[] args)
        {
            return Runtime.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, args);
        }

        private static void SetDelegate(string field, string method)
        {
            FieldInfo target = Runtime.GetField(field, BindingFlags.Static | BindingFlags.NonPublic);
            target.SetValue(null, Delegate.CreateDelegate(target.FieldType,
                typeof(CueSheetEncodingTests).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)));
        }

        private static void SetMenuDelegates(string method)
        {
            Array delegates = (Array)Runtime.GetField("originalCueMenuWriters",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            for (int i = 0; i < delegates.Length; i++)
                delegates.SetValue(Delegate.CreateDelegate(delegates.GetType().GetElementType(),
                    typeof(CueSheetEncodingTests).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)), i);
        }

        private static void TestNativeMenuWriterRegisters()
        {
            // Reproduce the real writers' nonstandard register convention in
            // actual x86 code, through a CLR unmanaged delegate transition.
            byte[] code = {
                0x8B, 0x44, 0x24, 0x04,             // MOV EAX,[ESP+4] (metadata)
                0xC7, 0x00, 0x78, 0x56, 0x34, 0x12,// MOV [EAX],0x12345678
                0xBE, 0xFF, 0, 0, 0,                // MOV ESI,0xFF
                0xBF, 0xFF, 1, 0, 0,                // MOV EDI,0x1FF
                0xBB, 0xFF, 0, 0, 0,                // MOV EBX,0xFF
                0xC2, 0x20, 0                       // RET 32
            };
            IntPtr native = NativeMethods.VirtualAlloc(IntPtr.Zero, new UIntPtr((uint)code.Length),
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE, NativeMethods.PAGE_EXECUTE_READWRITE);
            Assert(native != IntPtr.Zero, "Allocate native register-convention test");
            Marshal.Copy(code, 0, native, code.Length);
            IntPtr bridge = EnhancementRuntime.CreateCueMenuWriterBridge(native);
            Type delegateType = Runtime.GetField("originalCueMenuWriters", BindingFlags.Static |
                BindingFlags.NonPublic).FieldType.GetElementType();
            Delegate invoke = Marshal.GetDelegateForFunctionPointer(bridge, delegateType);
            IntPtr metadata = Marshal.AllocHGlobal(4);
            try
            {
                invoke.DynamicInvoke(metadata, IntPtr.Zero, 0u, 0u, IntPtr.Zero, 0u, IntPtr.Zero, 0u);
                Assert(Marshal.ReadInt32(metadata) == 0x12345678,
                    "Native writer arguments and return survive register clobbering");
            }
            finally { Marshal.FreeHGlobal(metadata); }
        }

        private static string Decode(byte[] bytes) { return new UTF8Encoding(false, true).GetString(bytes); }
        private static void Equal(string expected, string actual, string message)
        {
            Assert(expected == actual, message + ": expected " + expected + ", got " + actual);
        }
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
