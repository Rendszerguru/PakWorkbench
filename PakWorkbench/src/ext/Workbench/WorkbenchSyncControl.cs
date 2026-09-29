using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using PakWorkbench.src.ext.Workbench;
using PakWorkbench.src.ext.Preview.Xfusion;

namespace PakWorkbench.src.ext.Workbench;

#region Main Workbench Sync Control

public sealed partial class WorkbenchSyncControl : UserControl
{
	#region Helper Structs & Constant Sets

	private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
	private static readonly char[] _pathSeparators = ['/', '\\'];

	#endregion

	#region Regular Expressions

	[GeneratedRegex(@"@""(?<file>[^""]+),(?<line>\d+)""", RegexOptions.IgnoreCase)]
	private static partial Regex EnfusionScriptErrorRegex();

	[GeneratedRegex(@"(?<file>(?:[a-zA-Z]:\\[^:\n\r\(\)""]+|[a-zA-Z0-9_\-\\/]+\.[a-zA-Z0-9]+))[\(:]\s*(?<line>\d+)(?:[\:,]\s*(?<col>\d+))?", RegexOptions.IgnoreCase)]
	private static partial Regex FileLineColRegex();

	[GeneratedRegex(@"""Line""\s*:\s*(?<line>\d+)", RegexOptions.IgnoreCase)]
	private static partial Regex JsonLineRegex();

	[GeneratedRegex(@"""File""\s*:\s*""(?<file>[^""]+)""", RegexOptions.IgnoreCase)]
	private static partial Regex JsonFileRegex();

	[GeneratedRegex(@"\bline\s*(?<line>\d+)\b", RegexOptions.IgnoreCase)]
	private static partial Regex SimpleLineRegex();

	[GeneratedRegex(@"@""[^""]+,\d+""")]
	private static partial Regex EnfusionLineHighlightRegex();

	#endregion

	#region UI Control Fields

	private readonly TextBox _host = new()
	{
		Text = "127.0.0.1",
		Anchor = AnchorStyles.Left | AnchorStyles.Right,
		BackColor = UITheme.BgDarker,
		ForeColor = UITheme.TextMain,
		BorderStyle = BorderStyle.FixedSingle,
		Font = UITheme.MainFont
	};

	private readonly NumericUpDown _port = new()
	{
		Minimum = 1,
		Maximum = 65535,
		Value = 5775,
		Anchor = AnchorStyles.Left | AnchorStyles.Right,
		BackColor = UITheme.BgDarker,
		ForeColor = UITheme.TextMain,
		BorderStyle = BorderStyle.FixedSingle,
		Font = UITheme.MainFont
	};

