using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using HelperFunctionsLib;

[assembly: AssemblyTitle("EAC Enhancements")]
[assembly: AssemblyDescription("Responsive secure-rip UI and workflow enhancements for Exact Audio Copy")]
[assembly: AssemblyCompany("EAC community enhancement")]
[assembly: AssemblyProduct("EAC Enhancements")]
[assembly: AssemblyVersion("0.13.0.0")]
[assembly: AssemblyFileVersion("0.13.0.0")]

namespace AudioDataPlugIn
{
    [Guid("7D827A30-26E6-4EF7-A8D5-EB3FC72A9B4B")]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class AudioDataTransfer : IAudioDataTransfer
    {
        private const string PluginGuid = "{7D827A30-26E6-4EF7-A8D5-EB3FC72A9B4B}";

        internal static string PluginDisplayName
        {
            get
            {
                Version version = typeof(AudioDataTransfer).Assembly.GetName().Version;
                string versionText = version.Major + "." + version.Minor + "." + version.Build;
                if (version.Revision > 0)
                    versionText += "." + version.Revision;
                return "EAC Enhancements V" + versionText;
            }
        }

        public AudioDataTransfer()
        {
            // PluginHandler silently ignores assemblies whose constructor
            // throws. Reject unsupported EAC versions explicitly, but never
            // allow other initialization diagnostics to escape.
            try
            {
                EnhancementRuntime.Initialize();
            }
            catch (NotSupportedException)
            {
                throw;
            }
            catch (Exception error)
            {
                EnhancementRuntime.Log("Plugin constructor failed: " + error);
            }
        }

        public void StartNewSession(
            IMetadataLookup data,
            string drivename,
            int offset,
            bool aroffset,
            int mode)
        {
            EnhancementRuntime.ApplyConditionalFolderTemplate(
                data == null ? 0 : data.Year,
                data == null ? string.Empty : data.ExtendedDiscInformation);
            EnhancementRuntime.BeginRipSession(drivename, mode);
        }

        public void StartNewTransfer(int startpos, int length, bool test)
        {
            EnhancementRuntime.BeginTransfer(startpos, length, test);
        }

        public void ShowOptions()
        {
            EnhancementRuntime.ShowPluginOptions();
        }

        public string GetAudioTransferPluginName()
        {
            return PluginDisplayName;
        }

        public string GetAudioTransferPluginGuid()
        {
            return PluginGuid;
        }

        public void TransferAudioData(Array audiodata)
        {
            EnhancementRuntime.ObserveAudioTransfer();
        }

        public void TransferFinished()
        {
            EnhancementRuntime.FinishTransfer();
        }

        public string EndOfSession()
        {
            EnhancementRuntime.EndRipSession();
            return String.Empty;
        }

        public void SuspiciousPosition()
        {
            EnhancementRuntime.ObserveSuspiciousPosition();
        }
    }

    internal static partial class EnhancementRuntime
    {
        private const int MinimumPumpIntervalMs = 50;

        private const uint StaticImageBase = 0x00400000;
        private const int TrackSelectionCount = 100;
        private const uint CustomWorkflowCommand = 0x0312;
        private const uint WorkflowDestinationCommand = 0xA313;
        private const uint StartPreparedWorkflowCommand = 0xA314;
        private const uint RestoreWorkflowFolderCommand = 0xA315;
        private const uint OutputSettingsCommand = 0xA312;
        private const uint RefreshOutputSettingsCommand = 0x0314;
        private const uint ReferenceRipCommand = 0x0303;
        private const string CustomWorkflowMenuText = "&Test && Copy + Cue (100% Log)";
        private const string OutputSettingsMenuText = "EAC Enhancements &Options...";
        private const string OutputTemplateIniName = "EACEnhancements.ini";
        private const string OutputTemplateSection = "OutputTemplate";
        private const string LoggingEnvironmentVariable = "EACENHANCEMENTS_LOGGING";
        private const string ExtractionOptionsKey =
            @"Software\AWSoftware\EACU\Extraction Options";
        private const string StartupOptionsKey =
            @"Software\AWSoftware\EACU\StartUp Options";

        private static readonly object InitializationLock = new object();
        private static readonly object LogLock = new object();
        private static readonly object WorkflowHookLock = new object();
        private static readonly HashSet<uint> WorkflowHookedThreads = new HashSet<uint>();
        private static readonly List<IntPtr> WorkflowMessageHooks = new List<IntPtr>();
        private static readonly byte[] ExpectedCommandCompletionPrologue =
            { 0x55, 0x89, 0xE5, 0x83, 0xEC, 0x04 };

