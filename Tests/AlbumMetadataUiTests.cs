using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace AudioDataPlugIn
{
    internal static class AlbumMetadataUiTests
    {
        [STAThread]
        private static int Main()
        {
            using (Form host = new Form())
            {
                host.ClientSize = new System.Drawing.Size(1600, 180);
                host.CreateControl();

                CreateReferenceEdit(
                    host.Handle, 992, 300, 8, 220, 24);
                IntPtr genre = CreateReferenceEdit(
                    host.Handle, 996, 600, 40, 150, 24);
                IntPtr comment = CreateReferenceEdit(
                    host.Handle, 883, 900, 40, 220, 24);
                IntPtr composer = CreateReferenceEdit(
                    host.Handle, 880, 900, 8, 220, 24);
                IntPtr performer = CreateReferenceEdit(
                    host.Handle, 997, 300, 72, 220, 24);
                CreateReferenceLabel(
                    host.Handle, 950, "CD Title", 200, 11, 100, 18);
                IntPtr composerLabel = CreateReferenceLabel(
                    host.Handle, 956, "CD Composer", 800, 11, 100, 18);
                IntPtr performerLabel = CreateReferenceLabel(
                    host.Handle, 955, "CD Performer", 200, 75, 100, 18);
                IntPtr genreLabel = CreateReferenceLabel(
                    host.Handle, 953, "Genre", 530, 43, 70, 18);
                IntPtr commentLabel = CreateReferenceLabel(
                    host.Handle, 959, "Comment", 800, 43, 100, 18);
                host.Show();
                Application.DoEvents();
                NativeMethods.RECT originalPerformerRectangle;
                if (!NativeMethods.GetWindowRect(
                    performer, out originalPerformerRectangle))
                {
                    throw new Exception(
                        "Could not inspect the original CD Performer slot.");
                }
                NativeMethods.SendMessageStringW(
                    performer,
                    NativeMethods.WM_SETTEXT,
                    IntPtr.Zero,
                    "Guest Performer");

                EnhancementRuntime.ApplyStoredAlbumMetadataValues(
                    "Loaded Label",
                    "987654321",
                    "LOADED-1");
                EnhancementRuntime.InstallAlbumMetadataControls(host.Handle);
                Application.DoEvents();

                IntPtr barcode = NativeMethods.GetDlgItem(
                    host.Handle,
                    EnhancementRuntime.AlbumBarcodeControlId);
                IntPtr catalogNumber = NativeMethods.GetDlgItem(
                    host.Handle,
                    EnhancementRuntime.AlbumCatalogNumberControlId);
                IntPtr label = NativeMethods.GetDlgItem(
                    host.Handle,
                    EnhancementRuntime.AlbumLabelControlId);
                if (barcode == IntPtr.Zero || catalogNumber == IntPtr.Zero ||
                    label == IntPtr.Zero)
                {
                    throw new Exception(
                        "The album metadata edit controls were not created.");
                }
                AssertControlText(
                    host.Handle,
                    EnhancementRuntime.AlbumLabelControlId,
                    "Loaded Label");
                AssertControlText(
                    host.Handle,
                    EnhancementRuntime.AlbumBarcodeControlId,
                    "987654321");
                AssertControlText(
                    host.Handle,
                    EnhancementRuntime.AlbumCatalogNumberControlId,
                    "LOADED-1");

                AssertRectangle(
                    label,
                    originalPerformerRectangle,
                    "CD label");
                AssertAligned(barcode, genre, label, "barcode");
                AssertAligned(catalogNumber, comment, label, "catalog number");
                AssertMovedPerformer(
                    performer,
                    performerLabel,
                    composer,
                    composerLabel,
                    genre);
                AssertControlText(host.Handle, 0xA321, "CD Label");
                AssertControlText(host.Handle, 0xA31E, "CD Barcode");
                AssertControlText(host.Handle, 0xA31F, "CD Catalog #");
                AssertLabelsDoNotOverlap(
                    host.Handle,
                    label,
                    genre,
                    comment,
                    genreLabel,
                    commentLabel);

                NativeMethods.SendMessageStringW(
                    barcode,
                    NativeMethods.WM_SETTEXT,
                    IntPtr.Zero,
                    "012345678905");
                NativeMethods.SendMessageStringW(
                    catalogNumber,
                    NativeMethods.WM_SETTEXT,
                    IntPtr.Zero,
                    "ABC-123");
                NativeMethods.SendMessageStringW(
                    label,
                    NativeMethods.WM_SETTEXT,
                    IntPtr.Zero,
                    "Merge Records");
                Application.DoEvents();
                string expanded =
                    EnhancementRuntime.ExpandCurrentAlbumMetadataTokens(
                        "%label%|%barcode%|%catalognumber%");
                if (expanded != "Merge Records|012345678905|ABC-123")
                {
                    throw new Exception(
                        "Album metadata edit notifications did not update token values.");
                }
                Dictionary<string, string> folderMetadata =
                    EnhancementRuntime.ReadWorkflowFolderMetadata(host.Handle);
                string folder = WorkflowFolderPath.Resolve(
                    "%label% - %barcode% - %catalognumber%",
                    folderMetadata);
                if (folder != "Merge Records - 012345678905 - ABC-123")
                {
                    throw new Exception(
                        "Album metadata fields were not exposed to the folder template.");
                }
                host.Close();
            }

            Console.WriteLine("Album metadata UI tests passed.");
            return 0;
        }

        private static IntPtr CreateReferenceEdit(
            IntPtr parent,
            int controlId,
            int left,
            int top,
            int width,
            int height)
        {
            IntPtr control = NativeMethods.CreateWindowExW(
                0x00000200,
                "EDIT",
                String.Empty,
                0x50010080,
                left,
                top,
                width,
                height,
                parent,
                new IntPtr(controlId),
                IntPtr.Zero,
                IntPtr.Zero);
            if (control == IntPtr.Zero)
                throw new Exception("A synthetic EAC reference field could not be created.");
            return control;
        }

        private static IntPtr CreateReferenceLabel(
            IntPtr parent,
            int controlId,
            string text,
            int left,
            int top,
            int width,
            int height)
        {
            IntPtr control = NativeMethods.CreateWindowExW(
                0,
                "STATIC",
                text,
                0x50000000,
                left,
                top,
                width,
                height,
                parent,
                new IntPtr(controlId),
                IntPtr.Zero,
                IntPtr.Zero);
            if (control == IntPtr.Zero)
                throw new Exception("A synthetic EAC reference label could not be created.");
            return control;
        }

        private static void AssertAligned(
            IntPtr actual,
            IntPtr horizontalReference,
            IntPtr verticalReference,
            string description)
        {
            NativeMethods.RECT actualRectangle;
            NativeMethods.RECT horizontalRectangle;
            NativeMethods.RECT verticalRectangle;
            if (!NativeMethods.GetWindowRect(actual, out actualRectangle) ||
                !NativeMethods.GetWindowRect(
                    horizontalReference, out horizontalRectangle) ||
                !NativeMethods.GetWindowRect(
                    verticalReference, out verticalRectangle))
            {
                throw new Exception("Could not inspect the " + description + " field.");
            }
            if (actualRectangle.Left != horizontalRectangle.Left ||
                actualRectangle.Right != horizontalRectangle.Right ||
                actualRectangle.Top != verticalRectangle.Top ||
                actualRectangle.Bottom - actualRectangle.Top !=
                    horizontalRectangle.Bottom - horizontalRectangle.Top)
            {
                throw new Exception(
                    "The " + description +
                    " field is not aligned with EAC's metadata grid.");
            }
        }

        private static void AssertRectangle(
            IntPtr actual,
            NativeMethods.RECT expected,
            string description)
        {
            NativeMethods.RECT rectangle;
            if (!NativeMethods.GetWindowRect(actual, out rectangle) ||
                rectangle.Left != expected.Left ||
                rectangle.Top != expected.Top ||
                rectangle.Right != expected.Right ||
                rectangle.Bottom != expected.Bottom)
            {
                throw new Exception(
                    "The " + description +
                    " field did not occupy the original CD Performer slot.");
            }
        }

        private static void AssertMovedPerformer(
            IntPtr performer,
            IntPtr performerLabel,
            IntPtr composer,
            IntPtr composerLabel,
            IntPtr genre)
        {
            NativeMethods.RECT performerRectangle;
            NativeMethods.RECT performerLabelRectangle;
            NativeMethods.RECT composerRectangle;
            NativeMethods.RECT composerLabelRectangle;
            NativeMethods.RECT genreRectangle;
            if (!NativeMethods.GetWindowRect(
                    performer, out performerRectangle) ||
                !NativeMethods.GetWindowRect(
                    performerLabel, out performerLabelRectangle) ||
                !NativeMethods.GetWindowRect(
                    composer, out composerRectangle) ||
                !NativeMethods.GetWindowRect(
                    composerLabel, out composerLabelRectangle) ||
                !NativeMethods.GetWindowRect(genre, out genreRectangle))
            {
                throw new Exception(
                    "Could not inspect the relocated CD Performer controls.");
            }

            int groupGap = composerLabelRectangle.Left - genreRectangle.Right;
            if (performerLabelRectangle.Left !=
                    composerRectangle.Right + groupGap ||
                performerLabelRectangle.Right != performerRectangle.Left ||
                performerLabelRectangle.Top != composerLabelRectangle.Top ||
                performerLabelRectangle.Bottom != composerLabelRectangle.Bottom ||
                performerLabelRectangle.Right -
                    performerLabelRectangle.Left !=
                    composerLabelRectangle.Right -
                    composerLabelRectangle.Left ||
                performerRectangle.Top != composerRectangle.Top ||
                performerRectangle.Bottom != composerRectangle.Bottom ||
                performerRectangle.Right - performerRectangle.Left !=
                    composerRectangle.Right - composerRectangle.Left ||
                !String.Equals(
                    ReadControlText(performer),
                    "Guest Performer",
                    StringComparison.Ordinal))
            {
                throw new Exception(
                    "CD Performer was not moved intact to the right of CD Composer.");
            }
        }

        private static string ReadControlText(IntPtr control)
        {
            StringBuilder text = new StringBuilder(128);
            NativeMethods.GetWindowTextW(control, text, text.Capacity);
            return text.ToString();
        }

        private static void AssertControlText(
            IntPtr parent,
            int controlId,
            string expected)
        {
            IntPtr control = NativeMethods.GetDlgItem(parent, controlId);
            StringBuilder text = new StringBuilder(64);
            if (control == IntPtr.Zero ||
                NativeMethods.GetWindowTextW(control, text, text.Capacity) == 0 ||
                !String.Equals(text.ToString(), expected, StringComparison.Ordinal))
            {
                throw new Exception(
                    "The album metadata label '" + expected + "' is missing.");
            }
        }

        private static void AssertLabelsDoNotOverlap(
            IntPtr parent,
            IntPtr albumLabel,
            IntPtr genre,
            IntPtr comment,
            IntPtr genreLabel,
            IntPtr commentLabel)
        {
            NativeMethods.RECT albumLabelRectangle;
            NativeMethods.RECT genreRectangle;
            NativeMethods.RECT commentRectangle;
            NativeMethods.RECT barcodeLabelRectangle;
            NativeMethods.RECT catalogLabelRectangle;
            NativeMethods.RECT genreLabelRectangle;
            NativeMethods.RECT commentLabelRectangle;
            if (!NativeMethods.GetWindowRect(
                    albumLabel, out albumLabelRectangle) ||
                !NativeMethods.GetWindowRect(genre, out genreRectangle) ||
                !NativeMethods.GetWindowRect(comment, out commentRectangle) ||
                !NativeMethods.GetWindowRect(
                    NativeMethods.GetDlgItem(parent, 0xA31E),
                    out barcodeLabelRectangle) ||
                !NativeMethods.GetWindowRect(
                    NativeMethods.GetDlgItem(parent, 0xA31F),
                    out catalogLabelRectangle) ||
                !NativeMethods.GetWindowRect(
                    genreLabel, out genreLabelRectangle) ||
                !NativeMethods.GetWindowRect(
                    commentLabel, out commentLabelRectangle))
            {
                throw new Exception("Could not inspect album metadata label spacing.");
            }
            if (barcodeLabelRectangle.Left != genreLabelRectangle.Left ||
                barcodeLabelRectangle.Right != genreLabelRectangle.Right ||
                catalogLabelRectangle.Left != commentLabelRectangle.Left ||
                catalogLabelRectangle.Right != commentLabelRectangle.Right ||
                barcodeLabelRectangle.Top - albumLabelRectangle.Top !=
                    genreLabelRectangle.Top - genreRectangle.Top ||
                catalogLabelRectangle.Top - albumLabelRectangle.Top !=
                    commentLabelRectangle.Top - commentRectangle.Top ||
                barcodeLabelRectangle.Left <= albumLabelRectangle.Right ||
                catalogLabelRectangle.Left <= genreRectangle.Right)
            {
                throw new Exception(
                    "An album metadata label overlaps EAC's existing fields.");
            }
        }
    }
}
