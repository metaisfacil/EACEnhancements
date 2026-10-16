using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AudioDataPlugIn
{
    internal static class SpaceNormalizerTests
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                AssertTransform(null, String.Empty);
                AssertTransform(String.Empty, String.Empty);
                AssertTransform("\u3000Live\u3000in\u3000Tokyo\u3000", " Live in Tokyo ");
                AssertTransform("A\u00A0B\u1680C\u202FD\u205FE", "A B C D E");
                for (char space = '\u2000'; space <= '\u200A'; space++)
                    AssertTransform("A" + space + "B", "A B");
                AssertTransform(" \u3000\u00A0 \u2009 ", "      ");
                string preserved = "  LIVE in Tokyo / \u6771\u4EAC \uFF21\uFF22\uFF23 \u00E9 \uD83C\uDFB5 " +
                    "\t\r\n\v\f\u0085\u2028\u2029\u180E\u200B\u200C\u200D\u2060\uFEFF";
                AssertTransform(preserved, preserved);
                AssertTransform("A\u3000B", SpaceNormalizer.Transform(SpaceNormalizer.Transform("A\u3000B")));
                AssertMenuPlacement();
                AssertCommandIdUnique();
                AssertCommandScopeAndCommit();
                Console.WriteLine("Space normalizer tests passed.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void AssertTransform(string input, string expected)
        {
            string actual = SpaceNormalizer.Transform(input);
            if (!String.Equals(actual, expected, StringComparison.Ordinal))
                throw new Exception("Incorrect space normalization for <" + input + ">.");
        }

        private static void AssertCommandIdUnique()
        {
            foreach (FieldInfo field in typeof(EnhancementRuntime).GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.IsLiteral && field.FieldType == typeof(uint) &&
                    field.Name.EndsWith("Command", StringComparison.Ordinal) &&
                    field.Name != "NormalizeSpacesTransformCommand" &&
                    (uint)field.GetRawConstantValue() == 0xA328u)
                    throw new Exception("Normalize Spaces conflicts with " + field.Name + ".");
            }
        }

        private static void AssertCommandScopeAndCommit()
        {
            // Register common controls before creating EAC's native list view.
            using (ListView registration = new ListView())
            using (MetadataHost host = new MetadataHost())
            {
                registration.CreateControl();
                host.Title = CreateEdit(host.Handle, 992, "Live\u3000Album");
                host.Artist = CreateEdit(host.Handle, 993, "Album\u00A0Artist");
                IntPtr composer = CreateEdit(host.Handle, 880, "Album\u3000Composer");
                IntPtr comment = CreateEdit(host.Handle, 883, "Keep\u3000Comment");
                host.Tracks = NativeMethods.CreateWindowExW(0, "SysListView32", "", 0x50000001,
                    0, 30, 400, 100, host.Handle, new IntPtr(14), IntPtr.Zero, IntPtr.Zero);
                if (host.Tracks == IntPtr.Zero)
                    throw new Exception("Could not create the track fixture.");
                // LVCOLUMNW on x86; only the width member is used.
                IntPtr column = Marshal.AllocHGlobal(24);
                try
                {
                    for (int offset = 0; offset < 24; offset += 4)
                        Marshal.WriteInt32(column, offset, 0);
                    Marshal.WriteInt32(column, 0, 2);
                    Marshal.WriteInt32(column, 8, 100);
                    for (int index = 0; index < 4; index++)
                        if (NativeMethods.SendMessageW(host.Tracks, 0x1061,
                                new IntPtr(index), column).ToInt32() != index)
                            throw new Exception("Could not create a track column.");
                }
                finally { Marshal.FreeHGlobal(column); }
                string[,] inputs = {
                    { "Unchanged title", "Track\u3000Artist", "Track\u3000Composer" },
                    { "Second\u2009Title", "Unchanged artist", "Second\u00A0Composer" },
                    { "Third\u3000Title", "Guest\u202FArtist", "Third\u3000Composer" }
                };
                for (int track = 0; track < 3; track++)
                {
                    NativeMethods.LVITEM item = new NativeMethods.LVITEM();
                    item.Item = track;
                    if (NativeMethods.SendMessageListViewItemW(host.Tracks, 0x104D,
                            IntPtr.Zero, ref item).ToInt32() != track)
                        throw new Exception("Could not create a track row.");
                    for (int field = 0; field < 3; field++)
                    {
                        item.SubItem = field + 1;
                        item.Text = Marshal.StringToHGlobalUni(inputs[track, field]);
                        try
                        {
                            if (NativeMethods.SendMessageListViewItemW(host.Tracks,
                                    NativeMethods.LVM_SETITEMTEXTW, new IntPtr(track), ref item) == IntPtr.Zero)
                                throw new Exception("Could not populate a track row.");
                        }
                        finally { Marshal.FreeHGlobal(item.Text); }
                    }
                }
                MethodInfo callback = typeof(EnhancementRuntime).GetMethod("MainWindowSubclass",
                    BindingFlags.Static | BindingFlags.NonPublic);
                IntPtr focus = NativeMethods.GetFocus();
                callback.Invoke(null, new object[] { host.Handle, NativeMethods.WM_COMMAND,
                    new IntPtr(0xA328), IntPtr.Zero, UIntPtr.Zero, UIntPtr.Zero });
                if (host.CommittedTitle != "Live Album" || host.CommittedArtist != "Album Artist")
                    throw new Exception("Album titles and artists were not committed.");
                for (int track = 0; track < 3; track++)
                {
                    for (int field = 0; field < 2; field++)
                    {
                        string expected = inputs[track, field].Replace('\u3000', ' ')
                            .Replace('\u2009', ' ').Replace('\u202F', ' ');
                        if (ReadTrackCell(host.Tracks, track, field + 1) != expected)
                            throw new Exception("A track title or artist was not normalized.");
                        if (inputs[track, field] != expected && host.CommittedTracks[track, field] != expected)
                            throw new Exception("A track edit was not committed to metadata.");
                    }
                    if (ReadTrackCell(host.Tracks, track, 3) != inputs[track, 2])
                        throw new Exception("Track composers must remain unchanged.");
                }
                if (ReadText(composer) != "Album\u3000Composer" ||
                    ReadText(comment) != "Keep\u3000Comment" || NativeMethods.GetFocus() != focus)
                    throw new Exception("Normalization changed other metadata or keyboard focus.");
            }
        }

        private static IntPtr CreateEdit(IntPtr parent, int id, string value)
        {
            IntPtr edit = NativeMethods.CreateWindowExW(0, "EDIT", value, 0x50000000,
                0, 0, 240, 24, parent, new IntPtr(id), IntPtr.Zero, IntPtr.Zero);
            if (edit == IntPtr.Zero)
                throw new Exception("Could not create a metadata field.");
            return edit;
        }

        private static string ReadText(IntPtr edit)
        {
            StringBuilder text = new StringBuilder(512);
            NativeMethods.GetWindowTextW(edit, text, text.Capacity);
            return text.ToString();
        }

        private static string ReadTrackCell(IntPtr list, int track, int subItem)
        {
            NativeMethods.LVITEM item = new NativeMethods.LVITEM();
            item.SubItem = subItem;
            item.TextMaximum = 512;
            item.Text = Marshal.AllocHGlobal(1024);
            try
            {
                NativeMethods.SendMessageListViewItemW(list, NativeMethods.LVM_GETITEMTEXTW,
                    new IntPtr(track), ref item);
                return Marshal.PtrToStringUni(item.Text);
            }
            finally { Marshal.FreeHGlobal(item.Text); }
        }

        private sealed class MetadataHost : Form
        {
            internal IntPtr Title;
            internal IntPtr Artist;
            internal IntPtr Tracks;
            internal string CommittedTitle;
            internal string CommittedArtist;
            internal string[,] CommittedTracks = new string[3, 2];

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == NativeMethods.WM_COMMAND &&
                    ((message.WParam.ToInt64() >> 16) & 0xFFFF) == NativeMethods.EN_KILLFOCUS)
                {
                    int id = (int)message.WParam.ToInt64() & 0xFFFF;
                    if (id == 992) CommittedTitle = ReadText(Title);
                    else if (id == 993) CommittedArtist = ReadText(Artist);
                    else throw new Exception("Unexpected metadata field was committed.");
                }
                if (message.Msg == NativeMethods.WM_NOTIFY && message.LParam != IntPtr.Zero)
                {
                    NativeMethods.NMHDR header = (NativeMethods.NMHDR)Marshal.PtrToStructure(
                        message.LParam, typeof(NativeMethods.NMHDR));
                    if (header.Code == NativeMethods.LVN_ENDLABELEDITW)
                    {
                        NativeMethods.NMLVDISPINFO edit = (NativeMethods.NMLVDISPINFO)Marshal.PtrToStructure(
                            message.LParam, typeof(NativeMethods.NMLVDISPINFO));
                        if (edit.Item.SubItem != 1 && edit.Item.SubItem != 2)
                            throw new Exception("Unexpected track field was committed.");
                        CommittedTracks[edit.Item.Item, edit.Item.SubItem - 1] = Marshal.PtrToStringUni(edit.Item.Text);
                        message.Result = new IntPtr(1);
                        return;
                    }
                }
                base.WndProc(ref message);
            }
        }

        private static void AssertMenuPlacement()
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
                throw new Exception("Could not create the transform-menu fixture.");
            try
            {
                if (!NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING,
                        new UIntPtr(0xA31Bu), "&Title Case (EAC Enhancements)") ||
                    !NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING,
                        new UIntPtr(42u), "Other command"))
                    throw new Exception("Could not populate the transform-menu fixture.");

                if (!EnhancementRuntime.EnsureNormalizeSpacesMenuItem(menu) ||
                    EnhancementRuntime.EnsureNormalizeSpacesMenuItem(menu))
                    throw new Exception("Normalize Spaces was missing or duplicated.");
                if (NativeMethods.GetMenuItemCount(menu) != 3 ||
                    NativeMethods.GetMenuItemID(menu, 0) != 0xA31Bu ||
                    NativeMethods.GetMenuItemID(menu, 1) != 0xA328u ||
                    NativeMethods.GetMenuItemID(menu, 2) != 42u)
                    throw new Exception("Normalize Spaces must appear immediately below Title Case.");
                StringBuilder text = new StringBuilder(256);
                NativeMethods.GetMenuStringW(menu, 1, text, text.Capacity, NativeMethods.MF_BYPOSITION);
                if (text.ToString() != "Normalize &Spaces (EAC Enhancements)")
                    throw new Exception("Incorrect Normalize Spaces menu label.");
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(IntPtr menu);
    }
}