        private static bool initialized;
        private static NotSupportedException unsupportedEacError;
        private static volatile bool loggingEnabled;
        private static bool loggingForcedForProcess;
        private static bool hookInstalled;
        private static string hookStatus = "not initialized";
        private static bool workflowInstalled;
        private static string workflowStatus = "not initialized";
        private static IntPtr workflowCode;
        private static IntPtr workflowData;
        private static uint workflowSelectionBackupAddress;
        private static uint workflowAutoCloseFlagAddress;
        private static CallWndProcHookDelegate workflowCallWndProcHookDelegate;
        private static CallWndProcHookDelegate workflowGetMessageHookDelegate;
        private static MainWindowSubclassDelegate mainWindowSubclassDelegate;
        private static int mainWindowSubclassInstalled;
        private static int workflowMessageTraceCount;
        private static int lastWorkflowHookRefreshTick;
        private static IntPtr imageBase;
        private static EacVersionLayout layout;
        private static IntPtr commandCompletionAddress;
        private static IntPtr commandCompletionTrampoline;
        private static CommandCompletionDelegate originalCommandCompletion;
        private static CommandCompletionDelegate hookedCommandCompletion;
        private static int outputSettingsDialogActive;
        private static int workflowDestinationDialogActive;
        private static volatile bool suppressWorkflowFolderTemplate;
        private static volatile bool ripSessionActive;
        private static int ripSessionThreadId;
        private static int lastPumpTick;
        private static int assistedPumpCount;
        private static int audioTransferCount;
        private static bool firstAssistLogged;
        private static DateTime ripSessionStartedUtc;
        private static int ripSessionGeneration;
        private static int ripSessionSuspiciousCount;

        [ThreadStatic]
        private static bool insideAssistedPump;

        internal static string HookStatus
        {
            get { return hookStatus; }
        }

        internal static string WorkflowStatus
        {
            get { return workflowStatus; }
        }

        internal static void Initialize()
        {
            lock (InitializationLock)
            {
                if (unsupportedEacError != null)
                    throw new NotSupportedException(
                        unsupportedEacError.Message,
                        unsupportedEacError);
                if (initialized)
                    return;

                Process process = Process.GetCurrentProcess();
                ProcessModule mainModule = process.MainModule;
                if (mainModule == null)
                    throw new InvalidOperationException("The EAC main module was unavailable.");

                string executableName = Path.GetFileName(mainModule.FileName);
                if (executableName.IndexOf("EAC", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    initialized = true;
                    hookStatus = "inactive outside EAC";
                    workflowStatus = hookStatus;
                    return;
                }

                imageBase = mainModule.BaseAddress;
                try
                {
                    // This must remain the first EAC-specific operation. Detect
                    // accepts only the known 1.6 and 1.8 executable layouts.
                    layout = EacVersionLayout.Detect(mainModule, imageBase);
                }
                catch (NotSupportedException error)
                {
                    unsupportedEacError = error;
                    hookStatus = "inactive: " + error.Message;
                    workflowStatus = hookStatus;
                    throw;
                }

                initialized = true;
                Exception settingsFileError = null;
                try
                {
                    EnsureDefaultSettingsFile();
                }
                catch (Exception error)
                {
                    // A read-only installation must not prevent the plugin from loading.
                    settingsFileError = error;
                }
                InitializeLoggingPreference();
                if (settingsFileError != null)
                {
                    Log("Default settings file could not be created: " + settingsFileError);
                    ShowSettingsFileError("create", settingsFileError);
                }

                InitializeCommandLine();
                commandCompletionAddress = Add(imageBase, layout.CommandCompletionRva);
                Log(
                    "Plugin loaded into " + executableName + " as " + layout.Name +
                    " at image base 0x" + imageBase.ToInt64().ToString("X8") + ".");

                InstallCommandCompletionHook();
                try
                {
                    InstallWorkflowHooks();
                }
                catch (Exception error)
                {
                    workflowStatus = "disabled: " + error.Message;
                    Log("100% log workflow installation failed: " + error);
                }

                StartMenuInstaller();
            }
        }

        internal static void Log(string text)
        {
            if (!loggingEnabled)
                return;

            try
            {
                lock (LogLock)
                {
                    string directory = AppDomain.CurrentDomain.BaseDirectory;
                    string path = Path.Combine(directory, "EACEnhancements.log");
                    File.AppendAllText(
                        path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + text +
                        Environment.NewLine);
                }
            }
            catch
            {
                // Diagnostics are optional and must never affect extraction.
            }
        }

        private static void InitializeLoggingPreference()
        {
            try
            {
                loggingForcedForProcess = ParseIniBoolean(
                    Environment.GetEnvironmentVariable(LoggingEnvironmentVariable),
                    false);
                string iniPath = GetSettingsFilePath();
                bool savedPreference = ParseIniBoolean(
                    ReadIniValue(iniPath, "EnableLogging", "0"),
                    false);
                loggingEnabled = loggingForcedForProcess || savedPreference;
            }
            catch
            {
                loggingEnabled = false;
                loggingForcedForProcess = false;
            }
        }

        private static void UpdateLoggingPreference(bool enabled)
        {
            loggingEnabled = loggingForcedForProcess || enabled;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint CommandCompletionDelegate(
            IntPtr commandState,
            IntPtr eventHandlePointer);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr CallWndProcHookDelegate(
            int code,
            IntPtr wParam,
            IntPtr lParam);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr MainWindowSubclassDelegate(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData);

    }
}
