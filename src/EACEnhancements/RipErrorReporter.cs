using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace AudioDataPlugIn
{
    internal static partial class EnhancementRuntime
    {
	internal static void BeginRipSession(string driveName, int mode)
	{
		ripSessionStartedUtc = DateTime.UtcNow;
		Interlocked.Increment(ref ripSessionGeneration);
		Interlocked.Exchange(ref ripSessionSuspiciousCount, 0);
		ripSessionThreadId = (int)NativeMethods.GetCurrentThreadId();
		ripSessionActive = true;
		lastPumpTick = Environment.TickCount;
		assistedPumpCount = 0;
		audioTransferCount = 0;
		firstAssistLogged = false;
		Log("Rip session started on thread " + ripSessionThreadId + ", drive '" + (driveName ?? string.Empty) + "', mode " + mode + ".");
	}

	internal static void BeginTransfer(int startPosition, int length, bool test)
	{
		Log("Transfer started: position=" + startPosition + ", length=" + length + ", test=" + test + ".");
	}

	internal static void ObserveAudioTransfer()
	{
		Interlocked.Increment(ref audioTransferCount);
	}

	internal static void ObserveSuspiciousPosition()
	{
		Interlocked.Increment(ref ripSessionSuspiciousCount);
		Log("EAC reported a suspicious extraction position.");
	}

	internal static void FinishTransfer()
	{
		Log("Transfer finished after " + audioTransferCount + " audio callbacks.");
	}

	internal static void EndRipSession()
	{
		ripSessionActive = false;
		DateTime startedUtc = ripSessionStartedUtc;
		int generation = Interlocked.CompareExchange(ref ripSessionGeneration, 0, 0);
		int suspiciousCount = Interlocked.CompareExchange(ref ripSessionSuspiciousCount, 0, 0);
		bool restoreWorkflowDestination = suppressWorkflowFolderTemplate;
		string preferredOutputDirectory = workflowOutputDirectory;
		Log("Rip session ended after " + assistedPumpCount + " assisted message pumps.");
		bool reportErrors = IsRipErrorAlertEnabled();
		if (!reportErrors)
		{
			Log("Rip error alert skipped because it is disabled in EAC Enhancements options.");
			if (!restoreWorkflowDestination)
				return;
		}
		StartRipCompletionWatcher(
			startedUtc,
			generation,
			suspiciousCount,
			reportErrors,
			restoreWorkflowDestination,
			preferredOutputDirectory);
	}

	private static void StartRipCompletionWatcher(
		DateTime startedUtc,
		int generation,
		int suspiciousCount,
		bool reportErrors,
		bool restoreWorkflowDestination,
		string preferredOutputDirectory)
	{
		Thread thread = new Thread((ThreadStart)delegate
		{
			ReportRipErrorsAfterDialogCloses(
				startedUtc,
				generation,
				suspiciousCount,
				reportErrors,
				restoreWorkflowDestination,
				preferredOutputDirectory);
		});
		thread.IsBackground = true;
		thread.Name = "EAC Enhancements rip completion watcher";
		thread.Start();
	}

	private static void ReportRipErrorsAfterDialogCloses(
		DateTime startedUtc,
		int generation,
		int suspiciousCount,
		bool reportErrors,
		bool restoreWorkflowDestination,
		string preferredOutputDirectory)
	{
		bool restorationRequested = false;
		try
		{
			IntPtr intPtr = IntPtr.Zero;
			DateTime dateTime = DateTime.UtcNow.AddSeconds(15.0);
			while (DateTime.UtcNow < dateTime)
			{
				intPtr = ReadAbsolutePointer(layout.MainWindowGlobalVa);
				if (intPtr != IntPtr.Zero && NativeMethods.IsWindow(intPtr))
				{
					break;
				}
				Thread.Sleep(200);
			}
			if (intPtr == IntPtr.Zero || !NativeMethods.IsWindow(intPtr))
			{
				return;
			}
			while (NativeMethods.IsWindow(intPtr))
			{
				if (generation != Interlocked.CompareExchange(ref ripSessionGeneration, 0, 0))
				{
					return;
				}
				IntPtr lastActivePopup = NativeMethods.GetLastActivePopup(intPtr);
				bool flag = lastActivePopup == IntPtr.Zero || lastActivePopup == intPtr || !NativeMethods.IsWindow(lastActivePopup) || !NativeMethods.IsWindowVisible(lastActivePopup);
				if (NativeMethods.IsWindowEnabled(intPtr) && flag)
				{
					break;
				}
				Thread.Sleep(250);
			}
			if (!NativeMethods.IsWindow(intPtr))
			{
				return;
			}
			Thread.Sleep(500);
			// EndOfSession can precede EAC's cue/log/playlist finalization. Keep
			// the concrete workflow destination active until EAC's completion
			// popup is gone, then restore before the user can begin another rip.
			restorationRequested = RequestWorkflowRestoreForGeneration(
				generation,
				restoreWorkflowDestination);
			if (!reportErrors)
				return;
			FileInfo fileInfo = null;
			string text = null;
			DateTime dateTime2 = DateTime.UtcNow.AddSeconds(15.0);
			while (DateTime.UtcNow < dateTime2)
			{
				fileInfo = FindMostRecentRipLog(startedUtc, preferredOutputDirectory);
				if (fileInfo != null)
				{
					try
					{
						text = File.ReadAllText(fileInfo.FullName);
						if (RipLogErrorParser.IsLatestReportComplete(text))
							break;
					}
					catch
					{
						text = null;
					}
				}
				Thread.Sleep(300);
			}
			List<string> list = RipLogErrorParser.Parse(text, suspiciousCount);
			if (list.Count == 0)
			{
				Log("Rip error report: no extraction errors detected" + ((fileInfo == null) ? "." : (" in '" + fileInfo.FullName + "'.")));
				return;
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("EAC completed the rip with errors:");
			stringBuilder.AppendLine();
			foreach (string item in list)
			{
				stringBuilder.AppendLine("\u2022 " + item);
			}
			if (fileInfo != null)
			{
				stringBuilder.AppendLine();
				stringBuilder.Append("Log: ").Append(fileInfo.FullName);
			}
			Log("Rip error alert displayed: " + string.Join("; ", list.ToArray()) + ((fileInfo == null) ? "." : ("; log='" + fileInfo.FullName + "'.")));
			NativeMethods.MessageBoxW(intPtr, stringBuilder.ToString(), "EAC Rip Completed with Errors", 48u);
		}
		catch (Exception ex)
		{
			Log("Rip error reporter failed: " + ex);
		}
		finally
		{
			if (!restorationRequested)
				RequestWorkflowRestoreForGeneration(generation, restoreWorkflowDestination);
		}
	}

	private static bool RequestWorkflowRestoreForGeneration(
		int generation,
		bool restoreWorkflowDestination)
	{
		if (!restoreWorkflowDestination ||
			generation != Interlocked.CompareExchange(ref ripSessionGeneration, 0, 0))
			return false;
		RequestWorkflowFolderTemplateRestore();
		return true;
	}

	internal static FileInfo FindMostRecentRipLog(
		DateTime startedUtc,
		string preferredOutputDirectory)
	{
		DateTime dateTime = startedUtc.AddSeconds(-5.0);
		FileInfo fileInfo = null;
		foreach (string configuredOutputDirectory in GetConfiguredOutputDirectories(
			preferredOutputDirectory))
		{
			foreach (string item in EnumerateLogFilesBounded(configuredOutputDirectory, 2))
			{
				try
				{
					FileInfo fileInfo2 = new FileInfo(item);
					if (!(fileInfo2.LastWriteTimeUtc < dateTime) && (fileInfo == null || fileInfo2.LastWriteTimeUtc > fileInfo.LastWriteTimeUtc))
					{
						fileInfo = fileInfo2;
					}
				}
				catch
				{
				}
			}
		}
		return fileInfo;
	}

	private static IEnumerable<string> GetConfiguredOutputDirectories(
		string preferredOutputDirectory)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			AddOutputDirectory(hashSet, preferredOutputDirectory);
			using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\AWSoftware\\EACU\\Extraction Options"))
			{
				if (registryKey != null)
				{
					AddOutputDirectory(hashSet, registryKey.GetValue("DirectorySpecification", string.Empty) as string);
				}
			}
			using (RegistryKey registryKey2 = Registry.CurrentUser.OpenSubKey("Software\\AWSoftware\\EACU\\StartUp Options"))
			{
				if (registryKey2 != null)
				{
					AddOutputDirectory(hashSet, registryKey2.GetValue("ActualPath", string.Empty) as string);
				}
			}
			string iniPath = GetSettingsFilePath();
			AddOutputDirectory(hashSet, ReadIniValue(iniPath, "Root", string.Empty));
		}
		catch (Exception ex)
		{
			Log("Could not read all configured output directories: " + ex.Message);
		}
		return hashSet;
	}

	private static void AddOutputDirectory(HashSet<string> directories, string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}
		try
		{
			string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim()));
			if (Directory.Exists(fullPath))
			{
				directories.Add(fullPath);
			}
		}
		catch
		{
		}
	}

	private static IEnumerable<string> EnumerateLogFilesBounded(string root, int maximumDepth)
	{
		List<string> list = new List<string>();
		Queue<DirectoryDepth> queue = new Queue<DirectoryDepth>();
		queue.Enqueue(new DirectoryDepth(root, 0));
		while (queue.Count > 0)
		{
			DirectoryDepth directoryDepth = queue.Dequeue();
			try
			{
				list.AddRange(Directory.GetFiles(directoryDepth.Path, "*.log"));
				if (directoryDepth.Depth >= maximumDepth)
				{
					continue;
				}
				string[] directories = Directory.GetDirectories(directoryDepth.Path);
				foreach (string path in directories)
				{
					try
					{
						FileAttributes attributes = File.GetAttributes(path);
						if ((attributes & FileAttributes.ReparsePoint) == 0)
						{
							queue.Enqueue(new DirectoryDepth(path, directoryDepth.Depth + 1));
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
		return list;
	}

    }

    internal sealed class DirectoryDepth
    {
        internal DirectoryDepth(string path, int depth)
        {
            Path = path;
            Depth = depth;
        }

        internal string Path { get; private set; }
        internal int Depth { get; private set; }
    }
}
