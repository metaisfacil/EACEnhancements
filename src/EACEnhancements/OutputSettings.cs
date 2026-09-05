using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioDataPlugIn
{
	internal static partial class EnhancementRuntime
	{
	private const string DefaultFolderTemplate =
		"%albumartist% - %albumtitle% (((%year%))) [FLAC] {{{%comment%}}}";

	internal static void ShowPluginOptions()
	{
		try
		{
			Log("Plugin options request processed on thread " + NativeMethods.GetCurrentThreadId() + ".");
			if (Interlocked.CompareExchange(ref outputSettingsDialogActive, 1, 0) != 0)
				return;

			IntPtr mainWindow = ReadAbsolutePointer(layout.MainWindowGlobalVa);
			IntPtr ownerWindow = NativeMethods.GetActiveWindow();
			if (ownerWindow == IntPtr.Zero || !NativeMethods.IsWindow(ownerWindow))
				ownerWindow = mainWindow;
			RunOutputSettingsDialog(ownerWindow, mainWindow);
		}
		catch (Exception ex)
		{
			Interlocked.Exchange(ref outputSettingsDialogActive, 0);
			Log("Plugin options request failed: " + ex);
		}
	}

	private static void ShowOutputSettingsDialog()
	{
		try
		{
			Log("Output settings request processed on thread " + NativeMethods.GetCurrentThreadId() + ".");
			if (Interlocked.CompareExchange(ref outputSettingsDialogActive, 1, 0) == 0)
			{
				IntPtr mainWindow = ReadAbsolutePointer(layout.MainWindowGlobalVa);
				Thread thread = new Thread((ThreadStart)delegate
				{
					RunOutputSettingsDialog(mainWindow, mainWindow);
				});
				thread.IsBackground = true;
				thread.Name = "EAC Enhancements output settings";
				thread.SetApartmentState(ApartmentState.STA);
				thread.Start();
			}
		}
		catch (Exception ex)
		{
			Interlocked.Exchange(ref outputSettingsDialogActive, 0);
			Log("Output settings command failed: " + ex);
		}
	}

	// A modal dialog disables its owner until it closes. Either entry point can
	// run this on a plugin thread rather than EAC's, so record the owner state
	// up front and restore it in the finally. A failure between disabling and
	// closing otherwise leaves EAC's window permanently unresponsive.
	private static void RunOutputSettingsDialog(IntPtr ownerWindow, IntPtr mainWindow)
	{
		bool ownerWasEnabled = WasDialogOwnerEnabled(ownerWindow);
		try
		{
			byte b = Marshal.ReadByte(AddressFromStaticVa(layout.ChainFlagVa));
			WindowHandleOwner owner = new WindowHandleOwner(ownerWindow);
			if (b != 0 || ripSessionActive)
			{
				MessageBox.Show(owner, "Output settings cannot be changed while an extraction or 100% log preparation step is active.", "EAC Enhancements", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			OutputTemplateSettings settings = LoadOutputTemplateSettings();
			using (OutputTemplateDialog outputTemplateDialog = new OutputTemplateDialog(settings, mainWindow))
			{
				if (outputTemplateDialog.ShowDialog(owner) == DialogResult.OK)
				{
					SaveOutputTemplateSettings(
						outputTemplateDialog.Settings,
						true);
					MessageBox.Show(owner, "The enhancement options were saved and applied.", "EAC Enhancements", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				}
			}
		}
		catch (Exception ex)
		{
			Log("Output settings dialog failed: " + ex);
			MessageBox.Show(new WindowHandleOwner(ownerWindow), "The output settings could not be applied.\n\n" + ex.Message, "EAC Enhancements", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			RestoreDialogOwner(ownerWindow, ownerWasEnabled);
			Interlocked.Exchange(ref outputSettingsDialogActive, 0);
		}
	}

	internal static bool WasDialogOwnerEnabled(IntPtr ownerWindow)
	{
		return ownerWindow != IntPtr.Zero &&
			NativeMethods.IsWindow(ownerWindow) &&
			NativeMethods.IsWindowEnabled(ownerWindow);
	}

	// Restores only an owner that was enabled when the dialog started. EAC
	// disables its own windows during its modal steps, and those must be left
	// alone.
	internal static void RestoreDialogOwner(IntPtr ownerWindow, bool ownerWasEnabled)
	{
		if (!ownerWasEnabled ||
			ownerWindow == IntPtr.Zero ||
			!NativeMethods.IsWindow(ownerWindow) ||
			NativeMethods.IsWindowEnabled(ownerWindow))
		{
			return;
		}
		NativeMethods.EnableWindow(ownerWindow, true);
		Log("Re-enabled EAC's window after a plugin dialog left it disabled.");
	}

	internal static string GetSettingsFilePath()
	{
		string directory = null;
		try
		{
			directory = Path.GetDirectoryName(typeof(AudioDataTransfer).Assembly.Location);
		}
		catch
		{
			// Dynamic or unusual hosts may not expose an assembly location.
		}
		if (String.IsNullOrWhiteSpace(directory))
			directory = AppDomain.CurrentDomain.BaseDirectory;
		return Path.Combine(directory, OutputTemplateIniName);
	}

	internal static bool EnsureDefaultSettingsFile()
	{
		string iniPath = GetSettingsFilePath();
		if (File.Exists(iniPath))
			return false;

		string root = "C:\\EAC\\";
		string folderTemplate = DefaultFolderTemplate;
		using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ExtractionOptionsKey))
		{
			if (key != null)
			{
				string configuredRoot = key.GetValue(
					"DirectorySpecification",
					String.Empty) as string;
				if (!String.IsNullOrWhiteSpace(configuredRoot))
					root = configuredRoot;
			}
		}

		try
		{
			using (FileStream stream = new FileStream(
				iniPath,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.Read))
			using (StreamWriter writer = new StreamWriter(stream, Encoding.Unicode))
			{
				writer.WriteLine("[" + OutputTemplateSection + "]");
				writer.WriteLine("Root=" + SingleLineIniValue(root));
				writer.WriteLine("FolderTemplate=" + SingleLineIniValue(folderTemplate));
				writer.WriteLine("ShowRipErrorAlert=1");
				writer.WriteLine("ShowWorkflowSetupAlert=1");
				writer.WriteLine("CreateWorkflowFolders=1");
				writer.WriteLine(
					IncreaseExternalCompressorArgumentsLimitIniKey +
					"=1");
				writer.WriteLine("EnableLogging=0");
			}
		}
		catch (IOException)
		{
			// Another plugin entry point may have initialized at the same time.
			if (File.Exists(iniPath))
				return false;
			throw;
		}
		return true;
	}

	private static void ShowSettingsFileError(string operation, Exception error)
	{
		try
		{
			MessageBox.Show(
				FormatSettingsFileError(GetSettingsFilePath(), operation, error),
				"EAC Enhancements - Settings File Unavailable",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
		}
		catch
		{
			// Reporting a configuration error must not stop EAC from starting.
		}
	}

	internal static string FormatSettingsFileError(
		string iniPath,
		string operation,
		Exception error)
	{
		string action = String.Equals(operation, "create", StringComparison.OrdinalIgnoreCase)
			? "create"
			: "update";
		string reason = error == null || String.IsNullOrWhiteSpace(error.Message)
			? "Windows did not provide an error description."
			: error.Message.Trim();
		return
			"EAC Enhancements could not " + action + " its settings file:" +
			Environment.NewLine + Environment.NewLine + iniPath +
			Environment.NewLine + Environment.NewLine +
			"Windows reported: " + reason +
			Environment.NewLine + Environment.NewLine +
			"Existing settings (or built-in defaults if no file exists) will remain " +
			"active, but changes cannot be saved." +
			Environment.NewLine + Environment.NewLine +
			"This commonly means your Windows account does not have write access to " +
			"the EAC folder. Close EAC, then have an administrator grant your account " +
			"Modify permission to that folder, or install EAC in a folder your account " +
			"can write to. Running EAC as administrator can help confirm a permissions " +
			"problem, but should not be necessary after the folder permissions are fixed.";
	}

	private static string SingleLineIniValue(string value)
	{
		return (value ?? String.Empty).Replace("\r", String.Empty).Replace("\n", String.Empty);
	}

	private static OutputTemplateSettings LoadOutputTemplateSettings()
	{
		string text = GetSettingsFilePath();
		string value = ReadIniValue(text, "Root", string.Empty);
		string text2 = ReadIniValue(text, "FolderTemplate", DefaultFolderTemplate);
		bool showRipErrorAlert = ParseIniBoolean(
			ReadIniValue(text, "ShowRipErrorAlert", "1"),
			true);
		bool showWorkflowSetupAlert = ParseIniBoolean(
			ReadIniValue(text, "ShowWorkflowSetupAlert", "1"),
			true);
		bool createWorkflowFolders = ParseIniBoolean(
			ReadIniValue(text, "CreateWorkflowFolders", "1"),
			true);
		bool enableLogging = ParseIniBoolean(
			ReadIniValue(text, "EnableLogging", "0"),
			false);
		bool increaseExternalCompressorArgumentsLimit = ParseIniBoolean(
			ReadIniValue(
				text,
				IncreaseExternalCompressorArgumentsLimitIniKey,
				"1"),
			true);
		using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\AWSoftware\\EACU\\Extraction Options"))
		{
			if (string.IsNullOrWhiteSpace(value) && registryKey != null)
			{
				value = registryKey.GetValue("DirectorySpecification", string.Empty) as string;
			}
		}
		if (string.IsNullOrWhiteSpace(value))
		{
			value = "C:\\EAC\\";
		}
		return new OutputTemplateSettings(
			NormalizeRootFolder(value),
			text2 ?? string.Empty,
			showRipErrorAlert,
			showWorkflowSetupAlert,
			createWorkflowFolders,
			enableLogging,
			increaseExternalCompressorArgumentsLimit);
	}

	private static bool IsRipErrorAlertEnabled()
	{
		try
		{
			string iniPath = GetSettingsFilePath();
			return ParseIniBoolean(
				ReadIniValue(iniPath, "ShowRipErrorAlert", "1"),
				true);
		}
		catch (Exception error)
		{
			Log("Could not read the rip-error-alert option; defaulting to enabled: " + error.Message);
			return true;
		}
	}

	private static bool IsWorkflowSetupAlertEnabled()
	{
		try
		{
			string iniPath = GetSettingsFilePath();
			return ParseIniBoolean(
				ReadIniValue(iniPath, "ShowWorkflowSetupAlert", "1"),
				true);
		}
		catch (Exception error)
		{
			Log("Could not read the workflow-setup-alert option; defaulting to enabled: " + error.Message);
			return true;
		}
	}

	private static bool ParseIniBoolean(string value, bool fallback)
	{
		if (String.IsNullOrWhiteSpace(value))
			return fallback;

		switch (value.Trim().ToLowerInvariant())
		{
			case "1":
			case "true":
			case "yes":
			case "on":
				return true;
			case "0":
			case "false":
			case "no":
			case "off":
				return false;
			default:
				return fallback;
		}
	}

	private static string ReadIniValue(string iniPath, string key, string fallback)
	{
		StringBuilder stringBuilder = new StringBuilder(2048);
		NativeMethods.GetPrivateProfileStringW("OutputTemplate", key, fallback, stringBuilder, stringBuilder.Capacity, iniPath);
		return stringBuilder.ToString();
	}

	private static void SaveOutputTemplateSettings(
		OutputTemplateSettings settings,
		bool synchronizeLiveSettings)
	{
		string text = NormalizeRootFolder(settings.RootFolder);
		string text2 = NormalizeFolderTemplate(settings.FolderTemplate);
		Directory.CreateDirectory(text);
		string text4 = GetSettingsFilePath();
		if (!NativeMethods.WritePrivateProfileStringW("OutputTemplate", "Root", text, text4) ||
			!NativeMethods.WritePrivateProfileStringW("OutputTemplate", "FolderTemplate", text2, text4) ||
			!NativeMethods.WritePrivateProfileStringW(
				"OutputTemplate",
				"ShowRipErrorAlert",
				settings.ShowRipErrorAlert ? "1" : "0",
				text4) ||
			!NativeMethods.WritePrivateProfileStringW(
				"OutputTemplate",
				"ShowWorkflowSetupAlert",
				settings.ShowWorkflowSetupAlert ? "1" : "0",
				text4) ||
			!NativeMethods.WritePrivateProfileStringW(
				"OutputTemplate",
				"CreateWorkflowFolders",
				settings.CreateWorkflowFolders ? "1" : "0",
				text4) ||
			!NativeMethods.WritePrivateProfileStringW(
				"OutputTemplate",
				IncreaseExternalCompressorArgumentsLimitIniKey,
				settings.IncreaseExternalCompressorArgumentsLimit
					? "1"
					: "0",
				text4) ||
			!NativeMethods.WritePrivateProfileStringW(
				"OutputTemplate",
				"EnableLogging",
				settings.EnableLogging ? "1" : "0",
				text4))
		{
			int errorCode = Marshal.GetLastWin32Error();
			Exception error = errorCode == 0
				? new IOException("Windows did not report why the write failed.")
				: (Exception)new Win32Exception(errorCode);
			throw new IOException(
				FormatSettingsFileError(text4, "update", error),
				error);
		}
		UpdateLoggingPreference(settings.EnableLogging);
		UpdateExtendedCompressorArgumentsPreference(
			settings.IncreaseExternalCompressorArgumentsLimit);
		if (synchronizeLiveSettings)
			SaveActiveProfileSettingsToRegistry();
		using (RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\AWSoftware\\EACU\\Extraction Options"))
		{
			if (registryKey == null)
			{
				throw new InvalidOperationException("EAC's extraction settings could not be opened.");
			}
			registryKey.SetValue("DirectorySpecification", text, RegistryValueKind.String);
		}
		using (RegistryKey registryKey2 = Registry.CurrentUser.CreateSubKey("Software\\AWSoftware\\EACU\\StartUp Options"))
		{
			if (registryKey2 == null)
			{
				throw new InvalidOperationException("EAC's startup settings could not be opened.");
			}
			registryKey2.SetValue("ActualPath", text, RegistryValueKind.String);
		}
		IntPtr intPtr = ReadAbsolutePointer(layout.MainWindowGlobalVa);
		if (intPtr != IntPtr.Zero && NativeMethods.IsWindow(intPtr))
		{
			NativeMethods.SendMessageW(intPtr, 273u, new IntPtr(788), IntPtr.Zero);
		}
		Log(
			"Enhancement settings updated: root='" + text +
			"', folder='" + text2 +
			"', ripErrorAlert=" + settings.ShowRipErrorAlert +
			", workflowSetupAlert=" + settings.ShowWorkflowSetupAlert +
			", createWorkflowFolders=" + settings.CreateWorkflowFolders +
			", extendedCompressorArguments=" +
			settings.IncreaseExternalCompressorArgumentsLimit +
			", logging=" + settings.EnableLogging + ".");
	}

	private static WorkflowNamingSchemes workflowNamingSchemes;

	private static void ApplyTrackOnlyNamingSchemes()
	{
		SaveActiveProfileSettingsToRegistry();
		using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ExtractionOptionsKey))
		{
			if (key == null)
				throw new InvalidOperationException("EAC's extraction settings could not be opened.");
			if (workflowNamingSchemes == null)
				workflowNamingSchemes = WorkflowNamingSchemes.Capture(key);
			workflowNamingSchemes.ApplyTrackOnly(key);
		}
		RefreshLiveOutputSettings();
	}

	private static void RestoreWorkflowNamingSchemes()
	{
		if (workflowNamingSchemes == null)
			return;
		using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ExtractionOptionsKey))
		{
			if (key == null)
				throw new InvalidOperationException("EAC's extraction settings could not be opened.");
			workflowNamingSchemes.Restore(key);
		}
		RefreshLiveOutputSettings();
		workflowNamingSchemes = null;
	}

	private static void RefreshLiveOutputSettings()
	{
		LiveSettingsRefreshDelegate refresh =
			(LiveSettingsRefreshDelegate)Marshal.GetDelegateForFunctionPointer(
				AddressFromStaticVa(layout.LiveSettingsRefreshVa),
				typeof(LiveSettingsRefreshDelegate));
		refresh();
	}

	private static void MigrateLegacyWorkflowNamingSchemes()
	{
		try
		{
			string iniPath = GetSettingsFilePath();
			if (ReadIniValue(iniPath, "WorkflowOnlyFolderTemplates", "0") == "1")
				return;
			string template = ConvertBraceTokens(NormalizeFolderTemplate(
				ReadIniValue(iniPath, "FolderTemplate", DefaultFolderTemplate)));
			SaveActiveProfileSettingsToRegistry();
			bool changed;
			using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ExtractionOptionsKey))
			{
				if (key == null)
					throw new InvalidOperationException("EAC's extraction settings could not be opened.");
				changed = WorkflowNamingSchemes.RemoveLegacyFolderPrefixes(key, template);
			}
			if (changed)
				RefreshLiveOutputSettings();
			if (!NativeMethods.WritePrivateProfileStringW(
				"OutputTemplate", "WorkflowOnlyFolderTemplates", "1", iniPath))
				throw new IOException("Could not record the workflow naming migration.");
			Log("Workflow-only folder naming migration completed; namingSchemesChanged=" + changed + ".");
		}
		catch (Exception error)
		{
			Log("Workflow naming migration failed: " + error);
		}
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void LiveSettingsRefreshDelegate();

	internal static string NormalizeRootFolder(string value)
	{
		string text = Environment.ExpandEnvironmentVariables((value ?? string.Empty).Trim()).Replace('/', '\\');
		if (text.Length == 0 || !Path.IsPathRooted(text))
		{
			throw new ArgumentException("The root folder must be an absolute Windows path.");
		}
		string text2 = Path.GetFullPath(text);
		if (!text2.EndsWith("\\", StringComparison.Ordinal))
		{
			text2 += "\\";
		}
		return text2;
	}

	internal static string NormalizeFolderTemplate(string value)
	{
		string text = (value ?? string.Empty).Trim().Replace('/', '\\').Trim('\\');
		if (text.IndexOfAny(new char[7] { '<', '>', ':', '"', '|', '?', '*' }) >= 0)
		{
			throw new ArgumentException("The folder template contains a character Windows does not allow in folder names.");
		}
		return text;
	}

	internal static string ConvertBraceTokens(string template)
	{
		return Regex.Replace(template, "\\{\\s*([a-zA-Z0-9_]+)\\s*\\}", delegate(Match match)
		{
			string text = match.Groups[1].Value.ToLowerInvariant();
			switch (text)
			{
			case "albumartist":
				return "%albumartist%";
			case "albumtitle":
				return "%albumtitle%";
			case "artist":
				return "%artist%";
			case "title":
			case "tracktitle":
				return "%title%";
			case "year":
				return "%year%";
			case "genre":
				return "%genre%";
			case "comment":
				return "%comment%";
			case "composer":
				return "%composer%";
			case "tracknr":
				return "%tracknr%";
			case "tracknr2":
				return "%tracknr2%";
			case "cdnumber":
				return "%cdnumber%";
			case "totalcds":
				return "%totalcds%";
			default:
				throw new ArgumentException("Unsupported folder-template token: {" + text + "}.");
			}
		});
	}

	internal static string NamingSchemeTail(string value, string fallback)
	{
		string text = (string.IsNullOrWhiteSpace(value) ? fallback : value.Trim());
		text = text.Replace('/', '\\');
		int num = text.LastIndexOf('\\');
		string text2 = ((num >= 0) ? text.Substring(num + 1).Trim() : text);
		if (text2.Length != 0)
		{
			return text2;
		}
		return fallback;
	}

    }
}
