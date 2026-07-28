using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AudioDataPlugIn
{
    internal static partial class EnhancementRuntime
    {
        internal const int NativeExternalEncoderOptionsLimit = 500;
        internal const int ExtendedExternalEncoderOptionsLimit = 1000;
        internal const string ExtendedExternalEncoderOptionsMarker =
            "%eaceextendedargs%";
        internal const string
            IncreaseExternalCompressorArgumentsLimitIniKey =
                "IncreaseExternalCompressorArgumentsLimit";

        private const int ExternalEncoderOptionsControlId = 0x1777;
        private const int TestExternalEncoderControlId = 0x1783;
        private const int PropertySheetApplyControlId = 0x3021;
        private const int PsnSetActive = -200;
        private const int PsnKillActive = -201;
        private const int PsnApply = -202;
        private const uint HydrateExtendedCompressorArgumentsTimerId = 0xEAC7;
        private const uint ExtendedCompressorArgumentsTimerDelayMs = 100;
        private const uint ExtendedCompressorArgumentsSubclassId = 246194966u;
        private const uint ExtendedCompressorArgumentsParentSubclassId =
            246194967u;
        private const uint ExtendedCompressorArgumentsEditSubclassId =
            246194968u;
        private const string ExtendedCompressorArgumentsIniSection =
            "ExternalCompression";
        private const string ExtendedCompressorArgumentsIniKey =
            "ExtendedEncoderOptions";

        private static readonly object ExtendedCompressorArgumentsLock =
            new object();

        private static MainWindowSubclassDelegate
            extendedCompressorArgumentsSubclassDelegate;
        private static MainWindowSubclassDelegate
            extendedCompressorArgumentsParentSubclassDelegate;
        private static MainWindowSubclassDelegate
            extendedCompressorArgumentsEditSubclassDelegate;
        private static IntPtr extendedCompressorArgumentsParent;
        private static IntPtr extendedCompressorArgumentsPage;
        private static IntPtr extendedCompressorArgumentsEdit;
        private static bool updatingExtendedCompressorArgumentsEdit;
        private static int extendedCompressorArgumentsRedrawSuspendDepth;
        private static bool extendedCompressorArgumentsEditHydrated;
        private static bool extendedCompressorArgumentsUserEdited;
        private static string nativeExternalEncoderOptionsFallback =
            String.Empty;
        private static string pendingExtendedCompressorArgumentsDisplay =
            String.Empty;
        private static bool extendedCompressorArgumentsLoaded;
        private static string extendedCompressorArguments = String.Empty;
        private static string transientExtendedCompressorArguments =
            String.Empty;
        private static int extendedCompressorArgumentsEnabled = -1;

        internal static bool IsExtendedCompressorArgumentsEnabled()
        {
            int current = Interlocked.CompareExchange(
                ref extendedCompressorArgumentsEnabled,
                -1,
                -1);
            if (current >= 0)
                return current != 0;

            bool enabled = true;
            try
            {
                enabled = ParseIniBoolean(
                    ReadIniValue(
                        GetSettingsFilePath(),
                        IncreaseExternalCompressorArgumentsLimitIniKey,
                        "1"),
                    true);
            }
            catch (Exception error)
            {
                Log(
                    "Could not read the extended external-compressor argument option; defaulting to enabled: " +
                    error.Message);
            }

            Interlocked.CompareExchange(
                ref extendedCompressorArgumentsEnabled,
                enabled ? 1 : 0,
                -1);
            return Interlocked.CompareExchange(
                ref extendedCompressorArgumentsEnabled,
                -1,
                -1) != 0;
        }

        internal static void UpdateExtendedCompressorArgumentsPreference(
            bool enabled)
        {
            Interlocked.Exchange(
                ref extendedCompressorArgumentsEnabled,
                enabled ? 1 : 0);

            pendingExtendedCompressorArgumentsDisplay = String.Empty;
            SetTransientExtendedCompressorArguments(String.Empty);
            if (extendedCompressorArgumentsPage != IntPtr.Zero &&
                NativeMethods.IsWindow(extendedCompressorArgumentsPage))
            {
                NativeMethods.KillTimer(
                    extendedCompressorArgumentsPage,
                    new UIntPtr(
                        HydrateExtendedCompressorArgumentsTimerId));
            }

            if (extendedCompressorArgumentsEdit == IntPtr.Zero ||
                !NativeMethods.IsWindow(extendedCompressorArgumentsEdit))
            {
                return;
            }

            extendedCompressorArgumentsEditHydrated = false;
            extendedCompressorArgumentsUserEdited = false;
            if (enabled)
            {
                RefreshExtendedCompressorArgumentsEditLimit();
                HydrateExtendedCompressorArgumentsEdit(true);
            }
            else
            {
                RestoreNativeExternalEncoderOptionsFallback();
                NativeMethods.SendMessageW(
                    extendedCompressorArgumentsEdit,
                    NativeMethods.EM_SETLIMITTEXT,
                    new IntPtr(NativeExternalEncoderOptionsLimit),
                    IntPtr.Zero);
            }
        }

        internal static int MatchExtendedCompressorArgumentsToken(
            string template,
            int index)
        {
            return MatchExtendedCompressorArgumentsToken(
                template,
                index,
                IsExtendedCompressorArgumentsEnabled());
        }

        internal static int MatchExtendedCompressorArgumentsToken(
            string template,
            int index,
            bool enabled)
        {
            if (!enabled)
                return 0;
            if (String.IsNullOrEmpty(template) ||
                index < 0 ||
                template.Length - index <
                    ExtendedExternalEncoderOptionsMarker.Length)
            {
                return 0;
            }

            return String.Compare(
                    template,
                    index,
                    ExtendedExternalEncoderOptionsMarker,
                    0,
                    ExtendedExternalEncoderOptionsMarker.Length,
                    StringComparison.OrdinalIgnoreCase) == 0
                ? ExtendedExternalEncoderOptionsMarker.Length
                : 0;
        }

        internal static bool RequiresExtendedCompressorArguments(string value)
        {
            return (value ?? String.Empty).Length >
                NativeExternalEncoderOptionsLimit;
        }

        internal static string ExpandExtendedCompressorArguments(
            string template,
            string extendedArguments)
        {
            return Regex.Replace(
                template ?? String.Empty,
                Regex.Escape(ExtendedExternalEncoderOptionsMarker),
                delegate { return extendedArguments ?? String.Empty; },
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal static string ResolveExtendedCompressorArguments(
            string template,
            bool isEacExternalEncoderOptionsBuffer,
            string savedExtendedArguments,
            string temporaryExtendedArguments)
        {
            return ResolveExtendedCompressorArguments(
                template,
                isEacExternalEncoderOptionsBuffer,
                savedExtendedArguments,
                temporaryExtendedArguments,
                true);
        }

        internal static string ResolveExtendedCompressorArguments(
            string template,
            bool isEacExternalEncoderOptionsBuffer,
            string savedExtendedArguments,
            string temporaryExtendedArguments,
            bool enabled)
        {
            string value = template ?? String.Empty;
            if (!enabled)
                return value;
            string saved = savedExtendedArguments ?? String.Empty;
            string temporary = temporaryExtendedArguments ?? String.Empty;

            if (value.IndexOf(
                    ExtendedExternalEncoderOptionsMarker,
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ExpandExtendedCompressorArguments(
                    value,
                    temporary.Length == 0 ? saved : temporary);
            }

            return isEacExternalEncoderOptionsBuffer && saved.Length != 0
                ? saved
                : value;
        }

        private static string ExpandCurrentExtendedCompressorArguments(
            string template,
            IntPtr templatePointer)
        {
            if (!IsExtendedCompressorArgumentsEnabled())
                return template ?? String.Empty;
            string saved = GetExtendedCompressorArguments();
            string temporary;
            lock (ExtendedCompressorArgumentsLock)
                temporary = transientExtendedCompressorArguments;

            bool isSavedBuffer =
                templatePointer != IntPtr.Zero &&
                layout != null &&
                templatePointer ==
                    AddressFromStaticVa(layout.ExternalEncoderOptionsVa);
            return ResolveExtendedCompressorArguments(
                template,
                isSavedBuffer,
                saved,
                temporary);
        }

        private static string GetExtendedCompressorArguments()
        {
            lock (ExtendedCompressorArgumentsLock)
            {
                if (!extendedCompressorArgumentsLoaded)
                {
                    StringBuilder value = new StringBuilder(
                        ExtendedExternalEncoderOptionsLimit + 1);
                    NativeMethods.GetPrivateProfileStringW(
                        ExtendedCompressorArgumentsIniSection,
                        ExtendedCompressorArgumentsIniKey,
                        String.Empty,
                        value,
                        value.Capacity,
                        GetSettingsFilePath());
                    extendedCompressorArguments = value.ToString();
                    Log(
                        "Loaded " +
                        extendedCompressorArguments.Length +
                        " extended external-compressor argument characters from " +
                        GetSettingsFilePath() + ".");
                    if (extendedCompressorArguments.Length >
                        ExtendedExternalEncoderOptionsLimit)
                    {
                        Log(
                            "The saved extended external-compressor arguments exceed the supported " +
                            ExtendedExternalEncoderOptionsLimit +
                            "-character limit and were ignored.");
                        extendedCompressorArguments = String.Empty;
                    }
                    extendedCompressorArgumentsLoaded = true;
                }

                return extendedCompressorArguments;
            }
        }

        private static void PersistExtendedCompressorArguments(string value)
        {
            // The disabled option deliberately freezes the plugin-owned value.
            // In particular, do not remove it merely because EAC currently has
            // a stock-length value.
            if (!IsExtendedCompressorArgumentsEnabled())
                return;

            string normalized = value ?? String.Empty;
            if (normalized.Length > ExtendedExternalEncoderOptionsLimit)
            {
                throw new ArgumentOutOfRangeException(
                    "value",
                    "External-compressor arguments cannot exceed " +
                    ExtendedExternalEncoderOptionsLimit +
                    " characters.");
            }

            lock (ExtendedCompressorArgumentsLock)
            {
                if (!NativeMethods.WritePrivateProfileStringW(
                        ExtendedCompressorArgumentsIniSection,
                        ExtendedCompressorArgumentsIniKey,
                        normalized.Length == 0 ? null : normalized,
                        GetSettingsFilePath()))
                {
                    throw new InvalidOperationException(
                        "The extended external-compressor arguments could not be written to EACEnhancements.ini; Win32 error " +
                        Marshal.GetLastWin32Error() + ".");
                }

                extendedCompressorArguments = normalized;
                extendedCompressorArgumentsLoaded = true;
            }
        }

        private static string SetTransientExtendedCompressorArguments(
            string value)
        {
            lock (ExtendedCompressorArgumentsLock)
            {
                string previous = transientExtendedCompressorArguments;
                transientExtendedCompressorArguments =
                    value ?? String.Empty;
                return previous;
            }
        }

        private static void MaybeInstallExtendedCompressorArguments(
            IntPtr messageWindow)
        {
            if (!IsExtendedCompressorArgumentsEnabled())
                return;
            if (messageWindow == IntPtr.Zero)
                return;
            if (extendedCompressorArgumentsPage != IntPtr.Zero &&
                NativeMethods.IsWindow(extendedCompressorArgumentsPage))
            {
                return;
            }

            IntPtr candidate = messageWindow;
            for (int level = 0; level < 3 && candidate != IntPtr.Zero; level++)
            {
                IntPtr edit = NativeMethods.GetDlgItem(
                    candidate,
                    ExternalEncoderOptionsControlId);
                if (edit != IntPtr.Zero)
                {
                    InstallExtendedCompressorArguments(candidate, edit);
                    return;
                }
                candidate = NativeMethods.GetParent(candidate);
            }
        }

        private static void InstallExtendedCompressorArguments(
            IntPtr page,
            IntPtr edit)
        {
            if (page == IntPtr.Zero ||
                edit == IntPtr.Zero ||
                !NativeMethods.IsWindow(page) ||
                !NativeMethods.IsWindow(edit))
            {
                return;
            }

            extendedCompressorArgumentsSubclassDelegate =
                ExtendedCompressorArgumentsSubclass;
            IntPtr procedure = Marshal.GetFunctionPointerForDelegate(
                extendedCompressorArgumentsSubclassDelegate);
            if (!NativeMethods.SetWindowSubclass(
                    page,
                    procedure,
                    new UIntPtr(ExtendedCompressorArgumentsSubclassId),
                    UIntPtr.Zero))
            {
                extendedCompressorArgumentsSubclassDelegate = null;
                Log(
                    "The external-compressor argument editor could not be extended; Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
                return;
            }

            extendedCompressorArgumentsPage = page;
            extendedCompressorArgumentsEdit = edit;
            InstallExtendedCompressorArgumentsBoundarySubclasses(page, edit);
            extendedCompressorArgumentsEditHydrated = false;
            extendedCompressorArgumentsUserEdited = false;
            nativeExternalEncoderOptionsFallback = String.Empty;
            pendingExtendedCompressorArgumentsDisplay = String.Empty;
            RefreshExtendedCompressorArgumentsEditLimit();
            NativeMethods.SetTimer(
                page,
                new UIntPtr(HydrateExtendedCompressorArgumentsTimerId),
                ExtendedCompressorArgumentsTimerDelayMs,
                IntPtr.Zero);

            Log(
                "External-compressor argument editor limit raised from " +
                NativeExternalEncoderOptionsLimit + " to " +
                ExtendedExternalEncoderOptionsLimit + " characters.");
        }

        private static void InstallExtendedCompressorArgumentsBoundarySubclasses(
            IntPtr page,
            IntPtr edit)
        {
            extendedCompressorArgumentsEditSubclassDelegate =
                ExtendedCompressorArgumentsEditSubclass;
            IntPtr editProcedure = Marshal.GetFunctionPointerForDelegate(
                extendedCompressorArgumentsEditSubclassDelegate);
            if (!NativeMethods.SetWindowSubclass(
                    edit,
                    editProcedure,
                    new UIntPtr(
                        ExtendedCompressorArgumentsEditSubclassId),
                    UIntPtr.Zero))
            {
                Log(
                    "The external-compressor edit focus boundary could not be extended; Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }

            IntPtr parent = NativeMethods.GetParent(page);
            if (parent == IntPtr.Zero || !NativeMethods.IsWindow(parent))
                return;
            extendedCompressorArgumentsParentSubclassDelegate =
                ExtendedCompressorArgumentsParentSubclass;
            IntPtr parentProcedure = Marshal.GetFunctionPointerForDelegate(
                extendedCompressorArgumentsParentSubclassDelegate);
            if (NativeMethods.SetWindowSubclass(
                    parent,
                    parentProcedure,
                    new UIntPtr(
                        ExtendedCompressorArgumentsParentSubclassId),
                    UIntPtr.Zero))
            {
                extendedCompressorArgumentsParent = parent;
            }
            else
            {
                Log(
                    "The compression property-sheet Apply boundary could not be extended; Win32 error " +
                    Marshal.GetLastWin32Error() + ".");
            }
        }

        private static IntPtr ExtendedCompressorArgumentsParentSubclass(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData)
        {
            try
            {
                if (message != NativeMethods.WM_NCDESTROY &&
                    !IsExtendedCompressorArgumentsEnabled())
                {
                    return NativeMethods.DefSubclassProc(
                        hwnd,
                        message,
                        wParam,
                        lParam);
                }

                if (message == NativeMethods.WM_COMMAND)
                {
                    int command = (int)wParam.ToInt64() & 0xFFFF;
                    if (command == 1 ||
                        command == PropertySheetApplyControlId)
                    {
                        string displayed =
                            ReadExtendedCompressorArgumentsEdit(
                                extendedCompressorArgumentsEdit);
                        Log(
                            "Compression property-sheet command " +
                            command + " observed with " +
                            displayed.Length + " editor characters.");
                        if (RequiresExtendedCompressorArguments(displayed))
                        {
                            pendingExtendedCompressorArgumentsDisplay =
                                displayed;
                            string previous =
                                SetTransientExtendedCompressorArguments(
                                    displayed);
                            bool redrawSuspended =
                                BeginHiddenExtendedCompressorArgumentsEdit();
                            SetExtendedCompressorArgumentsEdit(
                                ExtendedExternalEncoderOptionsMarker);
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
                                SetTransientExtendedCompressorArguments(
                                    previous);
                                RestoreExtendedCompressorArgumentsEdit(
                                    displayed,
                                    false,
                                    redrawSuspended);
                            }
                        }
                    }
                }

                if (message == NativeMethods.WM_NCDESTROY)
                {
                    IntPtr result = NativeMethods.DefSubclassProc(
                        hwnd,
                        message,
                        wParam,
                        lParam);
                    extendedCompressorArgumentsParent = IntPtr.Zero;
                    return result;
                }
            }
            catch (Exception error)
            {
                Log(
                    "Compression property-sheet Apply boundary failed: " +
                    error);
            }

            return NativeMethods.DefSubclassProc(
                hwnd,
                message,
                wParam,
                lParam);
        }

        private static IntPtr ExtendedCompressorArgumentsEditSubclass(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData)
        {
            try
            {
                if (!IsExtendedCompressorArgumentsEnabled())
                {
                    return NativeMethods.DefSubclassProc(
                        hwnd,
                        message,
                        wParam,
                        lParam);
                }

                if (message == NativeMethods.WM_KILLFOCUS &&
                    !updatingExtendedCompressorArgumentsEdit)
                {
                    string displayed =
                        ReadExtendedCompressorArgumentsEdit(hwnd);
                    if (RequiresExtendedCompressorArguments(displayed))
                    {
                        return StageExtendedCompressorArgumentsOnFocusLoss(
                            hwnd,
                            message,
                            wParam,
                            lParam,
                            displayed);
                    }
                }
            }
            catch (Exception error)
            {
                Log(
                    "External-compressor edit focus boundary failed: " +
                    error);
            }

            return NativeMethods.DefSubclassProc(
                hwnd,
                message,
                wParam,
                lParam);
        }

        private static IntPtr ExtendedCompressorArgumentsSubclass(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData)
        {
            try
            {
                if (message != NativeMethods.WM_NCDESTROY &&
                    !IsExtendedCompressorArgumentsEnabled())
                {
                    return NativeMethods.DefSubclassProc(
                        hwnd,
                        message,
                        wParam,
                        lParam);
                }

                if (message == NativeMethods.WM_COMMAND &&
                    lParam == extendedCompressorArgumentsEdit)
                {
                    int command = (int)wParam.ToInt64() & 0xFFFF;
                    int notification =
                        (int)(wParam.ToInt64() >> 16) & 0xFFFF;
                    if (command == ExternalEncoderOptionsControlId &&
                        notification == NativeMethods.EN_KILLFOCUS &&
                        !updatingExtendedCompressorArgumentsEdit)
                    {
                        string displayed =
                            ReadExtendedCompressorArgumentsEdit(
                                extendedCompressorArgumentsEdit);
                        if (RequiresExtendedCompressorArguments(displayed))
                        {
                            return StageExtendedCompressorArgumentsOnFocusLoss(
                                hwnd,
                                message,
                                wParam,
                                lParam,
                                displayed);
                        }
                    }
                    if (command == ExternalEncoderOptionsControlId &&
                        notification == NativeMethods.EN_CHANGE)
                    {
                        if (updatingExtendedCompressorArgumentsEdit)
                            return IntPtr.Zero;

                        extendedCompressorArgumentsUserEdited = true;
                        string displayed =
                            ReadExtendedCompressorArgumentsEdit(
                                extendedCompressorArgumentsEdit);
                        if (RequiresExtendedCompressorArguments(displayed))
                        {
                            pendingExtendedCompressorArgumentsDisplay =
                                displayed;
                            SetTransientExtendedCompressorArguments(
                                displayed);
                        }
                        else
                        {
                            pendingExtendedCompressorArgumentsDisplay =
                                String.Empty;
                            SetTransientExtendedCompressorArguments(
                                String.Empty);
                        }
                    }
                }

                if (message == NativeMethods.WM_COMMAND &&
                    ((int)wParam.ToInt64() & 0xFFFF) ==
                        TestExternalEncoderControlId)
                {
                    return TestExtendedCompressorArguments(
                        hwnd,
                        message,
                        wParam,
                        lParam);
                }

                if (message == NativeMethods.WM_NOTIFY)
                {
                    int notification =
                        GetPropertySheetNotificationCode(lParam);
                    if (notification == PsnSetActive ||
                        notification == PsnKillActive ||
                        notification == PsnApply)
                    {
                        Log(
                            "External-compressor page notification " +
                            notification + " with " +
                            ReadExtendedCompressorArgumentsEdit(
                                extendedCompressorArgumentsEdit).Length +
                            " editor characters.");
                    }
                    if (notification == PsnSetActive)
                    {
                        IntPtr result = NativeMethods.DefSubclassProc(
                            hwnd,
                            message,
                            wParam,
                            lParam);
                        HydrateExtendedCompressorArgumentsEdit(true);
                        return result;
                    }
                    if (notification == PsnKillActive)
                    {
                        return ValidateExtendedCompressorArguments(
                            hwnd,
                            message,
                            wParam,
                            lParam);
                    }
                    if (notification == PsnApply)
                    {
                        return ApplyExtendedCompressorArguments(
                            hwnd,
                            message,
                            wParam,
                            lParam);
                    }
                }

                if (message == NativeMethods.WM_TIMER &&
                    wParam.ToInt64() ==
                        HydrateExtendedCompressorArgumentsTimerId)
                {
                    NativeMethods.KillTimer(
                        hwnd,
                        new UIntPtr(
                            HydrateExtendedCompressorArgumentsTimerId));
                    HydrateExtendedCompressorArgumentsEdit(false);
                    return IntPtr.Zero;
                }

                if (message == NativeMethods.WM_NCDESTROY)
                {
                    IntPtr result = NativeMethods.DefSubclassProc(
                        hwnd,
                        message,
                        wParam,
                        lParam);
                    extendedCompressorArgumentsPage = IntPtr.Zero;
                    extendedCompressorArgumentsEdit = IntPtr.Zero;
                    updatingExtendedCompressorArgumentsEdit = false;
                    extendedCompressorArgumentsRedrawSuspendDepth = 0;
                    extendedCompressorArgumentsEditHydrated = false;
                    extendedCompressorArgumentsUserEdited = false;
                    nativeExternalEncoderOptionsFallback = String.Empty;
                    pendingExtendedCompressorArgumentsDisplay = String.Empty;
                    SetTransientExtendedCompressorArguments(String.Empty);
                    return result;
                }
            }
            catch (Exception error)
            {
                Log(
                    "External-compressor argument editor callback failed: " +
                    error);
            }

            IntPtr defaultResult = NativeMethods.DefSubclassProc(
                hwnd,
                message,
                wParam,
                lParam);
            RefreshExtendedCompressorArgumentsEditLimit();
            return defaultResult;
        }

        private static IntPtr TestExtendedCompressorArguments(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam)
        {
            string displayed =
                pendingExtendedCompressorArgumentsDisplay.Length == 0
                    ? ReadExtendedCompressorArgumentsEdit(
                        extendedCompressorArgumentsEdit)
                    : pendingExtendedCompressorArgumentsDisplay;
            if (!RequiresExtendedCompressorArguments(displayed))
            {
                return NativeMethods.DefSubclassProc(
                    hwnd,
                    message,
                    wParam,
                    lParam);
            }

            string previous =
                SetTransientExtendedCompressorArguments(displayed);
            bool redrawSuspended =
                BeginHiddenExtendedCompressorArgumentsEdit();
            SetExtendedCompressorArgumentsEdit(
                ExtendedExternalEncoderOptionsMarker);
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
                SetTransientExtendedCompressorArguments(previous);
                RestoreExtendedCompressorArgumentsEdit(
                    displayed,
                    false,
                    redrawSuspended);
            }
        }

        private static IntPtr StageExtendedCompressorArgumentsOnFocusLoss(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            string displayed)
        {
            pendingExtendedCompressorArgumentsDisplay = displayed;
            string previous =
                SetTransientExtendedCompressorArguments(displayed);
            bool redrawSuspended =
                BeginHiddenExtendedCompressorArgumentsEdit();
            SetExtendedCompressorArgumentsEdit(
                ExtendedExternalEncoderOptionsMarker);
            IntPtr result;
            try
            {
                result = NativeMethods.DefSubclassProc(
                    hwnd,
                    message,
                    wParam,
                    lParam);
            }
            finally
            {
                SetTransientExtendedCompressorArguments(previous);
                RestoreExtendedCompressorArgumentsEdit(
                    displayed,
                    true,
                    redrawSuspended);
            }

            Log(
                "Staged " + displayed.Length +
                " external-compressor argument characters before native focus-loss processing.");
            return result;
        }

        private static int GetPropertySheetNotificationCode(
            IntPtr notificationPointer)
        {
            if (notificationPointer == IntPtr.Zero)
                return 0;
            NativeMethods.NMHDR header =
                (NativeMethods.NMHDR)Marshal.PtrToStructure(
                    notificationPointer,
                    typeof(NativeMethods.NMHDR));
            return header.Code;
        }

        private static IntPtr ValidateExtendedCompressorArguments(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam)
        {
            string displayed = ReadExtendedCompressorArgumentsEdit(
                extendedCompressorArgumentsEdit);
            if (!RequiresExtendedCompressorArguments(displayed))
            {
                return NativeMethods.DefSubclassProc(
                    hwnd,
                    message,
                    wParam,
                    lParam);
            }

            Log(
                "Validating extended external-compressor arguments (" +
                displayed.Length + " characters) with a transient marker.");
            pendingExtendedCompressorArgumentsDisplay = displayed;
            string previous =
                SetTransientExtendedCompressorArguments(displayed);
            bool redrawSuspended =
                BeginHiddenExtendedCompressorArgumentsEdit();
            SetExtendedCompressorArgumentsEdit(
                ExtendedExternalEncoderOptionsMarker);
            IntPtr result;
            try
            {
                result = NativeMethods.DefSubclassProc(
                    hwnd,
                    message,
                    wParam,
                    lParam);
            }
            finally
            {
                SetTransientExtendedCompressorArguments(previous);
                RestoreExtendedCompressorArgumentsEdit(
                    displayed,
                    true,
                    redrawSuspended);
            }

            return result;
        }

        private static IntPtr ApplyExtendedCompressorArguments(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam)
        {
            if (!IsExtendedCompressorArgumentsEnabled())
            {
                return NativeMethods.DefSubclassProc(
                    hwnd,
                    message,
                    wParam,
                    lParam);
            }

            string displayed =
                pendingExtendedCompressorArgumentsDisplay.Length == 0
                    ? ReadExtendedCompressorArgumentsEdit(
                        extendedCompressorArgumentsEdit)
                    : pendingExtendedCompressorArgumentsDisplay;
            bool useExtended =
                pendingExtendedCompressorArgumentsDisplay.Length != 0 ||
                RequiresExtendedCompressorArguments(displayed);
            Log(
                "Applying external-compressor arguments (displayed=" +
                displayed.Length + ", pending=" +
                pendingExtendedCompressorArgumentsDisplay.Length +
                ", extended=" + useExtended + ").");

            bool redrawSuspended = false;
            if (useExtended)
            {
                redrawSuspended =
                    BeginHiddenExtendedCompressorArgumentsEdit();
                RestoreNativeExternalEncoderOptionsFallback();
            }

            IntPtr result;
            try
            {
                result = NativeMethods.DefSubclassProc(
                    hwnd,
                    message,
                    wParam,
                    lParam);
            }
            finally
            {
                SetTransientExtendedCompressorArguments(String.Empty);
                if (useExtended)
                {
                    RestoreExtendedCompressorArgumentsEdit(
                        displayed,
                        false,
                        redrawSuspended);
                }
            }

            if (useExtended)
            {
                PersistExtendedCompressorArguments(displayed);
                pendingExtendedCompressorArgumentsDisplay = displayed;
            }
            else
            {
                PersistExtendedCompressorArguments(String.Empty);
                nativeExternalEncoderOptionsFallback = displayed;
                extendedCompressorArgumentsEditHydrated = true;
                extendedCompressorArgumentsUserEdited = false;
                pendingExtendedCompressorArgumentsDisplay = String.Empty;
            }
            RefreshExtendedCompressorArgumentsEditLimit();

            Log(
                "External-compressor arguments applied with " +
                displayed.Length + " displayed characters (extended=" +
                useExtended +
                ", stock fallback characters=" +
                nativeExternalEncoderOptionsFallback.Length + ").");
            return result;
        }

        private static void RestoreNativeExternalEncoderOptionsFallback()
        {
            string fallback = nativeExternalEncoderOptionsFallback ??
                String.Empty;
            if (fallback.Length > NativeExternalEncoderOptionsLimit ||
                String.Equals(
                    fallback,
                    ExtendedExternalEncoderOptionsMarker,
                    StringComparison.OrdinalIgnoreCase))
            {
                fallback = String.Empty;
            }

            SetExtendedCompressorArgumentsEdit(fallback);
            if (layout != null)
            {
                WriteExternalEncoderOptionsBuffer(
                    layout.TemporaryExternalEncoderOptionsVa,
                    fallback);
            }
        }

        private static void WriteExternalEncoderOptionsBuffer(
            uint staticVa,
            string value)
        {
            string normalized = value ?? String.Empty;
            if (normalized.Length > NativeExternalEncoderOptionsLimit)
            {
                normalized = normalized.Substring(
                    0,
                    NativeExternalEncoderOptionsLimit);
            }

            byte[] bytes = Encoding.Unicode.GetBytes(normalized + "\0");
            Marshal.Copy(
                bytes,
                0,
                AddressFromStaticVa(staticVa),
                bytes.Length);
        }

        private static void HydrateExtendedCompressorArgumentsEdit(
            bool restoreSavedValue)
        {
            if (!IsExtendedCompressorArgumentsEnabled())
                return;
            IntPtr edit = extendedCompressorArgumentsEdit;
            if (edit == IntPtr.Zero || !NativeMethods.IsWindow(edit))
                return;

            RefreshExtendedCompressorArgumentsEditLimit();
            if (!extendedCompressorArgumentsEditHydrated)
            {
                string nativeValue = layout == null
                    ? ReadExtendedCompressorArgumentsEdit(edit)
                    : ReadExternalEncoderOptionsBuffer(
                        layout.ExternalEncoderOptionsVa);
                nativeExternalEncoderOptionsFallback =
                    String.Equals(
                        nativeValue,
                        ExtendedExternalEncoderOptionsMarker,
                        StringComparison.OrdinalIgnoreCase)
                        ? String.Empty
                        : nativeValue;
                if (nativeExternalEncoderOptionsFallback.Length >
                    NativeExternalEncoderOptionsLimit)
                {
                    nativeExternalEncoderOptionsFallback =
                        nativeExternalEncoderOptionsFallback.Substring(
                            0,
                            NativeExternalEncoderOptionsLimit);
                }
                extendedCompressorArgumentsEditHydrated = true;
                extendedCompressorArgumentsUserEdited = false;
            }

            string value =
                pendingExtendedCompressorArgumentsDisplay.Length != 0
                    ? pendingExtendedCompressorArgumentsDisplay
                    : GetExtendedCompressorArguments();
            Log(
                "Hydrating the external-compressor editor (saved=" +
                value.Length + ", restore=" + restoreSavedValue +
                ", user-edited=" +
                extendedCompressorArgumentsUserEdited + ").");
            if (value.Length != 0 &&
                (restoreSavedValue ||
                 !extendedCompressorArgumentsUserEdited))
            {
                SetExtendedCompressorArgumentsEdit(value);
                Log(
                    "External-compressor editor contains " +
                    ReadExtendedCompressorArgumentsEdit(edit).Length +
                    " characters immediately after hydration.");
            }
        }

        private static void RefreshExtendedCompressorArgumentsEditLimit()
        {
            if (!IsExtendedCompressorArgumentsEnabled())
                return;
            IntPtr edit = extendedCompressorArgumentsEdit;
            if (edit == IntPtr.Zero || !NativeMethods.IsWindow(edit))
                return;

            if (NativeMethods.SendMessageW(
                    edit,
                    NativeMethods.EM_GETLIMITTEXT,
                    IntPtr.Zero,
                    IntPtr.Zero).ToInt64() !=
                ExtendedExternalEncoderOptionsLimit)
            {
                NativeMethods.SendMessageW(
                    edit,
                    NativeMethods.EM_SETLIMITTEXT,
                    new IntPtr(ExtendedExternalEncoderOptionsLimit),
                    IntPtr.Zero);
            }
        }

        private static string ReadExternalEncoderOptionsBuffer(uint staticVa)
        {
            return Marshal.PtrToStringUni(AddressFromStaticVa(staticVa)) ??
                String.Empty;
        }

        private static string ReadExtendedCompressorArgumentsEdit(
            IntPtr edit)
        {
            if (edit == IntPtr.Zero || !NativeMethods.IsWindow(edit))
                return String.Empty;
            StringBuilder value = new StringBuilder(
                ExtendedExternalEncoderOptionsLimit + 1);
            NativeMethods.GetWindowTextW(
                edit,
                value,
                value.Capacity);
            return value.ToString();
        }

        private static bool BeginHiddenExtendedCompressorArgumentsEdit()
        {
            IntPtr edit = extendedCompressorArgumentsEdit;
            if (edit == IntPtr.Zero || !NativeMethods.IsWindow(edit))
                return false;

            if (extendedCompressorArgumentsRedrawSuspendDepth == 0)
            {
                NativeMethods.SendMessageW(
                    edit,
                    NativeMethods.WM_SETREDRAW,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
            extendedCompressorArgumentsRedrawSuspendDepth++;
            return true;
        }

        private static void RestoreExtendedCompressorArgumentsEdit(
            string displayed,
            bool restoreNativeFallback,
            bool redrawSuspended)
        {
            try
            {
                if (restoreNativeFallback)
                    RestoreNativeExternalEncoderOptionsFallback();
            }
            finally
            {
                try
                {
                    SetExtendedCompressorArgumentsEdit(displayed);
                }
                finally
                {
                    EndHiddenExtendedCompressorArgumentsEdit(
                        redrawSuspended);
                }
            }
        }

        private static void EndHiddenExtendedCompressorArgumentsEdit(
            bool redrawSuspended)
        {
            if (!redrawSuspended)
                return;
            if (extendedCompressorArgumentsRedrawSuspendDepth <= 0)
                return;

            extendedCompressorArgumentsRedrawSuspendDepth--;
            if (extendedCompressorArgumentsRedrawSuspendDepth != 0)
                return;

            IntPtr edit = extendedCompressorArgumentsEdit;
            if (edit == IntPtr.Zero || !NativeMethods.IsWindow(edit))
                return;

            NativeMethods.SendMessageW(
                edit,
                NativeMethods.WM_SETREDRAW,
                new IntPtr(1),
                IntPtr.Zero);
            NativeMethods.InvalidateRect(edit, IntPtr.Zero, true);
            NativeMethods.UpdateWindow(edit);
        }

        private static void SetExtendedCompressorArgumentsEdit(string value)
        {
            if (extendedCompressorArgumentsEdit == IntPtr.Zero ||
                !NativeMethods.IsWindow(extendedCompressorArgumentsEdit))
            {
                return;
            }

            updatingExtendedCompressorArgumentsEdit = true;
            try
            {
                NativeMethods.SendMessageStringW(
                    extendedCompressorArgumentsEdit,
                    NativeMethods.WM_SETTEXT,
                    IntPtr.Zero,
                    value ?? String.Empty);
            }
            finally
            {
                updatingExtendedCompressorArgumentsEdit = false;
            }
        }
    }
}
