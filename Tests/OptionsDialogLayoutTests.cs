using System;
using System.Windows.Forms;

namespace AudioDataPlugIn
{
    internal static class OptionsDialogLayoutTests
    {
        [STAThread]
        private static void Main()
        {
            OutputTemplateSettings settings = new OutputTemplateSettings(
                @"C:\EAC\",
                "%albumartist% - %albumtitle%",
                true,
                true,
                true,
                false);
            using (OutputTemplateDialog dialog = new OutputTemplateDialog(settings, IntPtr.Zero))
            {
                dialog.CreateControl();
                dialog.PerformLayout();
                TableLayoutPanel layout = dialog.Controls[0] as TableLayoutPanel;
                if (layout == null || layout.Dock != DockStyle.Fill)
                    throw new Exception("The options layout does not fill the dialog.");
                CheckBox folderOption = FindCheckBox(
                    dialog,
                    "Create new folders for 100% log rips following folder template");
                if (folderOption == null || !folderOption.Checked)
                    throw new Exception("The folder-creation option is not enabled by default.");
                CheckBox setupAlertOption = FindCheckBox(
                    dialog,
                    "Show an alert if EAC is misconfigured before starting 100% log workflow");
                if (setupAlertOption == null || !setupAlertOption.Checked)
                    throw new Exception("The workflow setup alert is not enabled by default.");
                CheckBox loggingOption = FindCheckBox(
                    dialog,
                    "Enable EAC Enhancements diagnostic logging");
                if (loggingOption == null || loggingOption.Checked)
                    throw new Exception("Diagnostic logging is not disabled by default.");
                dialog.Show();
                Application.DoEvents();
                Button save = FindButton(dialog, "Save");
                Button cancel = FindButton(dialog, "Cancel");
                Button setupCheck = FindButton(dialog, "Check 100% Log Setup...");
                Button updateCheck = FindButton(dialog, "Check for Updates...");
                if (updateCheck == null)
                    throw new Exception("The manual update-check button is missing.");
                if (setupCheck == null || updateCheck.Left - setupCheck.Right != 8)
                    throw new Exception("The setup/update-check gap is not eight pixels.");
                if (save == null || cancel == null || cancel.Left - save.Right != 8)
                    throw new Exception("The Save/Cancel gap is not eight pixels.");
                int setupTop = setupCheck == null
                    ? -1
                    : setupCheck.PointToScreen(System.Drawing.Point.Empty).Y;
                int saveTop = save.PointToScreen(System.Drawing.Point.Empty).Y;
                if (setupTop != saveTop)
                    throw new Exception(
                        "The setup-check button is not aligned with Save and Cancel " +
                        "(setup=" + setupTop + ", save=" + saveTop + ").");
                int updateTop = updateCheck.PointToScreen(System.Drawing.Point.Empty).Y;
                if (updateTop != saveTop)
                    throw new Exception("The update-check button is not aligned with the bottom row.");
                dialog.Close();
            }

            using (EacSetupAuditDialog dialog = new EacSetupAuditDialog(new EacSetupAuditResult()))
            {
                if (dialog.ShowIcon)
                    throw new Exception("The setup report displays a default title-bar icon.");
                dialog.Show();
                Application.DoEvents();
                if (!(dialog.ActiveControl is Button))
                    throw new Exception("The setup report textbox receives initial focus.");
                TextBox report = FindTextBox(dialog);
                if (report == null || !report.ReadOnly || !report.TabStop)
                    throw new Exception("The setup report is not selectable and copyable.");
                dialog.Close();
            }
            Console.WriteLine("Dialog layout and focus tests passed.");
        }

        private static CheckBox FindCheckBox(Control parent, string text)
        {
            foreach (Control child in parent.Controls)
            {
                CheckBox checkBox = child as CheckBox;
                if (checkBox != null && checkBox.Text == text)
                    return checkBox;
                CheckBox nested = FindCheckBox(child, text);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private static TextBox FindTextBox(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                TextBox textBox = child as TextBox;
                if (textBox != null)
                    return textBox;
                TextBox nested = FindTextBox(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private static Button FindButton(Control parent, string text)
        {
            foreach (Control child in parent.Controls)
            {
                Button button = child as Button;
                if (button != null && button.Text == text)
                    return button;
                Button nested = FindButton(child, text);
                if (nested != null)
                    return nested;
            }
            return null;
        }
    }
}
