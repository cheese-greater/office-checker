using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OfficeLockChecker
{
    internal sealed class MainForm : XpForm
    {
        private const string ApplicationTitle = "Office Lock Checker";
        private const string IdleHint = "Drop Office documents or folders above, or use Choose Files and Choose Folder.";
        private const string IconResourceName = "OfficeLockChecker.app.ico";

        private readonly string[] startupPaths;
        private readonly DropZone dropZone = new DropZone();
        private readonly ResultsListView resultsList = new ResultsListView();
        private readonly XpButton chooseFilesButton = new XpButton();
        private readonly XpButton chooseFolderButton = new XpButton();
        private readonly XpButton refreshButton = new XpButton();
        private readonly XpButton clearButton = new XpButton();
        private readonly XpCheckBox includeSubfoldersCheckBox = new XpCheckBox();
        private readonly XpCheckBox hideUnlockedCheckBox = new XpCheckBox();
        private readonly ToolStripStatusLabel statusLabel = new ToolStripStatusLabel();

        private List<string> currentInputs = new List<string>();
        private List<CheckResult> currentResults = new List<CheckResult>();
        private CancellationTokenSource scanCancellation;
        private DateTime lastCheckedAt;

        public MainForm(string[] startupPaths)
        {
            this.startupPaths = startupPaths;
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Font = XpTheme.UiFont;
            Text = ApplicationTitle;
            Icon = LoadApplicationIcon();
            // The XP frame is drawn inside the window, so the window size is also the client size.
            Size = new Size(XpTheme.Scaled(1180), XpTheme.Scaled(680));
            MinimumSize = new Size(XpTheme.Scaled(760), XpTheme.Scaled(420));
            StartPosition = FormStartPosition.CenterScreen;
            BuildLayout();
            EnableDrop(this);
            statusLabel.Text = IdleHint;
            UpdateCommandStates();
            ResumeLayout(true);
        }

        protected override void OnShown(EventArgs eventArgs)
        {
            base.OnShown(eventArgs);
            if (startupPaths.Length > 0)
            {
                StartScan(startupPaths.ToList(), false);
            }
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    if (scanCancellation == null && currentInputs.Count > 0)
                    {
                        StartScan(currentInputs, true);
                    }
                    return true;
                case Keys.Escape:
                    if (scanCancellation != null)
                    {
                        scanCancellation.Cancel();
                        return true;
                    }
                    break;
                case Keys.Control | Keys.O:
                    ChooseFiles();
                    return true;
                case Keys.Control | Keys.C:
                    if (resultsList.Focused)
                    {
                        CopyRows(resultsList.SelectedItems.Cast<ListViewItem>());
                        return true;
                    }
                    break;
                case Keys.Control | Keys.A:
                    if (resultsList.Focused)
                    {
                        foreach (ListViewItem item in resultsList.Items)
                        {
                            item.Selected = true;
                        }
                        return true;
                    }
                    break;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private void BuildLayout()
        {
            dropZone.Text = "Check Office documents";
            dropZone.Description = "Drag documents or folders onto this window to see who has them open.";
            dropZone.ActionText = "Click here to choose files...";
            dropZone.Glyph = Icon;
            dropZone.Dock = DockStyle.Top;
            dropZone.Height = XpTheme.Scaled(86);
            dropZone.Click += delegate { ChooseFiles(); };

            ConfigureButton(chooseFilesButton, "Choose &Files...", delegate { ChooseFiles(); });
            ConfigureButton(chooseFolderButton, "Choose F&older...", delegate { ChooseFolder(); });
            ConfigureButton(refreshButton, "&Refresh", delegate { RefreshOrStop(); });
            ConfigureButton(clearButton, "C&lear", delegate { ClearResults(); });
            ConfigureCheckBox(includeSubfoldersCheckBox, "Include &subfolders", XpTheme.Scaled(16));
            ConfigureCheckBox(hideUnlockedCheckBox, "&Hide unlocked documents", XpTheme.Scaled(8));
            includeSubfoldersCheckBox.CheckedChanged += delegate
            {
                if (currentInputs.Count > 0)
                {
                    StartScan(currentInputs, true);
                }
            };
            hideUnlockedCheckBox.CheckedChanged += delegate { ShowResults(); };

            TableLayoutPanel toolbar = new TableLayoutPanel();
            toolbar.Dock = DockStyle.Top;
            toolbar.AutoSize = true;
            toolbar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            toolbar.Padding = new Padding(0, XpTheme.Scaled(8), 0, XpTheme.Scaled(6));
            Control[] toolbarControls =
            {
                chooseFilesButton, chooseFolderButton, refreshButton, clearButton, includeSubfoldersCheckBox, hideUnlockedCheckBox
            };
            toolbar.ColumnCount = toolbarControls.Length + 1;
            toolbar.RowCount = 1;
            toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int column = 0; column < toolbarControls.Length; column++)
            {
                toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                toolbar.Controls.Add(toolbarControls[column], column, 0);
            }
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            resultsList.Dock = DockStyle.Fill;
            resultsList.BorderStyle = BorderStyle.None;
            resultsList.View = View.Details;
            resultsList.FullRowSelect = true;
            resultsList.HideSelection = false;
            resultsList.ShowItemToolTips = true;
            resultsList.SmallImageList = CreateStatusImages();
            resultsList.ContextMenuStrip = CreateContextMenu();
            resultsList.DoubleClick += delegate { OpenSelectedFolder(); };
            AddColumn("Status", 160);
            AddColumn("Document", 250);
            AddColumn("Locked by", 190);
            AddColumn("Windows account", 200);
            AddColumn("Locked since", 140);
            AddColumn("Folder", 330);
            AddColumn("Lock file", 200);

            // XP list controls sit in a one-pixel steel-blue border.
            Panel listBorder = new Panel();
            listBorder.Dock = DockStyle.Fill;
            listBorder.BackColor = XpTheme.ControlBorder;
            listBorder.Padding = new Padding(Math.Max(1, XpTheme.Scaled(1)));
            listBorder.Controls.Add(resultsList);

            Panel contentPanel = new Panel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.Padding = new Padding(XpTheme.Scaled(10), XpTheme.Scaled(10), XpTheme.Scaled(10), XpTheme.Scaled(4));
            // Docking runs from the last control added, so the drop zone lands on top and the list fills what remains.
            contentPanel.Controls.Add(listBorder);
            contentPanel.Controls.Add(toolbar);
            contentPanel.Controls.Add(dropZone);

            StatusStrip statusStrip = new StatusStrip();
            statusLabel.Spring = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.BorderSides = ToolStripStatusLabelBorderSides.All;
            statusLabel.BorderStyle = Border3DStyle.SunkenOuter;
            statusStrip.Items.Add(statusLabel);

            Controls.Add(contentPanel);
            Controls.Add(statusStrip);
        }

        private void ConfigureButton(Button button, string text, EventHandler clickHandler)
        {
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(XpTheme.Scaled(90), XpTheme.Scaled(23));
            button.Padding = new Padding(XpTheme.Scaled(6), 0, XpTheme.Scaled(6), 0);
            button.Anchor = AnchorStyles.Left;
            button.Click += clickHandler;
        }

        private void ConfigureCheckBox(CheckBox checkBox, string text, int leftMargin)
        {
            checkBox.Text = text;
            checkBox.AutoSize = true;
            checkBox.Anchor = AnchorStyles.Left;
            checkBox.Margin = new Padding(leftMargin, XpTheme.Scaled(3), XpTheme.Scaled(3), XpTheme.Scaled(3));
        }

        private void AddColumn(string title, int width)
        {
            resultsList.Columns.Add(title, XpTheme.Scaled(width));
        }

        private ImageList CreateStatusImages()
        {
            int imageSize = XpTheme.Scaled(16);
            ImageList statusImages = new ImageList();
            statusImages.ColorDepth = ColorDepth.Depth32Bit;
            statusImages.ImageSize = new Size(imageSize, imageSize);
            foreach (LockStatus status in Enum.GetValues(typeof(LockStatus)))
            {
                Bitmap statusImage = new Bitmap(imageSize, imageSize);
                Color statusColor = GetStatusColor(status);
                float inset = imageSize * 0.15F;
                RectangleF orb = new RectangleF(inset, inset, imageSize - inset * 2, imageSize - inset * 2);
                // A glossy orb with a white highlight across its top half, like XP's status icons.
                RectangleF gloss = new RectangleF(orb.X + orb.Width * 0.18F, orb.Y + orb.Height * 0.06F, orb.Width * 0.64F, orb.Height * 0.5F);
                using (Graphics graphics = Graphics.FromImage(statusImage))
                using (SolidBrush orbBrush = new SolidBrush(statusColor))
                using (LinearGradientBrush glossBrush = new LinearGradientBrush(
                    gloss, Color.FromArgb(210, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
                using (Pen rimPen = new Pen(ControlPaint.Dark(statusColor, 0.1F)))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.FillEllipse(orbBrush, orb);
                    graphics.FillEllipse(glossBrush, gloss);
                    graphics.DrawEllipse(rimPen, orb);
                }
                statusImages.Images.Add(statusImage);
            }
            return statusImages;
        }

        private ContextMenuStrip CreateContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = XpTheme.UiFont;
            ToolStripMenuItem openFolderItem = new ToolStripMenuItem("Open containing folder", null,
                delegate { OpenSelectedFolder(); });
            ToolStripMenuItem copySelectedItem = new ToolStripMenuItem("Copy selected rows", null,
                delegate { CopyRows(resultsList.SelectedItems.Cast<ListViewItem>()); });
            copySelectedItem.ShortcutKeyDisplayString = "Ctrl+C";
            ToolStripMenuItem copyAllItem = new ToolStripMenuItem("Copy all rows", null,
                delegate { CopyRows(resultsList.Items.Cast<ListViewItem>()); });
            menu.Items.AddRange(new ToolStripItem[] { openFolderItem, new ToolStripSeparator(), copySelectedItem, copyAllItem });
            menu.Opening += delegate
            {
                bool hasSelection = resultsList.SelectedItems.Count > 0;
                openFolderItem.Enabled = hasSelection;
                copySelectedItem.Enabled = hasSelection;
                copyAllItem.Enabled = resultsList.Items.Count > 0;
            };
            return menu;
        }

        private void EnableDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += HandleDragOver;
            control.DragOver += HandleDragOver;
            control.DragLeave += HandleDragLeave;
            control.DragDrop += HandleDragDrop;
            foreach (Control child in control.Controls)
            {
                EnableDrop(child);
            }
        }

        private void HandleDragOver(object sender, DragEventArgs eventArgs)
        {
            bool hasFiles = eventArgs.Data.GetDataPresent(DataFormats.FileDrop);
            eventArgs.Effect = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
            dropZone.Highlighted = hasFiles;
        }

        private void HandleDragLeave(object sender, EventArgs eventArgs)
        {
            dropZone.Highlighted = false;
        }

        private void HandleDragDrop(object sender, DragEventArgs eventArgs)
        {
            dropZone.Highlighted = false;
            string[] droppedPaths = eventArgs.Data.GetData(DataFormats.FileDrop) as string[];
            if (droppedPaths == null || droppedPaths.Length == 0)
            {
                return;
            }
            // Start after the drop returns so Explorer is not left waiting on the drag operation.
            BeginInvoke(new Action(() => StartScan(droppedPaths.ToList(), false)));
        }

        private void ChooseFiles()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose Office documents to check";
                dialog.Multiselect = true;
                string officePatterns = string.Join(";", LockScanner.OfficeExtensions.Select(extension => "*" + extension));
                dialog.Filter = "Office documents|" + officePatterns + "|All files|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    StartScan(dialog.FileNames.ToList(), false);
                }
            }
        }

        private void ChooseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose a folder to check for locked Office documents.";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    StartScan(new List<string> { dialog.SelectedPath }, false);
                }
            }
        }

        private void RefreshOrStop()
        {
            if (scanCancellation != null)
            {
                scanCancellation.Cancel();
            }
            else if (currentInputs.Count > 0)
            {
                StartScan(currentInputs, true);
            }
        }

        private void ClearResults()
        {
            if (scanCancellation != null)
            {
                scanCancellation.Cancel();
                scanCancellation = null;
            }
            currentInputs = new List<string>();
            currentResults = new List<CheckResult>();
            ShowResults();
            UpdateCommandStates();
        }

        private async void StartScan(List<string> inputs, bool keepCurrentRows)
        {
            if (scanCancellation != null)
            {
                scanCancellation.Cancel();
            }
            CancellationTokenSource cancellation = new CancellationTokenSource();
            scanCancellation = cancellation;
            currentInputs = inputs;
            if (!keepCurrentRows)
            {
                currentResults = new List<CheckResult>();
                ShowResults();
            }
            statusLabel.Text = "Checking...";
            UpdateCommandStates();

            bool includeSubfolders = includeSubfoldersCheckBox.Checked;
            Progress<string> progress = new Progress<string>(folder =>
            {
                if (scanCancellation == cancellation)
                {
                    statusLabel.Text = "Checking " + folder;
                }
            });
            try
            {
                LockScanner scanner = new LockScanner(cancellation.Token, progress);
                List<CheckResult> results = await Task.Run(() => scanner.Scan(inputs, includeSubfolders), cancellation.Token);
                if (scanCancellation == cancellation)
                {
                    currentResults = results;
                    lastCheckedAt = DateTime.Now;
                    ShowResults();
                }
            }
            catch (OperationCanceledException)
            {
                if (scanCancellation == cancellation)
                {
                    statusLabel.Text = "Check stopped.";
                }
            }
            catch (Exception exception)
            {
                if (scanCancellation == cancellation)
                {
                    statusLabel.Text = "Check failed.";
                    MessageBox.Show(this, exception.Message, ApplicationTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                if (scanCancellation == cancellation)
                {
                    scanCancellation = null;
                    UpdateCommandStates();
                }
                cancellation.Dispose();
            }
        }

        private void ShowResults()
        {
            List<CheckResult> visibleResults = currentResults
                .Where(result => !hideUnlockedCheckBox.Checked || result.Status != LockStatus.NotLocked)
                .OrderBy(result => result.Status)
                .ThenBy(result => result.FolderPath, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(result => result.ItemName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            resultsList.BeginUpdate();
            resultsList.Items.Clear();
            foreach (CheckResult result in visibleResults)
            {
                resultsList.Items.Add(CreateListItem(result));
            }
            resultsList.EndUpdate();
            statusLabel.Text = BuildSummary(currentResults.Count - visibleResults.Count);
        }

        private static ListViewItem CreateListItem(CheckResult result)
        {
            ListViewItem item = new ListViewItem(CheckResult.GetLabel(result.Status), (int)result.Status);
            item.SubItems.Add(result.ItemName);
            item.SubItems.Add(result.LockedBy ?? string.Empty);
            item.SubItems.Add(result.WindowsAccount ?? string.Empty);
            item.SubItems.Add(result.LockedSince.HasValue ? result.LockedSince.Value.ToString("g") : string.Empty);
            item.SubItems.Add(result.FolderPath);
            item.SubItems.Add(result.LockFileName ?? string.Empty);
            item.BackColor = GetRowColor(result.Status);
            item.ToolTipText = string.IsNullOrEmpty(result.Detail)
                ? CheckResult.GetDescription(result.Status)
                : CheckResult.GetDescription(result.Status) + Environment.NewLine + result.Detail;
            item.Tag = result;
            return item;
        }

        private string BuildSummary(int hiddenTotal)
        {
            if (currentInputs.Count == 0)
            {
                return IdleHint;
            }
            int documentTotal = currentResults.Count(result => result.Status != LockStatus.OrphanLockFile && result.Status != LockStatus.Error);
            List<string> findings = new List<string>();
            AddFinding(findings, LockStatus.Locked, "locked", "locked");
            AddFinding(findings, LockStatus.InUseWithoutLockFile, "in use without a lock file", "in use without a lock file");
            AddFinding(findings, LockStatus.PossiblyStale, "possibly stale lock", "possibly stale locks");
            AddFinding(findings, LockStatus.OrphanLockFile, "orphan lock file", "orphan lock files");
            AddFinding(findings, LockStatus.Error, "error", "errors");

            StringBuilder summary = new StringBuilder();
            summary.AppendFormat("Checked {0} at {1:T}", Pluralize(documentTotal, "document", "documents"), lastCheckedAt);
            summary.Append(findings.Count > 0 ? ": " + string.Join(", ", findings) + "." : ". Nothing is locked.");
            if (hiddenTotal > 0)
            {
                summary.Append(" " + Pluralize(hiddenTotal, "unlocked document", "unlocked documents") + " hidden.");
            }
            return summary.ToString();
        }

        private void AddFinding(List<string> findings, LockStatus status, string singular, string plural)
        {
            int statusTotal = currentResults.Count(result => result.Status == status);
            if (statusTotal > 0)
            {
                findings.Add(Pluralize(statusTotal, singular, plural));
            }
        }

        private static string Pluralize(int amount, string singular, string plural)
        {
            return amount + " " + (amount == 1 ? singular : plural);
        }

        private void UpdateCommandStates()
        {
            bool isScanning = scanCancellation != null;
            refreshButton.Text = isScanning ? "S&top" : "&Refresh";
            refreshButton.Enabled = isScanning || currentInputs.Count > 0;
            clearButton.Enabled = isScanning || currentInputs.Count > 0;
            resultsList.Cursor = isScanning ? Cursors.AppStarting : Cursors.Default;
        }

        private void OpenSelectedFolder()
        {
            if (resultsList.SelectedItems.Count == 0)
            {
                return;
            }
            CheckResult result = (CheckResult)resultsList.SelectedItems[0].Tag;
            try
            {
                if (File.Exists(result.ItemPath))
                {
                    Process.Start("explorer.exe", "/select,\"" + result.ItemPath + "\"");
                }
                else if (Directory.Exists(result.ItemPath))
                {
                    Process.Start("explorer.exe", "\"" + result.ItemPath + "\"");
                }
                else if (Directory.Exists(result.FolderPath))
                {
                    Process.Start("explorer.exe", "\"" + result.FolderPath + "\"");
                }
            }
            catch (Win32Exception exception)
            {
                MessageBox.Show(this, exception.Message, ApplicationTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopyRows(IEnumerable<ListViewItem> items)
        {
            List<ListViewItem> itemsToCopy = items.ToList();
            if (itemsToCopy.Count == 0)
            {
                return;
            }
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(string.Join("\t", resultsList.Columns.Cast<ColumnHeader>().Select(column => column.Text)));
            foreach (ListViewItem item in itemsToCopy)
            {
                IEnumerable<string> cellTexts = item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(subItem => subItem.Text);
                builder.AppendLine(string.Join("\t", cellTexts));
            }
            try
            {
                Clipboard.SetText(builder.ToString());
            }
            catch (ExternalException exception)
            {
                string errorMessage = "Could not copy to the clipboard: " + exception.Message;
                MessageBox.Show(this, errorMessage, ApplicationTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static Icon LoadApplicationIcon()
        {
            using (Stream iconStream = typeof(MainForm).Assembly.GetManifestResourceStream(IconResourceName))
            {
                return iconStream == null ? null : new Icon(iconStream);
            }
        }

        private static Color GetStatusColor(LockStatus status)
        {
            switch (status)
            {
                case LockStatus.Locked:
                    return Color.FromArgb(220, 38, 38);
                case LockStatus.InUseWithoutLockFile:
                    return Color.FromArgb(234, 88, 12);
                case LockStatus.PossiblyStale:
                case LockStatus.OrphanLockFile:
                    return Color.FromArgb(217, 119, 6);
                case LockStatus.Error:
                    return Color.FromArgb(113, 113, 122);
                default:
                    return Color.FromArgb(22, 163, 74);
            }
        }

        private static Color GetRowColor(LockStatus status)
        {
            switch (status)
            {
                case LockStatus.Locked:
                    return Color.FromArgb(254, 226, 226);
                case LockStatus.InUseWithoutLockFile:
                    return Color.FromArgb(255, 237, 213);
                case LockStatus.PossiblyStale:
                case LockStatus.OrphanLockFile:
                    return Color.FromArgb(254, 243, 199);
                default:
                    return SystemColors.Window;
            }
        }
    }
}
