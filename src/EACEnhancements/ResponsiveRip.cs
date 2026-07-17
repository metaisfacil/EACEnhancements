using System;
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
		uint result = originalCommandCompletion(commandState, eventHandlePointer);
		try
		{
			MaybePumpMessages();
		}
		catch (Exception ex)
		{
			Log("Assisted message pump failed: " + ex);
		}
		return result;
	}

	private static void MaybePumpMessages()
	{
		if (!hookInstalled || insideAssistedPump)
		{
			return;
		}
		uint currentThreadId = NativeMethods.GetCurrentThreadId();
		IntPtr intPtr = ReadRipDialogHwnd();
		if (intPtr != IntPtr.Zero && NativeMethods.IsWindow(intPtr))
		{
			uint windowThreadProcessId = NativeMethods.GetWindowThreadProcessId(intPtr, IntPtr.Zero);
			if (windowThreadProcessId != currentThreadId)
			{
				return;
			}
		}
		else
		{
			if (!ripSessionActive || ripSessionThreadId != (int)currentThreadId)
			{
				return;
			}
			intPtr = IntPtr.Zero;
		}
		int tickCount = Environment.TickCount;
		if (tickCount - lastPumpTick < 50)
		{
			return;
		}
		insideAssistedPump = true;
		try
		{
			for (int i = 0; i < 256; i++)
			{
				NativeMethods.MSG message;
				if (!NativeMethods.PeekMessageW(out message, IntPtr.Zero, 0u, 0u, 1u))
				{
					break;
				}
				if (intPtr == IntPtr.Zero || !NativeMethods.IsDialogMessageW(intPtr, ref message))
				{
					NativeMethods.TranslateMessage(ref message);
					NativeMethods.DispatchMessageW(ref message);
				}
			}
			lastPumpTick = Environment.TickCount;
			assistedPumpCount++;
			if (!firstAssistLogged)
			{
				firstAssistLogged = true;
				Log("Responsive assist activated on thread " + currentThreadId + ", dialog=0x" + intPtr.ToInt64().ToString("X8") + ".");
			}
		}
		finally
		{
			insideAssistedPump = false;
		}
	}

	private static IntPtr ReadRipDialogHwnd()
	{
		try
		{
			int value = Marshal.ReadInt32(Add(imageBase, layout.RipDialogHwndRva));
			return new IntPtr(value);
		}
		catch
		{
			return IntPtr.Zero;
		}
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
