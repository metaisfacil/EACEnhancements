using System;
using System.Runtime.InteropServices;

namespace AudioDataPlugIn
{
    internal static class TitleCaseTransformerTests
    {
        private static int Main()
        {
            try
            {
                AssertTrack(
                    "THE  BEST OF ROCK: THE SONG & THE DANCE - THE END",
                    "The Best Of Rock: the Song & The Dance - The End");
                AssertTrack(
                    "SONG BY A BAND AND THE CHOIR OUT OF THE CITY",
                    "Song by a Band and the Choir out of the City");
                AssertTrack(
                    "ALPHA [FEAT. BETA] VS, GAMMA (FT. DELTA) E.P. CD1",
                    "Alpha [ft. Beta] vs. Gamma (ft. Delta) E.P. CD1");
                AssertTrack("CHILDREN'S MUSIC", "Children's Music");
                AssertTrack("ONE--TWO / THREE", "One--Two / Three");
                AssertTrack("ONE   TWO", "One  Two");

                AssertAlbum(
                    "THE BEST OF ROCK II III IV CD2",
                    "The Best of Rock II III IV CD2");
                AssertAlbum(
                    "A JOURNEY INTO THE NIGHT: THE ALBUM",
                    "A Journey into the Night: the Album");

                AssertTrack(String.Empty, String.Empty);
                AssertTrack(null, String.Empty);
                AssertDatabaseMenuResolution();
                Console.WriteLine("Title-case transformer tests passed.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void AssertDatabaseMenuResolution()
        {
            IntPtr root = CreateMenu();
            IntPtr database = CreateMenu();
            IntPtr transform = CreateMenu();
            if (root == IntPtr.Zero || database == IntPtr.Zero || transform == IntPtr.Zero)
                throw new InvalidOperationException("Could not create the menu-placement fixture.");

            try
            {
                if (!NativeMethods.AppendMenuW(
                        root,
                        NativeMethods.MF_POPUP,
                        new UIntPtr(unchecked((uint)database.ToInt32())),
                        "&Database") ||
                    !NativeMethods.AppendMenuW(
                        database,
                        NativeMethods.MF_POPUP,
                        new UIntPtr(unchecked((uint)transform.ToInt32())),
                        "Tr&ansform Current CD Information"))
                {
                    throw new InvalidOperationException("Could not populate the menu-placement fixture.");
                }

                if (EnhancementRuntime.FindDatabaseTransformMenu(root) != transform)
                {
                    throw new InvalidOperationException(
                        "The Database > Transform Current CD Information submenu was not resolved.");
                }
            }
            finally
            {
                DestroyMenu(root);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateMenu();

        private static void AssertTrack(string input, string expected)
        {
            string actual = TitleCaseTransformer.TransformTrackTitle(input);
            if (!String.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Track title mismatch. Expected <" + expected + ">, got <" + actual + ">.");
            }
        }

        private static void AssertAlbum(string input, string expected)
        {
            string actual = TitleCaseTransformer.TransformAlbumTitle(input);
            if (!String.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Album title mismatch. Expected <" + expected + ">, got <" + actual + ">.");
            }
        }
    }
}
