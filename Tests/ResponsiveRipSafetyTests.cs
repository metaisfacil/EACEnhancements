using System;

namespace AudioDataPlugIn
{
    internal static class ResponsiveRipSafetyTests
    {
        private static int Main()
        {
            AssertSafe(NativeMethods.WM_PAINT, true);
            AssertSafe(NativeMethods.WM_TIMER, true);
            AssertSafe(NativeMethods.WM_COMMAND, false);
            AssertSafe(NativeMethods.WM_CLOSE, false);
            AssertSafe(NativeMethods.WM_KEYDOWN, false);
            AssertSafe(NativeMethods.WM_LBUTTONUP, false);

            Console.WriteLine("Responsive rip reentrancy safety tests passed.");
            return 0;
        }

        private static void AssertSafe(uint message, bool expected)
        {
            bool actual = EnhancementRuntime.IsSafeAssistedMessage(message);
            if (actual != expected)
            {
                throw new Exception(
                    "Unexpected assisted-pump policy for message 0x" +
                    message.ToString("X") + ".");
            }
        }
    }
}
