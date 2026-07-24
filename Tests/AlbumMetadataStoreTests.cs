using System;
using System.Runtime.InteropServices;

namespace AudioDataPlugIn
{
    internal static class AlbumMetadataStoreTests
    {
        private static int Main()
        {
            TestRoundTripPreservesNativeText();
            TestUnicodeAndControlCharacters();
            TestEmptyValuesRemoveStoredPayload();
            TestMalformedOrForeignTextIsUntouched();
            TestNativeBufferAccess();

            Console.WriteLine("Album metadata store tests passed.");
            return 0;
        }

        private static void TestRoundTripPreservesNativeText()
        {
            const string original =
                "Original EAC extended-disc text\r\nwith two lines.";
            string stored = EnhancementRuntime.MergeAlbumMetadataStorePayload(
                original,
                "Merge Records",
                "602537521180",
                "MRG485B-1");
            if (!stored.StartsWith(
                original + "\r\nEACEnhancements/1:",
                StringComparison.Ordinal))
            {
                throw new Exception(
                    "The payload was not appended as namespaced native text.");
            }
            if (stored.IndexOf('\0') >= 0)
            {
                throw new Exception(
                    "The persisted representation contains an embedded null.");
            }

            string clean;
            string label;
            string barcode;
            string catalogNumber;
            bool found =
                EnhancementRuntime.TryExtractAlbumMetadataStorePayload(
                    stored,
                    out clean,
                    out label,
                    out barcode,
                    out catalogNumber);
            Assert(found, "round-trip payload was recognized");
            AssertEqual(original, clean, "original extended-disc text");
            AssertEqual("Merge Records", label, "label");
            AssertEqual("602537521180", barcode, "barcode");
            AssertEqual("MRG485B-1", catalogNumber, "catalog number");

            string replaced =
                EnhancementRuntime.MergeAlbumMetadataStorePayload(
                    stored,
                    "Sonovox",
                    "123",
                    "ABC");
            AssertEqual(
                1,
                Count(replaced, "EACEnhancements/1:"),
                "only one current payload");
        }

        private static void TestUnicodeAndControlCharacters()
        {
            string stored = EnhancementRuntime.MergeAlbumMetadataStorePayload(
                String.Empty,
                "Étiquette 日本",
                "12%34\r\n56",
                "\"CAT\" \u2603");
            string clean;
            string label;
            string barcode;
            string catalogNumber;
            Assert(
                EnhancementRuntime.TryExtractAlbumMetadataStorePayload(
                    stored,
                    out clean,
                    out label,
                    out barcode,
                    out catalogNumber),
                "Unicode payload was recognized");
            AssertEqual(String.Empty, clean, "empty native text");
            AssertEqual("Étiquette 日本", label, "Unicode label");
            AssertEqual("12%34\r\n56", barcode, "barcode control characters");
            AssertEqual("\"CAT\" \u2603", catalogNumber, "Unicode catalog");
        }

        private static void TestEmptyValuesRemoveStoredPayload()
        {
            string stored = EnhancementRuntime.MergeAlbumMetadataStorePayload(
                "Native",
                "Label",
                "Barcode",
                "Catalog");
            string removed = EnhancementRuntime.MergeAlbumMetadataStorePayload(
                stored,
                null,
                null,
                null);
            AssertEqual(
                "Native",
                removed,
                "empty fields remove only the enhancement payload");
        }

        private static void TestMalformedOrForeignTextIsUntouched()
        {
            string[] values =
            {
                "Ordinary extended-disc metadata",
                "EACEnhancements/1:not*base64",
                "EACEnhancements/2:eyJmb28iOiJiYXIifQ",
                "prefix\r\nEACEnhancements/1:e30"
            };
            foreach (string value in values)
            {
                string clean;
                string label;
                string barcode;
                string catalogNumber;
                if (EnhancementRuntime.TryExtractAlbumMetadataStorePayload(
                    value,
                    out clean,
                    out label,
                    out barcode,
                    out catalogNumber))
                {
                    throw new Exception(
                        "Foreign or malformed native text was claimed: " +
                        value);
                }
                AssertEqual(value, clean, "foreign native text");
            }
        }

        private static void TestNativeBufferAccess()
        {
            const int capacity = 64;
            IntPtr buffer = Marshal.AllocHGlobal(capacity * sizeof(char));
            try
            {
                EnhancementRuntime.WriteAlbumMetadataStoreBuffer(
                    buffer,
                    capacity,
                    "Native \u2603");
                AssertEqual(
                    "Native \u2603",
                    EnhancementRuntime.ReadAlbumMetadataStoreBuffer(
                        buffer,
                        capacity),
                    "native UTF-16 buffer round trip");

                bool rejected = false;
                try
                {
                    EnhancementRuntime.WriteAlbumMetadataStoreBuffer(
                        buffer,
                        capacity,
                        new string('x', capacity));
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }
                Assert(rejected, "unterminated full-capacity value rejected");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static int Count(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(
                value,
                index,
                StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static void Assert(bool condition, string description)
        {
            if (!condition)
                throw new Exception("Failed: " + description + ".");
        }

        private static void AssertEqual(
            int expected,
            int actual,
            string description)
        {
            if (expected != actual)
            {
                throw new Exception(
                    "Failed " + description + ": expected " + expected +
                    ", got " + actual + ".");
            }
        }

        private static void AssertEqual(
            string expected,
            string actual,
            string description)
        {
            if (!String.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new Exception(
                    "Failed " + description + ": expected '" + expected +
                    "', got '" + actual + "'.");
            }
        }
    }
}
