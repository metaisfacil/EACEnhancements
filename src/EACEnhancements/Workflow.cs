using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioDataPlugIn
{
    internal static partial class EnhancementRuntime
    {
	private static void InstallWorkflowHooks()
	{
		byte[] array = ReadBytes(layout.DispatchHookVa, 7);
		if (array.Length > 0 && array[0] == 233)
		{
			workflowStatus = "existing assembly-level workflow detected; DLL hooks skipped";
			Log(workflowStatus + ".");
			return;
		}
		RequireBytes(layout.DispatchHookVa, layout.ExpectedDispatch, "command dispatch");
		RequireBytes(layout.DispatchOldHandlerVa, layout.ExpectedOldHandler, "original 0x310 handler");
		RequireBytes(layout.GapsHookVa, layout.ExpectedGapsEndpoint, "Detect Gaps endpoint");
		RequireBytes(layout.CueChainHookVa, layout.ExpectedCueEndpoint, "CUE endpoint");
		RequireBytes(layout.CueSaveHookVa, layout.ExpectedCueSaveDecision, "CUE path decision");
		RequireBytes(layout.WaveformSaveHookVa, layout.ExpectedWaveformDecision, "waveform path decision");
		RequireBytes(layout.RipCompleteHookVa, layout.ExpectedRipComplete, "completion dialog");
		workflowCode = NativeMethods.VirtualAlloc(IntPtr.Zero, new UIntPtr(4096u), 12288u, 64u);
		workflowData = NativeMethods.VirtualAlloc(IntPtr.Zero, new UIntPtr(256u), 12288u, 4u);
		if (workflowCode == IntPtr.Zero || workflowData == IntPtr.Zero)
		{
			throw new InvalidOperationException("VirtualAlloc for the workflow payload failed with Win32 error " + Marshal.GetLastWin32Error() + ".");
		}
		uint num = Pointer32(workflowData);
		uint address = num + 100;
		workflowSelectionBackupAddress = num;
		workflowAutoCloseFlagAddress = address;
		X86CodeBuilder x86CodeBuilder = new X86CodeBuilder(workflowCode);
		int offset = x86CodeBuilder.Offset;
		x86CodeBuilder.Emit(Hex("3D 14 03 00 00"));
		int instructionOffset = x86CodeBuilder.EmitJzPlaceholder();
		x86CodeBuilder.Emit(Hex("3D 14 A3 00 00"));
		int instructionOffset2 = x86CodeBuilder.EmitJzPlaceholder();
		x86CodeBuilder.Emit(Hex("3D 12 03 00 00"));
		int instructionOffset3 = x86CodeBuilder.EmitJzPlaceholder();
		x86CodeBuilder.Emit(Hex("3D 10 03 00 00"));
		int instructionOffset4 = x86CodeBuilder.EmitJzPlaceholder();
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.DispatchNextHandlerVa));
		int offset2 = x86CodeBuilder.Offset;
		x86CodeBuilder.EmitMovByteAbsolute(RuntimeVa(layout.ChainFlagVa), 1);
		EmitPostCommand(x86CodeBuilder, 572u);
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.DispatchReturnVa));
		int offset3 = x86CodeBuilder.Offset;
		EmitPostCommand(x86CodeBuilder, WorkflowDestinationCommand);
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.DispatchReturnVa));
		int offset4 = x86CodeBuilder.Offset;
		x86CodeBuilder.EmitMovByteAbsolute(RuntimeVa(layout.ChainFlagVa), 2);
		x86CodeBuilder.EmitCopyBytesPreservingRegisters(RuntimeVa(layout.TrackSelectionArrayVa), num, TrackSelectionCount);
		EmitPostCommand(x86CodeBuilder, 539u);
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.DispatchReturnVa));
		int offset5 = x86CodeBuilder.Offset;
		x86CodeBuilder.Emit(Hex("60"));
		x86CodeBuilder.EmitCall(RuntimeVa(layout.LiveSettingsRefreshVa));
		x86CodeBuilder.Emit(Hex("61"));
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.DispatchReturnVa));
		x86CodeBuilder.PatchBranch(instructionOffset, x86CodeBuilder.AddressOf(offset5));
		x86CodeBuilder.PatchBranch(instructionOffset2, x86CodeBuilder.AddressOf(offset4));
		x86CodeBuilder.PatchBranch(instructionOffset3, x86CodeBuilder.AddressOf(offset3));
		x86CodeBuilder.PatchBranch(instructionOffset4, x86CodeBuilder.AddressOf(offset2));
		int offset6 = x86CodeBuilder.Offset;
		x86CodeBuilder.EmitCmpByteAbsolute(RuntimeVa(layout.ChainFlagVa), 2);
		int instructionOffset5 = x86CodeBuilder.EmitJzPlaceholder();
		x86CodeBuilder.EmitCmpByteAbsolute(RuntimeVa(layout.ChainFlagVa), 1);
		int instructionOffset6 = x86CodeBuilder.EmitJzPlaceholder();
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.GapsResumeVa));
		int offset7 = x86CodeBuilder.Offset;
		x86CodeBuilder.EmitMovByteAbsolute(RuntimeVa(layout.ChainFlagVa), 0);
		EmitPostCommand(x86CodeBuilder, 770u);
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.GapsResumeVa));
		int offset8 = x86CodeBuilder.Offset;
		x86CodeBuilder.EmitMovByteAbsolute(RuntimeVa(layout.ChainFlagVa), 3);
		EmitPostCommand(x86CodeBuilder, 586u);
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.GapsResumeVa));
		x86CodeBuilder.PatchBranch(instructionOffset5, x86CodeBuilder.AddressOf(offset8));
		x86CodeBuilder.PatchBranch(instructionOffset6, x86CodeBuilder.AddressOf(offset7));
		int offset9 = x86CodeBuilder.Offset;
		x86CodeBuilder.EmitCmpByteAbsolute(RuntimeVa(layout.ChainFlagVa), 3);
		int instructionOffset7 = x86CodeBuilder.EmitJzPlaceholder();
		x86CodeBuilder.EmitCmpByteAbsolute(RuntimeVa(layout.ChainFlagVa), 1);
		int instructionOffset8 = x86CodeBuilder.EmitJzPlaceholder();
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.CueChainResumeVa));
		int offset10 = x86CodeBuilder.Offset;
		EmitPostCommand(x86CodeBuilder, 539u);
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.CueChainResumeVa));
		int offset11 = x86CodeBuilder.Offset;
		x86CodeBuilder.EmitCopyBytesPreservingRegisters(num, RuntimeVa(layout.TrackSelectionArrayVa), TrackSelectionCount);
		x86CodeBuilder.EmitMovByteAbsolute(RuntimeVa(layout.ChainFlagVa), 0);
		x86CodeBuilder.EmitMovByteAbsolute(address, 1);
		EmitPostCommand(x86CodeBuilder, 771u);
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.CueChainResumeVa));
		x86CodeBuilder.PatchBranch(instructionOffset7, x86CodeBuilder.AddressOf(offset11));
		x86CodeBuilder.PatchBranch(instructionOffset8, x86CodeBuilder.AddressOf(offset10));
		int offset12 = x86CodeBuilder.Offset;
		x86CodeBuilder.EmitCmpByteAbsolute(RuntimeVa(layout.ChainFlagVa), 3);
		x86CodeBuilder.EmitJz(RuntimeVa(layout.CueSaveDefaultVa));
		x86CodeBuilder.EmitCmpDwordAbsoluteImmediate8(RuntimeVa(layout.OutputPathModeVa), 0);
		x86CodeBuilder.EmitJnz(RuntimeVa(layout.CueSavePromptVa));
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.CueSaveDefaultVa));
		int offset13 = x86CodeBuilder.Offset;
		x86CodeBuilder.EmitCmpByteAbsolute(address, 0);
		int instructionOffset9 = x86CodeBuilder.EmitJzPlaceholder();
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.WaveformSaveDefaultVa));
		int offset14 = x86CodeBuilder.Offset;
		x86CodeBuilder.EmitCmpDwordAbsoluteImmediate8(RuntimeVa(layout.OutputPathModeVa), 1);
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.WaveformSaveResumeVa));
		x86CodeBuilder.PatchBranch(instructionOffset9, x86CodeBuilder.AddressOf(offset14));
		int offset15 = x86CodeBuilder.Offset;
		x86CodeBuilder.EmitMovByteAbsolute(RuntimeVa(layout.CopyStatusCompleteFlagVa), 1);
		x86CodeBuilder.EmitCmpByteAbsolute(address, 0);
		int instructionOffset10 = x86CodeBuilder.EmitJzPlaceholder();
		x86CodeBuilder.EmitMovByteAbsolute(address, 0);
		x86CodeBuilder.Emit(Hex("6A 00"));
		x86CodeBuilder.EmitPushImmediate(811u);
		x86CodeBuilder.EmitPushImmediate(273u);
		x86CodeBuilder.Emit(Hex("FF 75 08"));
		x86CodeBuilder.EmitCall(RuntimeVa(layout.PostMessageWThunkVa));
		int offset16 = x86CodeBuilder.Offset;
		x86CodeBuilder.PatchBranch(instructionOffset10, x86CodeBuilder.AddressOf(offset16));
		x86CodeBuilder.EmitJmp(RuntimeVa(layout.RipCompleteResumeVa));
		byte[] array2 = x86CodeBuilder.ToArray();
		Marshal.Copy(array2, 0, workflowCode, array2.Length);
		NativeMethods.FlushInstructionCache(NativeMethods.GetCurrentProcess(), workflowCode, new UIntPtr((uint)array2.Length));
		WriteJumpPatch(layout.RipCompleteHookVa, x86CodeBuilder.AddressOf(offset15), 7);
		WriteJumpPatch(layout.WaveformSaveHookVa, x86CodeBuilder.AddressOf(offset13), 9);
		WriteJumpPatch(layout.CueSaveHookVa, x86CodeBuilder.AddressOf(offset12), 9);
		WriteJumpPatch(layout.CueChainHookVa, x86CodeBuilder.AddressOf(offset9), 23);
		WriteJumpPatch(layout.GapsHookVa, x86CodeBuilder.AddressOf(offset6), 30);
		WriteMemoryPatch(layout.DispatchOldHandlerVa, RepeatByte(144, 30));
		WriteJumpPatch(layout.DispatchHookVa, x86CodeBuilder.AddressOf(offset), 7);
		workflowInstalled = true;
		workflowStatus = "active; payload=0x" + Pointer32(workflowCode).ToString("X8") + ", state=0x" + num.ToString("X8");
		Log("100% log workflow " + workflowStatus + ".");
	}

	private static void EmitPostCommand(X86CodeBuilder code, uint command)
	{
		code.Emit(Hex("6A 00"));
		code.EmitPushImmediate(command);
		code.EmitPushImmediate(273u);
		code.EmitPushAbsolute(RuntimeVa(layout.MainWindowGlobalVa));
		code.EmitCall(RuntimeVa(layout.PostMessageWThunkVa));
	}

	private static void StartMenuInstaller()
	{
		if (workflowInstalled || workflowStatus.IndexOf("existing assembly-level", StringComparison.Ordinal) >= 0)
		{
			Thread thread = new Thread(InstallWorkflowMenuWhenReady);
			thread.IsBackground = true;
			thread.Name = "EAC Enhancements menu installer";
			thread.Start();
		}
	}

	private static void InstallWorkflowMenuWhenReady()
	{
		try
		{
			IntPtr intPtr = IntPtr.Zero;
			bool flag = false;
			for (int i = 0; i < 150; i++)
			{
				intPtr = ReadAbsolutePointer(layout.MainWindowGlobalVa);
				if (intPtr != IntPtr.Zero && NativeMethods.IsWindow(intPtr))
				{
					EnsureWorkflowCancellationHooks(intPtr);
					if (InstallWorkflowMenu(intPtr))
					{
						flag = true;
						break;
					}
				}
				Thread.Sleep(200);
			}
			if (!flag)
			{
				Log("100% log menu was not installed: EAC main menu was not ready.");
			}
			else
			{
				BeginCommandLineWhenReady(intPtr);
				MonitorWorkflowMenuState(intPtr);
			}
		}
		catch (Exception ex)
		{
			Log("100% log menu installation failed: " + ex);
		}
	}

	private static bool InstallWorkflowMenu(IntPtr mainWindow)
	{
		IntPtr menu = NativeMethods.GetMenu(mainWindow);
		if (menu == IntPtr.Zero)
		{
			return false;
		}
		IntPtr intPtr = FindActionMenu(menu);
		if (intPtr == IntPtr.Zero)
		{
			return false;
		}
		bool flag = false;
		if (NativeMethods.GetMenuState(intPtr, 786u, 0u) == uint.MaxValue)
		{
			if (!NativeMethods.AppendMenuW(intPtr, 2048u, UIntPtr.Zero, null) || !NativeMethods.AppendMenuW(intPtr, 0u, new UIntPtr(786u), "&Test && Copy + Cue (100% Log)"))
			{
				throw new InvalidOperationException("AppendMenuW failed for command 0x312 with Win32 error " + Marshal.GetLastWin32Error() + ".");
			}
			flag = true;
			Log("Installed Action-menu command 0x312: &Test && Copy + Cue (100% Log).");
		}
		bool flag2 = NativeMethods.GetMenuState(intPtr, 41746u, 0u) != uint.MaxValue;
		if (workflowInstalled && !flag2)
		{
			if (!NativeMethods.AppendMenuW(
				intPtr,
				NativeMethods.MF_STRING,
				new UIntPtr(OutputSettingsCommand),
				OutputSettingsMenuText))
			{
				throw new InvalidOperationException("AppendMenuW failed for settings command 0x" + 41746u.ToString("X") + " with Win32 error " + Marshal.GetLastWin32Error() + ".");
			}
			flag = true;
			Log(
				"Installed Action-menu command 0x" +
				OutputSettingsCommand.ToString("X") + ": " +
				OutputSettingsMenuText + ".");
		}
		if (flag)
		{
			NativeMethods.DrawMenuBar(mainWindow);
		}
		return true;
	}

	private static IntPtr FindActionMenu(IntPtr menu)
	{
		int menuItemCount = NativeMethods.GetMenuItemCount(menu);
		StringBuilder stringBuilder = new StringBuilder(256);
		for (int i = 0; i < menuItemCount; i++)
		{
			stringBuilder.Length = 0;
			NativeMethods.GetMenuStringW(menu, (uint)i, stringBuilder, stringBuilder.Capacity, 1024u);
			string text = stringBuilder.ToString().Replace("&", string.Empty).Trim();
			if (text.Equals("Action", StringComparison.OrdinalIgnoreCase))
			{
				return NativeMethods.GetSubMenu(menu, i);
			}
		}
		if (menuItemCount <= 2)
		{
			return IntPtr.Zero;
		}
		return NativeMethods.GetSubMenu(menu, 2);
	}

	private static void MonitorWorkflowMenuState(IntPtr mainWindow)
	{
		int lastEnabled = -1;
		while (NativeMethods.IsWindow(mainWindow))
		{
			try
			{
				EnsureWorkflowCancellationHooks(mainWindow);
				SynchronizeWorkflowMenuState(mainWindow, ref lastEnabled);
			}
			catch (Exception ex)
			{
				Log("100% log menu-state synchronization failed: " + ex.Message);
			}
			Thread.Sleep(100);
		}
	}

	private static void SynchronizeWorkflowMenuState(IntPtr mainWindow, ref int lastEnabled)
	{
		IntPtr menu = NativeMethods.GetMenu(mainWindow);
		if (menu == IntPtr.Zero)
		{
			return;
		}
		IntPtr intPtr = FindActionMenu(menu);
		if (intPtr == IntPtr.Zero)
		{
			return;
		}
		uint menuState = NativeMethods.GetMenuState(intPtr, 771u, 0u);
		if (menuState == uint.MaxValue)
		{
			IntPtr intPtr2 = FindMenuContainingCommand(menu, 771u);
			if (intPtr2 == IntPtr.Zero)
			{
				return;
			}
			menuState = NativeMethods.GetMenuState(intPtr2, 771u, 0u);
		}
		bool flag = (menuState & 3) == 0;
		uint menuState2 = NativeMethods.GetMenuState(intPtr, 786u, 0u);
		if (menuState2 != uint.MaxValue)
		{
			bool flag2 = (menuState2 & 3) == 0;
			if (flag2 != flag)
			{
				NativeMethods.EnableMenuItem(intPtr, 786u, (!flag) ? 1u : 0u);
				NativeMethods.DrawMenuBar(mainWindow);
			}
			int num = (flag ? 1 : 0);
			if (lastEnabled != num)
			{
				lastEnabled = num;
				Log("100% log menu is now " + (flag ? "enabled" : "disabled") + " (mirrors command 0x303).");
			}
		}
	}

	private static IntPtr FindMenuContainingCommand(IntPtr menu, uint command)
	{
		if (NativeMethods.GetMenuState(menu, command, 0u) != uint.MaxValue)
		{
			return menu;
		}
		int menuItemCount = NativeMethods.GetMenuItemCount(menu);
		for (int i = 0; i < menuItemCount; i++)
		{
			IntPtr subMenu = NativeMethods.GetSubMenu(menu, i);
			if (!(subMenu == IntPtr.Zero))
			{
				IntPtr intPtr = FindMenuContainingCommand(subMenu, command);
				if (intPtr != IntPtr.Zero)
				{
					return intPtr;
				}
			}
		}
		return IntPtr.Zero;
	}

	private static void EnsureWorkflowCancellationHooks(IntPtr mainWindow)
	{
		int tickCount = Environment.TickCount;
		if (lastWorkflowHookRefreshTick != 0 && tickCount - lastWorkflowHookRefreshTick < 500)
		{
			return;
		}
		lastWorkflowHookRefreshTick = tickCount;
		if (workflowCallWndProcHookDelegate == null)
		{
			workflowCallWndProcHookDelegate = WorkflowCallWndProc;
		}
		if (workflowGetMessageHookDelegate == null)
		{
			workflowGetMessageHookDelegate = WorkflowGetMessage;
		}
		HashSet<uint> hashSet = new HashSet<uint>();
		uint windowThreadProcessId = NativeMethods.GetWindowThreadProcessId(mainWindow, IntPtr.Zero);
		if (windowThreadProcessId != 0)
		{
			hashSet.Add(windowThreadProcessId);
		}
		try
		{
			foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
			{
				hashSet.Add((uint)thread.Id);
			}
		}
		catch
		{
		}
		uint currentThreadId = NativeMethods.GetCurrentThreadId();
		IntPtr functionPointerForDelegate = Marshal.GetFunctionPointerForDelegate(workflowCallWndProcHookDelegate);
		IntPtr functionPointerForDelegate2 = Marshal.GetFunctionPointerForDelegate(workflowGetMessageHookDelegate);
		foreach (uint item in hashSet)
		{
			if (item == 0 || item == currentThreadId)
			{
				continue;
			}
			lock (WorkflowHookLock)
			{
				if (WorkflowHookedThreads.Contains(item))
				{
					continue;
				}
				IntPtr intPtr = NativeMethods.SetWindowsHookExW(4, functionPointerForDelegate, IntPtr.Zero, item);
				IntPtr intPtr2 = NativeMethods.SetWindowsHookExW(3, functionPointerForDelegate2, IntPtr.Zero, item);
				if (!(intPtr == IntPtr.Zero) || !(intPtr2 == IntPtr.Zero))
				{
					if (intPtr != IntPtr.Zero)
					{
						WorkflowMessageHooks.Add(intPtr);
					}
					if (intPtr2 != IntPtr.Zero)
					{
						WorkflowMessageHooks.Add(intPtr2);
					}
					WorkflowHookedThreads.Add(item);
					Log("100% log cancellation guard active on thread " + item + " (call=" + ((intPtr != IntPtr.Zero) ? "yes" : "no") + ", queue=" + ((intPtr2 != IntPtr.Zero) ? "yes" : "no") + ").");
				}
			}
		}
	}

	private static IntPtr WorkflowCallWndProc(int code, IntPtr wParam, IntPtr lParam)
	{
		try
		{
			if (code >= 0 && lParam != IntPtr.Zero)
			{
				NativeMethods.CWPSTRUCT cWPSTRUCT = (NativeMethods.CWPSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.CWPSTRUCT));
				IntPtr intPtr = ReadAbsolutePointer(layout.MainWindowGlobalVa);
				if (cWPSTRUCT.hwnd == intPtr)
				{
					EnsureMainWindowSubclass(intPtr);
				}
				InspectWorkflowMessage(cWPSTRUCT.hwnd, cWPSTRUCT.message, cWPSTRUCT.wParam, cWPSTRUCT.lParam, "call");
			}
		}
		catch (Exception ex)
		{
			Log("100% log cancellation guard failed: " + ex.Message);
		}
		return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
	}

	private static void EnsureMainWindowSubclass(IntPtr mainWindow)
	{
		if (mainWindow == IntPtr.Zero || Interlocked.CompareExchange(ref mainWindowSubclassInstalled, -1, 0) != 0)
		{
			return;
		}
		try
		{
			mainWindowSubclassDelegate = MainWindowSubclass;
			IntPtr functionPointerForDelegate = Marshal.GetFunctionPointerForDelegate(mainWindowSubclassDelegate);
			if (!NativeMethods.SetWindowSubclass(mainWindow, functionPointerForDelegate, new UIntPtr(246194962u), UIntPtr.Zero))
			{
				mainWindowSubclassDelegate = null;
				Interlocked.Exchange(ref mainWindowSubclassInstalled, 0);
				Log("Output settings window subclass could not be installed.");
			}
			else
			{
				Interlocked.Exchange(ref mainWindowSubclassInstalled, 1);
				Log("Output settings window subclass active.");
			}
		}
		catch (Exception ex)
		{
			mainWindowSubclassDelegate = null;
			Interlocked.Exchange(ref mainWindowSubclassInstalled, 0);
			Log("Output settings window subclass failed: " + ex.Message);
		}
	}

	private static IntPtr MainWindowSubclass(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr referenceData)
	{
		try
		{
			int command = (int)wParam.ToInt64() & 0xFFFF;
			if (message == 273 && command == 41746)
			{
				ShowOutputSettingsDialog();
				return IntPtr.Zero;
			}
			if (message == 273 &&
				(command == (int)CustomWorkflowCommand ||
				 command == (int)WorkflowDestinationCommand))
			{
				ShowWorkflowDestinationDialog(hwnd);
				return IntPtr.Zero;
			}
			if (message == 273 && command == (int)RestoreWorkflowFolderCommand)
			{
				RestoreConfiguredWorkflowFolderTemplate(hwnd);
				return IntPtr.Zero;
			}
			if (message == NativeMethods.WM_COMMAND && command == (int)BeginCommandLineMetadataCommand)
			{
				StartCommandLineMetadataLookup(hwnd);
				return IntPtr.Zero;
			}
			if (message == NativeMethods.WM_COMMAND && command == (int)FinishCommandLineMetadataCommand)
			{
				FinishCommandLineMetadata(hwnd, false);
				return IntPtr.Zero;
			}
			if (message == NativeMethods.WM_COMMAND && command == (int)FailCommandLineMetadataCommand)
			{
				FinishCommandLineMetadata(hwnd, true);
				return IntPtr.Zero;
			}
		}
		catch (Exception ex)
		{
			Log("Output settings window subclass callback failed: " + ex.Message);
			return IntPtr.Zero;
		}
		return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
	}

	private static void ShowWorkflowDestinationDialog(IntPtr mainWindow)
	{
		if (Interlocked.CompareExchange(ref workflowDestinationDialogActive, 1, 0) != 0)
			return;

		try
		{
			if (Marshal.ReadByte(AddressFromStaticVa(layout.ChainFlagVa)) != 0 || ripSessionActive)
				return;

			bool askEveryTime =
				Marshal.ReadInt32(AddressFromStaticVa(layout.OutputPathModeVa)) == 1;
			if (askEveryTime)
			{
				OutputTemplateSettings settings = LoadOutputTemplateSettings();
				using (FolderBrowserDialog dialog = new FolderBrowserDialog())
				{
					dialog.Description = settings.CreateWorkflowFolders
						? "Choose the parent folder for this rip. EAC Enhancements will create " +
						  "the album folder inside it using your folder template."
						: "Choose the folder that should receive the contents of this rip.";
					dialog.ShowNewFolderButton = true;
					string initialPath = settings.RootFolder.TrimEnd('\\');
					if (Directory.Exists(initialPath))
						dialog.SelectedPath = initialPath;
					if (dialog.ShowDialog(new WindowHandleOwner(mainWindow)) != DialogResult.OK)
					{
						Log("100% log destination selection cancelled before preparation began.");
						return;
					}

					OutputTemplateSettings selectedSettings = new OutputTemplateSettings(
						dialog.SelectedPath,
						settings.FolderTemplate,
						settings.ShowRipErrorAlert,
						settings.CreateWorkflowFolders,
						settings.EnableLogging);
					SaveOutputTemplateSettings(selectedSettings);
					if (settings.CreateWorkflowFolders)
					{
						PrepareDedicatedWorkflowFolder(
							mainWindow,
							selectedSettings,
							"100% log destination prepared");
					}
					else
					{
						PrepareDirectWorkflowDestination(selectedSettings.RootFolder);
					}
				}
			}
			else
			{
				PrepareDedicatedWorkflowFolder(
					mainWindow,
					LoadOutputTemplateSettings(),
					"100% log standard-directory destination prepared");
			}

			NativeMethods.PostMessageW(
				mainWindow,
				273u,
				new IntPtr((int)StartPreparedWorkflowCommand),
				IntPtr.Zero);
		}
		catch (Exception ex)
		{
			RequestWorkflowFolderTemplateRestore();
			Log("100% log destination setup failed: " + ex);
			MessageBox.Show(
				new WindowHandleOwner(mainWindow),
				"The extraction destination could not be prepared.\r\n\r\n" + ex.Message,
				"EAC Enhancements",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);
		}
		finally
		{
			Interlocked.Exchange(ref workflowDestinationDialogActive, 0);
		}
	}

	private static void RequestWorkflowFolderTemplateRestore()
	{
		if (!suppressWorkflowFolderTemplate)
			return;
		IntPtr mainWindow = ReadAbsolutePointer(layout.MainWindowGlobalVa);
		if (mainWindow != IntPtr.Zero && NativeMethods.IsWindow(mainWindow))
		{
			NativeMethods.PostMessageW(
				mainWindow,
				273u,
				new IntPtr((int)RestoreWorkflowFolderCommand),
				IntPtr.Zero);
		}
	}

	private static void RestoreConfiguredWorkflowFolderTemplate(IntPtr mainWindow)
	{
		if (!suppressWorkflowFolderTemplate)
			return;
		suppressWorkflowFolderTemplate = false;
		try
		{
			OutputTemplateSettings settings = LoadOutputTemplateSettings();
			SaveOutputTemplateSettings(settings);
			ApplyConditionalFolderTemplateFromMainWindow(mainWindow);
			RestoreConfiguredOutputPath(settings.RootFolder);
			Log("Folder template and configured output path restored after the 100% log workflow.");
		}
		catch (Exception ex)
		{
			Log("Folder template could not be restored after the 100% log workflow: " + ex);
		}
	}

	private static void ApplyConditionalFolderTemplateFromMainWindow(IntPtr mainWindow)
	{
		int year;
		if (!Int32.TryParse(ReadChildControlText(mainWindow, 995), out year))
			year = 0;
		ApplyConditionalFolderTemplate(year, ReadChildControlText(mainWindow, 883));
	}

	private static void PrepareDirectWorkflowDestination(string destination)
	{
		string normalized = Path.GetFullPath(destination);
		Directory.CreateDirectory(normalized);
		suppressWorkflowFolderTemplate = true;
		ApplyTrackOnlyNamingSchemes();
		WriteEacPathBuffer(layout.StandardDirectoryPathVa, normalized);
		WriteEacPathBuffer(layout.ActualPathVa, normalized);
		Log("100% log contents destination prepared: '" + normalized + "'.");
	}

	private static void PrepareDedicatedWorkflowFolder(
		IntPtr mainWindow,
		OutputTemplateSettings settings,
		string logPrefix)
	{
		string template = String.IsNullOrWhiteSpace(settings.FolderTemplate)
			? "%albumartist% - %albumtitle%"
			: settings.FolderTemplate;
		Dictionary<string, string> metadata = ReadWorkflowFolderMetadata(mainWindow);
		string destination = WorkflowFolderPath.ResolveDestination(
			settings.RootFolder,
			template,
			metadata,
			true);
		Directory.CreateDirectory(destination);

		// EAC derives CUE, playlist, and log paths directly from these live
		// buffers. Point every output at the one concrete destination and keep
		// the filename convention track-only to avoid nesting it twice.
		suppressWorkflowFolderTemplate = true;
		ApplyTrackOnlyNamingSchemes();
		WriteEacPathBuffer(layout.StandardDirectoryPathVa, destination);
		WriteEacPathBuffer(layout.ActualPathVa, destination);
		Log(logPrefix + ": '" + destination + "'.");
	}

	private static Dictionary<string, string> ReadWorkflowFolderMetadata(IntPtr mainWindow)
	{
		return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "albumtitle", ReadChildControlText(mainWindow, 992) },
			{ "albumartist", ReadChildControlText(mainWindow, 993) },
			{ "artist", ReadChildControlText(mainWindow, 993) },
			{ "title", ReadChildControlText(mainWindow, 992) },
			{ "tracktitle", ReadChildControlText(mainWindow, 992) },
			{ "year", ReadChildControlText(mainWindow, 995) },
			{ "genre", ReadChildControlText(mainWindow, 994) },
			{ "comment", ReadChildControlText(mainWindow, 883) },
			{ "composer", ReadChildControlText(mainWindow, 880) },
			{ "albumcomposer", ReadChildControlText(mainWindow, 880) },
			{ "albuminterpret", ReadChildControlText(mainWindow, 997) },
			{ "cdnumber", ReadChildControlText(mainWindow, 881) },
			{ "totalcds", ReadChildControlText(mainWindow, 882) }
		};
	}

	private static void RestoreConfiguredOutputPath(string rootFolder)
	{
		string normalized = NormalizeRootFolder(rootFolder);
		WriteEacPathBuffer(layout.StandardDirectoryPathVa, normalized);
		WriteEacPathBuffer(layout.ActualPathVa, normalized);

		// EAC can persist its live path after extraction ends. Write the saved
		// values after restoring the live buffers so that both sources agree.
		using (RegistryKey extraction = Registry.CurrentUser.CreateSubKey(
			"Software\\AWSoftware\\EACU\\Extraction Options"))
		{
			if (extraction == null)
				throw new InvalidOperationException("EAC's extraction settings could not be opened.");
			extraction.SetValue("DirectorySpecification", normalized, RegistryValueKind.String);
		}
		using (RegistryKey startup = Registry.CurrentUser.CreateSubKey(
			"Software\\AWSoftware\\EACU\\StartUp Options"))
		{
			if (startup == null)
				throw new InvalidOperationException("EAC's startup settings could not be opened.");
			startup.SetValue("ActualPath", normalized, RegistryValueKind.String);
		}
	}

	private static void WriteEacPathBuffer(uint staticVa, string path)
	{
		const int Capacity = 512;
		string normalized = Path.GetFullPath(path).TrimEnd('\\') + "\\";
		if (normalized.Length >= Capacity)
			throw new PathTooLongException("The generated extraction path is too long for EAC.");
		char[] buffer = new char[Capacity];
		normalized.CopyTo(0, buffer, 0, normalized.Length);
		Marshal.Copy(buffer, 0, AddressFromStaticVa(staticVa), buffer.Length);
	}

	private static string ReadChildControlText(IntPtr parent, int controlId)
	{
		const uint WmGetText = 0x000D;
		string result = String.Empty;
		NativeMethods.EnumChildProc callback = delegate(IntPtr hwnd, IntPtr ignored)
		{
			if (NativeMethods.GetDlgCtrlID(hwnd) != controlId)
				return true;
			StringBuilder text = new StringBuilder(1024);
			NativeMethods.SendMessageTextW(hwnd, WmGetText, new IntPtr(text.Capacity), text);
			result = text.ToString();
			return false;
		};
		NativeMethods.EnumChildWindows(parent, callback, IntPtr.Zero);
		GC.KeepAlive(callback);
		return result;
	}

	private static IntPtr WorkflowGetMessage(int code, IntPtr wParam, IntPtr lParam)
	{
		try
		{
			if (code >= 0 && lParam != IntPtr.Zero)
			{
				NativeMethods.MSG mSG = (NativeMethods.MSG)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSG));
				InspectWorkflowMessage(mSG.hwnd, mSG.message, mSG.wParam, mSG.lParam, "queue");
			}
		}
		catch (Exception ex)
		{
			Log("100% log queued-message guard failed: " + ex.Message);
		}
		return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
	}

	private static void InspectWorkflowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, string source)
	{
		byte b = Marshal.ReadByte(AddressFromStaticVa(layout.ChainFlagVa));
		bool preparationActive = b == 2 || b == 3;
		bool outputSelectionPending =
			!preparationActive &&
			!ripSessionActive &&
			workflowAutoCloseFlagAddress != 0 &&
			Marshal.ReadByte(new IntPtr((int)workflowAutoCloseFlagAddress)) != 0;
		if (!preparationActive && !outputSelectionPending)
		{
			return;
		}
		bool flag = false;
		uint num = 0u;
		switch (message)
		{
		case 273u:
		{
			uint num2 = (uint)wParam.ToInt64();
			num = num2 & 0xFFFFu;
			flag = num == 2 || IsCancelControl(lParam);
			if (Interlocked.Increment(ref workflowMessageTraceCount) <= 64)
			{
				Log(
					"100% log " +
					(outputSelectionPending ? "output selection" : "stage " + b) +
					" saw WM_COMMAND 0x" + num.ToString("X") +
					", notify=0x" + ((num2 >> 16) & 0xFFFFu).ToString("X") +
					" on thread " + NativeMethods.GetCurrentThreadId() +
					" via " + source + ".");
			}
			break;
		}
		case 16u:
			flag = true;
			break;
		case 274u:
			num = (uint)(int)wParam.ToInt64() & 0xFFF0u;
			flag = num == 61536;
			break;
		case 514u:
			flag = IsCancelControl(hwnd);
			break;
		case 256u:
			num = (uint)wParam.ToInt64();
			flag = num == 27;
			break;
		}
		if (flag)
		{
			if (preparationActive)
				AbortCustomWorkflowIfActive(hwnd, message, num);
			else
				AbortPendingOutputSelection(hwnd, message, num);
		}
	}

	private static bool IsCancelControl(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
		{
			return false;
		}
		if (NativeMethods.GetDlgCtrlID(hwnd) == 2)
		{
			return true;
		}
		StringBuilder stringBuilder = new StringBuilder(64);
		NativeMethods.GetWindowTextW(hwnd, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString().Trim().Equals("Cancel", StringComparison.OrdinalIgnoreCase);
	}

	private static void AbortCustomWorkflowIfActive(IntPtr hwnd, uint message, uint command)
	{
		IntPtr ptr = AddressFromStaticVa(layout.ChainFlagVa);
		byte b = Marshal.ReadByte(ptr);
		if (b == 2 || b == 3)
		{
			Marshal.WriteByte(ptr, 0);
			if (workflowAutoCloseFlagAddress != 0)
			{
				Marshal.WriteByte(new IntPtr((int)workflowAutoCloseFlagAddress), 0);
			}
			if (workflowSelectionBackupAddress != 0)
			{
				byte[] array = new byte[TrackSelectionCount];
				Marshal.Copy(new IntPtr((int)workflowSelectionBackupAddress), array, 0, array.Length);
				Marshal.Copy(array, 0, AddressFromStaticVa(layout.TrackSelectionArrayVa), array.Length);
			}
			Log("Aborted 100% log chain at stage " + b + " before message 0x" + message.ToString("X") + ", command 0x" + command.ToString("X") + ", hwnd=0x" + hwnd.ToInt64().ToString("X8") + ".");
			RequestWorkflowFolderTemplateRestore();
		}
	}

	private static void AbortPendingOutputSelection(IntPtr hwnd, uint message, uint command)
	{
		if (ripSessionActive || workflowAutoCloseFlagAddress == 0)
			return;

		IntPtr autoCloseFlag = new IntPtr((int)workflowAutoCloseFlagAddress);
		if (Marshal.ReadByte(autoCloseFlag) == 0)
			return;

		Marshal.WriteByte(autoCloseFlag, 0);
		RequestWorkflowFolderTemplateRestore();
		Log(
			"Aborted pending 100% log extraction during output selection before message 0x" +
			message.ToString("X") + ", command 0x" + command.ToString("X") +
			", hwnd=0x" + hwnd.ToInt64().ToString("X8") + ".");
	}

	private static void WriteJumpPatch(uint staticVa, uint destination, int length)
	{
		long num = destination - (AddressFromStaticVa(staticVa).ToInt64() + 5);
		if (num < int.MinValue || num > int.MaxValue)
		{
			throw new InvalidOperationException("Workflow hook is outside rel32 range.");
		}
		byte[] array = RepeatByte(144, length);
		array[0] = 233;
		Buffer.BlockCopy(BitConverter.GetBytes((int)num), 0, array, 1, 4);
		WriteMemoryPatch(staticVa, array);
	}

	private static void WriteMemoryPatch(uint staticVa, byte[] patch)
	{
		IntPtr intPtr = AddressFromStaticVa(staticVa);
		uint oldProtection;
		if (!NativeMethods.VirtualProtect(intPtr, new UIntPtr((uint)patch.Length), 64u, out oldProtection))
		{
			throw new InvalidOperationException("VirtualProtect failed for 0x" + staticVa.ToString("X8") + ".");
		}
		try
		{
			Marshal.Copy(patch, 0, intPtr, patch.Length);
			NativeMethods.FlushInstructionCache(NativeMethods.GetCurrentProcess(), intPtr, new UIntPtr((uint)patch.Length));
		}
		finally
		{
			uint ignoredProtection;
			NativeMethods.VirtualProtect(intPtr, new UIntPtr((uint)patch.Length), oldProtection, out ignoredProtection);
		}
	}

	private static void RequireBytes(uint staticVa, byte[] expected, string description)
	{
		byte[] array = ReadBytes(staticVa, expected.Length);
		int num = FirstMismatch(array, expected);
		if (num >= 0)
		{
			throw new InvalidOperationException(description + " bytes differ at +0x" + num.ToString("X") + "; actual=" + ToHex(array) + ", expected=" + ToHex(expected));
		}
	}

	private static byte[] ReadBytes(uint staticVa, int length)
	{
		byte[] array = new byte[length];
		Marshal.Copy(AddressFromStaticVa(staticVa), array, 0, length);
		return array;
	}

	private static IntPtr ReadAbsolutePointer(uint staticVa)
	{
		return new IntPtr(Marshal.ReadInt32(AddressFromStaticVa(staticVa)));
	}

	private static uint RuntimeVa(uint staticVa)
	{
		return Pointer32(AddressFromStaticVa(staticVa));
	}

	private static IntPtr AddressFromStaticVa(uint staticVa)
	{
		long num = (long)staticVa - 4194304L;
		return new IntPtr(imageBase.ToInt64() + num);
	}

	private static uint Pointer32(IntPtr pointer)
	{
		return (uint)pointer.ToInt32();
	}

	private static byte[] Hex(string text)
	{
		string[] array = text.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		byte[] array2 = new byte[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array2[i] = Convert.ToByte(array[i], 16);
		}
		return array2;
	}

	private static byte[] RepeatByte(byte value, int count)
	{
		byte[] array = new byte[count];
		for (int i = 0; i < count; i++)
		{
			array[i] = value;
		}
		return array;
	}

    }
}
