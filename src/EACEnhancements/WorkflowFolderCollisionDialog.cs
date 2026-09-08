using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AudioDataPlugIn
{
    internal enum WorkflowFolderCollisionChoice
    {
        Cancel,
        Overwrite,
        SelectFolder
    }

    internal sealed class WorkflowFolderCollisionDialog : Form
    {
        internal const string MessageTemplate =
            "A folder with the name \"{0}\" already exists. You may overwrite if you choose, but this could result in data loss where tracks share the same titles. If you're not sure what to do, select a different folder.\r\n\r\n" +
            "Please decide how to continue.";

        internal WorkflowFolderCollisionChoice Choice { get; private set; }

        internal WorkflowFolderCollisionDialog(string ripFolderName)
        {
            Choice = WorkflowFolderCollisionChoice.Cancel;
            Text = "Folder Already Exists";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(590, 190);
            Padding = new Padding(16);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            Label message = new Label
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.TopLeft,
                Text = FormatMessage(ripFolderName)
            };

            Button overwrite = CreateButton("Overwrite", 88);
            overwrite.Click += delegate
            {
                Choice = WorkflowFolderCollisionChoice.Overwrite;
                DialogResult = DialogResult.OK;
                Close();
            };

            Button selectFolder = CreateButton("Select Folder", 102);
            selectFolder.Click += delegate
            {
                Choice = WorkflowFolderCollisionChoice.SelectFolder;
                DialogResult = DialogResult.OK;
                Close();
            };

            Button cancel = CreateButton("Cancel", 75);
            cancel.DialogResult = DialogResult.Cancel;

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(selectFolder);
            buttons.Controls.Add(overwrite);

            layout.Controls.Add(message, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            Controls.Add(layout);
            AcceptButton = selectFolder;
            CancelButton = cancel;
            ActiveControl = selectFolder;
        }

        internal static string FormatMessage(string ripFolderName)
        {
            return String.Format(MessageTemplate, ripFolderName);
        }

        private static Button CreateButton(string text, int width)
        {
            return new Button
            {
                Margin = new Padding(8, 0, 0, 0),
                Size = new Size(width, 28),
                Text = text,
                UseVisualStyleBackColor = true
            };
        }
    }

    internal static class WorkflowFolderPicker
    {
        private const uint FosForceFileSystem = 0x00000040;
        private const uint FosNoTestFileCreate = 0x00010000;
        private const uint SigdnFileSystemPath = 0x80058000;
        private const int CancelledHresult = unchecked((int)0x800704C7);
        private static readonly Guid FileSaveDialogClassId =
            new Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");

        internal static bool TrySelect(
            IntPtr ownerWindow,
            string parentFolder,
            string generatedFolderName,
            out string selectedFolder)
        {
            selectedFolder = null;
            object dialogObject = null;
            IShellItem parentItem = null;
            IShellItem resultItem = null;
            try
            {
                IFileDialog dialog = CreateConfiguredDialog(
                    parentFolder,
                    generatedFolderName,
                    out dialogObject,
                    out parentItem);

                int showResult = dialog.Show(ownerWindow);
                if (showResult == CancelledHresult)
                    return false;
                Marshal.ThrowExceptionForHR(showResult);

                dialog.GetResult(out resultItem);
                IntPtr pathPointer;
                resultItem.GetDisplayName(SigdnFileSystemPath, out pathPointer);
                try
                {
                    selectedFolder = NormalizeFolderPath(
                        Marshal.PtrToStringUni(pathPointer));
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pathPointer);
                }
                return true;
            }
            finally
            {
                ReleaseComObject(resultItem);
                if (!Object.ReferenceEquals(resultItem, parentItem))
                    ReleaseComObject(parentItem);
                ReleaseComObject(dialogObject);
            }
        }

        internal static void ValidateConfiguration(
            string parentFolder,
            string generatedFolderName)
        {
            object dialogObject = null;
            IShellItem parentItem = null;
            try
            {
                CreateConfiguredDialog(
                    parentFolder,
                    generatedFolderName,
                    out dialogObject,
                    out parentItem);
            }
            finally
            {
                ReleaseComObject(parentItem);
                ReleaseComObject(dialogObject);
            }
        }

        internal static void GetInitialSelection(
            string generatedDestination,
            out string parentFolder,
            out string generatedFolderName)
        {
            string normalized = NormalizeFolderPath(generatedDestination);
            parentFolder = Path.GetDirectoryName(normalized);
            generatedFolderName = Path.GetFileName(normalized);
            if (String.IsNullOrEmpty(parentFolder) ||
                String.IsNullOrEmpty(generatedFolderName))
            {
                throw new ArgumentException(
                    "The generated rip folder must have a parent folder and folder name.",
                    "generatedDestination");
            }
        }

        private static string NormalizeFolderPath(string path)
        {
            string normalized = Path.GetFullPath(path);
            return String.Equals(
                    normalized,
                    Path.GetPathRoot(normalized),
                    StringComparison.OrdinalIgnoreCase)
                ? normalized
                : normalized.TrimEnd('\\');
        }

        private static IFileDialog CreateConfiguredDialog(
            string parentFolder,
            string generatedFolderName,
            out object dialogObject,
            out IShellItem parentItem)
        {
            dialogObject = null;
            parentItem = null;
            Type dialogType = Type.GetTypeFromCLSID(FileSaveDialogClassId, true);
            dialogObject = Activator.CreateInstance(dialogType);
            IFileDialog dialog = (IFileDialog)dialogObject;

            uint options;
            dialog.GetOptions(out options);
            // FOS_PICKFOLDERS is only valid for IFileOpenDialog on supported
            // Windows versions. Keep this as a Save dialog so its name field
            // can represent a folder that does not exist yet.
            dialog.SetOptions(
                options | FosForceFileSystem | FosNoTestFileCreate);
            dialog.SetTitle("Select Rip Folder");
            dialog.SetOkButtonLabel("Select Folder");
            dialog.SetFileNameLabel("Folder name:");

            Guid shellItemInterfaceId = typeof(IShellItem).GUID;
            int createResult = SHCreateItemFromParsingName(
                parentFolder,
                IntPtr.Zero,
                ref shellItemInterfaceId,
                out parentItem);
            Marshal.ThrowExceptionForHR(createResult);
            dialog.SetFolder(parentItem);
            dialog.SetFileName(generatedFolderName);
            return dialog;
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bindingContext,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(
                IntPtr bindingContext,
                ref Guid handlerId,
                ref Guid interfaceId,
                out IntPtr result);

            void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem parent);

            void GetDisplayName(uint displayName, out IntPtr name);

            void GetAttributes(uint mask, out uint attributes);

            void Compare(
                [MarshalAs(UnmanagedType.Interface)] IShellItem shellItem,
                uint hint,
                out int order);
        }

        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            int Show(IntPtr owner);

            void SetFileTypes(uint fileTypeCount, IntPtr filterSpecifications);

            void SetFileTypeIndex(uint fileTypeIndex);

            void GetFileTypeIndex(out uint fileTypeIndex);

            void Advise(IntPtr events, out uint cookie);

            void Unadvise(uint cookie);

            void SetOptions(uint options);

            void GetOptions(out uint options);

            void SetDefaultFolder([MarshalAs(UnmanagedType.Interface)] IShellItem shellItem);

            void SetFolder([MarshalAs(UnmanagedType.Interface)] IShellItem shellItem);

            void GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

            void GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

            void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

            void AddPlace(
                [MarshalAs(UnmanagedType.Interface)] IShellItem shellItem,
                uint placement);

            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

            void Close(int errorCode);

            void SetClientGuid(ref Guid clientGuid);

            void ClearClientData();

            void SetFilter(IntPtr filter);
        }
    }
}
