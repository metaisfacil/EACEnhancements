using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AudioDataPlugIn
{
    internal static class TitleCaseTransformerTests
    {
        [STAThread]
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
                AssertAlbumTitleCommit("THE BEST OF ROCK", "The Best of Rock", true);
                AssertAlbumTitleCommit("The Best of Rock", "The Best of Rock", false);
                Console.WriteLine("Title-case transformer tests passed.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void AssertAlbumTitleCommit(string input, string expected, bool changed)
        {
            using (AlbumTitleHost host = new AlbumTitleHost())
            {
                host.TitleEdit = NativeMethods.CreateWindowExW(
                    0, "EDIT", input, 0x50000000,
                    0, 0, 240, 24, host.Handle, new IntPtr(992), IntPtr.Zero, IntPtr.Zero);
                if (host.TitleEdit == IntPtr.Zero)
                    throw new Exception("Could not create the album title fixture.");

                // Keep the title unfocused, as when the macro is invoked from
                // the menu while the user has been working in the track list.
                IntPtr focus = NativeMethods.GetFocus();
                if (EnhancementRuntime.TransformCurrentAlbumTitle(host.Handle) != changed)
                    throw new Exception("Incorrect album title change count.");
                if (host.CommitCount != 1 || host.CommittedTitle != expected)
                    throw new Exception("Title Case did not commit the album title to disc metadata: " +
                        host.CommitCount + " commits, title <" + host.CommittedTitle + ">, expected <" + expected + ">.");
                if (NativeMethods.GetFocus() != focus)
                    throw new Exception("Title Case changed keyboard focus.");

                // EAC refreshes the displayed title from its committed metadata
                // when starting a rip; the transformed text must survive that.
                NativeMethods.SendMessageStringW(
                    host.TitleEdit, NativeMethods.WM_SETTEXT, IntPtr.Zero, host.CommittedTitle);
                StringBuilder actual = new StringBuilder(512);
                NativeMethods.GetWindowTextW(host.TitleEdit, actual, actual.Capacity);
                if (actual.ToString() != expected)
                    throw new Exception("The album title reverted on metadata refresh.");
            }
        }

        private sealed class AlbumTitleHost : Form
        {
            internal IntPtr TitleEdit;
            internal string CommittedTitle = "Original Disc Metadata";
            internal int CommitCount;

            protected override void WndProc(ref Message message)
            {
                // EAC 1.6 and 1.8 copy the control text into the disc title in
                // their WM_COMMAND / EN_KILLFOCUS handler for control 992.
                if (message.Msg == NativeMethods.WM_COMMAND &&
                    message.WParam.ToInt64() == ((NativeMethods.EN_KILLFOCUS << 16) | 992) &&
                    TitleEdit != IntPtr.Zero && message.LParam == TitleEdit)
                {
                    StringBuilder title = new StringBuilder(512);
                    NativeMethods.GetWindowTextW(TitleEdit, title, title.Capacity);
                    CommittedTitle = title.ToString();
                    CommitCount++;
                }
                base.WndProc(ref message);
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
