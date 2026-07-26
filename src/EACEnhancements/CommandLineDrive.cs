using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AudioDataPlugIn
{
    internal static partial class EnhancementRuntime
    {
        private const uint IoctlStorageQueryProperty = 0x002D1400;
        private const uint IoctlStorageCheckVerify2 = 0x002D0800;
        private const uint FileShareReadWrite = 0x00000003;
        private const uint OpenExisting = 3;
        private const int ErrorNotReady = 21;
        private const int ErrorNoMediaInDrive = 1112;
        private const int MediaCheckAttempts = 12;
        private const int MediaCheckDelayMilliseconds = 250;

        private static void SelectCommandLineDrive(
            IntPtr mainWindow,
            string selector)
        {
            IntPtr combo = FindCommandLineDriveSelector(mainWindow);
            if (combo == IntPtr.Zero)
                throw new InvalidOperationException(
                    "EAC's drive selector was not found.");

            string driveLetter = NormalizeDriveLetter(selector);
            string storageIdentity = driveLetter == null
                ? null
                : ReadStorageIdentity(driveLetter);
            string[] items = ReadDriveSelectorItems(combo);
            int index = MatchCommandLineDriveItem(
                selector,
                storageIdentity,
                items);
            if (index == -2)
            {
                throw new InvalidOperationException(
                    "The drive selector '" + selector +
                    "' matches more than one EAC drive.");
            }
            if (index < 0)
            {
                throw new InvalidOperationException(
                    "The requested drive '" + selector +
                    "' was not found in EAC" +
                    (String.IsNullOrEmpty(storageIdentity)
                        ? "."
                        : " (Windows identified it as '" +
                          storageIdentity + "')."));
            }

            int current = NativeMethods.SendMessageW(
                combo,
                NativeMethods.CB_GETCURSEL,
                IntPtr.Zero,
                IntPtr.Zero).ToInt32();
            if (current != index)
            {
                int selected = NativeMethods.SendMessageW(
                    combo,
                    NativeMethods.CB_SETCURSEL,
                    new IntPtr(index),
                    IntPtr.Zero).ToInt32();
                if (selected < 0)
                    throw new InvalidOperationException(
                        "EAC rejected the requested drive selection.");

                IntPtr parent = NativeMethods.GetParent(combo);
                if (parent == IntPtr.Zero)
                    parent = mainWindow;
                int controlId = NativeMethods.GetDlgCtrlID(combo);
                NativeMethods.SendMessageW(
                    parent,
                    NativeMethods.WM_COMMAND,
                    new IntPtr(
                        controlId |
                        (NativeMethods.CBN_SELCHANGE << 16)),
                    combo);
            }

            Log(
                "Command-line drive '" + selector +
                "' selected EAC drive " + index +
                ": " + items[index] + ".");
        }

        private static void EnsureCommandLineDriveHasMedia(
            string selector)
        {
            string driveLetter = NormalizeDriveLetter(selector);
            if (driveLetter == null)
                return;

            string path = @"\\.\" + driveLetter;
            using (Microsoft.Win32.SafeHandles.SafeFileHandle device =
                NativeMethods.CreateFileW(
                    path,
                    0,
                    FileShareReadWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    0,
                    IntPtr.Zero))
            {
                if (device.IsInvalid)
                    return;

                byte[] mediaChangeCount = new byte[4];
                for (int attempt = 0; attempt < MediaCheckAttempts; attempt++)
                {
                    uint returned;
                    if (NativeMethods.DeviceIoControl(
                            device,
                            IoctlStorageCheckVerify2,
                            null,
                            0,
                            mediaChangeCount,
                            (uint)mediaChangeCount.Length,
                            out returned,
                            IntPtr.Zero))
                    {
                        return;
                    }

                    int error = Marshal.GetLastWin32Error();
                    if (!IsNoMediaDeviceError(error))
                    {
                        Log(
                            "Windows media verification for drive '" +
                            driveLetter + "' was inconclusive: error " +
                            error + ".");
                        return;
                    }
                    if (attempt + 1 < MediaCheckAttempts)
                        System.Threading.Thread.Sleep(
                            MediaCheckDelayMilliseconds);
                }
            }

            throw new InvalidOperationException(
                "No disc was detected in drive '" + driveLetter + "'.");
        }

        internal static bool IsNoMediaDeviceError(int error)
        {
            return error == ErrorNotReady ||
                error == ErrorNoMediaInDrive;
        }

        private static IntPtr FindCommandLineDriveSelector(
            IntPtr mainWindow)
        {
            IntPtr result = IntPtr.Zero;
            NativeMethods.EnumChildProc callback =
                delegate(IntPtr hwnd, IntPtr ignored)
                {
                    if (NativeMethods.GetDlgCtrlID(hwnd) !=
                        DriveSelectorControlId)
                    {
                        return true;
                    }
                    StringBuilder className = new StringBuilder(64);
                    NativeMethods.GetClassNameW(
                        hwnd,
                        className,
                        className.Capacity);
                    string value = className.ToString();
                    if (!value.Equals(
                            "mycombo",
                            StringComparison.OrdinalIgnoreCase) &&
                        !value.Equals(
                            "ComboBox",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    result = hwnd;
                    return false;
                };
            NativeMethods.EnumChildWindows(
                mainWindow,
                callback,
                IntPtr.Zero);
            GC.KeepAlive(callback);
            return result;
        }

        private static string[] ReadDriveSelectorItems(IntPtr combo)
        {
            int count = NativeMethods.SendMessageW(
                combo,
                NativeMethods.CB_GETCOUNT,
                IntPtr.Zero,
                IntPtr.Zero).ToInt32();
            if (count < 1 || count > 128)
                throw new InvalidOperationException(
                    "EAC reported an invalid drive count.");

            string[] items = new string[count];
            for (int i = 0; i < count; i++)
            {
                int length = NativeMethods.SendMessageW(
                    combo,
                    NativeMethods.CB_GETLBTEXTLEN,
                    new IntPtr(i),
                    IntPtr.Zero).ToInt32();
                if (length < 0 || length > 4096)
                    throw new InvalidOperationException(
                        "EAC reported an invalid drive name length.");
                StringBuilder text = new StringBuilder(length + 1);
                int copied = NativeMethods.SendMessageTextW(
                    combo,
                    NativeMethods.CB_GETLBTEXT,
                    new IntPtr(i),
                    text).ToInt32();
                if (copied < 0)
                    throw new InvalidOperationException(
                        "EAC rejected a drive-name request.");
                items[i] = text.ToString();
            }
            return items;
        }

        internal static int MatchCommandLineDriveItem(
            string selector,
            string storageIdentity,
            string[] items)
        {
            string requested = NormalizeDriveName(
                String.IsNullOrEmpty(storageIdentity)
                    ? selector
                    : storageIdentity);
            if (requested.Length == 0 || items == null)
                return -1;

            List<int> matches = new List<int>();
            for (int i = 0; i < items.Length; i++)
            {
                string item = NormalizeDriveName(items[i]);
                if (item.Equals(
                        requested,
                        StringComparison.OrdinalIgnoreCase) ||
                    item.StartsWith(
                        requested + " ",
                        StringComparison.OrdinalIgnoreCase) ||
                    (String.IsNullOrEmpty(storageIdentity) &&
                     item.IndexOf(
                         requested,
                         StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    matches.Add(i);
                }
            }
            return matches.Count == 1
                ? matches[0]
                : matches.Count == 0 ? -1 : -2;
        }

        internal static string NormalizeDriveLetter(string selector)
        {
            string value = (selector ?? String.Empty).Trim();
            if (value.Length == 3 &&
                (value[2] == '\\' || value[2] == '/'))
            {
                value = value.Substring(0, 2);
            }
            return value.Length == 2 &&
                Char.IsLetter(value[0]) &&
                value[1] == ':'
                ? Char.ToUpperInvariant(value[0]) + ":"
                : null;
        }

        private static string NormalizeDriveName(string value)
        {
            return Regex.Replace(
                (value ?? String.Empty).Trim(),
                "\\s+",
                " ");
        }

        private static string ReadStorageIdentity(string driveLetter)
        {
            string path = @"\\.\" + driveLetter;
            using (Microsoft.Win32.SafeHandles.SafeFileHandle device =
                NativeMethods.CreateFileW(
                    path,
                    0,
                    FileShareReadWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    0,
                    IntPtr.Zero))
            {
                if (device.IsInvalid)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "The requested drive '" + driveLetter +
                        "' could not be opened.");
                }

                byte[] query = new byte[12];
                byte[] descriptor = new byte[1024];
                uint returned;
                if (!NativeMethods.DeviceIoControl(
                        device,
                        IoctlStorageQueryProperty,
                        query,
                        (uint)query.Length,
                        descriptor,
                        (uint)descriptor.Length,
                        out returned,
                        IntPtr.Zero))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "The requested drive '" + driveLetter +
                        "' is not an accessible storage device.");
                }
                if (returned < 24)
                    throw new InvalidOperationException(
                        "Windows returned an incomplete identity for drive '" +
                        driveLetter + "'.");

                string vendor = ReadStorageDescriptorString(
                    descriptor,
                    BitConverter.ToInt32(descriptor, 12));
                string product = ReadStorageDescriptorString(
                    descriptor,
                    BitConverter.ToInt32(descriptor, 16));
                string revision = ReadStorageDescriptorString(
                    descriptor,
                    BitConverter.ToInt32(descriptor, 20));
                string identity = NormalizeDriveName(
                    vendor + " " + product + " " + revision);
                if (identity.Length == 0)
                    throw new InvalidOperationException(
                        "Windows returned no identity for drive '" +
                        driveLetter + "'.");
                return identity;
            }
        }

        private static string ReadStorageDescriptorString(
            byte[] descriptor,
            int offset)
        {
            if (offset <= 0 || offset >= descriptor.Length)
                return String.Empty;
            int end = offset;
            while (end < descriptor.Length && descriptor[end] != 0)
                end++;
            return Encoding.ASCII.GetString(
                descriptor,
                offset,
                end - offset).Trim();
        }
    }
}
