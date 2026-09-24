using System;
using System.Runtime.InteropServices;

namespace AudioDataPlugIn
{
    internal static class AdditionalWorkflowMenuTests
    {
        private const uint HtoaCommand = 0xA319;
        private const uint RegularCommand = 0x0312;
        private const uint RangeCommand = 0xA31D;
        private const uint SettingsCommand = 0xA312;

        private static int Main()
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
                throw new InvalidOperationException("Could not create the Action-menu fixture.");
            try
            {
                AddCommand(menu, HtoaCommand);
                AddCommand(menu, RegularCommand);
                AddCommand(menu, SettingsCommand);

                if (!EnhancementRuntime.UpdateAdditionalWorkflowMenuItems(menu, true))
                    throw new Exception("Enabling the range workflow did not update the menu.");
                AssertEnabledLayout(menu);
                if (EnhancementRuntime.UpdateAdditionalWorkflowMenuItems(menu, true))
                    throw new Exception("Enabling twice duplicated the range workflow.");

                if (!EnhancementRuntime.UpdateAdditionalWorkflowMenuItems(menu, false))
                    throw new Exception("Disabling the range workflow did not update the menu.");
                AssertDisabledLayout(menu);
                if (EnhancementRuntime.UpdateAdditionalWorkflowMenuItems(menu, false))
                    throw new Exception("Disabling twice changed the menu.");
            }
            finally
            {
                DestroyMenu(menu);
            }
            Console.WriteLine("Additional workflow menu tests passed.");
            return 0;
        }

        private static void AddCommand(IntPtr menu, uint command)
        {
            if (!NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING,
                new UIntPtr(command), command.ToString("X")))
                throw new InvalidOperationException("Could not populate the Action-menu fixture.");
        }

        private static void AssertEnabledLayout(IntPtr menu)
        {
            if (NativeMethods.GetMenuItemCount(menu) != 6 ||
                NativeMethods.GetMenuItemID(menu, 0) != HtoaCommand ||
                NativeMethods.GetMenuItemID(menu, 1) != RegularCommand ||
                !IsSeparator(menu, 2) ||
                NativeMethods.GetMenuItemID(menu, 3) != RangeCommand ||
                !IsSeparator(menu, 4) ||
                NativeMethods.GetMenuItemID(menu, 5) != SettingsCommand)
                throw new Exception("The range workflow is not enclosed by two dividers.");
        }

        private static void AssertDisabledLayout(IntPtr menu)
        {
            if (NativeMethods.GetMenuItemCount(menu) != 3 ||
                NativeMethods.GetMenuItemID(menu, 0) != HtoaCommand ||
                NativeMethods.GetMenuItemID(menu, 1) != RegularCommand ||
                NativeMethods.GetMenuItemID(menu, 2) != SettingsCommand)
                throw new Exception("Disabling left a workflow item or divider in the menu.");
        }

        private static bool IsSeparator(IntPtr menu, uint position)
        {
            uint state = NativeMethods.GetMenuState(menu, position,
                NativeMethods.MF_BYPOSITION);
            return state != uint.MaxValue &&
                (state & NativeMethods.MF_SEPARATOR) != 0;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(IntPtr menu);
    }
}
