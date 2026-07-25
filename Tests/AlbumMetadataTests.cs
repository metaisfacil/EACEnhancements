using System;

namespace AudioDataPlugIn
{
    internal static class AlbumMetadataTests
    {
        private static int Main()
        {
            AssertEqual(
                "--barcode \"012345678905\" --catalog \"ABC-123\" %source%",
                EnhancementRuntime.ExpandAlbumMetadataTokens(
                    "--barcode \"%barcode%\" --catalog \"%catalognumber%\" %source%",
                    "012345678905",
                    "ABC-123",
                    "Merge"),
                "both album metadata tokens");
            AssertEqual(
                "first / second",
                EnhancementRuntime.ExpandAlbumMetadataTokens(
                    "%BARCODE% / %CatalogNumber%",
                    "first",
                    "second",
                    "label"),
                "case-insensitive token names");
            AssertEqual(
                "'quoted' 50%%",
                EnhancementRuntime.ExpandAlbumMetadataTokens(
                    "%barcode% %catalognumber%",
                    "\"quoted\"",
                    "50%",
                    "label"),
                "values safe for EAC's second-stage formatter");
            AssertEqual(
                "unchanged %source%",
                EnhancementRuntime.ExpandAlbumMetadataTokens(
                    "unchanged %source%",
                    "ignored",
                    "ignored",
                    "ignored"),
                "normal EAC tokens");
            AssertEqual(
                "empty: ",
                EnhancementRuntime.ExpandAlbumMetadataTokens(
                    "empty: %barcode%",
                    null,
                    null,
                    null),
                "empty metadata");
            AssertEqual(
                "label: Merge Records",
                EnhancementRuntime.ExpandAlbumMetadataTokens(
                    "label: %label%",
                    null,
                    null,
                    "Merge Records"),
                "CD label token");
            AssertEqual(
                9,
                EnhancementRuntime.MatchCustomAlbumMetadataToken(
                    "-T \"BARCODE=%barcode%\"",
                    12),
                "barcode validation token");
            AssertEqual(
                15,
                EnhancementRuntime.MatchCustomAlbumMetadataToken(
                    "%CatalogNumber%",
                    0),
                "case-insensitive catalog-number validation token");
            AssertEqual(
                7,
                EnhancementRuntime.MatchCustomAlbumMetadataToken(
                    "-T \"LABEL=%LABEL%\"",
                    10),
                "case-insensitive label validation token");
            AssertEqual(
                0,
                EnhancementRuntime.MatchCustomAlbumMetadataToken(
                    "%barcode_extra%",
                    0),
                "non-matching replacement tag");
            AssertEqual(
                0,
                EnhancementRuntime.MatchCustomAlbumMetadataToken(
                    "%barcode%",
                    -1),
                "invalid token index");
            AssertEqual(
                15,
                EnhancementRuntime.MatchFilenameValidationAlbumMetadataToken(
                    "%catalognumber%",
                    0,
                    0x100),
                "Filename-page catalog-number token");
            AssertEqual(
                0,
                EnhancementRuntime.MatchFilenameValidationAlbumMetadataToken(
                    "%catalognumber%",
                    0,
                    0x230),
                "filename normalizer remains native");
            AssertEqual(
                0,
                EnhancementRuntime.MatchFilenameValidationAlbumMetadataToken(
                    "%catalognumber%",
                    0,
                    0x1FFF),
                "filename formatter remains native");

            Console.WriteLine("Album metadata token tests passed.");
            return 0;
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
