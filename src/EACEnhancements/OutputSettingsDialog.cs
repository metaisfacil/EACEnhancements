using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AudioDataPlugIn
{

internal sealed class OutputTemplateSettings
{
	internal string RootFolder { get; private set; }

	internal string FolderTemplate { get; private set; }

	internal bool ShowRipErrorAlert { get; private set; }

	internal bool ShowWorkflowSetupAlert { get; private set; }

	internal bool CreateWorkflowFolders { get; private set; }

	internal bool EnableLogging { get; private set; }

	internal OutputTemplateSettings(
		string rootFolder,
		string folderTemplate,
		bool showRipErrorAlert,
		bool showWorkflowSetupAlert,
		bool createWorkflowFolders,
		bool enableLogging)
	{
		RootFolder = rootFolder;
		FolderTemplate = folderTemplate;
		ShowRipErrorAlert = showRipErrorAlert;
		ShowWorkflowSetupAlert = showWorkflowSetupAlert;
		CreateWorkflowFolders = createWorkflowFolders;
		EnableLogging = enableLogging;
	}
}


internal sealed class WindowHandleOwner : IWin32Window
{
	public IntPtr Handle { get; private set; }

	internal WindowHandleOwner(IntPtr handle)
	{
		Handle = handle;
	}
}


internal sealed class OutputTemplateDialog : Form
{
	private readonly IntPtr mainWindow;

	private readonly TextBox rootTextBox;

	private readonly TextBox templateTextBox;

	private readonly CheckBox errorAlertCheckBox;

	private readonly CheckBox workflowSetupAlertCheckBox;

	private readonly CheckBox createWorkflowFoldersCheckBox;

	private readonly CheckBox loggingCheckBox;

	private readonly ToolTip createWorkflowFoldersToolTip;

	private readonly Button updateCheckButton;

	private BackgroundWorker updateCheckWorker;

	internal OutputTemplateSettings Settings { get; private set; }

	internal OutputTemplateDialog(OutputTemplateSettings settings, IntPtr mainWindow)
	{
		this.mainWindow = mainWindow;
		Text = "EAC Enhancements Options";
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.ShowInTaskbar = false;
		Font = SystemFonts.MessageBoxFont;
		base.AutoScaleMode = AutoScaleMode.Font;
		base.ClientSize = new Size(680, 384);
		base.Padding = new Padding(16);

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			ColumnCount = 3,
			RowCount = 10
		};
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 97F));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85F));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

		Label introduction = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			TextAlign = ContentAlignment.TopLeft,
			Text = "Choose the extraction root and the album-folder naming template used by the 100% log workflow."
		};
		layout.Controls.Add(introduction, 0, 0);
		layout.SetColumnSpan(introduction, 3);

		Label rootLabel = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			TextAlign = ContentAlignment.MiddleLeft,
			Text = "Root folder:"
		};
		layout.Controls.Add(rootLabel, 0, 1);
		rootTextBox = new TextBox
		{
			Anchor = AnchorStyles.Left | AnchorStyles.Right,
			Margin = new Padding(0, 4, 8, 4),
			Text = settings.RootFolder
		};
		layout.Controls.Add(rootTextBox, 1, 1);
		Button browseButton = new Button
		{
			Anchor = AnchorStyles.Left | AnchorStyles.Right,
			Margin = new Padding(0, 3, 0, 3),
			Size = new Size(85, 25),
			Text = "Browse...",
			UseVisualStyleBackColor = true
		};
		browseButton.Click += BrowseClicked;
		layout.Controls.Add(browseButton, 2, 1);

		Label templateLabel = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			TextAlign = ContentAlignment.MiddleLeft,
			Text = "Folder template:"
		};
		layout.Controls.Add(templateLabel, 0, 2);
		templateTextBox = new TextBox
		{
			Anchor = AnchorStyles.Left | AnchorStyles.Right,
			Margin = new Padding(0, 4, 0, 4),
			Text = settings.FolderTemplate
		};
		layout.Controls.Add(templateTextBox, 1, 2);
		layout.SetColumnSpan(templateTextBox, 2);

		Label templateHelp = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			TextAlign = ContentAlignment.TopLeft,
			Text = "(((%year%))) adds (year) only when a year is supplied.\r\n{{{%comment%}}} similarly adds {comment} only when a comment is supplied."
		};
		layout.Controls.Add(templateHelp, 1, 3);
		layout.SetColumnSpan(templateHelp, 2);

		errorAlertCheckBox = new CheckBox
		{
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = Padding.Empty,
			Text = "Show an alert after a rip completes with errors",
			Checked = settings.ShowRipErrorAlert,
			UseVisualStyleBackColor = true
		};
		layout.Controls.Add(errorAlertCheckBox, 1, 4);
		layout.SetColumnSpan(errorAlertCheckBox, 2);

		workflowSetupAlertCheckBox = new CheckBox
		{
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = Padding.Empty,
			Text = "Show an alert if EAC is misconfigured before starting 100% log workflow",
			Checked = settings.ShowWorkflowSetupAlert,
			UseVisualStyleBackColor = true
		};
		layout.Controls.Add(workflowSetupAlertCheckBox, 1, 5);
		layout.SetColumnSpan(workflowSetupAlertCheckBox, 2);

		createWorkflowFoldersCheckBox = new CheckBox
		{
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = Padding.Empty,
			Text = "Create new folders for 100% log rips following folder template",
			Checked = settings.CreateWorkflowFolders,
			UseVisualStyleBackColor = true
		};
		layout.Controls.Add(createWorkflowFoldersCheckBox, 1, 6);
		layout.SetColumnSpan(createWorkflowFoldersCheckBox, 2);
		createWorkflowFoldersToolTip = new ToolTip();
		createWorkflowFoldersToolTip.SetToolTip(
			createWorkflowFoldersCheckBox,
			"This option does nothing if 'Standard directory for extraction' is set to 'Use this directory'.\r\n" +
			"This setting controls whether or not the 100% log workflow creates a dedicated folder for each rip.");

		loggingCheckBox = new CheckBox
		{
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Margin = Padding.Empty,
			Text = "Enable EAC Enhancements diagnostic logging",
			Checked = settings.EnableLogging,
			UseVisualStyleBackColor = true
		};
		layout.Controls.Add(loggingCheckBox, 1, 7);
		layout.SetColumnSpan(loggingCheckBox, 2);

		Button setupCheckButton = new Button
		{
			Anchor = AnchorStyles.Left | AnchorStyles.Top,
			Margin = Padding.Empty,
			Size = new Size(164, 28),
			Text = "Check 100% Log Setup...",
			UseVisualStyleBackColor = true
		};
		setupCheckButton.Click += SetupCheckClicked;
		updateCheckButton = new Button
		{
			Anchor = AnchorStyles.Left | AnchorStyles.Top,
			Margin = new Padding(8, 0, 0, 0),
			Size = new Size(130, 28),
			Text = "Check for Updates...",
			UseVisualStyleBackColor = true
		};
		updateCheckButton.Click += UpdateCheckClicked;

		Button saveButton = new Button
		{
			Anchor = AnchorStyles.Left | AnchorStyles.Top,
			Size = new Size(75, 28),
			Margin = new Padding(0, 0, 8, 0),
			Text = "Save",
			UseVisualStyleBackColor = true
		};
		saveButton.Click += SaveClicked;
		Button cancelButton = new Button
		{
			Anchor = AnchorStyles.Left | AnchorStyles.Top,
			Size = new Size(75, 28),
			Margin = Padding.Empty,
			Text = "Cancel",
			DialogResult = DialogResult.Cancel,
			UseVisualStyleBackColor = true
		};
		TableLayoutPanel bottomRow = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			ColumnCount = 5,
			RowCount = 1
		};
		bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164F));
		bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138F));
		bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
		bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 83F));
		bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75F));
		bottomRow.Controls.Add(setupCheckButton, 0, 0);
		bottomRow.Controls.Add(updateCheckButton, 1, 0);
		bottomRow.Controls.Add(saveButton, 3, 0);
		bottomRow.Controls.Add(cancelButton, 4, 0);
		layout.Controls.Add(bottomRow, 0, 9);
		layout.SetColumnSpan(bottomRow, 3);

		base.Controls.Add(layout);
		base.AcceptButton = saveButton;
		base.CancelButton = cancelButton;
	}

	private void UpdateCheckClicked(object sender, EventArgs eventArgs)
	{
		if (updateCheckWorker != null)
			return;

		updateCheckButton.Enabled = false;
		updateCheckButton.Text = "Checking...";
		BackgroundWorker worker = new BackgroundWorker();
		updateCheckWorker = worker;
		worker.DoWork += delegate(object workerSender, DoWorkEventArgs workerEventArgs)
		{
			workerEventArgs.Result = UpdateChecker.GetLatestRelease();
		};
		worker.RunWorkerCompleted += UpdateCheckCompleted;
		worker.RunWorkerAsync();
	}

	private void UpdateCheckCompleted(object sender, RunWorkerCompletedEventArgs eventArgs)
	{
		BackgroundWorker worker = sender as BackgroundWorker;
		if (worker != null)
			worker.Dispose();
		updateCheckWorker = null;

		if (base.IsDisposed || base.Disposing)
			return;
		updateCheckButton.Enabled = true;
		updateCheckButton.Text = "Check for Updates...";

		if (eventArgs.Error != null)
		{
			MessageBox.Show(
				this,
				"The update check could not be completed.\r\n\r\n" + eventArgs.Error.Message,
				"EAC Enhancements",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);
			return;
		}

		GitHubReleaseInfo release = eventArgs.Result as GitHubReleaseInfo;
		Version installed = typeof(AudioDataTransfer).Assembly.GetName().Version;
		if (release == null)
		{
			MessageBox.Show(
				this,
				"The update check returned no release information.",
				"EAC Enhancements",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);
			return;
		}

		if (!UpdateChecker.IsNewer(release.Version, installed))
		{
			MessageBox.Show(
				this,
				"You already have the latest available version (" +
					UpdateChecker.FormatVersion(installed) + ").",
				"EAC Enhancements",
				MessageBoxButtons.OK,
				MessageBoxIcon.Information);
			return;
		}

		DialogResult result = MessageBox.Show(
			this,
			"A newer version (" + release.VersionText + ") is available. Open in browser?",
			"EAC Enhancements",
			MessageBoxButtons.YesNo,
			MessageBoxIcon.Information,
			MessageBoxDefaultButton.Button1);
		if (result == DialogResult.Yes)
		{
			try
			{
				Process.Start(release.PageUrl);
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					this,
					"The release page could not be opened.\r\n\r\n" + ex.Message,
					"EAC Enhancements",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);
			}
		}
	}

	private void SetupCheckClicked(object sender, EventArgs eventArgs)
	{
		try
		{
			EacSetupAuditResult result = EacSetupAudit.Run(mainWindow);
			using (EacSetupAuditDialog dialog = new EacSetupAuditDialog(result))
			{
				dialog.ShowDialog(this);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				this,
				"The 100% log setup check could not be completed.\r\n\r\n" + ex.Message,
				"EAC Enhancements",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);
		}
	}

	private void BrowseClicked(object sender, EventArgs eventArgs)
	{
		using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
		{
			folderBrowserDialog.Description = "Choose the root folder for EAC extractions";
			folderBrowserDialog.ShowNewFolderButton = true;
			string text = rootTextBox.Text.Trim().TrimEnd('\\');
			if (Directory.Exists(text))
			{
				folderBrowserDialog.SelectedPath = text;
			}
			if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
			{
				rootTextBox.Text = folderBrowserDialog.SelectedPath;
			}
		}
	}

	private void SaveClicked(object sender, EventArgs eventArgs)
	{
		try
		{
			string rootFolder = EnhancementRuntime.NormalizeRootFolder(rootTextBox.Text);
			string folderTemplate = EnhancementRuntime.NormalizeFolderTemplate(templateTextBox.Text);
			Settings = new OutputTemplateSettings(
				rootFolder,
				folderTemplate,
				errorAlertCheckBox.Checked,
				workflowSetupAlertCheckBox.Checked,
				createWorkflowFoldersCheckBox.Checked,
				loggingCheckBox.Checked);
			base.DialogResult = DialogResult.OK;
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "Invalid Output Settings", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
	}
}
}
