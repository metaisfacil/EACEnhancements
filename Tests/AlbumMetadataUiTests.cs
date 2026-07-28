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

                IntPtr title = CreateReferenceEdit(
                    host.Handle, 992, 300, 8, 220, 24);
                NativeMethods.SendMessageStringW(
                    title,
                    NativeMethods.WM_SETTEXT,
                    IntPtr.Zero,
                    "Loaded Album");
                IntPtr genre = CreateReferenceEdit(
                    host.Handle, 996, 600, 40, 150, 24);
                IntPtr comment = CreateReferenceEdit(
                    host.Handle, 883, 900, 40, 220, 24);
                IntPtr freedbGenre = CreateReferenceComboBox(
                    host.Handle, 998, 600, 72, 150, 180);
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
                IntPtr freedbGenreLabel = CreateReferenceLabel(
                    host.Handle, 954, "freedb Genre", 530, 75, 70, 18);
                IntPtr commentLabel = CreateReferenceLabel(
                    host.Handle, 959, "Comment", 800, 43, 100, 18);
                IntPtr firstTrackNumber = CreateReferenceEdit(
                    host.Handle, 674, 1450, 105, 40, 24);
                CreateReferenceEdit(
                    host.Handle, 882, 1450, 133, 40, 24);
                CreateReferenceEdit(
                    host.Handle, 881, 1450, 161, 40, 24);
                CreateReferenceTrackList(
                    host.Handle, 14, 200, 105, 920, 50);
                host.Show();
                Application.DoEvents();
                NativeMethods.RECT originalPerformerRectangle;
                NativeMethods.RECT originalCommentRectangle;
                NativeMethods.RECT originalCommentLabelRectangle;
                NativeMethods.RECT originalFreedbGenreRectangle;
                if (!NativeMethods.GetWindowRect(
                        performer, out originalPerformerRectangle) ||
                    !NativeMethods.GetWindowRect(
                        comment, out originalCommentRectangle) ||
                    !NativeMethods.GetWindowRect(
                        commentLabel, out originalCommentLabelRectangle) ||
                    !NativeMethods.GetWindowRect(
                        freedbGenre, out originalFreedbGenreRectangle))
                {
                    throw new Exception(
                        "Could not inspect the original EAC metadata slots.");
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
                if (EnhancementRuntime.HasPendingAlbumMetadataStoreChanges)
                {
                    throw new Exception(
                        "Programmatic metadata loads were marked as user edits.");
                }
                AssertRectangle(
                    label,
                    originalPerformerRectangle,
                    "CD label");
                AssertRectangle(
                    comment,
                    originalCommentRectangle,
                    "EAC 1.8 Comment");
                AssertRectangle(
                    freedbGenre,
                    originalFreedbGenreRectangle,
                    "non-1.6 freedb Genre");
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
                AssertAlbumMetadataTabOrder(
                    host.Handle,
                    comment,
                    barcode,
                    catalogNumber,
                    label);
                AssertEacAlbumMetadataTabBridge(
                    host.Handle,
                    comment,
                    barcode,
                    catalogNumber,
                    label,
                    firstTrackNumber);
                AssertAlbumMetadataEnabledState(
                    genre,
                    new[]
                    {
                        label,
                        barcode,
                        catalogNumber,
                        NativeMethods.GetDlgItem(host.Handle, 0xA321),
                        NativeMethods.GetDlgItem(host.Handle, 0xA31E),
                        NativeMethods.GetDlgItem(host.Handle, 0xA31F)
                    });
                AssertHydratedMetadataSurvivesUiRefresh(
                    title,
                    label,
                    barcode,
                    catalogNumber);
                AssertLabelsDoNotOverlap(
                    host.Handle,
                    label,
                    genre,
                    comment,
                    genreLabel,
                    commentLabel);

                EnhancementRuntime.LayoutAlbumMetadataControls(
                    host.Handle,
                    true);
                EnhancementRuntime.LayoutAlbumMetadataControls(
                    host.Handle,
                    true);
                Application.DoEvents();
                AssertEac16FreedbGenreLayout(
                    barcode,
                    comment,
                    commentLabel,
                    performer,
                    performerLabel,
                    freedbGenre,
                    freedbGenreLabel,
                    originalCommentRectangle,
                    originalCommentLabelRectangle,
                    originalFreedbGenreRectangle);

                ReplaceEditTextAsUser(barcode, "012345678905");
                ReplaceEditTextAsUser(catalogNumber, "ABC-123");
                ReplaceEditTextAsUser(label, "Merge Records");
                Application.DoEvents();
                string expanded =
                    EnhancementRuntime.ExpandCurrentAlbumMetadataTokens(
                        "%label%|%barcode%|%catalognumber%");
                if (expanded != "Merge Records|012345678905|ABC-123")
                {
                    throw new Exception(
                        "Album metadata edit notifications did not update token values.");
                }
                if (!EnhancementRuntime.HasPendingAlbumMetadataStoreChanges)
                {
                    throw new Exception(
                        "Album metadata edits were not marked for persistence.");
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

        private static void AssertHydratedMetadataSurvivesUiRefresh(
            IntPtr title,
            IntPtr label,
            IntPtr barcode,
            IntPtr catalogNumber)
        {
            EnhancementRuntime.ApplyStoredAlbumMetadataValues(
                "Refreshed Label",
                "123456789012",
                "REFRESH-1");

            NativeMethods.SendMessageStringW(
                title,
                NativeMethods.WM_SETTEXT,
                IntPtr.Zero,
                String.Empty);
            NativeMethods.SendMessageStringW(
                label,
                NativeMethods.WM_SETTEXT,
                IntPtr.Zero,
                String.Empty);
            NativeMethods.SendMessageStringW(
                barcode,
                NativeMethods.WM_SETTEXT,
                IntPtr.Zero,
                String.Empty);
            NativeMethods.SendMessageStringW(
                catalogNumber,
                NativeMethods.WM_SETTEXT,
                IntPtr.Zero,
                String.Empty);
            NativeMethods.SendMessageStringW(
                title,
                NativeMethods.WM_SETTEXT,
                IntPtr.Zero,
                "Refreshed Album");
            Application.DoEvents();

            AssertWindowText(label, "Refreshed Label", "refreshed label");
            AssertWindowText(barcode, "123456789012", "refreshed barcode");
            AssertWindowText(
                catalogNumber,
                "REFRESH-1",
                "refreshed catalog number");

            // Once the matching refresh has settled, a later empty title is
            // a real disc-context reset and must clear the sidecar fields.
            NativeMethods.SendMessageStringW(
                title,
                NativeMethods.WM_SETTEXT,
                IntPtr.Zero,
                String.Empty);
            Application.DoEvents();
            AssertWindowText(label, String.Empty, "cleared label");
            AssertWindowText(barcode, String.Empty, "cleared barcode");
            AssertWindowText(
                catalogNumber,
                String.Empty,
                "cleared catalog number");
            NativeMethods.SendMessageStringW(
                title,
                NativeMethods.WM_SETTEXT,
                IntPtr.Zero,
                "Loaded Album");
        }

        private static void AssertAlbumMetadataEnabledState(
            IntPtr referenceControl,
            IntPtr[] customControls)
        {
            NativeMethods.EnableWindow(referenceControl, false);
            Application.DoEvents();
            foreach (IntPtr control in customControls)
            {
                if (NativeMethods.IsWindowEnabled(control))
                {
                    throw new Exception(
                        "A custom album metadata control did not mirror the disabled state.");
                }
            }

            NativeMethods.EnableWindow(referenceControl, true);
            Application.DoEvents();
            foreach (IntPtr control in customControls)
            {
                if (!NativeMethods.IsWindowEnabled(control))
                {
                    throw new Exception(
                        "A custom album metadata control did not mirror the re-enabled state.");
                }
            }
        }

        private static void AssertEacAlbumMetadataTabBridge(
            IntPtr parent,
            IntPtr comment,
            IntPtr barcode,
            IntPtr catalogNumber,
            IntPtr label,
            IntPtr firstTrackNumber)
        {
            NativeMethods.SetFocus(comment);
            NativeMethods.SendMessageW(
                parent,
                0x0D88,
                comment,
                IntPtr.Zero);
            if (NativeMethods.GetFocus() != label)
            {
                throw new Exception(
                    "EAC's forward myedit navigation did not enter CD Label.");
            }

            NativeMethods.SendMessageW(
                label,
                NativeMethods.WM_KEYDOWN,
                new IntPtr(0x09),
                IntPtr.Zero);
            if (NativeMethods.GetFocus() != label)
            {
                throw new Exception(
                    "CD Label navigation changed focus while Tab was still down.");
            }
            Application.DoEvents();
            if (NativeMethods.GetFocus() != label)
            {
                throw new Exception(
                    "CD Label navigation advanced before Tab was released.");
            }
            NativeMethods.SendMessageW(
                label,
                NativeMethods.WM_KEYUP,
                new IntPtr(0x09),
                IntPtr.Zero);
            Application.DoEvents();
            if (NativeMethods.GetFocus() != barcode)
            {
                throw new Exception(
                    "Tab release did not advance from CD Label to CD Barcode.");
            }

            NativeMethods.SendMessageW(
                barcode,
                NativeMethods.WM_KEYDOWN,
                new IntPtr(0x09),
                IntPtr.Zero);
            if (NativeMethods.GetFocus() != barcode)
            {
                throw new Exception(
                    "CD Barcode navigation changed focus while Tab was still down.");
            }
            Application.DoEvents();
            if (NativeMethods.GetFocus() != barcode)
            {
                throw new Exception(
                    "CD Barcode navigation advanced before Tab was released.");
            }
            NativeMethods.SendMessageW(
                barcode,
                NativeMethods.WM_KEYUP,
                new IntPtr(0x09),
                IntPtr.Zero);
            Application.DoEvents();
            if (NativeMethods.GetFocus() != catalogNumber)
            {
                throw new Exception(
                    "Tab release did not advance from CD Barcode to CD Catalog #.");
            }

            NativeMethods.SendMessageW(
                catalogNumber,
                NativeMethods.WM_KEYDOWN,
                new IntPtr(0x09),
                IntPtr.Zero);
            if (NativeMethods.GetFocus() != catalogNumber)
            {
                throw new Exception(
                    "CD Catalog # navigation changed focus while Tab was still down.");
            }
            Application.DoEvents();
            if (NativeMethods.GetFocus() != catalogNumber)
            {
                throw new Exception(
                    "CD Catalog # navigation advanced before Tab was released.");
            }
            // A repeated keydown from the same physical keypress must not
            // advance again after focus is transferred.
            NativeMethods.SendMessageW(
                catalogNumber,
                NativeMethods.WM_KEYDOWN,
                new IntPtr(0x09),
                new IntPtr(0x40000000));
            Application.DoEvents();
            if (NativeMethods.GetFocus() != catalogNumber)
            {
                throw new Exception(
                    "A repeated Tab keydown advanced CD Catalog # early.");
            }
            NativeMethods.SendMessageW(
                catalogNumber,
                NativeMethods.WM_KEYUP,
                new IntPtr(0x09),
                IntPtr.Zero);
            Application.DoEvents();
            if (NativeMethods.GetFocus() != firstTrackNumber)
            {
                throw new Exception(
                    "Tab release did not continue from CD Catalog # to First track number.");
            }

            IntPtr dialogCode = NativeMethods.SendMessageW(
                catalogNumber,
                NativeMethods.WM_GETDLGCODE,
                IntPtr.Zero,
                IntPtr.Zero);
            if ((dialogCode.ToInt64() & 0x0002) == 0)
            {
                throw new Exception(
                    "The custom metadata edits did not request Tab key delivery.");
            }

            NativeMethods.SetFocus(firstTrackNumber);
            NativeMethods.SendMessageW(
                parent,
                0x0D88,
                firstTrackNumber,
                new IntPtr(0x8000));
            if (NativeMethods.GetFocus() != catalogNumber)
            {
                throw new Exception(
                    "EAC's reverse myedit navigation did not enter CD Catalog #.");
            }
        }

        private static void AssertAlbumMetadataTabOrder(
            IntPtr parent,
            IntPtr comment,
            IntPtr barcode,
            IntPtr catalogNumber,
            IntPtr label)
        {
            AssertNextTabControl(
                parent,
                comment,
                label,
                false,
                "EAC's Comment to CD Label");
            AssertNextTabControl(
                parent,
                label,
                barcode,
                false,
                "CD Label to CD Barcode");
            AssertNextTabControl(
                parent,
                barcode,
                catalogNumber,
                false,
                "CD Barcode to CD Catalog #");
            AssertNextTabControl(
                parent,
                catalogNumber,
                barcode,
                true,
                "CD Catalog # back to CD Barcode");
            AssertNextTabControl(
                parent,
                barcode,
                label,
                true,
                "CD Barcode back to CD Label");
            AssertNextTabControl(
                parent,
                label,
                comment,
                true,
                "CD Label back to EAC's Comment");
        }

        private static void AssertNextTabControl(
            IntPtr parent,
            IntPtr current,
            IntPtr expected,
            bool previous,
            string description)
        {
            IntPtr actual = NativeMethods.GetNextDlgTabItem(
                parent,
                current,
                previous);
            if (actual != expected)
            {
                throw new Exception(
                    "Unexpected album metadata tab order for " +
                    description + ".");
            }
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

        private static IntPtr CreateReferenceTrackList(
            IntPtr parent,
            int controlId,
            int left,
            int top,
            int width,
            int height)
        {
            IntPtr control = NativeMethods.CreateWindowExW(
                0x00000200,
                "SysListView32",
                String.Empty,
                0x50010000,
                left,
                top,
                width,
                height,
                parent,
                new IntPtr(controlId),
                IntPtr.Zero,
                IntPtr.Zero);
            if (control == IntPtr.Zero)
            {
                throw new Exception(
                    "A synthetic EAC track list could not be created.");
            }
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

        private static void ReplaceEditTextAsUser(
            IntPtr control,
            string value)
        {
            NativeMethods.SendMessageW(
                control,
                0x00B1,
                IntPtr.Zero,
                new IntPtr(-1));
            foreach (char character in value)
            {
                NativeMethods.SendMessageW(
                    control,
                    NativeMethods.WM_CHAR,
                    new IntPtr(character),
                    IntPtr.Zero);
            }
        }

        private static IntPtr CreateReferenceComboBox(
            IntPtr parent,
            int controlId,
            int left,
            int top,
            int width,
            int height)
        {
            IntPtr control = NativeMethods.CreateWindowExW(
                0,
                "COMBOBOX",
                String.Empty,
                0x50210203,
                left,
                top,
                width,
                height,
                parent,
                new IntPtr(controlId),
                IntPtr.Zero,
                IntPtr.Zero);
            if (control == IntPtr.Zero)
            {
                throw new Exception(
                    "A synthetic EAC freedb Genre combo could not be created.");
            }
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
                    " field did not occupy its expected slot.");
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

        private static void AssertEac16FreedbGenreLayout(
            IntPtr barcode,
            IntPtr comment,
            IntPtr commentLabel,
            IntPtr performer,
            IntPtr performerLabel,
            IntPtr freedbGenre,
            IntPtr freedbGenreLabel,
            NativeMethods.RECT originalComment,
            NativeMethods.RECT originalCommentLabel,
            NativeMethods.RECT originalFreedbGenre)
        {
            NativeMethods.RECT barcodeRectangle;
            NativeMethods.RECT commentRectangle;
            NativeMethods.RECT commentLabelRectangle;
            NativeMethods.RECT performerRectangle;
            NativeMethods.RECT performerLabelRectangle;
            NativeMethods.RECT freedbGenreRectangle;
            NativeMethods.RECT freedbGenreLabelRectangle;
            if (!NativeMethods.GetWindowRect(
                    barcode, out barcodeRectangle) ||
                !NativeMethods.GetWindowRect(
                    comment, out commentRectangle) ||
                !NativeMethods.GetWindowRect(
                    commentLabel, out commentLabelRectangle) ||
                !NativeMethods.GetWindowRect(
                    performer, out performerRectangle) ||
                !NativeMethods.GetWindowRect(
                    performerLabel, out performerLabelRectangle) ||
                !NativeMethods.GetWindowRect(
                    freedbGenre, out freedbGenreRectangle) ||
                !NativeMethods.GetWindowRect(
                    freedbGenreLabel, out freedbGenreLabelRectangle))
            {
                throw new Exception(
                    "Could not inspect the EAC 1.6 metadata layout.");
            }

            bool freedbGenreInCommentSlot =
                freedbGenreRectangle.Left == originalComment.Left &&
                freedbGenreRectangle.Top == originalComment.Top &&
                freedbGenreRectangle.Right == originalComment.Right &&
                freedbGenreRectangle.Bottom - freedbGenreRectangle.Top ==
                    originalFreedbGenre.Bottom -
                    originalFreedbGenre.Top &&
                freedbGenreLabelRectangle.Left ==
                    originalCommentLabel.Left &&
                freedbGenreLabelRectangle.Top ==
                    originalCommentLabel.Top &&
                freedbGenreLabelRectangle.Right ==
                    originalCommentLabel.Right &&
                freedbGenreLabelRectangle.Bottom ==
                    originalCommentLabel.Bottom;
            bool commentBeneathPerformer =
                commentRectangle.Left == performerRectangle.Left &&
                commentRectangle.Right == performerRectangle.Right &&
                commentRectangle.Top == originalComment.Top &&
                commentRectangle.Bottom - commentRectangle.Top ==
                    originalComment.Bottom - originalComment.Top &&
                commentLabelRectangle.Left ==
                    performerLabelRectangle.Left &&
                commentLabelRectangle.Right ==
                    performerLabelRectangle.Right &&
                commentLabelRectangle.Top == originalCommentLabel.Top &&
                commentLabelRectangle.Bottom - commentLabelRectangle.Top ==
                    originalCommentLabel.Bottom -
                    originalCommentLabel.Top;
            bool barcodeClear =
                barcodeRectangle.Right <= freedbGenreRectangle.Left ||
                freedbGenreRectangle.Right <= barcodeRectangle.Left ||
                barcodeRectangle.Bottom <= freedbGenreRectangle.Top ||
                freedbGenreRectangle.Bottom <= barcodeRectangle.Top;
            if (!freedbGenreInCommentSlot ||
                !commentBeneathPerformer ||
                !barcodeClear)
            {
                throw new Exception(
                    "The EAC 1.6 freedb Genre and Comment fields were not " +
                    "relocated without overlapping CD Barcode.");
            }
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

        private static void AssertWindowText(
            IntPtr control,
            string expected,
            string description)
        {
            StringBuilder text = new StringBuilder(512);
            if (control == IntPtr.Zero ||
                (NativeMethods.GetWindowTextW(
                        control,
                        text,
                        text.Capacity) == 0 &&
                    expected.Length != 0) ||
                !String.Equals(
                    text.ToString(),
                    expected,
                    StringComparison.Ordinal))
            {
                throw new Exception(
                    "The " + description + " was expected to be '" +
                    expected + "' but was '" + text + "'.");
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
