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
		return originalCommandCompletion(
			commandState,
			originalEventHandlePointer);
	}

	private static bool WaitForCommandWhilePumping(
		IntPtr commandState,
		IntPtr eventHandlePointer)
	{
		uint currentThreadId = NativeMethods.GetCurrentThreadId();
		if (!ripSessionActive ||
			ripSessionThreadId != (int)currentThreadId ||
			commandState == IntPtr.Zero || eventHandlePointer == IntPtr.Zero ||
			Marshal.ReadByte(Add(commandState, 1)) != 0)
		{
			return false;
		}

		IntPtr eventHandle = new IntPtr(Marshal.ReadInt32(eventHandlePointer));
		if (eventHandle == IntPtr.Zero)
		{
			return false;
		}

		// EAC normally waits forever here.  Slow corrective reads can therefore
		// starve the rip dialog for seconds at a time.  Drain queued work so input
		// cannot prevent Windows from generating low-priority paint messages, but
		// dispatch only paint.  Hold everything else and repost it after the drive
		// event completes; dispatching input here can re-enter EAC's extraction state
		// (for example via Cancel) while the ASPI handler is still on the stack.
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
						eventHandle,
						deferredMessages);
				}
			}
			while (waitResult == NativeMethods.WAIT_TIMEOUT);
		}
		finally
		{
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

	private static uint PumpOneMessage(
		IntPtr commandEvent,
		List<NativeMethods.MSG> deferredMessages)
	{
		if (!hookInstalled || insideAssistedPump || commandEvent == IntPtr.Zero)
		{
			return NativeMethods.WAIT_TIMEOUT;
		}
		uint currentThreadId = NativeMethods.GetCurrentThreadId();
		if (!ripSessionActive || ripSessionThreadId != (int)currentThreadId)
		{
			return NativeMethods.WAIT_TIMEOUT;
		}
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
				if (IsSafeAssistedMessage(message.message))
				{
					NativeMethods.DispatchMessageW(ref message);
				}
				else
				{
					DeferMessage(deferredMessages, message);
				}
			}
			assistedPumpCount++;
			if (!firstAssistLogged)
			{
				firstAssistLogged = true;
				Log("Responsive assist activated on thread " + currentThreadId + ".");
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
