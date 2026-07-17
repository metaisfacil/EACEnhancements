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
                "\"mp3V2Type\":\"Rock\",\"extendedDiscInformation\":\"Comment\"}," +
                "\"tracks\":[{\"number\":1,\"title\":\"One\",\"artist\":\"Artist\"}," +
                "{\"number\":2,\"title\":\"Two\",\"artist\":\"Guest\"}]}";
            string d1 = Encode(json);
            CommandLineMetadata metadata = D1MetadataCodec.Decode(d1);
            Assert(metadata.TrackCount == 2, "track count");
            Assert(metadata.CddbId == 0x89ABCDEFu, "CDDB ID");
            Assert(metadata.TrackStartPositions[1] == 6000, "TOC");
            Assert(metadata.AlbumTitle == "Album", "album title");
            Assert(metadata.Tracks[1].Title == "Two", "track title");

            CommandLineInvocation invocation = CommandLineInvocation.Parse(new[]
            {
                "EAC.exe", "--eace-100-log", "--eace-metadata=" + d1
            });
            Assert(invocation.RunHundredPercentLog && invocation.Metadata != null, "combined invocation");
            AssertThrows(delegate { CommandLineInvocation.Parse(new[] { "EAC.exe", "--eace-100-log" }); });
            AssertThrows(delegate { D1MetadataCodec.Decode("d1.A"); });
            AssertThrows(delegate
            {
                D1MetadataCodec.Decode(Encode(
                    "{\"disc\":{\"trackCount\":1,\"surprise\":true},\"tracks\":[{}]}"));
            });

            Console.WriteLine("Command-line metadata tests passed.");
            return 0;
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
