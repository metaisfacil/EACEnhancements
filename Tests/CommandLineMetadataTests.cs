using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AudioDataPlugIn
{
    internal static class CommandLineMetadataTests
    {
        private static int Main()
        {
            string json =
                "{\"disc\":{\"trackCount\":2,\"cddbId\":\"89ABCDEF\"," +
                "\"leadoutPosition\":12345,\"trackStartPositions\":[150,6000]," +
                "\"albumArtist\":\"Artist\",\"albumTitle\":\"Album\",\"year\":2026," +
                "\"mp3V2Type\":\"Rock\",\"extendedDiscInformation\":\"Comment\"," +
                "\"label\":\"Label\",\"barcode\":\"012345678905\"," +
                "\"catalogNumber\":\"ABC-123\"}," +
                "\"tracks\":[{\"number\":1,\"title\":\"One\",\"artist\":\"Artist\"}," +
                "{\"number\":2,\"title\":\"Two\",\"artist\":\"Guest\"}]}";
            string d1 = Encode(json);
            CommandLineMetadata metadata = D1MetadataCodec.Decode(d1);
            Assert(metadata.TrackCount == 2, "track count");
            Assert(metadata.CddbId == 0x89ABCDEFu, "CDDB ID");
            Assert(metadata.TrackStartPositions[1] == 6000, "TOC");
            Assert(metadata.AlbumTitle == "Album", "album title");
            Assert(metadata.Label == "Label", "label");
            Assert(metadata.Barcode == "012345678905", "barcode");
            Assert(metadata.CatalogNumber == "ABC-123", "catalog number");
            Assert(metadata.Tracks[1].Title == "Two", "track title");

            CommandLineInvocation invocation = CommandLineInvocation.Parse(new[]
            {
                "EAC.exe", "--eace-100-log", "--eace-drive=j:",
                "--eace-dest=C:\\Rips\\%albumartist% - %albumtitle%",
                "--eace-metadata=" + d1
            });
            Assert(invocation.RunHundredPercentLog && invocation.Metadata != null, "combined invocation");
            Assert(invocation.Drive == "j:", "drive selector");
            Assert(
                invocation.Destination ==
                    "C:\\Rips\\%albumartist% - %albumtitle%",
                "destination template");
            AssertThrows(delegate { CommandLineInvocation.Parse(new[] { "EAC.exe", "--eace-100-log" }); });
            AssertThrows(delegate { CommandLineInvocation.Parse(new[] { "EAC.exe", "--eace-drive=J:" }); });
            AssertThrows(delegate
            {
                CommandLineInvocation.Parse(new[]
                {
                    "EAC.exe", "--eace-dest=relative\\album",
                    "--eace-metadata=" + d1
                });
            });
            AssertThrows(delegate
            {
                CommandLineInvocation.Parse(new[]
                {
                    "EAC.exe", "--eace-dest=C:\\Album",
                    "--eace-metadata=" + d1
                });
            });
            AssertThrows(delegate
            {
                CommandLineInvocation.Parse(new[]
                {
                    "EAC.exe", "--eace-drive=J:", "--eace-drive=F:",
                    "--eace-metadata=" + d1
                });
            });
            AssertThrows(delegate { D1MetadataCodec.Decode("d1.A"); });
            AssertThrows(delegate
            {
                D1MetadataCodec.Decode(Encode(
                    "{\"disc\":{\"trackCount\":1,\"surprise\":true},\"tracks\":[{}]}"));
            });
            AssertDriveSelection();
            AssertNoMediaErrors();
            AssertCommandIdsDoNotCollide();
            AssertMetadataReplacementAddresses();
            AssertCommandLineErrorFormatting();

            Console.WriteLine("Command-line metadata tests passed.");
            return 0;
        }

        private static void AssertDriveSelection()
        {
            string[] drives =
            {
                "ASUS    DRW-24B1ST   j 1.11 Adapter: 0 ID: 0",
                "TEAC    DW-224E-V 4.CA Adapter: 1 ID: 2"
            };
            Assert(
                EnhancementRuntime.NormalizeDriveLetter("j:\\") == "J:",
                "drive-letter normalization");
            Assert(
                EnhancementRuntime.MatchCommandLineDriveItem(
                    "J:",
                    "TEAC DW-224E-V 4.CA",
                    drives) == 1,
                "drive-letter identity matching");
            Assert(
                EnhancementRuntime.MatchCommandLineDriveItem(
                    "ASUS DRW-24B1ST",
                    null,
                    drives) == 0,
                "drive-name matching");
            Assert(
                EnhancementRuntime.MatchCommandLineDriveItem(
                    "Drive",
                    null,
                    new[] { "Drive One", "Drive Two" }) == -2,
                "ambiguous drive matching");
        }

        private static void AssertNoMediaErrors()
        {
            Assert(
                EnhancementRuntime.IsNoMediaDeviceError(21),
                "ERROR_NOT_READY is a no-media result");
            Assert(
                EnhancementRuntime.IsNoMediaDeviceError(1112),
                "ERROR_NO_MEDIA_IN_DRIVE is a no-media result");
            Assert(
                !EnhancementRuntime.IsNoMediaDeviceError(5),
                "unrelated device errors remain inconclusive");
        }

        private static void AssertCommandIdsDoNotCollide()
        {
            uint[] commandLine =
            {
                EnhancementRuntime.StartCommandLineRequestCommand,
                EnhancementRuntime.BeginCommandLineMetadataCommand,
                EnhancementRuntime.FinishCommandLineMetadataCommand,
                EnhancementRuntime.FailCommandLineMetadataCommand,
                EnhancementRuntime.FinishCommandLineRunCommand
            };
            uint[] occupied =
            {
                0xA312, 0xA313, 0xA314, 0xA315,
                0xA316, 0xA317, 0xA318, 0xA319,
                0xA31A, 0xA31B, 0xA31C, 0xA31D,
                0xA31E, 0xA31F, 0xA320, 0xA321
            };
            for (int i = 0; i < commandLine.Length; i++)
            {
                for (int j = i + 1; j < commandLine.Length; j++)
                    Assert(commandLine[i] != commandLine[j], "unique command-line dispatcher IDs");
                foreach (uint value in occupied)
                    Assert(commandLine[i] != value, "command-line dispatcher ID collision");
            }
        }

        private static void AssertMetadataReplacementAddresses()
        {
            uint guard;
            uint accepted;
            EnhancementRuntime.SelectCommandLineMetadataReplacementAddresses(
                "EAC 1.8",
                out guard,
                out accepted);
            Assert(
                guard == 0x0040875A && accepted == 0x0040879D,
                "EAC 1.8 metadata replacement branch");
            EnhancementRuntime.SelectCommandLineMetadataReplacementAddresses(
                "EAC 1.6",
                out guard,
                out accepted);
            Assert(
                guard == 0x00408576 && accepted == 0x004085B9,
                "EAC 1.6 metadata replacement branch");
        }

        private static void AssertCommandLineErrorFormatting()
        {
            string text = EnhancementRuntime.FormatCommandLineRipErrors(
                new[] { "Read error - track 2", "Sync error - track 4" },
                @"C:\Rip\Album.log");
            Assert(
                text.Contains("rip completed with errors") &&
                text.Contains("Read error - track 2") &&
                text.Contains(@"Log: C:\Rip\Album.log"),
                "stderr rip-error formatting");

            TextWriter previous = Console.Error;
            StringWriter capture = new StringWriter();
            try
            {
                Console.SetError(capture);
                EnhancementRuntime.WriteCommandLineStandardError("failure");
                Assert(
                    capture.ToString().Contains("failure"),
                    "stderr output");
            }
            finally
            {
                Console.SetError(previous);
            }
        }

        private static string Encode(string json)
        {
            byte[] input = Encoding.UTF8.GetBytes(json);
            byte[] compressed;
            using (MemoryStream output = new MemoryStream())
            {
                using (DeflateStream deflater = new DeflateStream(output, CompressionMode.Compress, true))
                    deflater.Write(input, 0, input.Length);
                compressed = output.ToArray();
            }
            return "d1." + Convert.ToBase64String(compressed)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static void Assert(bool condition, string description)
        {
            if (!condition)
                throw new Exception("Failed: " + description);
        }

        private static void AssertThrows(Action action)
        {
            try { action(); }
            catch (FormatException) { return; }
            throw new Exception("Expected FormatException.");
        }
    }
}
