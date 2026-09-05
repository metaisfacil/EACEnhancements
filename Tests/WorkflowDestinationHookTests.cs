using System;
using System.Runtime.InteropServices;

namespace AudioDataPlugIn
{
    internal static class WorkflowDestinationHookTests
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RunHook();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint freeType);

        private static int Main()
        {
            foreach (EacVersionLayout layout in EacVersionLayout.KnownLayouts)
            {
                byte[] original = layout.ExpectedWaveformDecision;
                if (original.Length != 9 || original[0] != 0x83 ||
                    original[1] != 0x3D || original[6] != 1 || original[7] != 0x75 ||
                    BitConverter.ToUInt32(original, 2) != layout.OutputPathModeVa ||
                    layout.WaveformSaveResumeVa != layout.WaveformSaveHookVa + 9 ||
                    layout.WaveformSaveDefaultVa != layout.WaveformSaveHookVa + 9 + original[8])
                    throw new Exception(layout.Name + " displaced destination branch does not match stock EAC.");
            }

            // Execute the production x86 payload against private memory. Each
            // EAC continuation is replaced by a return stub identifying the path.
            IntPtr memory = NativeMethods.VirtualAlloc(IntPtr.Zero, new UIntPtr(4096u), 0x3000u, 0x40u);
            if (memory == IntPtr.Zero)
                throw new Exception("Could not allocate the destination-hook test payload.");
            try
            {
                uint start = unchecked((uint)memory.ToInt32());
                uint workflowFlag = start + 512;
                uint pathMode = start + 516;
                uint defaultPath = start + 256;
                uint promptPath = start + 272;
                X86CodeBuilder code = new X86CodeBuilder(memory);
                EnhancementRuntime.EmitWaveformSaveHook(code, workflowFlag, pathMode, defaultPath, promptPath);
                byte[] payload = code.ToArray();
                Marshal.Copy(payload, 0, memory, payload.Length);
                Marshal.Copy(new byte[] { 0xB8, 0, 0, 0, 0, 0xC3 }, 0, IntPtr.Add(memory, 256), 6);
                Marshal.Copy(new byte[] { 0xB8, 1, 0, 0, 0, 0xC3 }, 0, IntPtr.Add(memory, 272), 6);
                NativeMethods.FlushInstructionCache(NativeMethods.GetCurrentProcess(), memory, new UIntPtr(4096u));
                RunHook run = (RunHook)Marshal.GetDelegateForFunctionPointer(memory, typeof(RunHook));
                for (byte workflow = 0; workflow <= 1; workflow++)
                {
                    foreach (int mode in new[] { 0, 1, 2, -1 })
                    {
                        Marshal.WriteByte(memory, 512, workflow);
                        Marshal.WriteInt32(memory, 516, mode);
                        int expected = workflow == 0 && mode == 1 ? 1 : 0;
                        if (run() != expected)
                            throw new Exception("Wrong destination path: workflow=" + workflow + ", mode=" + mode);
                        if (Marshal.ReadByte(memory, 512) != workflow || Marshal.ReadInt32(memory, 516) != mode)
                            throw new Exception("The hook changed EAC's destination mode or workflow state.");
                    }
                }
            }
            finally
            {
                VirtualFree(memory, UIntPtr.Zero, 0x8000u);
            }
            Console.WriteLine("Workflow destination x86 hook tests passed.");
            return 0;
        }
    }
}