	private readonly ComboBox _editor = new()
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Anchor = AnchorStyles.Left | AnchorStyles.Right,
		BackColor = UITheme.BgDarker,
		ForeColor = UITheme.TextMain,
		FlatStyle = FlatStyle.Flat,
		Font = UITheme.MainFont
	};

	private readonly TextBox _editorPath = new()
	{
		Anchor = AnchorStyles.Left | AnchorStyles.Right,
		BackColor = UITheme.BgDarker,
		ForeColor = UITheme.TextMain,
		BorderStyle = BorderStyle.FixedSingle,
		Font = UITheme.MainFont
	};

	private readonly TextBox _projectRoot = new()
	{
		Anchor = AnchorStyles.Left | AnchorStyles.Right,
		BackColor = UITheme.BgDarker,
		ForeColor = UITheme.TextMain,
		BorderStyle = BorderStyle.FixedSingle,
		Font = UITheme.MainFont
	};

	private readonly TextBox _relativePath = new()
	{
		Text = @"scripts\Game\PakWorkbenchSyncTest.c",
		Anchor = AnchorStyles.Left | AnchorStyles.Right,
		BackColor = UITheme.BgDarker,
		ForeColor = UITheme.TextMain,
		BorderStyle = BorderStyle.FixedSingle,
		Font = UITheme.MainFont
	};

	private readonly TextBox _sourceFile = new()
	{
		Anchor = AnchorStyles.Left | AnchorStyles.Right,
		BackColor = UITheme.BgDarker,
		ForeColor = UITheme.TextMain,
		BorderStyle = BorderStyle.FixedSingle,
		Font = UITheme.MainFont
	};

	private readonly CheckBox _autoValidateOnSave = new()
	{
		Text = "Validate on save watcher sync",
		Checked = true,
		AutoSize = true,
		Anchor = AnchorStyles.Left,
		ForeColor = UITheme.TextMain,
		Font = UITheme.MainFont
	};

	private XFusionPreviewController _previewController = null!;

	private readonly Label _status = new()
	{
		AutoSize = false,
		Dock = DockStyle.Fill,
		Text = " Ready.",
		TextAlign = ContentAlignment.MiddleLeft,
		Padding = new(5, 0, 0, 0),
		ForeColor = UITheme.TextMain,
		Font = UITheme.MainFont
	};

	private Button _btnTestNet = null!;
	private Button _btnSync = null!;
	private Button _btnValidate = null!;
	private Button _btnOpenWorkbench = null!;
	private Button _btnToggleWatcher = null!;
	private FlowLayoutPanel? _panelButtons;
	private TableLayoutPanel? _mainLayout;

	#endregion

	#region Private State Fields

	private FileSystemWatcher? _watcher;
	private System.Threading.Timer? _syncTimer;
	private string? _watchedFile;
	private bool _busy;
	private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WorkbenchSyncControl.json");

	private readonly ToolTip _syncToolTip = new();
	private float _splitterRatio = 0.40f;
	private bool _isUserResizingSplitter = false;
	private bool _isLayoutReady = false;

	private string? _lastLoggedJumpFile;
	private int _lastLoggedJumpLine = -1;
	private int _lastLoggedJumpColumn = -1;
	private int _lastHoveredLogLine = -1;

	// Hybrid Jump Registry
	private readonly EditorProviderRegistry _jumpRegistry = new(new FallbackJumpProvider());
	private List<EditorCliConfig> _currentEditorConfigs = [];

	#endregion

	#region Logging & Jump State Helper

	private bool ShouldLogJump(string filePath, int line, int column)
	{
		bool changed = !string.Equals(_lastLoggedJumpFile, filePath, StringComparison.OrdinalIgnoreCase)
			|| _lastLoggedJumpLine != line
			|| _lastLoggedJumpColumn != column;

		if (!changed)
			return false;

		_lastLoggedJumpFile = filePath;
		_lastLoggedJumpLine = line;
		_lastLoggedJumpColumn = column;

		return true;
	}

	#endregion

	#region Constructor & Initialization

	public WorkbenchSyncControl()
	{
		SuspendLayout();

		AutoScaleDimensions = new(96F, 96F);
		AutoScaleMode = AutoScaleMode.Dpi;

		Dock = DockStyle.Fill;
		Font = UITheme.MainFont;
		BackColor = UITheme.BgMain;
		ForeColor = UITheme.TextMain;

		BuildEditorList();
		BuildUi();
		LoadConfig();

		ResumeLayout(true);
		PerformLayout();
	}

	public void Cleanup()
	{
		StopWatcher();
		SaveConfig();
		SavePrefs();
	}

	#endregion

	#region Public Properties & Methods

	public string PhysicalTargetPath { get; private set; } = string.Empty;

	public static bool IsSyncSupported(string fileName)
	{
		if (string.IsNullOrEmpty(fileName)) return false;
		return fileName.EndsWith(".c", StringComparison.OrdinalIgnoreCase);
	}

	public void SetTargetScript(string physicalPath, string relativePath)
	{
		if (IsDisposed) return;
		if (InvokeRequired)
		{
			BeginInvoke(() => SetTargetScript(physicalPath, relativePath));
			return;
		}

		PhysicalTargetPath = physicalPath;
		_sourceFile.Text = physicalPath;
		_relativePath.Text = relativePath.Replace('/', '\\');

		Log($"New target script set: {_relativePath.Text} (Physical: {physicalPath})");
	}

	#endregion

	#region Editor Discovery & Auto-Detection

	private void BuildEditorList()
	{
		_editor.Items.Clear();
		_editor.Items.Add("System Default");

		var detectedEditors = EditorDiscovery.DiscoverInstalledEditors();

		foreach (var editor in detectedEditors)
		{
			_editor.Items.Add(editor);
		}

		_editor.Items.Add("Custom executable");
		_editor.SelectedIndex = 0;
	}

	private void AutoDetectEditors()
	{
		BuildEditorList();

		if (_editor.Items.Count > 2)
		{
			_editor.SelectedIndex = 1;
			if (_editor.SelectedItem is EditorInfo selected)
			{
				_editorPath.Text = selected.Path;
				Log($"Auto-detected editor: {selected.Name} ({selected.Path})");
			}
		}
		else
		{
			Log("No common editor was auto-detected. Use Browse...");
		}
	}

	#endregion

	#region UI Construction & Layout

	private void BuildUi()
	{
		SplitContainer mainSplit = new()
		{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Horizontal,
			FixedPanel = FixedPanel.Panel1,
			Panel1MinSize = 220,
			Panel2MinSize = 100,
			BackColor = UITheme.BgMain
		};
		mainSplit.Panel1.Padding = new(6, 6, 6, 0);
		mainSplit.Panel2.Padding = new(6, 6, 6, 0);

		Panel scrollContainer = new()
		{
			Dock = DockStyle.Fill,
			AutoScroll = true,
			BackColor = UITheme.BgPanel,
			BorderStyle = BorderStyle.FixedSingle
		};

		_mainLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			RowCount = 3,
			Padding = new(8, 6, 8, 4),
			BackColor = UITheme.BgPanel
		};
		_mainLayout.RowStyles.Add(new(SizeType.AutoSize));
		_mainLayout.RowStyles.Add(new(SizeType.AutoSize));
		_mainLayout.RowStyles.Add(new(SizeType.AutoSize));

		Panel groupConnection = CreateSectionPanel("⚙ Connection Details", CreateConnectionTable());
		_mainLayout.Controls.Add(groupConnection, 0, 0);

		Panel groupPaths = CreateSectionPanel("📁 Project & Paths", CreatePathsTable());
		_mainLayout.Controls.Add(groupPaths, 0, 1);

		_panelButtons = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Padding = new(0, 2, 0, 2),
			Margin = new(0),
			WrapContents = true,
			BackColor = UITheme.BgPanel
		};

		_btnTestNet = CreateActionButton("1. Test NET API", async () => await RunSafeAsync(TestNetApiAsync, 1));
		_btnSync = CreateActionButton("2. Copy + Open + Sync", async () => await RunSafeAsync(SyncNowAsync, 2), isPrimary: true);
		_btnValidate = CreateActionButton("3. Validate Scripts", async () => await RunSafeAsync(ValidateAsync, 3));
		_btnOpenWorkbench = CreateActionButton("Open in Workbench", async () => await RunSafeAsync(OpenInWorkbenchAsync));
		_btnToggleWatcher = CreateActionButton("Start / Stop Save Watcher", () => { ToggleWatcher(); return Task.CompletedTask; });

		_panelButtons.Controls.Add(_btnTestNet);
		_panelButtons.Controls.Add(_btnSync);
		_panelButtons.Controls.Add(_btnValidate);
		_panelButtons.Controls.Add(_btnOpenWorkbench);
		_panelButtons.Controls.Add(_btnToggleWatcher);

		_mainLayout.Controls.Add(_panelButtons, 0, 2);

		scrollContainer.Controls.Add(_mainLayout);
		mainSplit.Panel1.Controls.Add(scrollContainer);

		Panel logHeaderPanel = new()
		{
			Dock = DockStyle.Top,
			Height = 28,
			BackColor = UITheme.BgDark
		};

		Button btnClearLog = new()
		{
			Text = "Clear Log",
			Dock = DockStyle.Right,
			AutoSize = true,
			FlatStyle = FlatStyle.Flat,
			BackColor = UITheme.BgDark,
			ForeColor = UITheme.TextMain,
			Font = UITheme.MainFont,
			Cursor = Cursors.Hand
		};
		btnClearLog.FlatAppearance.BorderColor = UITheme.BgSelected;
		btnClearLog.FlatAppearance.MouseOverBackColor = UITheme.BgHover;
		btnClearLog.FlatAppearance.MouseDownBackColor = UITheme.BgDarker;

		btnClearLog.Click += (s, e) =>
		{
			if (_previewController.UIControl is XFusion xf)
			{
				xf.Clear();
				xf.Invalidate();
			}
		};

		Label lblLogTitle = new()
		{
			Text = " 📜 Output Log (Double-click error line to jump to editor)",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = UITheme.Accent,
			Font = UITheme.MainFontBold,
			BackColor = Color.Transparent
		};

		logHeaderPanel.Controls.Add(btnClearLog);
		logHeaderPanel.Controls.Add(lblLogTitle);

		_previewController = new XFusionPreviewController(UITheme.CodeFont);
		_previewController.Initialize();
		_previewController.ConfigureForSyncLog();

		_syncToolTip.OwnerDraw = true;
		_syncToolTip.BackColor = UITheme.BgDarker;
		_syncToolTip.ForeColor = UITheme.TextMain;

		_syncToolTip.Popup += (s, e) =>
		{
			e.ToolTipSize = Size.Add(TextRenderer.MeasureText(_syncToolTip.GetToolTip(e.AssociatedControl), UITheme.MainFont), new Size(12, 10));
		};

        _syncToolTip.Draw += (s, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(UITheme.BgDarker), e.Bounds);
            e.Graphics.DrawRectangle(new Pen(UITheme.BgSelected), 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);

            Rectangle textBounds = new(e.Bounds.X + 6, e.Bounds.Y + 3, e.Bounds.Width - 12, e.Bounds.Height - 6);

            TextRenderer.DrawText(
                e.Graphics,
                e.ToolTipText,
                UITheme.MainFont,
                textBounds,
                UITheme.TextMain,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left
            );
        };

        if (_previewController.UIControl is XFusion xfInstance)
		{
			xfInstance.WordWrap = true;

			xfInstance.HighlightTextInsteadOfBackground = true;
			xfInstance.TopMargin = 8;
			xfInstance.BottomMargin = 4;

			xfInstance.MouseMove += (s, e) =>
			{
				xfInstance.GetLocationFromPoint(e.X, e.Y, out int lineIndex, out int charIdx);
				bool isOverHighlight = false;
				string? toolTipPath = null;

				if (lineIndex >= 0)
				{
					try
					{
						string lineText = "";
						var lines = xfInstance.Lines;
						if (lines is string[] arr && lineIndex < arr.Length) lineText = arr[lineIndex];
						else if (lines is System.Collections.IList list && lineIndex < list.Count) lineText = list[lineIndex]?.ToString() ?? "";

						if (!string.IsNullOrEmpty(lineText))
						{
							var match = EnfusionLineHighlightRegex().Match(lineText);
							if (match.Success)
							{
								if (charIdx >= match.Index && charIdx < match.Index + match.Length)
								{
									isOverHighlight = true;

									if (EnfusionScriptErrorRegex().Match(lineText) is { Success: true } enfMatch)
									{
										string parsedFile = enfMatch.Groups["file"].Value;
										toolTipPath = ResolveFilePath(parsedFile);
									}
								}
							}
						}
					}
					catch { }
				}

				xfInstance.Cursor = isOverHighlight ? Cursors.Hand : Cursors.Default;

				if (isOverHighlight && !string.IsNullOrEmpty(toolTipPath))
				{
					if (_lastHoveredLogLine != lineIndex)
					{
						_lastHoveredLogLine = lineIndex;
						_syncToolTip.Show($"Full path:\n{toolTipPath}", xfInstance, e.X + 15, e.Y + 15, 4000);
					}
				}
				else
				{
					if (_lastHoveredLogLine != -1)
					{
						_syncToolTip.Hide(xfInstance);
						_lastHoveredLogLine = -1;
					}
				}
			};

			xfInstance.MouseLeave += (s, e) =>
			{
				if (_lastHoveredLogLine != -1)
				{
					_syncToolTip.Hide(xfInstance);
					_lastHoveredLogLine = -1;
				}
			};

			xfInstance.MouseClick += (s, e) =>
			{
				if (e.Button != MouseButtons.Left) return;
				xfInstance.GetLocationFromPoint(e.X, e.Y, out int lineIndex, out int charIdx);

				if (lineIndex >= 0)
				{
					try
					{
						string lineText = "";
						var lines = xfInstance.Lines;
						if (lines is string[] arr && lineIndex < arr.Length) lineText = arr[lineIndex];
						else if (lines is System.Collections.IList list && lineIndex < list.Count) lineText = list[lineIndex]?.ToString() ?? "";

						if (!string.IsNullOrWhiteSpace(lineText))
						{
							var match = EnfusionLineHighlightRegex().Match(lineText);
							if (match.Success)
							{
								if (charIdx >= match.Index && charIdx < match.Index + match.Length)
								{
									ParseAndJumpToError(lineText);
								}
							}
						}
					}
					catch { }
				}
			};
		}

		_previewController.UIControl.Dock = DockStyle.None;
		_previewController.UIControl.MouseDoubleClick += OnLogMouseDoubleClick;

		Panel logPanel = new()
		{
			Dock = DockStyle.Fill,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = UITheme.BgPreview
		};

		logPanel.Controls.Add(logHeaderPanel);
		logPanel.Controls.Add(_previewController.UIControl);

		logPanel.Resize += (s, e) =>
		{
			if (_previewController?.UIControl != null)
			{
				int topOffset = logHeaderPanel.Height;
				int availWidth = logPanel.ClientSize.Width;
				int availHeight = Math.Max(10, logPanel.ClientSize.Height - topOffset);

				_previewController.UIControl.SetBounds(0, topOffset, availWidth, availHeight);
				_previewController.UIControl.Refresh();
			}
		};

		mainSplit.Panel2.Controls.Add(logPanel);

		Panel statusPanel = new() { Dock = DockStyle.Bottom, Height = 30, BackColor = UITheme.BgDark };
		statusPanel.Controls.Add(_status);

		Controls.Add(mainSplit);
		Controls.Add(statusPanel);

		void UpdateMinPanelSize()
		{
			if (_mainLayout != null)
			{
				Size prefSize = _mainLayout.GetPreferredSize(new Size(mainSplit.Panel1.Width, 0));
				int requiredHeight = prefSize.Height + 16;
				mainSplit.Panel1MinSize = Math.Max(150, requiredHeight);
			}
		}

		mainSplit.Resize += (s, e) => {
			UpdateMinPanelSize();
			if (!_isUserResizingSplitter && _isLayoutReady && mainSplit.Height > 100) {
				try {
					int targetDistance = (int)(mainSplit.Height * _splitterRatio);
					if (targetDistance >= mainSplit.Panel1MinSize && targetDistance <= mainSplit.Height - mainSplit.Panel2MinSize)
					{
						mainSplit.SplitterDistance = targetDistance;
					}
					else if (targetDistance < mainSplit.Panel1MinSize)
					{
						mainSplit.SplitterDistance = mainSplit.Panel1MinSize;
					}
				} catch { }
			}
		};

		mainSplit.SplitterMoving += (s, e) => {
			_isUserResizingSplitter = true;
			if (mainSplit.Height > 0) {
				int pct = (int)Math.Round((double)e.SplitY / mainSplit.Height * 100);
				Point localCursorPos = mainSplit.PointToClient(Cursor.Position);
				localCursorPos.Offset(15, 5);
				_syncToolTip.Show($"{pct}%", mainSplit, localCursorPos);
			}
		};

		mainSplit.DoubleClick += (s, e) => {
			_splitterRatio = 0.40f;
			UpdateMinPanelSize();
			int targetDistance = (int)(mainSplit.Height * _splitterRatio);
			if (targetDistance < mainSplit.Panel1MinSize) targetDistance = mainSplit.Panel1MinSize;
			try { mainSplit.SplitterDistance = targetDistance; } catch { }

			SavePrefs();

			if (mainSplit.Height > 0) {
				Point localCursorPos = mainSplit.PointToClient(Cursor.Position);
				localCursorPos.Offset(15, 5);
				_syncToolTip.Show($"Reset: 40%", mainSplit, localCursorPos, 1500);
			}
		};

		mainSplit.SplitterMoved += (s, e) => {
			_isUserResizingSplitter = false;
			if (_isLayoutReady && mainSplit.Height > 0)
			{
				_splitterRatio = (float)mainSplit.SplitterDistance / mainSplit.Height;
				SavePrefs();
				_syncToolTip.Hide(mainSplit);
			}
		};

		this.Load += (s, e) => {
			if (string.IsNullOrWhiteSpace(_editorPath.Text))
				AutoDetectEditors();

			this.BeginInvoke((MethodInvoker)delegate {
				UpdateMinPanelSize();
				if (mainSplit.Height > 150)
				{
					int targetDistance = (int)(mainSplit.Height * _splitterRatio);
					if (targetDistance < mainSplit.Panel1MinSize) targetDistance = mainSplit.Panel1MinSize;

					try {
						if (targetDistance < mainSplit.Height - 50)
						{
							mainSplit.SplitterDistance = targetDistance;
						}
					} catch { }
					_isLayoutReady = true;
				}
				else
				{
					_isLayoutReady = true;
				}
			});
		};

		_editor.SelectedIndexChanged += (s, e) =>
		{
			if (_editor.SelectedItem is EditorInfo info)
			{
				_editorPath.Text = info.Path;
			}
		};
	}

	private static Panel CreateSectionPanel(string title, TableLayoutPanel content)
	{
		Panel container = new()
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Margin = new(0, 0, 0, 6)
		};

		Label header = new()
		{
			Text = " " + title,
			Dock = DockStyle.Top,
			Height = 28,
			TextAlign = ContentAlignment.MiddleLeft,
			BackColor = UITheme.BgDark,
			ForeColor = UITheme.Accent,
			Font = UITheme.MainFontBold
		};

		content.Dock = DockStyle.Top;
		content.Padding = new(8, 4, 8, 4);

		container.Controls.Add(content);
		container.Controls.Add(header);

		return container;
	}

	private TableLayoutPanel CreateConnectionTable()
	{
		TableLayoutPanel table = new()
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 4,
			BackColor = UITheme.BgPanel
		};
		table.ColumnStyles.Add(new(SizeType.AutoSize));
		table.ColumnStyles.Add(new(SizeType.Percent, 60f));
		table.ColumnStyles.Add(new(SizeType.AutoSize));
		table.ColumnStyles.Add(new(SizeType.Percent, 40f));

		Label lblHost = new()
		{
			Text = "NET API Host:",
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new(0, 4, 10, 4),
			ForeColor = UITheme.TextMain,
			Font = UITheme.MainFont
		};

		Label lblPort = new()
		{
			Text = "NET API Port:",
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new(15, 4, 10, 4),
			ForeColor = UITheme.TextMain,
			Font = UITheme.MainFont
		};

		_host.Margin = new(0, 2, 10, 2);
		_port.Margin = new(0, 2, 0, 2);

		table.Controls.Add(lblHost, 0, 0);
		table.Controls.Add(_host, 1, 0);
		table.Controls.Add(lblPort, 2, 0);
		table.Controls.Add(_port, 3, 0);

		return table;
	}

	private TableLayoutPanel CreatePathsTable()
	{
		TableLayoutPanel table = CreateConfigTable();

		Button btnDetect = CreateStandardButton("Detect");
		btnDetect.Click += (_, _) => AutoDetectEditors();
		AddRow(table, "Target Editor:", _editor, btnDetect);

		Button btnBrowseEditor = CreateStandardButton("Browse...");
		btnBrowseEditor.Click += (_, _) => BrowseExecutable();
		AddRow(table, "Editor Executable:", _editorPath, btnBrowseEditor);

		Button btnBrowseRoot = CreateStandardButton("Browse...");
		btnBrowseRoot.Click += (_, _) => BrowseFolder(_projectRoot);
		AddRow(table, "Workbench Project Root:", _projectRoot, btnBrowseRoot);

		TableLayoutPanel dualPathsPanel = new()
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 2,
			Margin = new(0, 2, 0, 2),
			BackColor = UITheme.BgPanel
		};
		dualPathsPanel.ColumnStyles.Add(new(SizeType.Percent, 50f));
		dualPathsPanel.ColumnStyles.Add(new(SizeType.Percent, 50f));

		TableLayoutPanel tSource = CreateConfigTable();
		tSource.Margin = new(0, 0, 5, 0);
		Button btnBrowseSource = CreateStandardButton("Browse...");
		btnBrowseSource.Click += (_, _) => BrowseFile(_sourceFile);
		AddRow(tSource, "Source .c File (Work file):", _sourceFile, btnBrowseSource);

		TableLayoutPanel tRel = CreateConfigTable();
		tRel.Margin = new(5, 0, 0, 0);
		Button btnBrowseRel = CreateStandardButton("Browse...");
		btnBrowseRel.Click += (_, _) => BrowseRelativeScript();
		AddRow(tRel, "Script Relative Path:", _relativePath, btnBrowseRel);

		dualPathsPanel.Controls.Add(tSource, 0, 0);
		dualPathsPanel.Controls.Add(tRel, 1, 0);

		int rowDual = table.RowCount;
		table.RowCount++;
		table.RowStyles.Add(new(SizeType.AutoSize));
		table.Controls.Add(dualPathsPanel, 0, rowDual);
		table.SetColumnSpan(dualPathsPanel, 3);

		AddRow(table, "Validation Options:", _autoValidateOnSave, null);

		return table;
	}

	private static TableLayoutPanel CreateConfigTable()
	{
		TableLayoutPanel table = new()
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 3,
			BackColor = UITheme.BgPanel
		};
		table.ColumnStyles.Add(new(SizeType.AutoSize));
		table.ColumnStyles.Add(new(SizeType.Percent, 100f));
		table.ColumnStyles.Add(new(SizeType.AutoSize));
		return table;
	}

	private static void AddRow(TableLayoutPanel table, string labelText, Control inputControl, Control? buttonControl)
	{
		int row = table.RowCount;
		table.RowCount++;
		table.RowStyles.Add(new(SizeType.AutoSize));

		Label lbl = new()
		{
			Text = labelText,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new(0, 4, 15, 4),
			ForeColor = UITheme.TextMain,
			Font = UITheme.MainFont
		};

		inputControl.Margin = new(0, 2, 6, 2);

		table.Controls.Add(lbl, 0, row);
		table.Controls.Add(inputControl, 1, row);

		if (buttonControl != null)
		{
			buttonControl.Margin = new(0, 2, 0, 2);
			table.Controls.Add(buttonControl, 2, row);
		}
		else
		{
			table.Controls.Add(new Panel { Width = 0, Height = 0, Margin = new(0) }, 2, row);
		}
	}

	private static Button CreateStandardButton(string text)
	{
		Button btn = new()
		{
			Text = text,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			MinimumSize = new(85, 25),
			Padding = new(8, 2, 8, 2),
			Anchor = AnchorStyles.Left | AnchorStyles.Right,
			BackColor = UITheme.BgSelected,
			ForeColor = UITheme.TextMain,
			FlatStyle = FlatStyle.Flat,
			Font = UITheme.MainFont
		};
		btn.FlatAppearance.BorderSize = 0;
		btn.FlatAppearance.MouseOverBackColor = UITheme.BgHover;
		btn.FlatAppearance.MouseDownBackColor = UITheme.BgDarker;
		return btn;
	}

	private static Button CreateActionButton(string text, Func<Task> action, bool isPrimary = false)
	{
		Button btn = new()
		{
			Text = text,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			MinimumSize = new(120, 32),
			Padding = new(10, 4, 10, 4),
			Margin = new(0, 2, 8, 2),
			BackColor = isPrimary ? UITheme.Accent : UITheme.BgSelected,
			ForeColor = Color.White,
			FlatStyle = FlatStyle.Flat,
			Font = isPrimary ? UITheme.MainFontBold : UITheme.MainFont
		};
		btn.FlatAppearance.BorderSize = 0;
		btn.FlatAppearance.MouseOverBackColor = isPrimary ? UITheme.AccentHover : UITheme.BgHover;
		btn.FlatAppearance.MouseDownBackColor = UITheme.BgDarker;

		btn.Click += async (_, _) =>
		{
			btn.Enabled = false;
			try
			{
				await action();
			}
			finally
			{
				btn.Enabled = true;
			}
		};

		return btn;
	}

	#endregion

	#region Configuration & Persistence

	private void LoadConfig()
	{
		AppConfig config = new();
		bool rewriteNeeded = false;

		try
		{
			if (File.Exists(_configPath))
			{
				string json = File.ReadAllText(_configPath);
				var loaded = JsonSerializer.Deserialize<AppConfig>(json);
				if (loaded != null)
				{
					config = loaded;
					if ((config.EditorConfigs == null || config.EditorConfigs.Count == 0) && json.Contains("\"EditorConfigs\""))
					{
						try
						{
							using var doc = JsonDocument.Parse(json);
							if (doc.RootElement.TryGetProperty("EditorConfigs", out var legacyProp))
							{
								var legacyList = JsonSerializer.Deserialize<List<EditorCliConfig>>(legacyProp.GetRawText());
								if (legacyList != null && legacyList.Count > 0)
								{
									config.EditorConfigs = legacyList;
								}
							}
						}
						catch { }
					}
				}
			}
		}
		catch { }

		if (config.EditorConfigs == null || config.EditorConfigs.Count == 0)
		{
			config.EditorConfigs = EditorConfigDefaults.GetDefaultEditorConfigs();
			rewriteNeeded = true;
		}

		_currentEditorConfigs = config.EditorConfigs;
		_jumpRegistry.UpdateCliConfigs(_currentEditorConfigs);
		EditorDiscovery.RegisterKnownExecutables(_currentEditorConfigs);

		if (rewriteNeeded || !File.Exists(_configPath))
		{
			try { File.WriteAllText(_configPath, JsonSerializer.Serialize(config, _jsonOptions)); } catch { }
		}

		BuildEditorList();

		if (!string.IsNullOrWhiteSpace(config.EditorPath) && File.Exists(config.EditorPath))
		{
			string customName = EditorDiscovery.GetFriendlyName(Path.GetFileName(config.EditorPath), config.EditorPath);
			bool isKnown = false;

			foreach (var item in _editor.Items)
			{
				if (item is EditorInfo info && string.Equals(info.Path, config.EditorPath, StringComparison.OrdinalIgnoreCase))
				{
					isKnown = true;
					break;
				}
			}

			if (!isKnown)
			{
				_editor.Items.Insert(_editor.Items.Count - 1, new EditorInfo(customName, config.EditorPath));
			}
		}

		_host.Text = string.IsNullOrWhiteSpace(config.Host) ? "127.0.0.1" : config.Host;
		_port.Value = config.Port >= _port.Minimum && config.Port <= _port.Maximum ? config.Port : 5775;

		if (config.EditorIndex >= 0 && config.EditorIndex < _editor.Items.Count)
			_editor.SelectedIndex = config.EditorIndex;

		_editorPath.Text = config.EditorPath ?? "";
		_projectRoot.Text = config.ProjectRoot ?? "";
		_relativePath.Text = string.IsNullOrWhiteSpace(config.RelativePath) ? @"scripts\Game\PakWorkbenchSyncTest.c" : config.RelativePath;
		_sourceFile.Text = config.SourceFile ?? "";
		_autoValidateOnSave.Checked = config.AutoValidateOnSave;

		LoadPrefs();
	}

	private void LoadPrefs()
	{
		try
		{
			int savedPct = PlayerPrefs.GetInt("WorkbenchSync_SplitterPct", 40);
			if (savedPct < 10) savedPct = 10;
			if (savedPct > 90) savedPct = 90;
			_splitterRatio = savedPct / 100.0f;
		}
		catch
		{
			_splitterRatio = 0.40f;
		}
	}

	private void SaveConfig()
	{
		try
		{
			var config = new AppConfig
			{
				Host = _host.Text,
				Port = _port.Value,
				EditorIndex = _editor.SelectedIndex,
				EditorPath = _editorPath.Text,
				ProjectRoot = _projectRoot.Text,
				RelativePath = _relativePath.Text,
				SourceFile = _sourceFile.Text,
				AutoValidateOnSave = _autoValidateOnSave.Checked,
				EditorConfigs = _currentEditorConfigs
			};
			File.WriteAllText(_configPath, JsonSerializer.Serialize(config, _jsonOptions));
		}
		catch { }
	}

	private void SavePrefs()
	{
		try
		{
			int pct = (int)Math.Round(_splitterRatio * 100);
			PlayerPrefs.SetInt("WorkbenchSync_SplitterPct", pct);
		}
		catch { }
	}

	#endregion

	#region UI Event Handlers & Dialogs

	private void BrowseExecutable()
	{
		using System.Windows.Forms.OpenFileDialog dialog = new() { Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*", Title = "Select editor executable" };

		string currentPath = _editorPath.Text.Trim();
		if (!string.IsNullOrWhiteSpace(currentPath))
		{
			try
			{
				string? dir = Path.GetDirectoryName(currentPath);
				if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
				{
					dialog.InitialDirectory = dir;
				}
			}
			catch { }
		}

		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			_editorPath.Text = dialog.FileName;

			string customName = EditorDiscovery.GetFriendlyName(Path.GetFileName(dialog.FileName), dialog.FileName);
			var customInfo = new EditorInfo(customName, dialog.FileName);

			bool exists = false;
			for (int i = 0; i < _editor.Items.Count; i++)
			{
				if (_editor.Items[i] is EditorInfo info && string.Equals(info.Path, dialog.FileName, StringComparison.OrdinalIgnoreCase))
				{
					_editor.SelectedIndex = i;
					exists = true;
					break;
				}
			}

			if (!exists)
			{
				_editor.Items.Insert(_editor.Items.Count - 1, customInfo);
				_editor.SelectedItem = customInfo;
			}
		}
	}

	private void BrowseFolder(TextBox target)
	{
		using FolderBrowserDialog dialog = new() { Description = "Select the Workbench project root" };

		string currentPath = target.Text.Trim();
		if (Directory.Exists(currentPath))
		{
			dialog.SelectedPath = currentPath;
		}

		if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
	}

	private void BrowseFile(TextBox target)
	{
		using System.Windows.Forms.OpenFileDialog dialog = new() { Filter = "Enforce Script (*.c)|*.c|All files (*.*)|*.*", Title = "Select source .c file" };
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			target.Text = dialog.FileName;

			if (target == _sourceFile)
			{
				AutoCalculateRelativePath(dialog.FileName);
			}
		}
	}

	private void AutoCalculateRelativePath(string sourcePath)
	{
		if (string.IsNullOrWhiteSpace(sourcePath)) return;

		string rootPath = _projectRoot.Text;

		if (!string.IsNullOrWhiteSpace(rootPath))
		{
			string relative = Path.GetRelativePath(rootPath, sourcePath);

			if (!relative.StartsWith("..") && !Path.IsPathRooted(relative))
			{
				_relativePath.Text = relative;
				return;
			}
		}

		ReadOnlySpan<char> span = sourcePath.AsSpan();

		int index = span.IndexOf("scripts", StringComparison.OrdinalIgnoreCase);

		if (index >= 0)
		{
			_relativePath.Text = span[index..].ToString();
		}
		else
		{
			_relativePath.Text = sourcePath;
		}
	}

	private void BrowseRelativeScript()
	{
		string root = _projectRoot.Text.Trim();
		using System.Windows.Forms.OpenFileDialog dialog = new() { Filter = "Enforce Script (*.c)|*.c|All files (*.*)|*.*", Title = "Select script inside project root" };
		if (Directory.Exists(root)) dialog.InitialDirectory = root;

		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			string selectedPath = dialog.FileName;
			if (!string.IsNullOrWhiteSpace(root) && selectedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				string rel = selectedPath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				_relativePath.Text = rel;
			}
			else
			{
				_relativePath.Text = selectedPath;
			}
		}
	}

	#endregion

	#region Workbench & Network Operations

	private WorkbenchNetApiClient Client
	{
		get
		{
			string host = _host.Text.Trim();
			if (string.IsNullOrWhiteSpace(host))
				throw new ArgumentException("The Host (IP address) field cannot be empty!", "host");
			return new WorkbenchNetApiClient(host, (int)_port.Value);
		}
	}

	private async Task WaitForNetApiAsync(int timeoutSeconds)
	{
		var sw = Stopwatch.StartNew();
		int port = (int)_port.Value;

		while (sw.Elapsed.TotalSeconds < timeoutSeconds)
		{
			try
			{
				using JsonDocument response = await Client.IsWorkbenchRunningAsync();
				if (response != null)
				{
					Log("NET API connection successfully established and responding.");
					return;
				}
			}
			catch
			{
			}

			await Task.Delay(1000);
		}

		Log($"WARNING: Timed out waiting for NET API on port {port}. (It might not be enabled or Workbench is still loading)");
	}

	private async Task EnsureWorkbenchAndSteamRunning()
	{
		bool steamRunning = Process.GetProcessesByName("steam").Length > 0;
		if (!steamRunning)
		{
			Log("Steam is not running. Starting Steam...");
			Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true });

			Log("Waiting for Steam to initialize (this prevents the 'Can't initialize engine' error)...");

			await Task.Run(() =>
			{
				try {
					WorkbenchLauncherModule.WaitForSteamReady(45);
				} catch { }
			});

			Log("Steam wait completed and ready.");
		}

		if (!WorkbenchLauncherModule.IsWorkbenchRunning())
		{
			Log("Workbench is not running. Attempting to start it with the selected project...");
			string wbExe = WorkbenchLauncherModule.GetWorkbenchPathFromRegistry();

			if (!string.IsNullOrEmpty(wbExe) && File.Exists(wbExe))
			{
				string root = _projectRoot.Text.Trim();
				string gproj = Path.Combine(root, "addon.gproj");

				string vanillaGamePath = WorkbenchLauncherModule.GetReforgerVanillaPath();
				string gameData = !string.IsNullOrEmpty(vanillaGamePath) ? Path.Combine(vanillaGamePath, "addons", "data") : "";

				string args = "";
				if (Directory.Exists(root) && File.Exists(gproj))
				{
					string addonsDirArg = !string.IsNullOrEmpty(gameData) ? $"{root},{gameData}" : root;
					args = $"-gproj \"{gproj}\" -addonsDir \"{addonsDirArg}\"";
				}

				Process.Start(new ProcessStartInfo
				{
					FileName = wbExe,
					Arguments = args,
					WorkingDirectory = Path.GetDirectoryName(wbExe) ?? "",
					UseShellExecute = false
				});

				Log($"Launched Workbench: {wbExe} {args}");
				Log($"Waiting for Workbench NET API to initialize (polling port {_port.Value})...");
				await WaitForNetApiAsync(30);
			}
			else
			{
				Log("Could not find Workbench executable in Registry.");
			}
		}
	}

	private async Task TestNetApiAsync()
	{
		await EnsureWorkbenchAndSteamRunning();
		using JsonDocument response = await Client.IsWorkbenchRunningAsync();
		Log("IsWorkbenchRunning response:\n" + JsonSerializer.Serialize(response.RootElement, _jsonOptions));
		SetStatus("NET API reachable.");
	}

	private void EnsureSourceCopiedToDestination()
	{
		if (InvokeRequired)
		{
			Invoke(EnsureSourceCopiedToDestination);
			return;
		}

		string source = _sourceFile.Text.Trim();
		string root = _projectRoot.Text.Trim();
		string relative = NormalizeRelativePath(_relativePath.Text);

		if (!string.IsNullOrWhiteSpace(source) && File.Exists(source) && Directory.Exists(root))
		{
			string destination = Path.GetFullPath(Path.Combine(root, relative));
			if (IsUnderDirectory(destination, root))
			{
				string? destinationDir = Path.GetDirectoryName(destination);
				if (!string.IsNullOrEmpty(destinationDir))
					Directory.CreateDirectory(destinationDir);

				File.Copy(source, destination, true);
				Log($"Synced work file to project:\n  FROM: {source}\n  TO:   {destination}");
			}
		}
	}

	private async Task SyncNowAsync()
	{
		await EnsureWorkbenchAndSteamRunning();
		string source = _sourceFile.Text.Trim();
		string root = _projectRoot.Text.Trim();
		string relative = NormalizeRelativePath(_relativePath.Text);

		if (!Directory.Exists(root))
			throw new DirectoryNotFoundException($"Workbench project root does not exist: {root}");

		string destination = Path.GetFullPath(Path.Combine(root, relative));

		if (!IsUnderDirectory(destination, root))
			throw new InvalidOperationException("The script path escapes the Workbench project root.");

		string? destinationDir = Path.GetDirectoryName(destination);
		if (!string.IsNullOrEmpty(destinationDir))
			Directory.CreateDirectory(destinationDir);

		string fileToEdit;

		if (!string.IsNullOrWhiteSpace(source) && File.Exists(source))
		{
			File.Copy(source, destination, true);
			Log($"Copied:\n  FROM: {source}\n  TO:   {destination}");
			fileToEdit = source;
		}
		else
		{
			if (!File.Exists(destination))
			{
				File.WriteAllText(destination, "// Script created by PakWorkbench\n");
				Log($"Created script file at: {destination}");
			}
			fileToEdit = destination;
		}

		await OpenResourceAsync(relative);
		StartWatcher(fileToEdit);
		LaunchEditor(fileToEdit);

		SetStatus("Script ready in project root and opened in editor.");
	}

	private async Task OpenInWorkbenchAsync()
	{
		await EnsureWorkbenchAndSteamRunning();
		string relative = NormalizeRelativePath(_relativePath.Text);
		await OpenResourceAsync(relative);
		SetStatus("OpenResource sent.");
	}

	private async Task OpenResourceAsync(string relative)
	{
		using JsonDocument response = await Client.OpenResourceAsync(relative);
		Log("OpenResource response:\n" + JsonSerializer.Serialize(response.RootElement, _jsonOptions));
		await Client.BringModuleWindowToFrontAsync("ScriptEditor");
		Log("BringModuleWindowToFront(ScriptEditor) sent.");
	}

	private static string FilterValidationResponse(JsonDocument doc, string relativePath)
	{
		string fileName = Path.GetFileName(relativePath);
		StringBuilder formattedLog = new();
		int issueCount = 0;

		foreach (var prop in doc.RootElement.EnumerateObject())
		{
			bool isError = prop.Name.Equals("Errors", StringComparison.OrdinalIgnoreCase);
			bool isWarning = prop.Name.Equals("Warnings", StringComparison.OrdinalIgnoreCase);

			if ((isError || isWarning) && prop.Value.ValueKind == JsonValueKind.Array)
			{
				string prefix = isError ? "SCRIPT    (E):" : "SCRIPT    (W):";

				foreach (var item in prop.Value.EnumerateArray())
				{
					string fileAbs = item.TryGetProperty("fileAbs", out var f) ? f.GetString() ?? "" : "";
					string file = item.TryGetProperty("file", out var f2) ? f2.GetString() ?? "" : "";

					if (fileAbs.Contains(fileName, StringComparison.OrdinalIgnoreCase) ||
						file.Contains(fileName, StringComparison.OrdinalIgnoreCase))
					{
						string errorMsg = item.TryGetProperty("error", out var e) ? e.GetString() ?? "Unknown issue" : "Unknown issue";
						int line = item.TryGetProperty("line", out var l) ? l.GetInt32() : 0;

						string displayPath = !string.IsNullOrWhiteSpace(file) ? file : fileAbs;

						formattedLog.AppendLine($"{prefix} @\"{displayPath},{line}\": {errorMsg}");
						issueCount++;
					}
				}
			}
		}

		return issueCount == 0 ? "Success: No errors found." : formattedLog.ToString().TrimEnd();
	}

	private static string GetTargetConfiguration(string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath)) return "Game";

		ReadOnlySpan<char> span = relativePath.AsSpan();

		int firstSepIndex = span.IndexOfAny(_pathSeparators);
		if (firstSepIndex < 0) return "Game";

		ReadOnlySpan<char> firstPart = span[..firstSepIndex];

		if (firstPart.Equals("scripts", StringComparison.OrdinalIgnoreCase))
		{
			ReadOnlySpan<char> remainder = span[(firstSepIndex + 1)..];
			int secondSepIndex = remainder.IndexOfAny(_pathSeparators);

			ReadOnlySpan<char> secondPart = secondSepIndex >= 0 ? remainder[..secondSepIndex] : remainder;

			if (secondPart.Equals("WorkbenchGame", StringComparison.OrdinalIgnoreCase))
			{
				return "Workbench";
			}

			if (!secondPart.IsEmpty)
			{
				return secondPart.ToString();
			}
		}

		return "Game";
	}

	private async Task ValidateAsync()
	{
		await EnsureWorkbenchAndSteamRunning();
		EnsureSourceCopiedToDestination();
		string relative = NormalizeRelativePath(_relativePath.Text);

		await OpenResourceAsync(relative);

		string targetConfig = GetTargetConfiguration(relative);

		using JsonDocument response = await Client.ValidateScriptsAsync(targetConfig);

		string filteredResponseJson = FilterValidationResponse(response, relative);

		Log("ValidateScripts response:\n" + filteredResponseJson);
		SetStatus("Validation request completed.");
	}

	#endregion

	#region Editor Jump & Log Execution

	private void LaunchEditor(string file)
	{
		OpenInEditorAtLine(file, 1, 1);
	}

	private void OnLogMouseDoubleClick(object? sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left || _previewController.UIControl is not XFusion xf) return;

		xf.GetLocationFromPoint(e.X, e.Y, out int lineIndex, out _);

		if (lineIndex < 0) return;

		string lineText = xf.Lines switch
		{
			string[] arr when lineIndex < arr.Length => arr[lineIndex],
			System.Collections.IList list when lineIndex < list.Count => list[lineIndex]?.ToString() ?? "",
			_ => ""
		};

		if (!string.IsNullOrWhiteSpace(lineText))
		{
			ParseAndJumpToError(lineText);
		}
	}

	private void ParseAndJumpToError(string lineText)
	{
		string? targetFile = null;
		int lineNumber = -1;
		int colNumber = 1;

		if (EnfusionScriptErrorRegex().Match(lineText) is { Success: true } enfMatch)
		{
			targetFile = enfMatch.Groups["file"].Value;
			_ = int.TryParse(enfMatch.Groups["line"].Value, out lineNumber);
		}
		else if (FileLineColRegex().Match(lineText) is { Success: true } fileLineColMatch)
		{
			targetFile = fileLineColMatch.Groups["file"].Value.Trim('"', '\'');
			_ = int.TryParse(fileLineColMatch.Groups["line"].Value, out lineNumber);

			if (fileLineColMatch.Groups["col"] is { Success: true } colGroup)
			{
				_ = int.TryParse(colGroup.Value, out colNumber);
			}
		}
		else if (JsonLineRegex().Match(lineText) is { Success: true } jsonLineMatch &&
				 int.TryParse(jsonLineMatch.Groups["line"].Value, out lineNumber))
		{
			if (JsonFileRegex().Match(lineText) is { Success: true } jsonFileMatch)
			{
				targetFile = jsonFileMatch.Groups["file"].Value;
			}
		}
		else if (SimpleLineRegex().Match(lineText) is { Success: true } simpleLineMatch)
		{
			_ = int.TryParse(simpleLineMatch.Groups["line"].Value, out lineNumber);
		}

		if (lineNumber <= 0) return;

		string finalPath = ResolveFilePath(targetFile);

		if (!string.IsNullOrEmpty(finalPath) && File.Exists(finalPath))
		{
			OpenInEditorAtLine(finalPath, lineNumber, colNumber);
		}
		else
		{
			Log($"[Jump] Target file could not be found for line {lineNumber}.");
		}
	}

	private string ResolveFilePath(string? targetFile)
	{
		string source = _sourceFile.Text.Trim();
		string root = _projectRoot.Text.Trim();
		string relative = NormalizeRelativePath(_relativePath.Text);
		string rootTarget = Directory.Exists(root) ? Path.GetFullPath(Path.Combine(root, relative)) : "";

		if (string.IsNullOrWhiteSpace(targetFile))
		{
			if (!string.IsNullOrWhiteSpace(source) && File.Exists(source)) return source;
			if (!string.IsNullOrWhiteSpace(rootTarget) && File.Exists(rootTarget)) return rootTarget;
			return "";
		}

		targetFile = targetFile.Trim().Replace('/', Path.DirectorySeparatorChar);

		if (!string.IsNullOrWhiteSpace(source) && File.Exists(source))
		{
			string sourceName = Path.GetFileName(source);
			string targetName = Path.GetFileName(targetFile);

			if (string.Equals(sourceName, targetName, StringComparison.OrdinalIgnoreCase)) return source;
			if (!string.IsNullOrWhiteSpace(relative) && targetFile.EndsWith(relative, StringComparison.OrdinalIgnoreCase)) return source;
		}

		if (File.Exists(targetFile)) return Path.GetFullPath(targetFile);

		if (Directory.Exists(root))
		{
			string combined = Path.GetFullPath(Path.Combine(root, targetFile));
			if (File.Exists(combined)) return combined;
		}

		if (!string.IsNullOrWhiteSpace(source) && File.Exists(source)) return source;
		if (!string.IsNullOrWhiteSpace(rootTarget) && File.Exists(rootTarget)) return rootTarget;

		return "";
	}

	private void OpenInEditorAtLine(string filePath, int line, int column = 1)
	{
		string editor = _editorPath.Text.Trim();
		string fullPath = Path.GetFullPath(filePath);

		if (!File.Exists(fullPath))
		{
			Log($"[Jump] File does not exist: {fullPath}");
			return;
		}

		line = Math.Max(1, line);
		column = Math.Max(1, column);

		// SYSTEM DEFAULT
		if (_editor.SelectedItem?.ToString() == "System Default" || string.IsNullOrWhiteSpace(editor) || !File.Exists(editor))
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = fullPath,
				UseShellExecute = true
			});

			if (ShouldLogJump(fullPath, line, column))
			{
				Log($"Opened {Path.GetFileName(fullPath)} at line {line}, col {column} using Windows default application.");
			}

			return;
		}

		// HYBRID JUMP PROVIDER DELEGATION
		string exeName = Path.GetFileName(editor);
		var provider = _jumpRegistry.GetProvider(exeName);

		bool success = provider.Jump(editor, fullPath, line, column);

		if (!success && exeName.Equals("devenv.exe", StringComparison.OrdinalIgnoreCase))
		{
			Log($"[Jump] Visual Studio jump failed for {Path.GetFileName(fullPath)} at line {line}, col {column}.");
			return;
		}

		if (ShouldLogJump(fullPath, line, column))
		{
			Log($"Opened {Path.GetFileName(fullPath)} at line {line}, col {column} using {exeName}");
		}
	}

	#endregion

	#region File System Watcher & Auto-Sync

	private void ToggleWatcher()
	{
		if (_watcher != null)
		{
			StopWatcher();
			return;
		}

		string source = _sourceFile.Text.Trim();
		string root = _projectRoot.Text.Trim();
		string relative = NormalizeRelativePath(_relativePath.Text);
		string destination = Path.GetFullPath(Path.Combine(root, relative));

		string targetToWatch = (!string.IsNullOrWhiteSpace(source) && File.Exists(source)) ? source : destination;

		if (!File.Exists(targetToWatch))
		{
			MessageBox.Show(this, "Run 'Copy + Open + Sync' first so the file exists.", "Save watcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		StartWatcher(targetToWatch);
	}

	private void StartWatcher(string file)
	{
		StopWatcher();

		_watchedFile = Path.GetFullPath(file);
		string? directory = Path.GetDirectoryName(_watchedFile);
		string fileName = Path.GetFileName(_watchedFile);

		if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
			throw new InvalidOperationException("Cannot determine watched file directory or filename.");

		_watcher = new(directory)
		{
			Filter = fileName,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
			IncludeSubdirectories = false,
			EnableRaisingEvents = true
		};

		_watcher.Changed += OnWatchedFileChanged;
		_watcher.Created += OnWatchedFileChanged;
		_watcher.Renamed += OnWatchedFileRenamed;
		_watcher.Deleted += OnWatchedFileDeleted;

		Log($"Save watcher started: {_watchedFile}");
		Log($"Watching directory: {directory}");
		SetStatus("Watching script for saves.");
	}

	private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
	{
		if (IsWatchedFile(e.FullPath))
		{
			Log($"File system change detected: {e.ChangeType} → {e.FullPath}");
			ScheduleSync();
		}
	}

	private void OnWatchedFileRenamed(object sender, RenamedEventArgs e)
	{
		if (IsWatchedFile(e.FullPath) || IsWatchedFile(e.OldFullPath))
		{
			Log($"File rename detected: {e.OldFullPath} → {e.FullPath}");
			ScheduleSync();
		}
	}

	private void OnWatchedFileDeleted(object sender, FileSystemEventArgs e)
	{
		if (IsWatchedFile(e.FullPath))
		{
			Log($"File delete detected: {e.FullPath}");
			ScheduleSync();
		}
	}

	private bool IsWatchedFile(string path)
	{
		if (string.IsNullOrWhiteSpace(_watchedFile)) return false;
		return string.Equals(Path.GetFullPath(path), _watchedFile, StringComparison.OrdinalIgnoreCase);
	}

	private void StopWatcher()
	{
		if (_watcher != null)
		{
			_watcher.Dispose();
			_watcher = null;
			Log("Save watcher stopped.");
			SetStatus("Watcher stopped.");
		}

		_syncTimer?.Change(Timeout.Infinite, Timeout.Infinite);
		_syncTimer?.Dispose();
		_syncTimer = null;

		_watchedFile = null;
	}

	private void ScheduleSync()
	{
		if (_syncTimer == null)
		{
			_syncTimer = new System.Threading.Timer(_ =>
			{
				if (IsDisposed) return;
				BeginInvoke(async () => await ProcessScheduledSyncAsync());
			}, null, 300, Timeout.Infinite);
		}
		else
		{
			_syncTimer.Change(300, Timeout.Infinite);
		}
	}

	private async Task ProcessScheduledSyncAsync()
	{
		if (_busy || _watchedFile == null) return;
		SetWorkflowState(true);

		try
		{
			if (!File.Exists(_watchedFile))
			{
				Log($"File is not available yet: {_watchedFile}");
				return;
			}

			Log($"File changed: {_watchedFile}");
			EnsureSourceCopiedToDestination();

			bool autoValidate = _autoValidateOnSave.Checked;
			string relativePathString = NormalizeRelativePath(_relativePath.Text);

			if (autoValidate)
			{
				string targetConfig = GetTargetConfiguration(relativePathString);
				using JsonDocument response = await Client.ValidateScriptsAsync(targetConfig);

				string filteredResponseJson = FilterValidationResponse(response, relativePathString);

				Log("Automatic Workbench validation:\n" + filteredResponseJson);
				SetStatus("Saved → Workbench validation requested.");
			}
			else
			{
				SetStatus("Saved → File synced to project (Validation skipped).");
			}
		}
		catch (Exception ex)
		{
			Log("Automatic sync error: " + ex.Message);
		}
		finally
		{
			SetWorkflowState(false);
		}
	}

	#endregion

	#region UI Helpers & Utility Functions

	private void SetWorkflowState(bool isBusy, int completedStep = 0)
	{
		if (IsDisposed) return;
		if (InvokeRequired)
		{
			BeginInvoke(() => SetWorkflowState(isBusy, completedStep));
			return;
		}

		_busy = isBusy;
		if (_panelButtons != null)
		{
			_panelButtons.Enabled = !isBusy;
		}

		if (!isBusy && completedStep > 0)
		{
			ResetButtonStyles();

			if (completedStep == 1 && _btnSync != null)
			{
				HighlightButton(_btnSync);
			}
			else if (completedStep == 2 && _btnValidate != null)
			{
				HighlightButton(_btnValidate);
			}
		}
	}

	private void ResetButtonStyles()
	{
		SetButtonStyle(_btnTestNet, isPrimary: false);
		SetButtonStyle(_btnSync, isPrimary: false);
		SetButtonStyle(_btnValidate, isPrimary: false);
	}

	private static void HighlightButton(Button btn)
	{
		SetButtonStyle(btn, isPrimary: true);
	}

	private static void SetButtonStyle(Button? btn, bool isPrimary)
	{
		if (btn == null) return;
		btn.BackColor = isPrimary ? UITheme.Accent : UITheme.BgSelected;
		btn.Font = isPrimary ? UITheme.MainFontBold : UITheme.MainFont;
		btn.FlatAppearance.MouseOverBackColor = isPrimary ? UITheme.AccentHover : UITheme.BgHover;
	}

	private async Task RunSafeAsync(Func<Task> action, int stepIndex = 0)
	{
		if (_busy) return;
		SetWorkflowState(true);

		try
		{
			await action();
			SetWorkflowState(false, stepIndex);
		}
		catch (ArgumentException ex) when (ex.ParamName == "host")
		{
			SetStatus("Invalid Host.");
			Log("ERROR: The Host field was empty during the connection attempt.");
			MessageBox.Show(this, "The Host (IP address) field cannot be empty!\nPlease enter a valid IP address (e.g. 127.0.0.1).", "Missing Information", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
		catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
		{
			SetStatus("Connection refused.");
			Log($"ERROR: Workbench is not running or NET API is disabled/unreachable on port {_port.Value}");
			MessageBox.Show(this, $"A connection could not be established because the target machine actively refused it.\n\nMake sure Arma Reforger Workbench is running and NET API is enabled on the specified port ({_port.Value}).", "Workbench Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
		catch (Exception ex)
		{
			SetStatus("Error.");
			Log("ERROR:\n" + ex.ToString());
			MessageBox.Show(this, ex.Message, "PakWorkbench Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
		finally
		{
			if (_busy) SetWorkflowState(false);
		}
	}

	private void Log(string text)
	{
		if (IsDisposed) return;
		if (InvokeRequired)
		{
			BeginInvoke(() => Log(text));
			return;
		}
		_previewController.AppendBatchText($"[{DateTime.Now:HH:mm:ss}] {text}");
	}

	private void SetStatus(string text)
	{
		if (IsDisposed) return;
		if (InvokeRequired)
		{
			BeginInvoke(() => SetStatus(text));
			return;
		}
		_status.Text = " " + text;
	}

	private static string NormalizeRelativePath(string relativePath)
	{
		string trimmed = relativePath.Trim().TrimStart('\\', '/');
		return trimmed.Replace('/', Path.DirectorySeparatorChar);
	}

	private static bool IsUnderDirectory(string path, string parentDirectory)
	{
		string fullPath = Path.GetFullPath(path);
		string fullParent = Path.GetFullPath(parentDirectory);

		if (!fullParent.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
			fullParent += Path.DirectorySeparatorChar;

		return fullPath.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase);
	}

	private static string QuoteArgument(string arg) => $"\"{arg.Replace("\"", "\\\"")}\"";

	#endregion
}

#endregion
