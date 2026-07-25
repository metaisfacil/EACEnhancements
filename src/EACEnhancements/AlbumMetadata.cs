using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AudioDataPlugIn
{
    internal static partial class EnhancementRuntime
    {
        internal const int AlbumBarcodeControlId = 0xA31C;
        internal const int AlbumCatalogNumberControlId = 0xA31D;
        internal const int AlbumLabelControlId = 0xA320;

        private const int AlbumBarcodeLabelControlId = 0xA31E;
        private const int AlbumCatalogNumberLabelControlId = 0xA31F;
        private const int AlbumLabelLabelControlId = 0xA321;
        private const uint MetadataTokenLexerStaticVa = 0x0050C1E0;
        private const int MetadataTokenLexerPatchLength = 6;
        private const int LiteralPercentTokenId = 0x1B;
        private const uint MetadataTemplateFormatterStaticVa = 0x0050CF80;
        private const int MetadataTemplateFormatterPatchLength = 10;
        private const uint FilenameValidationTemplateCapacity = 0x100;
        // The Filename pages reject only negative lexer results and token 0x12
        // when its related option is disabled. Zero is an ordinary token ID.
        private const int FilenameValidationAcceptedTokenId = 0;
        private const int GenreControlId = 996;
        private const int CommentControlId = 883;
        private const int CdComposerControlId = 880;
        private const int CdPerformerControlId = 997;
        private const int CdTitleLabelControlId = 950;
        private const int CdComposerLabelControlId = 956;
        private const int CdPerformerLabelControlId = 955;
        private const int GenreLabelControlId = 953;
        private const int CommentLabelControlId = 959;
        private const int FreedbGenreLabelControlId = 954;
        private const int FreedbGenreControlId = 998;
        private const int GwlStyle = -16;
        private const int GwlExtendedStyle = -20;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint AlbumMetadataStoreSaveTimerId = 0xEAC5;
        private const uint AlbumMetadataStoreSaveDelayMilliseconds = 750;

        private static readonly byte[] ExpectedMetadataTemplateFormatterPrologue =
            { 0x55, 0x89, 0xE5, 0x55, 0x81, 0xEC, 0xE4, 0x02, 0x00, 0x00 };
        private static readonly byte[] ExpectedMetadataTokenLexerPrologue =
            { 0x55, 0x89, 0xE5, 0x83, 0xEC, 0x04 };

        private static IntPtr albumBarcodeLabel;
        private static IntPtr albumBarcodeEdit;
        private static IntPtr albumCatalogNumberLabel;
        private static IntPtr albumCatalogNumberEdit;
        private static IntPtr albumLabelLabel;
        private static IntPtr albumLabelEdit;
        private static volatile string albumBarcode = String.Empty;
        private static volatile string albumCatalogNumber = String.Empty;
        private static volatile string albumLabel = String.Empty;
        private static IntPtr albumMetadataParent;
        private static MainWindowSubclassDelegate albumMetadataParentSubclassDelegate;
        private static MainWindowSubclassDelegate albumMetadataEditSubclassDelegate;
        private static MainWindowSubclassDelegate albumMetadataStateControlSubclassDelegate;
        private static IntPtr albumMetadataUserEditControl;
        private static int lastAlbumMetadataInstallTick;
        private static IntPtr metadataTemplateFormatterTrampoline;
        private static MetadataTemplateFormatterDelegate originalMetadataTemplateFormatter;
        private static MetadataTemplateFormatterDelegate hookedMetadataTemplateFormatter;
        private static IntPtr metadataTokenLexerTrampoline;
        private static MetadataTokenLexerDelegate originalMetadataTokenLexer;
        private static MetadataTokenLexerDelegate hookedMetadataTokenLexer;
        private static IntPtr filenameTokenLexerTrampoline;
        private static MetadataTokenLexerDelegate originalFilenameTokenLexer;
        private static MetadataTokenLexerDelegate hookedFilenameTokenLexer;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int MetadataTokenLexerDelegate(
            IntPtr indexPointer,
            IntPtr template,
            uint templateCapacity);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint MetadataTemplateFormatterDelegate(
            IntPtr value1,
            uint value1Capacity,
            IntPtr value2,
            uint value2Capacity,
            int quoteFileNames,
            int trackNumber,
            int trackIndex,
            IntPtr metadata,
            IntPtr output,
            uint outputCapacity,
            IntPtr value3,
            uint value3Capacity,
            int hasCover,
            uint bitrate,
            int isVariousArtists,
            int isRange,
            IntPtr value4,
            uint value4Capacity,
            IntPtr value5,
            uint value5Capacity,
            IntPtr template,
            uint templateCapacity);

        private static void InstallAlbumMetadataTokenLexerHook()
        {
            RequireBytes(
                MetadataTokenLexerStaticVa,
                ExpectedMetadataTokenLexerPrologue,
                "metadata replacement-tag lexer");

            IntPtr lexerAddress = AddressFromStaticVa(MetadataTokenLexerStaticVa);
            metadataTokenLexerTrampoline = NativeMethods.VirtualAlloc(
                IntPtr.Zero,
                new UIntPtr((uint)(MetadataTokenLexerPatchLength + 5)),
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE,
                NativeMethods.PAGE_EXECUTE_READWRITE);
            if (metadataTokenLexerTrampoline == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "VirtualAlloc failed for the metadata replacement-tag lexer trampoline with Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }

            Marshal.Copy(
                ExpectedMetadataTokenLexerPrologue,
                0,
                metadataTokenLexerTrampoline,
                MetadataTokenLexerPatchLength);
            WriteRelativeJump(
                Add(metadataTokenLexerTrampoline, MetadataTokenLexerPatchLength),
                Add(lexerAddress, MetadataTokenLexerPatchLength),
                5);
            originalMetadataTokenLexer =
                (MetadataTokenLexerDelegate)Marshal.GetDelegateForFunctionPointer(
                    metadataTokenLexerTrampoline,
                    typeof(MetadataTokenLexerDelegate));
            hookedMetadataTokenLexer = HookedMetadataTokenLexer;
            IntPtr hook = Marshal.GetFunctionPointerForDelegate(
                hookedMetadataTokenLexer);
            WriteJumpPatch(
                MetadataTokenLexerStaticVa,
                Pointer32(hook),
                MetadataTokenLexerPatchLength);
            Log(
                "Album metadata replacement-tag lexer hook active at 0x" +
                lexerAddress.ToInt64().ToString("X8") + ".");
        }

        private static int HookedMetadataTokenLexer(
            IntPtr indexPointer,
            IntPtr template,
            uint templateCapacity)
        {
            try
            {
                if (indexPointer != IntPtr.Zero && template != IntPtr.Zero)
                {
                    int index = Marshal.ReadInt32(indexPointer);
                    string text =
                        Marshal.PtrToStringUni(template) ?? String.Empty;
                    int tokenLength =
                        MatchCustomAlbumMetadataToken(text, index);
                    if (tokenLength > 0)
                    {
                        Marshal.WriteInt32(indexPointer, index + tokenLength);
                        return LiteralPercentTokenId;
                    }
                }
            }
            catch (Exception error)
            {
                Log("Album metadata replacement-tag validation failed: " + error);
            }

            return originalMetadataTokenLexer(
                indexPointer,
                template,
                templateCapacity);
        }

        private static void InstallFilenameValidationTokenHooks()
        {
            uint lexerStaticVa = layout.FilenameTokenLexerVa;
            RequireBytes(
                lexerStaticVa,
                ExpectedMetadataTokenLexerPrologue,
                "filename replacement-tag lexer");

            IntPtr lexerAddress = AddressFromStaticVa(lexerStaticVa);
            filenameTokenLexerTrampoline = NativeMethods.VirtualAlloc(
                IntPtr.Zero,
                new UIntPtr((uint)(MetadataTokenLexerPatchLength + 5)),
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE,
                NativeMethods.PAGE_EXECUTE_READWRITE);
            if (filenameTokenLexerTrampoline == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "VirtualAlloc failed for the filename replacement-tag lexer trampoline with Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }

            Marshal.Copy(
                ExpectedMetadataTokenLexerPrologue,
                0,
                filenameTokenLexerTrampoline,
                MetadataTokenLexerPatchLength);
            WriteRelativeJump(
                Add(filenameTokenLexerTrampoline, MetadataTokenLexerPatchLength),
                Add(lexerAddress, MetadataTokenLexerPatchLength),
                5);
            originalFilenameTokenLexer =
                (MetadataTokenLexerDelegate)Marshal.GetDelegateForFunctionPointer(
                    filenameTokenLexerTrampoline,
                    typeof(MetadataTokenLexerDelegate));
            hookedFilenameTokenLexer = HookedFilenameTokenLexer;
            IntPtr hook = Marshal.GetFunctionPointerForDelegate(
                hookedFilenameTokenLexer);
            WriteJumpPatch(
                lexerStaticVa,
                Pointer32(hook),
                MetadataTokenLexerPatchLength);

            Log(
                "Album metadata filename validation hook active for " +
                layout.Name + " at 0x" +
                lexerAddress.ToInt64().ToString("X8") + ".");
        }

        private static int HookedFilenameTokenLexer(
            IntPtr indexPointer,
            IntPtr template,
            uint templateCapacity)
        {
            try
            {
                if (indexPointer != IntPtr.Zero &&
                    template != IntPtr.Zero)
                {
                    int index = Marshal.ReadInt32(indexPointer);
                    string text =
                        Marshal.PtrToStringUni(template) ?? String.Empty;
                    int tokenLength =
                        MatchFilenameValidationAlbumMetadataToken(
                            text,
                            index,
                            templateCapacity);
                    if (tokenLength > 0)
                    {
                        Marshal.WriteInt32(indexPointer, index + tokenLength);
                        return FilenameValidationAcceptedTokenId;
                    }
                }
            }
            catch (Exception error)
            {
                Log("Album metadata filename-tag validation failed: " + error);
            }

            return originalFilenameTokenLexer(
                indexPointer,
                template,
                templateCapacity);
        }

        internal static int MatchFilenameValidationAlbumMetadataToken(
            string template,
            int index,
            uint templateCapacity)
        {
            return templateCapacity == FilenameValidationTemplateCapacity
                ? MatchCustomAlbumMetadataToken(template, index)
                : 0;
        }

        internal static int MatchCustomAlbumMetadataToken(
            string template,
            int index)
        {
            if (String.IsNullOrEmpty(template) ||
                index < 0 ||
                index >= template.Length)
            {
                return 0;
            }

            string[] tokens =
                { "%barcode%", "%catalognumber%", "%label%" };
            foreach (string token in tokens)
            {
                if (template.Length - index >= token.Length &&
                    String.Compare(
                        template,
                        index,
                        token,
                        0,
                        token.Length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return token.Length;
                }
            }

            return 0;
        }

        private static void InstallAlbumMetadataFormatterHook()
        {
            RequireBytes(
                MetadataTemplateFormatterStaticVa,
                ExpectedMetadataTemplateFormatterPrologue,
                "metadata template formatter");

            IntPtr formatterAddress =
                AddressFromStaticVa(MetadataTemplateFormatterStaticVa);
            metadataTemplateFormatterTrampoline = NativeMethods.VirtualAlloc(
                IntPtr.Zero,
                new UIntPtr((uint)(MetadataTemplateFormatterPatchLength + 5)),
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE,
                NativeMethods.PAGE_EXECUTE_READWRITE);
            if (metadataTemplateFormatterTrampoline == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "VirtualAlloc failed for the metadata formatter trampoline with Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }

            Marshal.Copy(
                ExpectedMetadataTemplateFormatterPrologue,
                0,
                metadataTemplateFormatterTrampoline,
                MetadataTemplateFormatterPatchLength);
            WriteRelativeJump(
                Add(metadataTemplateFormatterTrampoline, MetadataTemplateFormatterPatchLength),
                Add(formatterAddress, MetadataTemplateFormatterPatchLength),
                5);
            originalMetadataTemplateFormatter =
                (MetadataTemplateFormatterDelegate)Marshal.GetDelegateForFunctionPointer(
                    metadataTemplateFormatterTrampoline,
                    typeof(MetadataTemplateFormatterDelegate));
            hookedMetadataTemplateFormatter = HookedMetadataTemplateFormatter;
            IntPtr hook = Marshal.GetFunctionPointerForDelegate(
                hookedMetadataTemplateFormatter);
            WriteJumpPatch(
                MetadataTemplateFormatterStaticVa,
                Pointer32(hook),
                MetadataTemplateFormatterPatchLength);
            Log(
                "Album metadata formatter hook active at 0x" +
                formatterAddress.ToInt64().ToString("X8") + ".");
        }

        private static uint HookedMetadataTemplateFormatter(
            IntPtr value1,
            uint value1Capacity,
            IntPtr value2,
            uint value2Capacity,
            int quoteFileNames,
            int trackNumber,
            int trackIndex,
            IntPtr metadata,
            IntPtr output,
            uint outputCapacity,
            IntPtr value3,
            uint value3Capacity,
            int hasCover,
            uint bitrate,
            int isVariousArtists,
            int isRange,
            IntPtr value4,
            uint value4Capacity,
            IntPtr value5,
            uint value5Capacity,
            IntPtr template,
            uint templateCapacity)
        {
            IntPtr expandedTemplate = IntPtr.Zero;
            try
            {
                string original = template == IntPtr.Zero
                    ? String.Empty
                    : Marshal.PtrToStringUni(template) ?? String.Empty;
                string expanded = ExpandCurrentAlbumMetadataTokens(original);
                if (!String.Equals(original, expanded, StringComparison.Ordinal))
                {
                    expandedTemplate = Marshal.StringToHGlobalUni(expanded);
                    template = expandedTemplate;
                    templateCapacity = (uint)expanded.Length;
                }
            }
            catch (Exception error)
            {
                Log("Album metadata token expansion failed: " + error);
            }

            try
            {
                return originalMetadataTemplateFormatter(
                    value1,
                    value1Capacity,
                    value2,
                    value2Capacity,
                    quoteFileNames,
                    trackNumber,
                    trackIndex,
                    metadata,
                    output,
                    outputCapacity,
                    value3,
                    value3Capacity,
                    hasCover,
                    bitrate,
                    isVariousArtists,
                    isRange,
                    value4,
                    value4Capacity,
                    value5,
                    value5Capacity,
                    template,
                    templateCapacity);
            }
            finally
            {
                if (expandedTemplate != IntPtr.Zero)
                    Marshal.FreeHGlobal(expandedTemplate);
            }
        }

        internal static string ExpandAlbumMetadataTokens(
            string template,
            string barcode,
            string catalogNumber,
            string label)
        {
            string result = template ?? String.Empty;
            result = ReplaceAlbumMetadataToken(result, "%barcode%", barcode);
            result = ReplaceAlbumMetadataToken(
                result,
                "%catalognumber%",
                catalogNumber);
            result = ReplaceAlbumMetadataToken(result, "%label%", label);
            return result;
        }

        internal static string ExpandCurrentAlbumMetadataTokens(string template)
        {
            return ExpandAlbumMetadataTokens(
                template,
                albumBarcode,
                albumCatalogNumber,
                albumLabel);
        }

        private static string ReplaceAlbumMetadataToken(
            string template,
            string token,
            string value)
        {
            string escapedValue = (value ?? String.Empty)
                .Replace("\"", "'")
                .Replace("%", "%%");
            return Regex.Replace(
                template,
                Regex.Escape(token),
                delegate { return escapedValue; },
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void MaybeInstallAlbumMetadataControls(IntPtr mainWindow)
        {
            if (AreAlbumMetadataControlsAvailable())
                return;
            int tick = Environment.TickCount;
            if (tick - lastAlbumMetadataInstallTick < 1000)
                return;
            lastAlbumMetadataInstallTick = tick;
            InstallAlbumMetadataControls(mainWindow);
        }

        internal static void InstallAlbumMetadataControls(IntPtr mainWindow)
        {
            if (mainWindow == IntPtr.Zero ||
                (albumBarcodeEdit != IntPtr.Zero &&
                 NativeMethods.IsWindow(albumBarcodeEdit)))
            {
                return;
            }

            IntPtr genre = FindDescendantControl(mainWindow, GenreControlId);
            IntPtr comment = FindDescendantControl(mainWindow, CommentControlId);
            IntPtr composer = FindDescendantControl(
                mainWindow, CdComposerControlId);
            IntPtr performer = FindDescendantControl(
                mainWindow, CdPerformerControlId);
            IntPtr titleLabel = FindDescendantControl(
                mainWindow, CdTitleLabelControlId);
            IntPtr composerLabel = FindDescendantControl(
                mainWindow, CdComposerLabelControlId);
            IntPtr performerLabel = FindDescendantControl(
                mainWindow, CdPerformerLabelControlId);
            IntPtr genreLabel = FindDescendantControl(
                mainWindow, GenreLabelControlId);
            IntPtr commentLabel = FindDescendantControl(
                mainWindow, CommentLabelControlId);
            if (genre == IntPtr.Zero || comment == IntPtr.Zero ||
                composer == IntPtr.Zero || performer == IntPtr.Zero ||
                titleLabel == IntPtr.Zero || composerLabel == IntPtr.Zero ||
                performerLabel == IntPtr.Zero || genreLabel == IntPtr.Zero ||
                commentLabel == IntPtr.Zero)
            {
                return;
            }

            albumMetadataParent = NativeMethods.GetParent(genre);
            if (albumMetadataParent == IntPtr.Zero ||
                NativeMethods.GetParent(comment) != albumMetadataParent ||
                NativeMethods.GetParent(composer) != albumMetadataParent ||
                NativeMethods.GetParent(performer) != albumMetadataParent ||
                NativeMethods.GetParent(titleLabel) != albumMetadataParent ||
                NativeMethods.GetParent(composerLabel) != albumMetadataParent ||
                NativeMethods.GetParent(performerLabel) != albumMetadataParent ||
                NativeMethods.GetParent(genreLabel) != albumMetadataParent ||
                NativeMethods.GetParent(commentLabel) != albumMetadataParent)
            {
                albumMetadataParent = IntPtr.Zero;
                Log("Album metadata controls were not installed because EAC's reference fields did not share a parent window.");
                return;
            }

            // A newly created EAC metadata panel normally represents a
            // fresh disc context. A database load can occur just before the
            // panel exists, in which case retain the pending stored values.
            if (!albumMetadataStoreLoadPending)
            {
                albumBarcode = String.Empty;
                albumCatalogNumber = String.Empty;
                albumLabel = String.Empty;
            }

            int editStyle = NativeMethods.GetWindowLongW(comment, GwlStyle);
            int editExtendedStyle =
                NativeMethods.GetWindowLongW(comment, GwlExtendedStyle);
            int genreLabelStyle =
                NativeMethods.GetWindowLongW(genreLabel, GwlStyle);
            int commentLabelStyle =
                NativeMethods.GetWindowLongW(commentLabel, GwlStyle);
            int performerLabelStyle =
                NativeMethods.GetWindowLongW(performerLabel, GwlStyle);

            albumBarcodeLabel = NativeMethods.CreateWindowExW(
                0,
                "STATIC",
                "CD Barcode",
                unchecked((uint)genreLabelStyle),
                0,
                0,
                1,
                1,
                albumMetadataParent,
                new IntPtr(AlbumBarcodeLabelControlId),
                IntPtr.Zero,
                IntPtr.Zero);
            albumBarcodeEdit = NativeMethods.CreateWindowExW(
                unchecked((uint)editExtendedStyle),
                "EDIT",
                String.Empty,
                unchecked((uint)editStyle),
                0,
                0,
                1,
                1,
                albumMetadataParent,
                new IntPtr(AlbumBarcodeControlId),
                IntPtr.Zero,
                IntPtr.Zero);
            albumCatalogNumberLabel = NativeMethods.CreateWindowExW(
                0,
                "STATIC",
                "CD Catalog #",
                unchecked((uint)commentLabelStyle),
                0,
                0,
                1,
                1,
                albumMetadataParent,
                new IntPtr(AlbumCatalogNumberLabelControlId),
                IntPtr.Zero,
                IntPtr.Zero);
            albumCatalogNumberEdit = NativeMethods.CreateWindowExW(
                unchecked((uint)editExtendedStyle),
                "EDIT",
                String.Empty,
                unchecked((uint)editStyle),
                0,
                0,
                1,
                1,
                albumMetadataParent,
                new IntPtr(AlbumCatalogNumberControlId),
                IntPtr.Zero,
                IntPtr.Zero);
            albumLabelLabel = NativeMethods.CreateWindowExW(
                0,
                "STATIC",
                "CD Label",
                unchecked((uint)performerLabelStyle),
                0,
                0,
                1,
                1,
                albumMetadataParent,
                new IntPtr(AlbumLabelLabelControlId),
                IntPtr.Zero,
                IntPtr.Zero);
            albumLabelEdit = NativeMethods.CreateWindowExW(
                unchecked((uint)editExtendedStyle),
                "EDIT",
                String.Empty,
                unchecked((uint)editStyle),
                0,
                0,
                1,
                1,
                albumMetadataParent,
                new IntPtr(AlbumLabelControlId),
                IntPtr.Zero,
                IntPtr.Zero);

            if (albumBarcodeLabel == IntPtr.Zero ||
                albumBarcodeEdit == IntPtr.Zero ||
                albumCatalogNumberLabel == IntPtr.Zero ||
                albumCatalogNumberEdit == IntPtr.Zero ||
                albumLabelLabel == IntPtr.Zero ||
                albumLabelEdit == IntPtr.Zero)
            {
                DestroyAlbumMetadataControls();
                throw new InvalidOperationException(
                    "CreateWindowExW failed for the album metadata fields with Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }

            IntPtr font = NativeMethods.SendMessageW(
                comment,
                NativeMethods.WM_GETFONT,
                IntPtr.Zero,
                IntPtr.Zero);
            SetAlbumMetadataControlFont(albumBarcodeEdit, font);
            SetAlbumMetadataControlFont(albumCatalogNumberEdit, font);
            SetAlbumMetadataControlFont(albumLabelEdit, font);
            SetAlbumMetadataControlFont(
                albumBarcodeLabel,
                NativeMethods.SendMessageW(
                    genreLabel,
                    NativeMethods.WM_GETFONT,
                    IntPtr.Zero,
                    IntPtr.Zero));
            SetAlbumMetadataControlFont(
                albumCatalogNumberLabel,
                NativeMethods.SendMessageW(
                    commentLabel,
                    NativeMethods.WM_GETFONT,
                    IntPtr.Zero,
                    IntPtr.Zero));
            SetAlbumMetadataControlFont(
                albumLabelLabel,
                NativeMethods.SendMessageW(
                    performerLabel,
                    NativeMethods.WM_GETFONT,
                    IntPtr.Zero,
                    IntPtr.Zero));
            NativeMethods.SendMessageW(
                albumBarcodeEdit,
                NativeMethods.EM_SETLIMITTEXT,
                new IntPtr(511),
                IntPtr.Zero);
            NativeMethods.SendMessageW(
                albumCatalogNumberEdit,
                NativeMethods.EM_SETLIMITTEXT,
                new IntPtr(511),
                IntPtr.Zero);
            NativeMethods.SendMessageW(
                albumLabelEdit,
                NativeMethods.EM_SETLIMITTEXT,
                new IntPtr(511),
                IntPtr.Zero);

            albumMetadataEditSubclassDelegate = AlbumMetadataEditSubclass;
            IntPtr editSubclassProcedure =
                Marshal.GetFunctionPointerForDelegate(
                    albumMetadataEditSubclassDelegate);
            if (!NativeMethods.SetWindowSubclass(
                    albumLabelEdit,
                    editSubclassProcedure,
                    new UIntPtr(246194964u),
                    UIntPtr.Zero) ||
                !NativeMethods.SetWindowSubclass(
                    albumBarcodeEdit,
                    editSubclassProcedure,
                    new UIntPtr(246194964u),
                    UIntPtr.Zero) ||
                !NativeMethods.SetWindowSubclass(
                    albumCatalogNumberEdit,
                    editSubclassProcedure,
                    new UIntPtr(246194964u),
                    UIntPtr.Zero))
            {
                DestroyAlbumMetadataControls();
                albumMetadataEditSubclassDelegate = null;
                throw new InvalidOperationException(
                    "SetWindowSubclass failed for EAC's album metadata edits with Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }

            albumMetadataParentSubclassDelegate = AlbumMetadataParentSubclass;
            IntPtr subclassProcedure = Marshal.GetFunctionPointerForDelegate(
                albumMetadataParentSubclassDelegate);
            if (!NativeMethods.SetWindowSubclass(
                albumMetadataParent,
                subclassProcedure,
                new UIntPtr(246194963u),
                UIntPtr.Zero))
            {
                DestroyAlbumMetadataControls();
                albumMetadataParentSubclassDelegate = null;
                albumMetadataEditSubclassDelegate = null;
                throw new InvalidOperationException(
                    "SetWindowSubclass failed for EAC's album metadata panel with Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }

            albumMetadataStateControlSubclassDelegate =
                AlbumMetadataStateControlSubclass;
            IntPtr stateSubclassProcedure = Marshal.GetFunctionPointerForDelegate(
                albumMetadataStateControlSubclassDelegate);
            if (!NativeMethods.SetWindowSubclass(
                    genre,
                    stateSubclassProcedure,
                    new UIntPtr(246194965u),
                    UIntPtr.Zero))
            {
                // The parent timer remains a fallback state synchronizer.
                Log(
                    "Immediate album metadata enabled-state synchronization " +
                    "could not be installed; Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }

            LayoutAlbumMetadataControls(albumMetadataParent);
            SetAlbumMetadataEditText(albumLabelEdit, albumLabel);
            SetAlbumMetadataEditText(albumBarcodeEdit, albumBarcode);
            SetAlbumMetadataEditText(
                albumCatalogNumberEdit,
                albumCatalogNumber);
            CompletePendingAlbumMetadataStoreLoadIfReady(
                albumMetadataParent);
            ApplyAlbumMetadataControlState(albumMetadataParent);
            Log("CD Label, CD Barcode, and CD Catalog # fields installed on EAC's main window.");
        }

        private static IntPtr AlbumMetadataEditSubclass(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData)
        {
            bool userEditMessage =
                message == NativeMethods.WM_CHAR ||
                (message == NativeMethods.WM_KEYDOWN &&
                    wParam.ToInt64() == 0x2E) ||
                (message == NativeMethods.WM_IME_COMPOSITION &&
                    (lParam.ToInt64() & 0x800) != 0) ||
                message == NativeMethods.WM_CUT ||
                message == NativeMethods.WM_PASTE ||
                message == NativeMethods.WM_CLEAR ||
                message == NativeMethods.WM_UNDO;
            if (!userEditMessage)
            {
                return NativeMethods.DefSubclassProc(
                    hwnd,
                    message,
                    wParam,
                    lParam);
            }

            IntPtr previousUserEditControl = albumMetadataUserEditControl;
            albumMetadataUserEditControl = hwnd;
            try
            {
                return NativeMethods.DefSubclassProc(
                    hwnd,
                    message,
                    wParam,
                    lParam);
            }
            finally
            {
                albumMetadataUserEditControl = previousUserEditControl;
            }
        }

        private static IntPtr AlbumMetadataStateControlSubclass(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData)
        {
            IntPtr result = NativeMethods.DefSubclassProc(
                hwnd,
                message,
                wParam,
                lParam);
            if (message == NativeMethods.WM_ENABLE)
                SetAlbumMetadataControlsEnabled(wParam != IntPtr.Zero);
            return result;
        }

        private static IntPtr AlbumMetadataParentSubclass(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData)
        {
            try
            {
                if (message == NativeMethods.WM_SIZE)
                {
                    IntPtr result = NativeMethods.DefSubclassProc(
                        hwnd, message, wParam, lParam);
                    LayoutAlbumMetadataControls(hwnd);
                    return result;
                }
                if (message == NativeMethods.WM_TIMER)
                {
                    IntPtr result = NativeMethods.DefSubclassProc(
                        hwnd, message, wParam, lParam);
                    ObserveAlbumMetadataCommand(
                        hwnd,
                        message,
                        wParam,
                        lParam);
                    return result;
                }
                ObserveAlbumMetadataCommand(hwnd, message, wParam, lParam);
            }
            catch (Exception error)
            {
                Log("Album metadata panel subclass callback failed: " + error);
            }
            return NativeMethods.DefSubclassProc(
                hwnd, message, wParam, lParam);
        }

        private static void SetAlbumMetadataControlFont(IntPtr control, IntPtr font)
        {
            if (control != IntPtr.Zero && font != IntPtr.Zero)
            {
                NativeMethods.SendMessageW(
                    control,
                    NativeMethods.WM_SETFONT,
                    font,
                    new IntPtr(1));
            }
        }

        private static void LayoutAlbumMetadataControls(IntPtr parent)
        {
            LayoutAlbumMetadataControls(
                parent,
                layout != null &&
                String.Equals(
                    layout.Name,
                    "EAC 1.6",
                    StringComparison.Ordinal));
        }

        internal static void LayoutAlbumMetadataControls(
            IntPtr parent,
            bool useEac16FreedbGenreLayout)
        {
            if (!AreAlbumMetadataControlsAvailable())
                return;

            NativeMethods.RECT genre;
            NativeMethods.RECT comment;
            NativeMethods.RECT composer;
            NativeMethods.RECT title;
            NativeMethods.RECT titleLabel;
            NativeMethods.RECT composerLabel;
            NativeMethods.RECT genreLabel;
            NativeMethods.RECT commentLabel;
            IntPtr genreControl =
                NativeMethods.GetDlgItem(parent, GenreControlId);
            IntPtr commentControl =
                NativeMethods.GetDlgItem(parent, CommentControlId);
            IntPtr commentLabelControl =
                NativeMethods.GetDlgItem(parent, CommentLabelControlId);
            IntPtr performer =
                NativeMethods.GetDlgItem(parent, CdPerformerControlId);
            IntPtr performerLabel =
                NativeMethods.GetDlgItem(parent, CdPerformerLabelControlId);
            if (performer == IntPtr.Zero || performerLabel == IntPtr.Zero)
                return;
            if (!TryGetChildClientRectangle(
                    parent,
                    genreControl,
                    out genre) ||
                !TryGetChildClientRectangle(
                    parent,
                    commentControl,
                    out comment) ||
                !TryGetChildClientRectangle(
                    parent,
                    NativeMethods.GetDlgItem(parent, CdComposerControlId),
                    out composer) ||
                !TryGetChildClientRectangle(
                    parent,
                    NativeMethods.GetDlgItem(parent, CdTitleControlId),
                    out title) ||
                !TryGetChildClientRectangle(
                    parent,
                    NativeMethods.GetDlgItem(parent, CdTitleLabelControlId),
                    out titleLabel) ||
                !TryGetChildClientRectangle(
                    parent,
                    NativeMethods.GetDlgItem(parent, CdComposerLabelControlId),
                    out composerLabel) ||
                !TryGetChildClientRectangle(
                    parent,
                    NativeMethods.GetDlgItem(parent, GenreLabelControlId),
                    out genreLabel) ||
                !TryGetChildClientRectangle(
                    parent,
                    commentLabelControl,
                    out commentLabel))
            {
                return;
            }

            NativeMethods.RECT commentSlot = comment;
            NativeMethods.RECT commentLabelSlot = commentLabel;
            if (useEac16FreedbGenreLayout)
            {
                // Comment is moved by the EAC 1.6 branch below. Continue to
                // derive its original column from the unmoved CD Composer
                // anchors so repeated WM_SIZE layouts remain idempotent.
                commentSlot.Left = composer.Left;
                commentSlot.Right = composer.Right;
                commentLabelSlot.Left = composerLabel.Left;
                commentLabelSlot.Right = composerLabel.Right;
            }

            int rowPitch = comment.Top - composer.Top;
            if (rowPitch <= 0)
                return;
            int thirdRowTop = comment.Top + rowPitch;
            int thirdLabelTop = thirdRowTop + titleLabel.Top - title.Top;
            int thirdRowHeight = comment.Bottom - comment.Top;
            int columnGap = composerLabel.Left - genre.Right;
            int performerLabelLeft = composer.Right + columnGap;
            int performerEditLeft =
                performerLabelLeft + composerLabel.Right - composerLabel.Left;

            PositionAlbumMetadataControl(
                albumLabelLabel,
                titleLabel.Left,
                thirdLabelTop,
                titleLabel.Right - titleLabel.Left,
                titleLabel.Bottom - titleLabel.Top);
            PositionAlbumMetadataControl(
                albumLabelEdit,
                title.Left,
                thirdRowTop,
                title.Right - title.Left,
                thirdRowHeight);
            PositionAlbumMetadataControl(
                albumBarcodeLabel,
                genreLabel.Left,
                thirdRowTop + genreLabel.Top - genre.Top,
                genreLabel.Right - genreLabel.Left,
                genreLabel.Bottom - genreLabel.Top);
            PositionAlbumMetadataControl(
                albumBarcodeEdit,
                genre.Left,
                thirdRowTop,
                genre.Right - genre.Left,
                thirdRowHeight);
            PositionAlbumMetadataControl(
                albumCatalogNumberLabel,
                commentLabelSlot.Left,
                thirdRowTop + commentLabelSlot.Top - commentSlot.Top,
                commentLabelSlot.Right - commentLabelSlot.Left,
                commentLabelSlot.Bottom - commentLabelSlot.Top);
            PositionAlbumMetadataControl(
                albumCatalogNumberEdit,
                commentSlot.Left,
                thirdRowTop,
                commentSlot.Right - commentSlot.Left,
                thirdRowHeight);
            PositionAlbumMetadataControl(
                performerLabel,
                performerLabelLeft,
                composerLabel.Top,
                composerLabel.Right - composerLabel.Left,
                composerLabel.Bottom - composerLabel.Top);
            PositionAlbumMetadataControl(
                performer,
                performerEditLeft,
                composer.Top,
                composer.Right - composer.Left,
                composer.Bottom - composer.Top);

            if (useEac16FreedbGenreLayout)
            {
                IntPtr freedbGenreLabel =
                    NativeMethods.GetDlgItem(
                        parent,
                        FreedbGenreLabelControlId);
                IntPtr freedbGenre =
                    NativeMethods.GetDlgItem(parent, FreedbGenreControlId);
                NativeMethods.RECT freedbGenreRectangle;
                if (freedbGenreLabel != IntPtr.Zero &&
                    freedbGenre != IntPtr.Zero &&
                    TryGetChildClientRectangle(
                        parent,
                        freedbGenre,
                        out freedbGenreRectangle))
                {
                    // EAC 1.6 retains a freedb-specific Genre combo in the
                    // third-row slot now used by CD Barcode. Put it in
                    // Comment's original slot and move Comment beneath the
                    // relocated CD Performer. EAC 1.7/1.8 removed this
                    // legacy combo and never enter this branch.
                    PositionAlbumMetadataControl(
                        commentLabelControl,
                        performerLabelLeft,
                        commentLabelSlot.Top,
                        composerLabel.Right - composerLabel.Left,
                        commentLabelSlot.Bottom - commentLabelSlot.Top);
                    PositionAlbumMetadataControl(
                        commentControl,
                        performerEditLeft,
                        commentSlot.Top,
                        composer.Right - composer.Left,
                        commentSlot.Bottom - commentSlot.Top);
                    PositionAlbumMetadataControl(
                        freedbGenreLabel,
                        commentLabelSlot.Left,
                        commentLabelSlot.Top,
                        commentLabelSlot.Right - commentLabelSlot.Left,
                        commentLabelSlot.Bottom - commentLabelSlot.Top);
                    PositionAlbumMetadataControl(
                        freedbGenre,
                        commentSlot.Left,
                        commentSlot.Top,
                        commentSlot.Right - commentSlot.Left,
                        freedbGenreRectangle.Bottom -
                            freedbGenreRectangle.Top);
                }
            }
        }

        private static void PositionAlbumMetadataControl(
            IntPtr control,
            int left,
            int top,
            int width,
            int height)
        {
            NativeMethods.SetWindowPos(
                control,
                IntPtr.Zero,
                left,
                top,
                width,
                height,
                SwpNoZOrder | SwpNoActivate);
        }

        private static bool TryGetChildClientRectangle(
            IntPtr parent,
            IntPtr child,
            out NativeMethods.RECT rectangle)
        {
            rectangle = new NativeMethods.RECT();
            if (child == IntPtr.Zero ||
                !NativeMethods.GetWindowRect(child, out rectangle))
            {
                return false;
            }

            NativeMethods.POINT topLeft = new NativeMethods.POINT
            {
                X = rectangle.Left,
                Y = rectangle.Top
            };
            NativeMethods.POINT bottomRight = new NativeMethods.POINT
            {
                X = rectangle.Right,
                Y = rectangle.Bottom
            };
            if (!NativeMethods.ScreenToClient(parent, ref topLeft) ||
                !NativeMethods.ScreenToClient(parent, ref bottomRight))
            {
                return false;
            }

            rectangle.Left = topLeft.X;
            rectangle.Top = topLeft.Y;
            rectangle.Right = bottomRight.X;
            rectangle.Bottom = bottomRight.Y;
            return true;
        }

        private static void ObserveAlbumMetadataCommand(
            IntPtr parent,
            uint message,
            IntPtr wParam,
            IntPtr lParam)
        {
            if (message == NativeMethods.WM_CLOSE)
            {
                NativeMethods.KillTimer(
                    parent,
                    new UIntPtr(AlbumMetadataStoreSaveTimerId));
                PersistPendingAlbumMetadataStoreChanges();
                return;
            }
            if (message == NativeMethods.WM_TIMER)
            {
                if (wParam.ToInt64() == AlbumMetadataStoreSaveTimerId)
                {
                    NativeMethods.KillTimer(
                        parent,
                        new UIntPtr(AlbumMetadataStoreSaveTimerId));
                    PersistPendingAlbumMetadataStoreChanges();
                }
                if (layout != null &&
                    String.Equals(
                        layout.Name,
                        "EAC 1.6",
                        StringComparison.Ordinal))
                {
                    // EAC 1.6's metadata refresh moves its native Performer
                    // and freedb Genre controls back into the slots occupied
                    // by CD Label and CD Barcode.
                    LayoutAlbumMetadataControls(parent);
                }
                ApplyAlbumMetadataControlState(parent);
                return;
            }
            if (message != NativeMethods.WM_COMMAND || lParam == IntPtr.Zero)
                return;

            long commandValue = wParam.ToInt64();
            int command = (int)commandValue & 0xFFFF;
            int notification = (int)(commandValue >> 16) & 0xFFFF;
            bool isAlbumMetadataEdit =
                (lParam == albumBarcodeEdit &&
                    command == AlbumBarcodeControlId) ||
                (lParam == albumCatalogNumberEdit &&
                    command == AlbumCatalogNumberControlId) ||
                (lParam == albumLabelEdit &&
                    command == AlbumLabelControlId);
            if (isAlbumMetadataEdit &&
                notification == NativeMethods.EN_KILLFOCUS)
            {
                NativeMethods.KillTimer(
                    parent,
                    new UIntPtr(AlbumMetadataStoreSaveTimerId));
                PersistPendingAlbumMetadataStoreChanges();
                return;
            }
            if (notification != NativeMethods.EN_CHANGE)
                return;

            if (lParam == albumBarcodeEdit &&
                command == AlbumBarcodeControlId)
            {
                if (albumMetadataUserEditControl != lParam)
                    return;
                albumMetadataStoreLoadPending = false;
                albumBarcode = ReadWindowText(albumBarcodeEdit);
                MarkAlbumMetadataStoreDirty(parent);
            }
            else if (lParam == albumCatalogNumberEdit &&
                command == AlbumCatalogNumberControlId)
            {
                if (albumMetadataUserEditControl != lParam)
                    return;
                albumMetadataStoreLoadPending = false;
                albumCatalogNumber = ReadWindowText(albumCatalogNumberEdit);
                MarkAlbumMetadataStoreDirty(parent);
            }
            else if (lParam == albumLabelEdit &&
                command == AlbumLabelControlId)
            {
                if (albumMetadataUserEditControl != lParam)
                    return;
                albumMetadataStoreLoadPending = false;
                albumLabel = ReadWindowText(albumLabelEdit);
                MarkAlbumMetadataStoreDirty(parent);
            }
            else if (command == CdTitleControlId)
            {
                if (String.IsNullOrEmpty(ReadWindowText(lParam)))
                {
                    if (!albumMetadataStoreLoadPending)
                        ClearAlbumMetadataControls();
                }
                else
                {
                    CompletePendingAlbumMetadataStoreLoadIfReady(parent);
                }
            }
        }

        private static void CompletePendingAlbumMetadataStoreLoadIfReady(
            IntPtr parent)
        {
            if (!albumMetadataStoreLoadPending ||
                !AreAlbumMetadataControlsAvailable())
            {
                return;
            }
            IntPtr title = NativeMethods.GetDlgItem(parent, CdTitleControlId);
            if (title == IntPtr.Zero ||
                String.IsNullOrEmpty(ReadWindowText(title)))
            {
                return;
            }

            SetAlbumMetadataEditText(albumLabelEdit, albumLabel);
            SetAlbumMetadataEditText(albumBarcodeEdit, albumBarcode);
            SetAlbumMetadataEditText(
                albumCatalogNumberEdit,
                albumCatalogNumber);
            albumMetadataStoreLoadPending = false;
        }

        private static void MarkAlbumMetadataStoreDirty(IntPtr parent)
        {
            IntPtr title = NativeMethods.GetDlgItem(
                parent,
                CdTitleControlId);
            if (title == IntPtr.Zero ||
                String.IsNullOrEmpty(ReadWindowText(title)))
            {
                return;
            }
            SetAlbumMetadataStoreDirty();
            NativeMethods.SetTimer(
                parent,
                new UIntPtr(AlbumMetadataStoreSaveTimerId),
                AlbumMetadataStoreSaveDelayMilliseconds,
                IntPtr.Zero);
        }

        private static string ReadWindowText(IntPtr control)
        {
            if (control == IntPtr.Zero || !NativeMethods.IsWindow(control))
                return String.Empty;
            StringBuilder value = new StringBuilder(512);
            NativeMethods.GetWindowTextW(control, value, value.Capacity);
            return value.ToString();
        }

        private static void ApplyAlbumMetadataControlState(IntPtr parent)
        {
            if (!AreAlbumMetadataControlsAvailable())
                return;
            IntPtr genre = NativeMethods.GetDlgItem(parent, GenreControlId);
            bool enabled = genre != IntPtr.Zero &&
                NativeMethods.IsWindowEnabled(genre);
            SetAlbumMetadataControlsEnabled(enabled);
        }

        private static void SetAlbumMetadataControlsEnabled(bool enabled)
        {
            if (!AreAlbumMetadataControlsAvailable())
                return;
            NativeMethods.EnableWindow(albumBarcodeLabel, enabled);
            NativeMethods.EnableWindow(albumBarcodeEdit, enabled);
            NativeMethods.EnableWindow(albumCatalogNumberLabel, enabled);
            NativeMethods.EnableWindow(albumCatalogNumberEdit, enabled);
            NativeMethods.EnableWindow(albumLabelLabel, enabled);
            NativeMethods.EnableWindow(albumLabelEdit, enabled);
        }

        private static bool AreAlbumMetadataControlsAvailable()
        {
            return albumBarcodeLabel != IntPtr.Zero &&
                NativeMethods.IsWindow(albumBarcodeLabel) &&
                albumBarcodeEdit != IntPtr.Zero &&
                NativeMethods.IsWindow(albumBarcodeEdit) &&
                albumCatalogNumberLabel != IntPtr.Zero &&
                NativeMethods.IsWindow(albumCatalogNumberLabel) &&
                albumCatalogNumberEdit != IntPtr.Zero &&
                NativeMethods.IsWindow(albumCatalogNumberEdit) &&
                albumLabelLabel != IntPtr.Zero &&
                NativeMethods.IsWindow(albumLabelLabel) &&
                albumLabelEdit != IntPtr.Zero &&
                NativeMethods.IsWindow(albumLabelEdit);
        }

        private static IntPtr FindDescendantControl(
            IntPtr parent,
            int controlId)
        {
            IntPtr result = IntPtr.Zero;
            NativeMethods.EnumChildProc callback = delegate(IntPtr hwnd, IntPtr ignored)
            {
                if (NativeMethods.GetDlgCtrlID(hwnd) != controlId)
                    return true;
                result = hwnd;
                return false;
            };
            NativeMethods.EnumChildWindows(parent, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return result;
        }

        private static void ClearAlbumMetadataControls()
        {
            albumMetadataStoreLoadPending = false;
            albumBarcode = String.Empty;
            albumCatalogNumber = String.Empty;
            albumLabel = String.Empty;
            if (albumBarcodeEdit != IntPtr.Zero &&
                NativeMethods.IsWindow(albumBarcodeEdit))
            {
                NativeMethods.SendMessageStringW(
                    albumBarcodeEdit,
                    NativeMethods.WM_SETTEXT,
                    IntPtr.Zero,
                    String.Empty);
            }
            if (albumCatalogNumberEdit != IntPtr.Zero &&
                NativeMethods.IsWindow(albumCatalogNumberEdit))
            {
                NativeMethods.SendMessageStringW(
                    albumCatalogNumberEdit,
                    NativeMethods.WM_SETTEXT,
                    IntPtr.Zero,
                    String.Empty);
            }
            if (albumLabelEdit != IntPtr.Zero &&
                NativeMethods.IsWindow(albumLabelEdit))
            {
                NativeMethods.SendMessageStringW(
                    albumLabelEdit,
                    NativeMethods.WM_SETTEXT,
                    IntPtr.Zero,
                    String.Empty);
            }
        }

        private static void DestroyAlbumMetadataControls()
        {
            IntPtr[] controls =
            {
                albumBarcodeLabel,
                albumBarcodeEdit,
                albumCatalogNumberLabel,
                albumCatalogNumberEdit,
                albumLabelLabel,
                albumLabelEdit
            };
            foreach (IntPtr control in controls)
            {
                if (control != IntPtr.Zero && NativeMethods.IsWindow(control))
                    NativeMethods.DestroyWindow(control);
            }
            albumBarcodeLabel = IntPtr.Zero;
            albumBarcodeEdit = IntPtr.Zero;
            albumCatalogNumberLabel = IntPtr.Zero;
            albumCatalogNumberEdit = IntPtr.Zero;
            albumLabelLabel = IntPtr.Zero;
            albumLabelEdit = IntPtr.Zero;
            albumMetadataParent = IntPtr.Zero;
            albumMetadataUserEditControl = IntPtr.Zero;
        }
    }
}
