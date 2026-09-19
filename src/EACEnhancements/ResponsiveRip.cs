using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AudioDataPlugIn
{
    internal static partial class EnhancementRuntime
    {
	private static void InstallCommandCompletionHook()
	{
		byte[] array = new byte[ExpectedCommandCompletionPrologue.Length];
		Marshal.Copy(commandCompletionAddress, array, 0, array.Length);
		int num = FirstMismatch(array, ExpectedCommandCompletionPrologue);
		if (num >= 0)
		{
			hookStatus = "disabled: unexpected command-completion prologue; mismatch=" + num + ", actual=" + ToHex(array) + ", expected=" + ToHex(ExpectedCommandCompletionPrologue);
			Log(hookStatus);
			return;
		}
		commandCompletionTrampoline = NativeMethods.VirtualAlloc(IntPtr.Zero, new UIntPtr(16u), 12288u, 64u);
		if (commandCompletionTrampoline == IntPtr.Zero)
		{
			throw new InvalidOperationException("VirtualAlloc failed with Win32 error " + Marshal.GetLastWin32Error() + ".");
		}
		Marshal.Copy(array, 0, commandCompletionTrampoline, 6);
		WriteRelativeJump(Add(commandCompletionTrampoline, 6), Add(commandCompletionAddress, 6), 5);
		originalCommandCompletion = (CommandCompletionDelegate)Marshal.GetDelegateForFunctionPointer(commandCompletionTrampoline, typeof(CommandCompletionDelegate));
		commandCompletionRelayEvent = NativeMethods.CreateEventW(
			IntPtr.Zero,
			true,
			false,
			null);
		if (commandCompletionRelayEvent == IntPtr.Zero)
		{
			throw new InvalidOperationException(
				"CreateEvent failed with Win32 error " +
				Marshal.GetLastWin32Error() + ".");
		}
		commandCompletionRelayEventPointer = Marshal.AllocHGlobal(4);
		Marshal.WriteInt32(
			commandCompletionRelayEventPointer,
			commandCompletionRelayEvent.ToInt32());
		hookedCommandCompletion = HookedCommandCompletion;
		IntPtr functionPointerForDelegate = Marshal.GetFunctionPointerForDelegate(hookedCommandCompletion);
		uint oldProtection;
		if (!NativeMethods.VirtualProtect(commandCompletionAddress, new UIntPtr(6u), 64u, out oldProtection))
		{
			throw new InvalidOperationException("VirtualProtect failed with Win32 error " + Marshal.GetLastWin32Error() + ".");
		}
		try
		{
			WriteRelativeJump(commandCompletionAddress, functionPointerForDelegate, 6);
			NativeMethods.FlushInstructionCache(NativeMethods.GetCurrentProcess(), commandCompletionAddress, new UIntPtr(6u));
		}
		finally
		{
			uint ignoredProtection;
			NativeMethods.VirtualProtect(commandCompletionAddress, new UIntPtr(6u), oldProtection, out ignoredProtection);
		}
		hookInstalled = true;
		hookStatus = "active at 0x" + commandCompletionAddress.ToInt64().ToString("X8") + ", trampoline 0x" + commandCompletionTrampoline.ToInt64().ToString("X8");
		Log("Responsive hook " + hookStatus + ".");
	}

	private static uint HookedCommandCompletion(IntPtr commandState, IntPtr eventHandlePointer)
	{
		IntPtr originalEventHandlePointer = eventHandlePointer;
		try
		{
			if (WaitForCommandWhilePumping(commandState, eventHandlePointer))
			{
				originalEventHandlePointer =
					commandCompletionRelayEventPointer;
			}
		}
		catch (Exception ex)
		{
			Log("Assisted command wait failed: " + ex);
		}
		// Do not dispatch queued input here.  The command has completed, but its
		// caller still owns live extraction state until this hook returns.  EAC's
		// native outer pump will process Cancel after that stack has unwound.
		uint result = originalCommandCompletion(
			commandState,
			originalEventHandlePointer);
		if (originalEventHandlePointer == commandCompletionRelayEventPointer)
		{
			// The original routine skips its wait (and ResetEvent) once
			// SRB_Status is already final, which is the usual case after an
			// assisted wait.  Clear the manual-reset relay ourselves so it never
			// carries a stale signal into the next command.
			NativeMethods.ResetEvent(commandCompletionRelayEvent);
		}
		return result;
	}

	// ASPI SRB_ExecSCSICmd: EAC issues every drive command through one global
	// SRB and passes its address to the completion routine this hook wraps.
	private const int SrbStatusOffset = 0x01;

	private enum AssistedWait
	{
		None,
		RipSession,
		AnalyzingDialog
	}

	private static bool WaitForCommandWhilePumping(
		IntPtr commandState,
		IntPtr eventHandlePointer)
	{
		uint currentThreadId = NativeMethods.GetCurrentThreadId();
		if (commandState == IntPtr.Zero || eventHandlePointer == IntPtr.Zero ||
			Marshal.ReadByte(Add(commandState, SrbStatusOffset)) != 0 ||
			ClassifyAssistedWait(commandState, currentThreadId) ==
				AssistedWait.None)
		{
			return false;
		}

		IntPtr eventHandle = new IntPtr(Marshal.ReadInt32(eventHandlePointer));
		if (eventHandle == IntPtr.Zero)
		{
			return false;
		}

		// EAC normally waits forever here.  Slow corrective reads can therefore
		// starve the rip dialog for seconds at a time, and cue sheet creation
		// blocks its gap-detection and UPC/ISRC dialogs once per command.  Drain
		// queued work so input cannot prevent Windows from generating low-priority
		// paint messages, but dispatch only what the wait kind allows.  Hold
		// everything else and repost it after the drive event completes;
		// dispatching input here can re-enter EAC's extraction state (for example
		// via Cancel) while the ASPI handler is still on the stack.
		IntPtr[] waitHandles = { eventHandle };
		List<NativeMethods.MSG> deferredMessages =
			new List<NativeMethods.MSG>();
		uint waitResult = NativeMethods.WAIT_TIMEOUT;
		try
		{
			do
			{
				waitResult = NativeMethods.MsgWaitForMultipleObjectsEx(
					1u,
					waitHandles,
					50u,
					NativeMethods.QS_ALLINPUT,
					NativeMethods.MWMO_INPUTAVAILABLE);
				if (waitResult == 1u)
				{
					waitResult = PumpOneMessage(
						commandState,
						eventHandle,
						deferredMessages);
				}
			}
			while (waitResult == NativeMethods.WAIT_TIMEOUT);
		}
		finally
		{
			if (deferredMessages.Count != 0)
			{
				DrainQueuedMessages(deferredMessages);
			}
			ReplayDeferredMessages(currentThreadId, deferredMessages);
		}

		if (waitResult != 0u)
		{
			return false;
		}

		// Waiting can consume an auto-reset event.  Give EAC's original cleanup
		// routine a private signaled relay instead of re-signaling the real event,
		// where another waiter could steal it before the original routine runs.
		if (!NativeMethods.SetEvent(commandCompletionRelayEvent))
		{
			int error = Marshal.GetLastWin32Error();
			// Relay failure is not expected, but restoring the real event preserves
			// EAC's original behavior and avoids turning diagnostics into a hang.
			NativeMethods.SetEvent(eventHandle);
			throw new InvalidOperationException(
				"Could not signal EAC's command relay event; Win32 error " +
				error + ".");
		}
		return true;
	}

	private static AssistedWait ClassifyAssistedWait(
		IntPtr commandState,
		uint currentThreadId)
	{
		if (ripSessionActive && ripSessionThreadId == (int)currentThreadId)
		{
			return AssistedWait.RipSession;
		}
		// Cue sheet creation runs gap detection and the UPC/ISRC scan from
		// modal "Analyzing" dialog procedures on EAC's UI thread outside any rip
		// session (and possibly after EndOfSession during finalization).  Assist
		// only while one of those known dialogs is up and this thread owns it.
		if (IsMainWindowThread(currentThreadId) &&
			FindAnalyzingDialog() != IntPtr.Zero)
		{
			return AssistedWait.AnalyzingDialog;
		}
		return AssistedWait.None;
	}

	private static bool IsMainWindowThread(uint threadId)
	{
		if (layout == null)
		{
			return false;
		}
		IntPtr mainWindow = ReadAbsolutePointer(layout.MainWindowGlobalVa);
		return mainWindow != IntPtr.Zero &&
			NativeMethods.IsWindow(mainWindow) &&
			NativeMethods.GetWindowThreadProcessId(mainWindow, IntPtr.Zero) ==
				threadId;
	}

	// Locates a known "Analyzing" dialog: the main window's most recently
	// active popup while the main window itself is disabled, whose dialog
	// procedure is one EAC uses for gap or UPC/ISRC detection.  EAC hands the
	// procedure straight to DialogBoxParamW, so DWLP_DLGPROC identifies it.
	// Returns zero for any other window, which disables the assist.
	private static IntPtr FindAnalyzingDialog()
	{
		IntPtr mainWindow = ReadAbsolutePointer(layout.MainWindowGlobalVa);
		if (mainWindow == IntPtr.Zero ||
			!NativeMethods.IsWindow(mainWindow) ||
			NativeMethods.IsWindowEnabled(mainWindow))
		{
			return IntPtr.Zero;
		}
		IntPtr popup = NativeMethods.GetLastActivePopup(mainWindow);
		if (popup == IntPtr.Zero || popup == mainWindow ||
			!NativeMethods.IsWindow(popup) ||
			!NativeMethods.IsWindowEnabled(popup))
		{
			return IntPtr.Zero;
		}
		uint dialogProc = (uint)NativeMethods.GetWindowLongW(
			popup,
			NativeMethods.DWLP_DLGPROC);
		if (IsAnalyzingDialogProc(layout.AnalyzingDialogProcVas, dialogProc))
		{
			return popup;
		}
		if (!unrecognizedModalDialogLogged)
		{
			unrecognizedModalDialogLogged = true;
			Log(
				"Drive command issued under modal dialog 0x" +
				popup.ToInt64().ToString("X8") + " with dialog procedure 0x" +
				dialogProc.ToString("X8") + "; not assisted.");
		}
		return IntPtr.Zero;
	}

	internal static bool IsAnalyzingDialogProc(uint[] knownProcs, uint dialogProc)
	{
		if (knownProcs == null || dialogProc == 0)
		{
			return false;
		}
		foreach (uint known in knownProcs)
		{
			if (known == dialogProc)
			{
				return true;
			}
		}
		return false;
	}

	private static uint PumpOneMessage(
		IntPtr commandState,
		IntPtr commandEvent,
		List<NativeMethods.MSG> deferredMessages)
	{
		if (!hookInstalled || insideAssistedPump || commandEvent == IntPtr.Zero)
		{
			return NativeMethods.WAIT_TIMEOUT;
		}
		uint currentThreadId = NativeMethods.GetCurrentThreadId();
		AssistedWait kind = ClassifyAssistedWait(commandState, currentThreadId);
		if (kind == AssistedWait.None)
		{
			return NativeMethods.WAIT_TIMEOUT;
		}
		IntPtr scanDialog = kind == AssistedWait.AnalyzingDialog
			? FindAnalyzingDialog()
			: IntPtr.Zero;
		bool modalLoopGuarded = scanDialog != IntPtr.Zero &&
			EnsureScanDialogModalLoopGuard(currentThreadId);
		insideAssistedPump = true;
		try
		{
			uint waitResult = NativeMethods.WaitForSingleObject(
				commandEvent,
				0u);
			if (waitResult != NativeMethods.WAIT_TIMEOUT)
			{
				return waitResult;
			}
			NativeMethods.MSG message;
			if (NativeMethods.PeekMessageW(
				out message,
				IntPtr.Zero,
				0u,
				0u,
				NativeMethods.PM_REMOVE))
			{
				if (ShouldDispatchAssistedMessage(
					message.message,
					message.hwnd,
					message.wParam,
					kind == AssistedWait.RipSession,
					scanDialog,
					modalLoopGuarded))
				{
					DispatchAssistedMessage(
						ref message,
						kind,
						scanDialog,
						deferredMessages);
				}
				else
				{
					DeferMessage(deferredMessages, message);
				}
			}
			if (kind == AssistedWait.RipSession)
			{
				assistedPumpCount++;
				if (!firstAssistLogged)
				{
					firstAssistLogged = true;
					Log("Responsive assist activated on thread " + currentThreadId + ".");
				}
			}
			else
			{
				analyzingDialogAssistCount++;
				if (!analyzingDialogAssistLogged)
				{
					analyzingDialogAssistLogged = true;
					Log(
						"Analyzing dialog assist activated on thread " +
						currentThreadId + "; dialog 0x" +
						scanDialog.ToInt64().ToString("X8") + ".");
				}
			}
			return NativeMethods.WaitForSingleObject(commandEvent, 0u);
		}
		finally
		{
			insideAssistedPump = false;
		}
	}

	internal static bool IsSafeAssistedMessage(uint message)
	{
		// EAC's rip dialog advances several visual fields from its UI timer.
		// Mouse/button/command/key messages remain deferred, so the timer cannot
		// synthesize the unsafe Cancel reentrancy that the input messages caused.
		return message == NativeMethods.WM_PAINT ||
			message == NativeMethods.WM_TIMER;
	}

	private static void DispatchAssistedMessage(
		ref NativeMethods.MSG message,
		AssistedWait kind,
		IntPtr scanDialog,
		List<NativeMethods.MSG> deferredMessages)
	{
		if (kind != AssistedWait.AnalyzingDialog)
		{
			NativeMethods.DispatchMessageW(ref message);
			return;
		}
		// A caption press dispatched here runs DefWindowProc's move loop on top
		// of this wait.  That loop retrieves and dispatches every thread message
		// itself, so the WH_GETMESSAGE guard filters it with the same policy
		// while the dispatch is on the stack.
		scanDialogModalLoopDialog = scanDialog;
		scanDialogModalLoopDeferred = deferredMessages;
		scanDialogModalLoopGuardActive = true;
		try
		{
			NativeMethods.DispatchMessageW(ref message);
		}
		finally
		{
			scanDialogModalLoopGuardActive = false;
			scanDialogModalLoopDeferred = null;
			scanDialogModalLoopDialog = IntPtr.Zero;
		}
	}

	private static bool EnsureScanDialogModalLoopGuard(uint threadId)
	{
		if (scanDialogModalLoopGuardHook != IntPtr.Zero)
		{
			return scanDialogModalLoopGuardThreadId == (int)threadId;
		}
		if (scanDialogModalLoopGuardAttempted)
		{
			return false;
		}
		scanDialogModalLoopGuardAttempted = true;
		IntPtr hook = NativeMethods.SetWindowsHookExW(
			NativeMethods.WH_GETMESSAGE,
			Marshal.GetFunctionPointerForDelegate(
				scanDialogModalLoopGuardDelegate),
			IntPtr.Zero,
			threadId);
		if (hook == IntPtr.Zero)
		{
			Log(
				"Scan dialog modal loop guard installation failed with Win32 " +
				"error " + Marshal.GetLastWin32Error() +
				"; the dialog cannot be dragged during UPC/ISRC reads.");
			return false;
		}
		scanDialogModalLoopGuardHook = hook;
		scanDialogModalLoopGuardThreadId = (int)threadId;
		Log("Scan dialog modal loop guard active on thread " + threadId + ".");
		return true;
	}

	private static IntPtr ScanDialogModalLoopGuard(
		int code,
		IntPtr wParam,
		IntPtr lParam)
	{
		try
		{
			if (code >= 0 && lParam != IntPtr.Zero &&
				scanDialogModalLoopGuardActive &&
				scanDialogModalLoopDeferred != null &&
				(wParam.ToInt64() & NativeMethods.PM_REMOVE) != 0)
			{
				NativeMethods.MSG message = (NativeMethods.MSG)
					Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSG));
				if (!IsSafeInsideScanDialogModalLoop(
					message.message,
					message.hwnd,
					scanDialogModalLoopDialog))
				{
					DeferMessage(scanDialogModalLoopDeferred, message);
					if (!scanDialogModalLoopGuardLogged)
					{
						scanDialogModalLoopGuardLogged = true;
						Log(
							"Scan dialog modal loop guard held message 0x" +
							message.message.ToString("X") + " for window 0x" +
							message.hwnd.ToInt64().ToString("X8") + ".");
					}
					message.message = NativeMethods.WM_NULL;
					message.wParam = IntPtr.Zero;
					message.lParam = IntPtr.Zero;
					Marshal.StructureToPtr(message, lParam, false);
				}
			}
		}
		catch (Exception ex)
		{
			Log("Scan dialog modal loop guard failed: " + ex.Message);
		}
		return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
	}

	internal static bool IsSafeInsideScanDialogModalLoop(
		uint message,
		IntPtr hwnd,
		IntPtr scanDialog)
	{
		// The move loop consumes mouse and keyboard input itself (Escape
		// cancels the drag) and dispatches everything else.  Paint may go
		// anywhere; WM_TIMER and posted messages for other windows are held.
		if (message == NativeMethods.WM_PAINT ||
			message == NativeMethods.WM_NULL ||
			message == NativeMethods.WM_QUIT)
		{
			return true;
		}
		if (scanDialog == IntPtr.Zero || hwnd == IntPtr.Zero ||
			(hwnd != scanDialog && !NativeMethods.IsChild(scanDialog, hwnd)))
		{
			return false;
		}
		return IsClientMouseMessage(message) ||
			message == NativeMethods.WM_KEYDOWN ||
			message == NativeMethods.WM_KEYUP ||
			message == NativeMethods.WM_CHAR;
	}

	internal static bool ShouldDispatchAssistedMessage(
		uint message,
		IntPtr hwnd,
		IntPtr wParam,
		bool ripSession,
		IntPtr scanDialog,
		bool modalLoopGuarded)
	{
		if (ripSession)
		{
			return IsSafeAssistedMessage(message);
		}
		if (message == NativeMethods.WM_PAINT)
		{
			return true;
		}
		if (scanDialog == IntPtr.Zero || hwnd == IntPtr.Zero)
		{
			return false;
		}
		// A caption press starts DefWindowProc's move loop.  Allow it only when
		// the WH_GETMESSAGE guard is in place to filter that nested loop.
		if (message == NativeMethods.WM_NCLBUTTONDOWN)
		{
			return modalLoopGuarded && hwnd == scanDialog &&
				wParam.ToInt64() == NativeMethods.HTCAPTION;
		}
		// WM_TIMER must stay deferred here.  EAC's main window polls the drive
		// from its timer whenever no rip is in progress (a TOC read every tenth
		// tick), and issuing that through EAC's single global SRB while the
		// UPC/ISRC command is still in flight corrupts both results.  The rip
		// session is exempt only because ripping sets the busy flag that skips
		// the poll.
		//
		// A single gap-detection or UPC/ISRC command can take the drive hundreds
		// of milliseconds, so deferring input for its duration makes the dialog
		// visibly stutter.  Unlike the rip dialog, these window procedures only
		// record Cancel in a flag that EAC checks between commands; they never
		// re-enter the drive or end the dialog.  Service client-area mouse traffic aimed at that
		// dialog and its controls: that covers hover, cursor updates and the
		// Cancel button, whose click sends WM_COMMAND synchronously.  Nothing
		// else is dispatched.  Other non-client presses and Alt/F10 keys would
		// start DefWindowProc's size or menu loops, and messages for other
		// windows (posted main-window commands, the rip dialog, thread
		// messages) must wait for the command to finish.
		return IsClientMouseMessage(message) &&
			(hwnd == scanDialog || NativeMethods.IsChild(scanDialog, hwnd));
	}

	internal static bool IsClientMouseMessage(uint message)
	{
		// WM_MOUSEMOVE through WM_MBUTTONDBLCLK; wheel and X buttons are left
		// out because DefWindowProc forwards them to the owner as WM_APPCOMMAND.
		return (message >= NativeMethods.WM_MOUSEMOVE &&
				message <= NativeMethods.WM_MBUTTONDBLCLK) ||
			message == NativeMethods.WM_NCMOUSEMOVE ||
			message == NativeMethods.WM_MOUSEHOVER ||
			message == NativeMethods.WM_MOUSELEAVE;
	}

	// Deferred messages are reposted behind whatever is still queued, so a
	// WM_LBUTTONDOWN removed just before the drive event fired would otherwise
	// land after its WM_LBUTTONUP.  Move the rest of the queue into the same
	// list first so the replay preserves the original order.  Retrieval order
	// is posted, then input, then paint, then timer, so the drain stops at the
	// first WM_PAINT or WM_TIMER: WM_PAINT is regenerated while the region stays
	// invalid and cannot be removed, and WM_TIMER stays coalesced in the queue.
	private static void DrainQueuedMessages(
		List<NativeMethods.MSG> deferredMessages)
	{
		for (int i = 0; i < 1024; i++)
		{
			NativeMethods.MSG head;
			if (!NativeMethods.PeekMessageW(
				out head,
				IntPtr.Zero,
				0u,
				0u,
				NativeMethods.PM_NOREMOVE) ||
				head.message == NativeMethods.WM_PAINT ||
				head.message == NativeMethods.WM_TIMER)
			{
				return;
			}
			NativeMethods.MSG message;
			if (!NativeMethods.PeekMessageW(
				out message,
				head.hwnd,
				head.message,
				head.message,
				NativeMethods.PM_REMOVE))
			{
				return;
			}
			DeferMessage(deferredMessages, message);
		}
	}

	private static void DeferMessage(
		List<NativeMethods.MSG> deferredMessages,
		NativeMethods.MSG message)
	{
		if (message.message == NativeMethods.WM_MOUSEMOVE &&
			deferredMessages.Count > 0)
		{
			int lastIndex = deferredMessages.Count - 1;
			NativeMethods.MSG previous = deferredMessages[lastIndex];
			if (previous.message == message.message &&
				previous.hwnd == message.hwnd)
			{
				deferredMessages[lastIndex] = message;
				return;
			}
		}
		if (message.message == NativeMethods.WM_TIMER)
		{
			// Windows coalesces a timer into one queued WM_TIMER no matter how
			// long the thread is blocked.  Replaying one per removed tick would
			// instead burst EAC's main-window timer handler after each command.
			foreach (NativeMethods.MSG deferred in deferredMessages)
			{
				if (deferred.message == message.message &&
					deferred.hwnd == message.hwnd &&
					deferred.wParam == message.wParam)
				{
					return;
				}
			}
		}
		deferredMessages.Add(message);
	}

	private static void ReplayDeferredMessages(
		uint threadId,
		IEnumerable<NativeMethods.MSG> deferredMessages)
	{
		int failed = 0;
		foreach (NativeMethods.MSG message in deferredMessages)
		{
			bool posted = message.hwnd != IntPtr.Zero
				? NativeMethods.PostMessageW(
					message.hwnd,
					message.message,
					message.wParam,
					message.lParam)
				: NativeMethods.PostThreadMessageW(
					threadId,
					message.message,
					message.wParam,
					message.lParam);
			if (!posted)
				failed++;
		}
		if (failed != 0)
			Log("Could not replay " + failed + " deferred rip message(s).");
	}

	private static void WriteRelativeJump(IntPtr source, IntPtr destination, int patchLength)
	{
		if (patchLength < 5)
		{
			throw new ArgumentOutOfRangeException("patchLength");
		}
		long num = destination.ToInt64() - (source.ToInt64() + 5);
		if (num < int.MinValue || num > int.MaxValue)
		{
			throw new InvalidOperationException("Hook destination is outside rel32 range.");
		}
		byte[] array = new byte[patchLength];
		array[0] = 233;
		byte[] bytes = BitConverter.GetBytes((int)num);
		Buffer.BlockCopy(bytes, 0, array, 1, bytes.Length);
		for (int i = 5; i < array.Length; i++)
		{
			array[i] = 144;
		}
		Marshal.Copy(array, 0, source, array.Length);
	}

	private static IntPtr Add(IntPtr address, int offset)
	{
		return new IntPtr(address.ToInt64() + offset);
	}

	private static int FirstMismatch(byte[] left, byte[] right)
	{
		if (left.Length != right.Length)
		{
			return Math.Min(left.Length, right.Length);
		}
		for (int i = 0; i < left.Length; i++)
		{
			if (left[i] != right[i])
			{
				return i;
			}
		}
		return -1;
	}

	private static string ToHex(byte[] bytes)
	{
		return BitConverter.ToString(bytes).Replace('-', ' ');
	}

    }
}
