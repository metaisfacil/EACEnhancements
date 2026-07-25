using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AudioDataPlugIn
{
    internal static partial class EnhancementRuntime
    {
        // EAC's native metadata record contains a 32,768-character
        // ExtendedDiscInformation buffer at this offset in both 1.6 and
        // 1.8. Keeping our data in that supported string avoids changing
        // CDDB.sdf's schema or the native record ABI.
        internal const int AlbumMetadataStoreExtendedDiscOffset = 0x199A6;
        internal const int AlbumMetadataStoreExtendedDiscCapacity = 0x8000;

        private const string AlbumMetadataStoreMarker =
            "EACEnhancements/1:";
        private const int AlbumMetadataStorePayloadLimit = 8192;
        private const int MetadataStoreSavePatchLength = 8;
        private const int MetadataStoreLoadPatchLength = 5;
        private static readonly object AlbumMetadataStoreStateLock =
            new object();

        private static readonly byte[] ExpectedMetadataStoreSavePrologue =
            { 0x55, 0x89, 0xE5, 0x57, 0x83, 0x7D, 0x0C, 0x00 };
        private static readonly byte[] ExpectedMetadataStoreLoadPrologue =
            { 0x55, 0x89, 0xE5, 0x6A, 0x5D };
        private static readonly Regex AlbumMetadataStorePayloadAtEnd =
            new Regex(
                @"(?:\r\n|\r|\n)?EACEnhancements/1:([A-Za-z0-9_-]+)$",
                RegexOptions.CultureInvariant);

        private static IntPtr metadataStoreSaveTrampoline;
        private static IntPtr metadataStoreFindTrampoline;
        private static IntPtr metadataStoreNextTrampoline;
        private static MetadataStoreSaveDelegate originalMetadataStoreSave;
        private static MetadataStoreFindDelegate originalMetadataStoreFind;
        private static MetadataStoreNextDelegate originalMetadataStoreNext;
        private static MetadataStoreSaveDelegate hookedMetadataStoreSave;
        private static MetadataStoreFindDelegate hookedMetadataStoreFind;
        private static MetadataStoreNextDelegate hookedMetadataStoreNext;
        private static volatile bool albumMetadataStoreLoadPending;
        private static IntPtr currentAlbumMetadataStoreRecord;
        private static int currentAlbumMetadataStoreCddbId;
        private static bool albumMetadataStoreDirty;
        private static bool albumMetadataStoreSaveInProgress;

        internal static bool HasPendingAlbumMetadataStoreChanges
        {
            get
            {
                lock (AlbumMetadataStoreStateLock)
                    return albumMetadataStoreDirty;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void MetadataStoreSaveDelegate(
            IntPtr metadata,
            int cddbId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate byte MetadataStoreFindDelegate(
            IntPtr metadata,
            int cddbId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint MetadataStoreNextDelegate(
            IntPtr metadata,
            IntPtr cddbId);

        private static void InstallAlbumMetadataStoreHooks()
        {
            uint saveVa;
            uint findVa;
            uint nextVa;
            if (layout.Name == "EAC 1.8")
            {
                saveVa = 0x00487650;
                findVa = 0x00486010;
                nextVa = 0x00486160;
            }
            else if (layout.Name == "EAC 1.6")
            {
                saveVa = 0x004840A0;
                findVa = 0x00482A60;
                nextVa = 0x00482BB0;
            }
            else
            {
                throw new NotSupportedException(
                    "Album metadata persistence has no layout for " +
                    layout.Name + ".");
            }

            // Verify and prepare every hook before changing EAC code. If a
            // later write fails, all successfully written entry points are
            // restored immediately.
            RequireBytes(
                saveVa,
                ExpectedMetadataStoreSavePrologue,
                "metadata database save");
            RequireBytes(
                findVa,
                ExpectedMetadataStoreLoadPrologue,
                "metadata database find");
            RequireBytes(
                nextVa,
                ExpectedMetadataStoreLoadPrologue,
                "metadata database next");

            metadataStoreSaveTrampoline = CreateAlbumMetadataStoreTrampoline(
                saveVa,
                ExpectedMetadataStoreSavePrologue);
            metadataStoreFindTrampoline = CreateAlbumMetadataStoreTrampoline(
                findVa,
                ExpectedMetadataStoreLoadPrologue);
            metadataStoreNextTrampoline = CreateAlbumMetadataStoreTrampoline(
                nextVa,
                ExpectedMetadataStoreLoadPrologue);

            originalMetadataStoreSave =
                (MetadataStoreSaveDelegate)Marshal.GetDelegateForFunctionPointer(
                    metadataStoreSaveTrampoline,
                    typeof(MetadataStoreSaveDelegate));
            originalMetadataStoreFind =
                (MetadataStoreFindDelegate)Marshal.GetDelegateForFunctionPointer(
                    metadataStoreFindTrampoline,
                    typeof(MetadataStoreFindDelegate));
            originalMetadataStoreNext =
                (MetadataStoreNextDelegate)Marshal.GetDelegateForFunctionPointer(
                    metadataStoreNextTrampoline,
                    typeof(MetadataStoreNextDelegate));
            hookedMetadataStoreSave = HookedMetadataStoreSave;
            hookedMetadataStoreFind = HookedMetadataStoreFind;
            hookedMetadataStoreNext = HookedMetadataStoreNext;

            bool savePatched = false;
            bool findPatched = false;
            bool nextPatched = false;
            try
            {
                WriteJumpPatch(
                    saveVa,
                    Pointer32(Marshal.GetFunctionPointerForDelegate(
                        hookedMetadataStoreSave)),
                    MetadataStoreSavePatchLength);
                savePatched = true;
                WriteJumpPatch(
                    findVa,
                    Pointer32(Marshal.GetFunctionPointerForDelegate(
                        hookedMetadataStoreFind)),
                    MetadataStoreLoadPatchLength);
                findPatched = true;
                WriteJumpPatch(
                    nextVa,
                    Pointer32(Marshal.GetFunctionPointerForDelegate(
                        hookedMetadataStoreNext)),
                    MetadataStoreLoadPatchLength);
                nextPatched = true;
            }
            catch (Exception installationError)
            {
                bool rollbackSucceeded = true;
                if (nextPatched)
                {
                    rollbackSucceeded =
                        TryRestoreAlbumMetadataStoreHook(
                        nextVa,
                        ExpectedMetadataStoreLoadPrologue,
                        "next") && rollbackSucceeded;
                }
                if (findPatched)
                {
                    rollbackSucceeded =
                        TryRestoreAlbumMetadataStoreHook(
                        findVa,
                        ExpectedMetadataStoreLoadPrologue,
                        "find") && rollbackSucceeded;
                }
                if (savePatched)
                {
                    rollbackSucceeded =
                        TryRestoreAlbumMetadataStoreHook(
                        saveVa,
                        ExpectedMetadataStoreSavePrologue,
                        "save") && rollbackSucceeded;
                }
                throw new InvalidOperationException(
                    "Album metadata persistence hooks could not be installed; " +
                    (rollbackSucceeded
                        ? "all written entry points were rolled back."
                        : "at least one entry point could not be rolled back."),
                    installationError);
            }

            Log(
                "Album metadata persistence hooks active at save=0x" +
                saveVa.ToString("X8") + ", find=0x" +
                findVa.ToString("X8") + ", next=0x" +
                nextVa.ToString("X8") + ".");
        }

        private static bool TryRestoreAlbumMetadataStoreHook(
            uint staticVa,
            byte[] originalBytes,
            string description)
        {
            try
            {
                WriteMemoryPatch(staticVa, originalBytes);
                return true;
            }
            catch (Exception rollbackError)
            {
                Log(
                    "Album metadata persistence " + description +
                    " hook rollback failed: " + rollbackError);
                return false;
            }
        }

        private static IntPtr CreateAlbumMetadataStoreTrampoline(
            uint staticVa,
            byte[] originalBytes)
        {
            int length = originalBytes.Length;
            IntPtr trampoline = NativeMethods.VirtualAlloc(
                IntPtr.Zero,
                new UIntPtr((uint)(length + 5)),
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE,
                NativeMethods.PAGE_EXECUTE_READWRITE);
            if (trampoline == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "VirtualAlloc failed for an album metadata persistence " +
                    "trampoline with Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }

            Marshal.Copy(originalBytes, 0, trampoline, length);
            WriteRelativeJump(
                Add(trampoline, length),
                Add(AddressFromStaticVa(staticVa), length),
                5);
            return trampoline;
        }

        private static void HookedMetadataStoreSave(
            IntPtr metadata,
            int cddbId)
        {
            string originalExtendedDisc = null;
            bool replaced = false;
            bool prepared = false;
            bool saved = false;
            try
            {
                if (metadata != IntPtr.Zero && cddbId != 0)
                {
                    IntPtr extendedDisc = Add(
                        metadata,
                        AlbumMetadataStoreExtendedDiscOffset);
                    originalExtendedDisc = ReadAlbumMetadataStoreBuffer(
                        extendedDisc,
                        AlbumMetadataStoreExtendedDiscCapacity);
                    string storedExtendedDisc =
                        MergeAlbumMetadataStorePayload(
                            originalExtendedDisc,
                            albumLabel,
                            albumBarcode,
                            albumCatalogNumber);
                    if (storedExtendedDisc.Length <
                        AlbumMetadataStoreExtendedDiscCapacity)
                    {
                        prepared = true;
                        if (!String.Equals(
                                storedExtendedDisc,
                                originalExtendedDisc,
                                StringComparison.Ordinal))
                        {
                            WriteAlbumMetadataStoreBuffer(
                                extendedDisc,
                                AlbumMetadataStoreExtendedDiscCapacity,
                                storedExtendedDisc);
                            replaced = true;
                            Log(
                                "Prepared CD Label, CD Barcode, and CD Catalog # " +
                                "for EAC's local metadata database.");
                        }
                    }
                    else if (storedExtendedDisc.Length >=
                        AlbumMetadataStoreExtendedDiscCapacity)
                    {
                        Log(
                            "Album metadata was not persisted because EAC's " +
                            "extended-disc field is full.");
                    }
                }
            }
            catch (Exception error)
            {
                Log("Preparing album metadata for database save failed: " + error);
            }

            try
            {
                originalMetadataStoreSave(metadata, cddbId);
                saved = true;
            }
            finally
            {
                if (replaced)
                {
                    try
                    {
                        WriteAlbumMetadataStoreBuffer(
                            Add(
                                metadata,
                                AlbumMetadataStoreExtendedDiscOffset),
                            AlbumMetadataStoreExtendedDiscCapacity,
                            originalExtendedDisc);
                    }
                    catch (Exception error)
                    {
                        Log(
                            "Restoring EAC's live extended-disc metadata " +
                            "after save failed: " + error);
                    }
                }
                if (saved && prepared)
                    SetAlbumMetadataStoreSaved(metadata, cddbId);
            }
        }

        private static byte HookedMetadataStoreFind(
            IntPtr metadata,
            int cddbId)
        {
            PersistPendingAlbumMetadataStoreChanges();
            byte result = originalMetadataStoreFind(metadata, cddbId);
            SetAlbumMetadataStoreContext(metadata, cddbId);
            if (result != 0)
                LoadAlbumMetadataStorePayload(metadata);
            return result;
        }

        private static uint HookedMetadataStoreNext(
            IntPtr metadata,
            IntPtr cddbId)
        {
            PersistPendingAlbumMetadataStoreChanges();
            uint result = originalMetadataStoreNext(metadata, cddbId);
            if ((result & 0xFF) != 0)
            {
                if (cddbId != IntPtr.Zero)
                {
                    SetAlbumMetadataStoreContext(
                        metadata,
                        Marshal.ReadInt32(cddbId));
                }
                LoadAlbumMetadataStorePayload(metadata);
            }
            return result;
        }

        private static void SetAlbumMetadataStoreContext(
            IntPtr metadata,
            int cddbId)
        {
            lock (AlbumMetadataStoreStateLock)
            {
                currentAlbumMetadataStoreRecord = metadata;
                currentAlbumMetadataStoreCddbId = cddbId;
                albumMetadataStoreDirty = false;
            }
        }

        private static void SetAlbumMetadataStoreDirty()
        {
            lock (AlbumMetadataStoreStateLock)
                albumMetadataStoreDirty = true;
        }

        private static void SetAlbumMetadataStoreSaved(
            IntPtr metadata,
            int cddbId)
        {
            lock (AlbumMetadataStoreStateLock)
            {
                currentAlbumMetadataStoreRecord = metadata;
                currentAlbumMetadataStoreCddbId = cddbId;
                albumMetadataStoreDirty = false;
            }
        }

        private static void PersistPendingAlbumMetadataStoreChanges()
        {
            IntPtr metadata;
            int cddbId;
            lock (AlbumMetadataStoreStateLock)
            {
                if (!albumMetadataStoreDirty ||
                    albumMetadataStoreSaveInProgress ||
                    currentAlbumMetadataStoreRecord == IntPtr.Zero ||
                    currentAlbumMetadataStoreCddbId == 0 ||
                    originalMetadataStoreSave == null)
                {
                    return;
                }
                metadata = currentAlbumMetadataStoreRecord;
                cddbId = currentAlbumMetadataStoreCddbId;
                albumMetadataStoreSaveInProgress = true;
            }

            try
            {
                HookedMetadataStoreSave(metadata, cddbId);
                Log(
                    "Automatically persisted edited CD Label, CD Barcode, " +
                    "and CD Catalog # metadata.");
            }
            catch (Exception error)
            {
                Log("Automatically persisting album metadata failed: " + error);
            }
            finally
            {
                lock (AlbumMetadataStoreStateLock)
                    albumMetadataStoreSaveInProgress = false;
            }
        }

        private static void LoadAlbumMetadataStorePayload(IntPtr metadata)
        {
            try
            {
                IntPtr extendedDisc = Add(
                    metadata,
                    AlbumMetadataStoreExtendedDiscOffset);
                string stored = ReadAlbumMetadataStoreBuffer(
                    extendedDisc,
                    AlbumMetadataStoreExtendedDiscCapacity);
                string clean;
                string label;
                string barcode;
                string catalogNumber;
                bool found = TryExtractAlbumMetadataStorePayload(
                    stored,
                    out clean,
                    out label,
                    out barcode,
                    out catalogNumber);
                if (found)
                {
                    WriteAlbumMetadataStoreBuffer(
                        extendedDisc,
                        AlbumMetadataStoreExtendedDiscCapacity,
                        clean);
                }
                ApplyStoredAlbumMetadataValues(
                    found ? label : String.Empty,
                    found ? barcode : String.Empty,
                    found ? catalogNumber : String.Empty);
                Log(
                    "EAC local metadata entry loaded; album metadata " +
                    "payload=" + (found ? "present" : "absent") + ".");
            }
            catch (Exception error)
            {
                // Loading ordinary EAC metadata must never fail merely
                // because our optional sidecar could not be decoded.
                Log("Loading persisted album metadata failed: " + error);
            }
        }

        internal static string MergeAlbumMetadataStorePayload(
            string extendedDisc,
            string label,
            string barcode,
            string catalogNumber)
        {
            string clean = extendedDisc ?? String.Empty;
            string ignoredLabel;
            string ignoredBarcode;
            string ignoredCatalogNumber;
            string previous;
            while (TryExtractAlbumMetadataStorePayload(
                clean,
                out previous,
                out ignoredLabel,
                out ignoredBarcode,
                out ignoredCatalogNumber))
            {
                clean = previous;
            }

            label = label ?? String.Empty;
            barcode = barcode ?? String.Empty;
            catalogNumber = catalogNumber ?? String.Empty;
            if (label.Length == 0 &&
                barcode.Length == 0 &&
                catalogNumber.Length == 0)
            {
                return clean;
            }

            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "label", label },
                    { "barcode", barcode },
                    { "catalognumber", catalogNumber }
                };
            string json = new JavaScriptSerializer().Serialize(values);
            string payload = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(json))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return clean +
                (clean.Length == 0 ? String.Empty : "\r\n") +
                AlbumMetadataStoreMarker +
                payload;
        }

        internal static bool TryExtractAlbumMetadataStorePayload(
            string extendedDisc,
            out string cleanExtendedDisc,
            out string label,
            out string barcode,
            out string catalogNumber)
        {
            string stored = extendedDisc ?? String.Empty;
            cleanExtendedDisc = stored;
            label = String.Empty;
            barcode = String.Empty;
            catalogNumber = String.Empty;
            Match match = AlbumMetadataStorePayloadAtEnd.Match(stored);
            if (!match.Success)
                return false;

            string payload = match.Groups[1].Value;
            if (payload.Length == 0 ||
                payload.Length > AlbumMetadataStorePayloadLimit)
            {
                return false;
            }

            try
            {
                string padded = payload
                    .Replace('-', '+')
                    .Replace('_', '/');
                switch (padded.Length % 4)
                {
                    case 2:
                        padded += "==";
                        break;
                    case 3:
                        padded += "=";
                        break;
                    case 1:
                        return false;
                }
                string json = Encoding.UTF8.GetString(
                    Convert.FromBase64String(padded));
                Dictionary<string, string> values =
                    new JavaScriptSerializer().Deserialize<
                        Dictionary<string, string>>(json);
                if (values == null ||
                    !values.TryGetValue("label", out label) ||
                    !values.TryGetValue("barcode", out barcode) ||
                    !values.TryGetValue(
                        "catalognumber",
                        out catalogNumber))
                {
                    label = String.Empty;
                    barcode = String.Empty;
                    catalogNumber = String.Empty;
                    return false;
                }

                label = label ?? String.Empty;
                barcode = barcode ?? String.Empty;
                catalogNumber = catalogNumber ?? String.Empty;
                cleanExtendedDisc = stored.Substring(0, match.Index);
                return true;
            }
            catch
            {
                label = String.Empty;
                barcode = String.Empty;
                catalogNumber = String.Empty;
                return false;
            }
        }

        internal static string ReadAlbumMetadataStoreBuffer(
            IntPtr buffer,
            int capacity)
        {
            if (buffer == IntPtr.Zero)
                throw new ArgumentNullException("buffer");
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException("capacity");

            int length = 0;
            while (length < capacity &&
                Marshal.ReadInt16(buffer, length * sizeof(char)) != 0)
            {
                length++;
            }
            if (length == capacity)
            {
                throw new InvalidOperationException(
                    "EAC's extended-disc buffer is not null terminated.");
            }
            return Marshal.PtrToStringUni(buffer, length) ?? String.Empty;
        }

        internal static void WriteAlbumMetadataStoreBuffer(
            IntPtr buffer,
            int capacity,
            string value)
        {
            if (buffer == IntPtr.Zero)
                throw new ArgumentNullException("buffer");
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException("capacity");

            value = value ?? String.Empty;
            if (value.Length >= capacity)
            {
                throw new ArgumentException(
                    "The extended-disc value exceeds EAC's native buffer.",
                    "value");
            }
            byte[] bytes = Encoding.Unicode.GetBytes(value);
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            Marshal.WriteInt16(buffer, bytes.Length, 0);
        }

        internal static void ApplyStoredAlbumMetadataValues(
            string label,
            string barcode,
            string catalogNumber)
        {
            albumLabel = label ?? String.Empty;
            albumBarcode = barcode ?? String.Empty;
            albumCatalogNumber = catalogNumber ?? String.Empty;
            lock (AlbumMetadataStoreStateLock)
                albumMetadataStoreDirty = false;
            if (AreAlbumMetadataControlsAvailable())
            {
                SetAlbumMetadataEditText(albumLabelEdit, albumLabel);
                SetAlbumMetadataEditText(albumBarcodeEdit, albumBarcode);
                SetAlbumMetadataEditText(
                    albumCatalogNumberEdit,
                    albumCatalogNumber);
                albumMetadataStoreLoadPending = false;
            }
            else
            {
                albumMetadataStoreLoadPending = true;
            }
        }

        private static void SetAlbumMetadataEditText(
            IntPtr control,
            string value)
        {
            if (control != IntPtr.Zero && NativeMethods.IsWindow(control))
            {
                NativeMethods.SendMessageStringW(
                    control,
                    NativeMethods.WM_SETTEXT,
                    IntPtr.Zero,
                    value ?? String.Empty);
            }
        }
    }
}
