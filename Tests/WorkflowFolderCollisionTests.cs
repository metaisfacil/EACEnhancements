using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace AudioDataPlugIn
{
    internal static class WorkflowFolderCollisionTests
    {
        [STAThread]
        private static int Main()
        {
            const string folderName = "Artist - Album (2004)";
            string expectedMessage =
                "A folder with the name \"Artist - Album (2004)\" already exists. You may overwrite if you choose, but this could result in data loss where tracks share the same titles. If you're not sure what to do, select a different folder.\r\n\r\n" +
                "Please decide how to continue.";
            AssertEqual(
                WorkflowFolderCollisionDialog.FormatMessage(folderName),
                expectedMessage);

            using (WorkflowFolderCollisionDialog dialog =
                new WorkflowFolderCollisionDialog(folderName))
            {
                List<string> buttons = new List<string>();
                CollectButtonText(dialog.Controls, buttons);
                AssertEqual(String.Join("|", buttons.ToArray()),
                    "Cancel|Select Folder|Overwrite");
                if (dialog.Choice != WorkflowFolderCollisionChoice.Cancel)
                    throw new Exception("Closing the collision dialog must default to cancellation.");
            }

            string destination = Path.Combine(
                Path.GetFullPath("C:\\EAC Rips"),
                "Artist",
                folderName);
            string parentFolder;
            string generatedFolderName;
            WorkflowFolderPicker.GetInitialSelection(
                destination,
                out parentFolder,
                out generatedFolderName);
            AssertEqual(parentFolder, Path.GetDirectoryName(destination));
            AssertEqual(generatedFolderName, folderName);

            // Exercise the real Windows common-dialog configuration without
            // showing it. In particular, this catches invalid option/class
            // combinations that COM reports as E_INVALIDARG.
            WorkflowFolderPicker.ValidateConfiguration(
                Path.GetTempPath(),
                folderName);

            Console.WriteLine("Workflow folder collision tests passed.");
            return 0;
        }

        private static void CollectButtonText(
            Control.ControlCollection controls,
            ICollection<string> buttons)
        {
            foreach (Control control in controls)
            {
                Button button = control as Button;
                if (button != null)
                    buttons.Add(button.Text);
                CollectButtonText(control.Controls, buttons);
            }
        }

        private static void AssertEqual(string actual, string expected)
        {
            if (!String.Equals(actual, expected, StringComparison.Ordinal))
                throw new Exception("Expected '" + expected + "' but got '" + actual + "'.");
        }
    }
}
