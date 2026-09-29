#define USE_XFUSION
#define ENABLE_GRAPH_PLUGIN

using PakWorkbench.src;
using PakWorkbench.src.ext.Enfusion;
using PakWorkbench.src.ext.Preview.Xfusion;
using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Linq;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;

namespace PakWorkbench.src.ext
{
	public partial class DeepSearchControl : UserControl
	{
		[LibraryImport("user32.dll", EntryPoint = "SendMessageW", SetLastError = true)]
		private static partial IntPtr SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

		private static IntPtr SendMessage(IntPtr hWnd, int wMsg, bool wParam, int lParam)
		{
			return SendMessage(hWnd, wMsg, (IntPtr)(wParam ? 1 : 0), (IntPtr)lParam);
		}

		private const int WM_SETREDRAW = 11;

		private List<Pak> loadedPaks;
		private ModernTextBox searchInput = null!;
		private ModernTextBox searchInputVar = null!;
		private ComboBox txtExtensions = null!;
		private ModernTextBox txtExclude = null!;
		private Button btnSearch = null!;
		private Button btnHelp = null!;
		private ListView resultsList = null!;
		private Label statusLabel = null!;
		private ContextMenuStrip resultsContextMenu = null!;

#if ENABLE_GRAPH_PLUGIN
		private DeepSearchGraphPlugin? _GraphPlugin;
#endif

		private ModernCheckBox chkMatchCase = null!;
		private ModernCheckBox chkWholeWord = null!;
		private ModernCheckBox chkRegex = null!;
		private ModernCheckBox chkMultiLine = null!;
		private ModernCheckBox chkResolveST = null!;

		private Button btnHelperStart = null!;
		private Button btnHelperEnd = null!;
		private Button btnHelperNum = null!;
		private Button btnHelperAny = null!;

		private XFusionPreviewController _previewController = null!;

		private int sortColumn = -1;
		private string _preStExtensions = "";

		private TableLayoutPanel mainLayout = null!;
		private FlowLayoutPanel row1Layout = null!;
		private FlowLayoutPanel row2Panel = null!;
		private FlowLayoutPanel graphViewContainer = null!;
		private SplitContainer mainSplit = null!;

		private Label lblHint = null!;
		private Label lblExt = null!;
		private Label lblExcl = null!;
		private ToolTip toolTipHelp = null!;
		private string _currentPopupText = "";
		private bool isUserResizingSplitter = false;

		private CancellationTokenSource? _searchCts;
		private bool _isSearching = false;

		private bool _isLayoutReady = false;
		private float _splitterRatio = 0.60f;

		private List<string> _searchHistory = [];
		private int _historyIndex = -1;
		private List<string> _searchHistoryVar = [];
		private int _historyIndexVar = -1;

		private static readonly string[] _splitNewLines = ["\r\n", "\n"];
		private static readonly string[] _splitHistory = ["|||"];
		private static readonly char[] _splitExts = [',', ' '];
		private static readonly char[] _splitExcludes = [',', ';'];
		private static readonly char[] _splitCharsNewLines = ['\r', '\n'];

		public class SearchResultInfo
		{
			public Pak SourcePak { get; set; } = null!;
			public Pak Entry { get; set; } = null!;
			public PakEntryFile EntryFile { get; set; } = null!;
			public int LineNumber { get; set; }
			public int LineNumberEnd { get; set; } = -1;
			public string SearchTerm { get; set; } = string.Empty;
			public string SearchTermVar { get; set; } = string.Empty;
			public string FileName { get; set; } = string.Empty;
		}

		public DeepSearchControl()
		{
			loadedPaks = [];
			this.Dock = DockStyle.Fill;
			this.BackColor = UITheme.BgMain;
			this.ForeColor = UITheme.TextMain;

			InitializeToolTipHelp();
			InitializeMainLayoutAndInputs();
			InitializeHelperButtons();
			InitializeExtensionsAndResults();
			InitializePreviewPanel();
			FinalizeInitialization();
			LoadSavedSettingsAndEvents();
		}

		public class ModernCheckBox : CheckBox
		{
			public ModernCheckBox()
			{
				this.FlatStyle = FlatStyle.Flat;
				this.FlatAppearance.BorderSize = 0;
				this.BackColor = UITheme.BgDark;
				this.ForeColor = UITheme.TextMain;
				this.Cursor = Cursors.Hand;

				this.CheckedChanged += (s, e) =>
				{
					this.BackColor = this.Checked
						? UITheme.Accent
						: UITheme.BgDark;

					this.ForeColor = this.Checked
						? Color.White
						: UITheme.TextMain;
				};
			}
		}

		public class ModernTextBox : TextBox
		{
			public ModernTextBox()
			{
				this.BorderStyle = BorderStyle.FixedSingle;
				this.BackColor = UITheme.BgDarker;
				this.ForeColor = UITheme.TextMain;
				this.Font = UITheme.MainFont;
			}
		}

		#region Initialization

		private void InitializeToolTipHelp()
		{
			toolTipHelp = new()
			{
				UseFading = true,
				UseAnimation = true,
				OwnerDraw = true,
				IsBalloon = false
			};

			toolTipHelp.Popup += (s, e) =>
			{
				string text = "";
				if (e.AssociatedControl != null)
				{
					text = toolTipHelp.GetToolTip(e.AssociatedControl) ?? string.Empty;
				}
				if (string.IsNullOrEmpty(text)) text = _currentPopupText;
				e.ToolTipSize = MeasureToolTipText(text);
			};

			toolTipHelp.Draw += (s, e) =>
			{
				e.Graphics.Clear(UITheme.ToolTipBg);

				Color borderColor = UITheme.BorderDefault;
				Color titleColor = UITheme.SyntaxHighlight;
				Color textColor = UITheme.TextMain;
				Color bulletColor = UITheme.LogWarning;
				Color highlightWhite = UITheme.Accent;

				using Pen borderPen = new(borderColor, 1);
				e.Graphics.DrawRectangle(borderPen, new Rectangle(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1));

				Font titleFont = UITheme.ToolTipFontBold;
				Font bodyFont  = UITheme.ToolTipFont;
				Font boldFont  = UITheme.ToolTipFontBold;

				string toolTipText = e.ToolTipText ?? string.Empty;
				string[] lines = toolTipText.Split(_splitNewLines, StringSplitOptions.None);
				float yOffset = UITheme.Scale(8);
				float xOffset = UITheme.Scale(12);

				foreach (string line in lines)
				{
					if (string.IsNullOrWhiteSpace(line)) continue;

					if (line.StartsWith("💡"))
					{
						using SolidBrush titleBrush = new(titleColor);
						e.Graphics.DrawString(line, titleFont, titleBrush, xOffset, yOffset);
						yOffset += UITheme.Scale(22);
					}
					else if (line.StartsWith('•'))
					{
						using SolidBrush bulletBrush = new(bulletColor);
						e.Graphics.DrawString("•", boldFont, bulletBrush, xOffset, yOffset);

						int colonIndex = line.IndexOf(':');
						float currentX = xOffset + UITheme.Scale(12);

						if (colonIndex > 0)
						{
							string keyword = line[1..colonIndex].Trim();
							string details = line[(colonIndex + 1)..];

							using SolidBrush kwBrush = new(highlightWhite);
							e.Graphics.DrawString(keyword, boldFont, kwBrush, currentX, yOffset);
							currentX += e.Graphics.MeasureString(keyword, boldFont).Width - UITheme.Scale(2);

							using SolidBrush textBrush = new(textColor);
							e.Graphics.DrawString(":" + details, bodyFont, textBrush, currentX, yOffset);
						}
						else
						{
							using SolidBrush textBrush = new(textColor);
							e.Graphics.DrawString(line[1..].Trim(), bodyFont, textBrush, currentX, yOffset);
						}
						yOffset += UITheme.Scale(18);
					}
					else
					{
						using SolidBrush textBrush = new(textColor);
						e.Graphics.DrawString(line, bodyFont, textBrush, xOffset + UITheme.Scale(12), yOffset);
						yOffset += UITheme.Scale(18);
					}
				}
			};
		}

		private void InitializeMainLayoutAndInputs()
		{
			mainLayout = new()
			{
				Dock = DockStyle.Top,
				Height = UITheme.Scale(105),
				ColumnCount = 1,
				RowCount = 3,
				Padding = new(UITheme.Scale(10), UITheme.Scale(8), UITheme.Scale(10), UITheme.Scale(4)),
				BackColor = UITheme.BgPanel,
				RowStyles = {
					new(SizeType.Absolute, UITheme.Scale(25)),
					new(SizeType.Absolute, UITheme.Scale(36)),
					new(SizeType.Absolute, UITheme.Scale(38))
				}
			};

			TableLayoutPanel headerTable = new() {
				Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0),
				ColumnStyles = { new(SizeType.Percent, 100F), new(SizeType.AutoSize) }
			};

			lblHint = new() { Text = "🔍 Search Content (Single-line):", Font = UITheme.MainFontBold, ForeColor = UITheme.Accent, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, UITheme.Scale(4), 0, 0) };
			headerTable.Controls.Add(lblHint, 0, 0);

			btnHelp = new() { Text = "❔ Help", Height = UITheme.Scale(25), AutoSize = true, BackColor = UITheme.BgSelected, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0), Font = UITheme.MainFontBold };
			btnHelp.FlatAppearance.BorderSize = 0;
			btnHelp.Anchor = AnchorStyles.Right;

			string helpText =
				"💡 SMART SEARCH ENGINE QUICK GUIDE\r\n" +
				"• Single-line: Searches standard query in the main input field.\r\n" +
				"• Multi-line [☰]: Splits layout. Matches 'Query 1' (block start)\r\n" +
				"  and 'Query 2' (condition) across line breaks seamlessly.\r\n" +
				"• Wildcards: Use '*' or '?' for smart partial matching (e.g., Prefab*Ctrl).\r\n" +
				"• RegEx Mode [.*]: Enables advanced regular expression syntax.\r\n" +
				"• Resolve StringTable [ST]: Finds ID by text in .conf language files.";

			btnHelp.MouseHover += (s, e) =>
			{
				_currentPopupText = helpText;
				Size dynSize = MeasureToolTipText(helpText);
				int xPosition = btnHelp.Width - dynSize.Width;
				int yPosition = btnHelp.Height + UITheme.Scale(5);
				toolTipHelp.Show(helpText, btnHelp, xPosition, yPosition);
			};

			btnHelp.MouseLeave += (s, e) => toolTipHelp.Hide(btnHelp);
			headerTable.Controls.Add(btnHelp, 1, 0);

			mainLayout.Controls.Add(headerTable, 0, 0);

			TableLayoutPanel searchTable = new() {
				Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0),
				ColumnStyles = { new(SizeType.Percent, 100F), new(SizeType.AutoSize) }
			};

			chkMultiLine = CreateToggleButton("[☰]", "Toggle Multi-line Block Mode (Splits layout into 2 input fields)");
			chkMultiLine.AutoSize = false;
			chkMultiLine.Size = new Size(UITheme.Scale(42), UITheme.Scale(28));

			searchInput = new() { Font = UITheme.CodeFont, Margin = new Padding(UITheme.Scale(4), UITheme.Scale(2), UITheme.Scale(4), 0), Width = UITheme.Scale(220) };
			searchInput.KeyDown += (s, e) => {
				if (e.KeyCode == Keys.Enter)
				{
					e.SuppressKeyPress = true;
					if (chkMultiLine.Checked) searchInputVar.Focus();
					else btnSearch.PerformClick();
				}
				else
				{
					HandleHistoryNavigation(searchInput, _searchHistory, ref _historyIndex, e);
				}
			};
			searchInput.TextChanged += (s, e) => AutoFitSearchInputWidth(searchInput, 220, 450);

			searchInputVar = new() { Font = UITheme.CodeFont, ForeColor = UITheme.TextAccent, Margin = new Padding(UITheme.Scale(4), UITheme.Scale(2), UITheme.Scale(4), 0), Visible = false, Width = UITheme.Scale(220) };

			searchInputVar.KeyDown += (s, e) => {
				if (e.KeyCode == Keys.Enter)
				{
					e.SuppressKeyPress = true;
					btnSearch.PerformClick();
				}
				else
				{
					HandleHistoryNavigation(searchInputVar, _searchHistoryVar, ref _historyIndexVar, e);
				}
			};
			searchInputVar.TextChanged += (s, e) => AutoFitSearchInputWidth(searchInputVar, 220, 450);

			btnSearch = new() { Text = "🚀 Start Search", BackColor = UITheme.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(UITheme.Scale(4), UITheme.Scale(1), 0, UITheme.Scale(1)), Font = UITheme.MainFontBold };
			btnSearch.FlatAppearance.BorderSize = 0;
			btnSearch.AutoSize = false;
			btnSearch.Size = new Size(UITheme.Scale(140), UITheme.Scale(28));
			btnSearch.Click += BtnSearch_Click;

			UpdateLayoutColumns();

			chkMultiLine.CheckedChanged += HandleMultiLineToggle;

			row1Layout = new() {
				Dock = DockStyle.Fill, Margin = new Padding(0), AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
				Controls = { chkMultiLine, searchInput, searchInputVar, btnSearch }
			};

			searchTable.Controls.Add(row1Layout, 0, 0);

			graphViewContainer = new() { Dock = DockStyle.Fill, Margin = new Padding(0), AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };

			mainLayout.Controls.Add(searchTable, 0, 1);
		}

		private void HandleHistoryNavigation(ModernTextBox input, List<string> history, ref int index, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Up)
			{
				e.SuppressKeyPress = true;
				if (history.Count > 0)
				{
					index++;
					if (index >= history.Count) index = history.Count - 1;
					input.Text = history[index];
				}
			}
			else if (e.KeyCode == Keys.Down)
			{
				e.SuppressKeyPress = true;
				if (history.Count > 0)
				{
					index--;
					if (index < 0)
					{
						index = -1;
						input.Text = "";
					}
					else
					{
						input.Text = history[index];
					}
				}
			}
		}

		private void InitializeHelperButtons()
		{
			btnHelperStart = CreateHelperButton(" Starts With", "^");
			btnHelperEnd = CreateHelperButton(" Ends With", "$");
			btnHelperNum = CreateHelperButton("🔢 Any Number", @"\d+");
			btnHelperAny = CreateHelperButton(" Wildcard Any", ".*");
		}

		private readonly ToolTip deepSearchToolTip = new();

		private void InitializeExtensionsAndResults()
		{
			this.SuspendLayout();
			mainLayout.SuspendLayout();

			chkMatchCase = CreateToggleButton("[Aa]", "Match Case \r\n(Case sensitive search)");
			chkWholeWord = CreateToggleButton("[ab]", "Whole Word \r\n(Matches complete words only)");
			chkRegex = CreateToggleButton("[.*]", "Smart / RegEx Mode");

			string stHelpText = "Resolve StringTable \r\n(Finds ID by text in .conf language files)";
			chkResolveST = CreateToggleButton("[ST]", stHelpText);

			chkRegex.CheckedChanged += (s, e) => {
				chkWholeWord.Enabled = !chkRegex.Checked;
			};

			Label lblRegexHint = new() { Text = "Regex:", Font = UITheme.MainFont, ForeColor = UITheme.TextMuted, AutoSize = true, Margin = new Padding(UITheme.Scale(8), UITheme.Scale(6), UITheme.Scale(2), 0) };

			lblExt = new() { Text = "Target Ext:", Font = UITheme.MainFontBold, ForeColor = UITheme.TextDarkGray, AutoSize = true, Margin = new Padding(UITheme.Scale(12), UITheme.Scale(6), UITheme.Scale(4), 0) };
			txtExtensions = new() { Width = UITheme.Scale(110), Font = UITheme.MainFont, BackColor = UITheme.BgDark, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, UITheme.Scale(3), UITheme.Scale(10), 0) };

			CheckedListBox checkedListBox = new()
			{
				BorderStyle = BorderStyle.None,
				CheckOnClick = false,
				Width = UITheme.Scale(150),
				Height = UITheme.Scale(220),
				BackColor = UITheme.BgDark,
				ForeColor = Color.White,
				Font = UITheme.MainFont
			};

			string[] availableExtensions = ["*.*", ".c", ".conf", ".layout", ".et", ".emat", ".ptc", ".acp", ".siga", ".imageset", ".pap", ".asi", ".bt"];
			checkedListBox.Items.AddRange(availableExtensions);

			ToolStripControlHost host = new(checkedListBox)
			{
				Margin = Padding.Empty,
				Padding = Padding.Empty
			};

			ToolStripDropDown extensionDropDown = new() {
				Items = { host },
				Padding = Padding.Empty
			};

			BindExtensionEvents(checkedListBox, extensionDropDown);

			lblExcl = new() { Text = "Exclude:", Font = UITheme.MainFontBold, ForeColor = UITheme.TextDarkGray, AutoSize = true, Margin = new Padding(0, UITheme.Scale(6), UITheme.Scale(4), 0) };
			txtExclude = new() { Width = UITheme.Scale(120), Font = UITheme.MainFont, Margin = new Padding(0, UITheme.Scale(3), UITheme.Scale(10), 0), Text = "deprecated, test, old" };

			row2Panel = new() {
				Dock = DockStyle.Fill, Margin = new Padding(0, UITheme.Scale(2), 0, 0), AutoSize = true, WrapContents = false,
				Controls = {
					chkMatchCase, chkWholeWord, chkRegex, chkResolveST,
					lblRegexHint, btnHelperStart, btnHelperEnd, btnHelperNum, btnHelperAny,
					lblExt, txtExtensions,
					lblExcl, txtExclude,
					graphViewContainer
				}
			};

			mainLayout.Controls.Add(row2Panel, 0, 2);

			resultsList = new DarkListView() {
				Dock = DockStyle.Fill,
				View = View.Details,
				FullRowSelect = true,
				GridLines = false,
				BackColor = UITheme.BgPanel,
				ForeColor = UITheme.TextMain,
				BorderStyle = BorderStyle.None,
				Font = UITheme.MainFont,
				OwnerDraw = true,
				EmptyAreaColor = UITheme.BgPanel,
				HeaderBackColor = UITheme.BgDark
			};

			resultsList.DrawColumnHeader += (s, e) => {
				using (SolidBrush headerBg = new(UITheme.BgDark))
				{
					e.Graphics.FillRectangle(headerBg, e.Bounds);
				}

				using Pen separatorPen = new(UITheme.BorderDefault, 1);
				e.Graphics.DrawLine(separatorPen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);

				string headerText = e.Header?.Text ?? string.Empty;
				Rectangle textBounds = new(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
				TextRenderer.DrawText(e.Graphics, headerText, resultsList.Font, textBounds, UITheme.TextMain, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
			};

			resultsList.DrawItem += (s, e) => {
				e.DrawDefault = false;
			};

			resultsList.DrawSubItem += (s, e) => {
				if (e.Item == null || e.SubItem == null) return;

				bool isSelected = e.Item.Selected;
				Color backColor = isSelected ? UITheme.Accent : resultsList.BackColor;
				Color textColor = isSelected ? Color.White : resultsList.ForeColor;

				using (Brush bgBrush = new SolidBrush(backColor))
				{
					e.Graphics.FillRectangle(bgBrush, e.Bounds);
				}

				if (!isSelected)
				{
					using Pen gridPen = new(UITheme.BgSelected, 1);
					e.Graphics.DrawLine(gridPen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
				}

				TextRenderer.DrawText(
					e.Graphics,
					e.SubItem.Text ?? string.Empty,
					resultsList.Font,
					new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
					textColor,
					TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis
				);
			};

			resultsList.ColumnClick += ResultsList_ColumnClick;
			resultsList.SelectedIndexChanged += ResultsList_SelectedIndexChanged;
			resultsList.DoubleClick += ResultsList_DoubleClick;

			resultsContextMenu = new()
			{
				BackColor = UITheme.BgSelected,
				ForeColor = Color.White,
				ShowImageMargin = false
			};

			resultsList.MouseClick += (s, e) => {
				if (e.Button == MouseButtons.Right)
				{
					var hitTest = resultsList.HitTest(e.Location);
					if (hitTest.Item != null)
					{
						foreach (ListViewItem item in resultsList.SelectedItems) item.Selected = false;
						hitTest.Item.Selected = true;
						resultsContextMenu.Show(resultsList, e.Location);
					}
				}
			};

			mainSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
			mainSplit.SuspendLayout();
			mainSplit.FixedPanel = FixedPanel.Panel1;
			mainSplit.Panel1.Padding = new Padding(UITheme.Scale(6), UITheme.Scale(6), UITheme.Scale(6), 0);
			mainSplit.Panel2.Padding = new Padding(UITheme.Scale(6), UITheme.Scale(6), UITheme.Scale(6), 0);
			mainSplit.Panel1.Controls.Add(resultsList);

			int savedPct = PlayerPrefs.GetInt("DeepSearch_SplitterPct", 60);
			if (savedPct < 10) savedPct = 10;
			if (savedPct > 90) savedPct = 90;
			_splitterRatio = savedPct / 100.0f;

			mainSplit.Resize += (s, e) => {
				if (!isUserResizingSplitter && _isLayoutReady && mainSplit.Height > UITheme.Scale(100)) {
					try { mainSplit.SplitterDistance = (int)(mainSplit.Height * _splitterRatio); } catch { }
				}
			};

			mainSplit.SplitterMoving += (s, e) => {
				isUserResizingSplitter = true;
				if (mainSplit.Height > 0) {
					int pct = (int)Math.Round((double)e.SplitY / mainSplit.Height * 100);

					Point localCursorPos = mainSplit.PointToClient(Cursor.Position);
					localCursorPos.Offset(UITheme.Scale(15), UITheme.Scale(5));

					deepSearchToolTip.Show($"{pct}%", mainSplit, localCursorPos);
				}
			};

			mainSplit.DoubleClick += (s, e) => {
				_splitterRatio = 0.60f;
				int targetDistance = (int)(mainSplit.Height * _splitterRatio);
				if (targetDistance <= 0) targetDistance = UITheme.Scale(350);
				try { mainSplit.SplitterDistance = targetDistance; } catch { }

				PlayerPrefs.SetInt("DeepSearch_SplitterPct", 60);

				if (mainSplit.Height > 0) {
					Point localCursorPos = mainSplit.PointToClient(Cursor.Position);
					localCursorPos.Offset(UITheme.Scale(15), UITheme.Scale(5));
					deepSearchToolTip.Show($"Reset: 60%", mainSplit, localCursorPos, 1500);
				}
			};

			this.Load += (s, e) => {
				this.BeginInvoke((MethodInvoker)delegate {
					if (mainSplit.Height > UITheme.Scale(150))
					{
						int targetDistance = (int)(mainSplit.Height * _splitterRatio);

						try {
							if (targetDistance > UITheme.Scale(50) && targetDistance < mainSplit.Height - UITheme.Scale(50))
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

			mainSplit.SplitterMoved += (s, e) => {
				isUserResizingSplitter = false;
				if (_isLayoutReady && mainSplit.Height > 0)
				{
					_splitterRatio = (float)mainSplit.SplitterDistance / mainSplit.Height;
					int currentPct = (int)Math.Round(_splitterRatio * 100);
					PlayerPrefs.SetInt("DeepSearch_SplitterPct", currentPct);
					deepSearchToolTip.Hide(mainSplit);
				}
			};

			mainSplit.ResumeLayout(false);
			mainLayout.ResumeLayout(false);
			this.ResumeLayout(true);
		}

		private void InitializePreviewPanel()
		{
			Label lblPreviewHeader = new() { Text = " 💻 Code Context Preview:", Dock = DockStyle.Top, Height = UITheme.Scale(28), TextAlign = ContentAlignment.MiddleLeft, BackColor = UITheme.BgDark, ForeColor = UITheme.Accent, Font = UITheme.MainFontBold };

			_previewController = new XFusionPreviewController(UITheme.CodeFont);
			_previewController.Initialize();

			Panel previewPanel = new() {
				Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = UITheme.BgDarker,
				Controls = { _previewController.UIControl, lblPreviewHeader }
			};
			mainSplit.Panel2.Controls.Add(previewPanel);

			statusLabel = new() { Dock = DockStyle.Bottom, Height = UITheme.Scale(30), TextAlign = ContentAlignment.MiddleLeft, Text = " Ready. Awaiting loaded archives.", BackColor = UITheme.BgDark, Font = UITheme.MainFont, ForeColor = UITheme.TextMain };
		}

		private void FinalizeInitialization()
		{
			this.Controls.Add(mainSplit);
			this.Controls.Add(mainLayout);
			this.Controls.Add(statusLabel);

#if ENABLE_GRAPH_PLUGIN
			_GraphPlugin = new DeepSearchGraphPlugin();
			_GraphPlugin.InitializeUI(this, graphViewContainer, resultsContextMenu,
				getSelectedItem: () => {
					if (resultsList.SelectedItems.Count > 0 && resultsList.SelectedItems[0].Tag is SearchResultInfo info) return info;
					return null;
				},
				updateStatus: (msg) => {
					if (this.InvokeRequired) this.Invoke((MethodInvoker)delegate { statusLabel.Text = msg; });
					else statusLabel.Text = msg;
				});
#endif
		}

		private void BindCheckBoxToPreference(CheckBox chk, string prefKey, bool defaultValue = false)
		{
			chk.Checked = PlayerPrefs.GetBool(prefKey, defaultValue);
			chk.CheckedChanged += (s, e) => PlayerPrefs.SetBool(prefKey, chk.Checked);
		}

		private void LoadSavedSettingsAndEvents()
		{
			string savedHistory = PlayerPrefs.GetString("DeepSearch_History", "");
			if (!string.IsNullOrEmpty(savedHistory))
			{
				_searchHistory = [.. savedHistory.Split(_splitHistory, StringSplitOptions.RemoveEmptyEntries)];
			}

			string savedHistoryVar = PlayerPrefs.GetString("DeepSearch_HistoryVar", "");
			if (!string.IsNullOrEmpty(savedHistoryVar))
			{
				_searchHistoryVar = [.. savedHistoryVar.Split(_splitHistory, StringSplitOptions.RemoveEmptyEntries)];
			}

			chkMultiLine.Checked = PlayerPrefs.GetBool("DeepSearch_MultiLine", false);

			if (chkMultiLine.Checked) {
				searchInput.Text = PlayerPrefs.GetString("DeepSearch_InputMulti1", "");
				searchInputVar.Text = PlayerPrefs.GetString("DeepSearch_InputMulti2", "");
				txtExtensions.Text = PlayerPrefs.GetString("DeepSearch_ExtMulti", ".c");
			} else {
				searchInput.Text = PlayerPrefs.GetString("DeepSearch_InputSingle", "");
				searchInputVar.Text = PlayerPrefs.GetString("DeepSearch_InputMulti2", "");
				txtExtensions.Text = PlayerPrefs.GetString("DeepSearch_ExtSingle", ".c");
			}

			txtExclude.Text = PlayerPrefs.GetString("DeepSearch_Exclude", "deprecated, test, old");

			BindCheckBoxToPreference(chkMatchCase, "DeepSearch_MatchCase");
			BindCheckBoxToPreference(chkWholeWord, "DeepSearch_WholeWord");
			BindCheckBoxToPreference(chkRegex, "DeepSearch_Regex");
			BindCheckBoxToPreference(chkResolveST, "DeepSearch_ResolveST");

			sortColumn = PlayerPrefs.GetInt("DeepSearch_SortCol", -1);
			int savedSortDir = PlayerPrefs.GetInt("DeepSearch_SortDir", 0);
			if (savedSortDir == 1) resultsList.Sorting = SortOrder.Ascending;
			else if (savedSortDir == 2) resultsList.Sorting = SortOrder.Descending;

			searchInput.TextChanged += (s, e) => {
				if (chkMultiLine.Checked) PlayerPrefs.SetString("DeepSearch_InputMulti1", searchInput.Text);
				else PlayerPrefs.SetString("DeepSearch_InputSingle", searchInput.Text);
			};

			searchInputVar.TextChanged += (s, e) => PlayerPrefs.SetString("DeepSearch_InputMulti2", searchInputVar.Text);
			txtExclude.TextChanged += (s, e) => PlayerPrefs.SetString("DeepSearch_Exclude", txtExclude.Text);

			chkResolveST.CheckedChanged += (s, e) =>
			{
				if (chkResolveST.Checked)
				{
					if (txtExtensions.Text != ".c, .layout, .conf, .et")
					{
						_preStExtensions = txtExtensions.Text;
					}
					txtExtensions.Text = ".c, .layout, .conf, .et";
				}
				else
				{
					if (!string.IsNullOrEmpty(_preStExtensions))
					{
						txtExtensions.Text = _preStExtensions;
					}
				}
			};
		}

		private void SaveCurrentExtensionPreferences()
		{
			string extKey = chkMultiLine.Checked ? "DeepSearch_ExtMulti" : "DeepSearch_ExtSingle";
			PlayerPrefs.SetString(extKey, txtExtensions.Text);
		}

		private void BindExtensionEvents(CheckedListBox checkedListBox, ToolStripDropDown extensionDropDown)
		{
			bool isSyncing = false;

			checkedListBox.MouseDown += (s, e) =>
			{
				int index = checkedListBox.IndexFromPoint(e.Location);
				if (index == ListBox.NoMatches) return;

				if (e.X > UITheme.Scale(22))
				{
					isSyncing = true;
					for (int i = 0; i < checkedListBox.Items.Count; i++)
					{
						checkedListBox.SetItemChecked(i, i == index);
					}
					txtExtensions.Text = checkedListBox.Items[index].ToString() ?? "";

					SaveCurrentExtensionPreferences();

					isSyncing = false;
					this.BeginInvoke((MethodInvoker)delegate { extensionDropDown.Close(); });
				}
				else
				{
					bool currentCheck = checkedListBox.GetItemChecked(index);
					checkedListBox.SetItemChecked(index, !currentCheck);

					isSyncing = true;
					List<string> checkedExts = [];
					for (int i = 0; i < checkedListBox.Items.Count; i++)
					{
						if (checkedListBox.GetItemChecked(i))
						{
							checkedExts.Add(checkedListBox.Items[i].ToString() ?? "");
						}
					}
					txtExtensions.Text = string.Join(", ", checkedExts);

					SaveCurrentExtensionPreferences();
					isSyncing = false;
				}
			};

			txtExtensions.TextChanged += (s, e) =>
			{
				if (isSyncing) return;
				isSyncing = true;
				var tokens = txtExtensions.Text.Split(_splitExts, StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < checkedListBox.Items.Count; i++)
				{
					string itemStr = checkedListBox.Items[i].ToString() ?? "";
					bool shouldBeChecked = tokens.Contains(itemStr);
					if (checkedListBox.GetItemChecked(i) != shouldBeChecked)
					{
						checkedListBox.SetItemChecked(i, shouldBeChecked);
					}
				}
				isSyncing = false;

				SaveCurrentExtensionPreferences();
			};

			txtExtensions.DropDown += (s, e) =>
			{
				if (txtExtensions.IsHandleCreated)
				{
					txtExtensions.BeginInvoke((MethodInvoker)delegate { txtExtensions.DroppedDown = false; });
				}
				extensionDropDown.Show(txtExtensions, new Point(0, txtExtensions.Height));
			};
		}

		private void HandleMultiLineToggle(object? sender, EventArgs e)
		{
			_ = SendMessage(this.Handle, WM_SETREDRAW, false, 0);
			mainLayout.SuspendLayout();
			row1Layout.SuspendLayout();

			if (chkMultiLine.Checked)
			{
				lblHint.Text = "🔍 Block Search (Multi-line) ➔ Start && Condition:";
				searchInputVar.Visible = true;
				UpdateLayoutColumns();

				searchInput.PlaceholderText = "Block start (e.g. decor)";
				searchInputVar.PlaceholderText = "Condition (e.g. m_varib)";
				txtExtensions.Text = PlayerPrefs.GetString("DeepSearch_ExtMulti", ".c");
			}
			else
			{
				lblHint.Text = "🔍 Search Content (Single-line):";
				searchInputVar.Visible = false;
				UpdateLayoutColumns();

				searchInput.PlaceholderText = "Enter query term...";
				txtExtensions.Text = PlayerPrefs.GetString("DeepSearch_ExtSingle", ".c");
			}

			PlayerPrefs.SetBool("DeepSearch_MultiLine", chkMultiLine.Checked);

			if (chkMultiLine.Checked) {
				searchInput.Text = PlayerPrefs.GetString("DeepSearch_InputMulti1", "");
			} else {
				searchInput.Text = PlayerPrefs.GetString("DeepSearch_InputSingle", "");
			}

			row1Layout.ResumeLayout(true);
			mainLayout.ResumeLayout(true);
			_ = SendMessage(this.Handle, WM_SETREDRAW, true, 0);
			this.Refresh();
		}

		#endregion

		#region Helper Functions for UI Calculation

		private static void AutoFitSearchInputWidth(ModernTextBox box, int minWidth = 200, int maxWidth = 500)
		{
			if (box == null) return;

			int padding = UITheme.Scale(25);
			Size textSize = TextRenderer.MeasureText(
				string.IsNullOrEmpty(box.Text) ? (box.PlaceholderText ?? "") : box.Text,
				box.Font
			);

			int calculatedWidth = textSize.Width + padding;
			int finalWidth = Math.Clamp(calculatedWidth, UITheme.Scale(minWidth), UITheme.Scale(maxWidth));

			if (box.Width != finalWidth)
			{
				box.Width = finalWidth;
			}
		}

		private Size MeasureToolTipText(string text)
		{
			if (string.IsNullOrEmpty(text)) return new Size(UITheme.Scale(150), UITheme.Scale(40));

			int maxWidth = 0;
			float totalHeight = UITheme.Scale(8);
			float xOffset = UITheme.Scale(12);

			using Graphics g = this.CreateGraphics();
			Font titleFont = UITheme.TextTitleFix;
			Font bodyFont  = UITheme.TextBodyFix;
			Font boldFont  = UITheme.TextBoldFix;

			string[] lines = text.Split(_splitNewLines, StringSplitOptions.None);
			foreach (string line in lines)
			{
				if (string.IsNullOrWhiteSpace(line)) continue;

				float currentWidth = xOffset;

				if (line.StartsWith("💡"))
				{
					currentWidth += g.MeasureString(line, titleFont).Width;
					totalHeight += UITheme.Scale(22);
				}
				else if (line.StartsWith('•'))
				{
					int colonIndex = line.IndexOf(':');
					float currentX = xOffset + UITheme.Scale(12);

					if (colonIndex > 0)
					{
						string keyword = line[1..colonIndex].Trim();
						string details = line[(colonIndex + 1)..];

						currentX += g.MeasureString(keyword, boldFont).Width - UITheme.Scale(2);
						currentX += g.MeasureString(":" + details, bodyFont).Width;
						currentWidth = currentX;
					}
					else
					{
						currentWidth = xOffset + UITheme.Scale(12) + g.MeasureString(line, bodyFont).Width;
					}
					totalHeight += UITheme.Scale(18);
				}
				else
				{
					currentWidth = xOffset + UITheme.Scale(12) + g.MeasureString(line, bodyFont).Width;
					totalHeight += UITheme.Scale(18);
				}

				currentWidth += UITheme.Scale(12);
				if (currentWidth > maxWidth) maxWidth = (int)currentWidth;
			}

			return new Size(maxWidth, (int)(totalHeight + UITheme.Scale(8)));
		}

		private static void UpdateLayoutColumns()
		{
		}

		private ModernCheckBox CreateToggleButton(string text, string tooltip)
		{
			var chk = new ModernCheckBox
			{
				Text = text,
				Appearance = Appearance.Button,
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleCenter,
				Cursor = Cursors.Hand,
				Margin = new Padding(UITheme.Scale(1)),
				Font = UITheme.ControlBoldFix,
				Size = new Size(UITheme.Scale(38), UITheme.Scale(26))
			};
			toolTipHelp.SetToolTip(chk, tooltip);

			return chk;
		}

		private Button CreateHelperButton(string text, string appendValue)
		{
			var btn = new Button
			{
				Text = text,
				AutoSize = true,
				Height = UITheme.Scale(25),
				BackColor = UITheme.ToggleInactive,
				ForeColor = UITheme.TextMain,
				FlatStyle = FlatStyle.Flat,
				Cursor = Cursors.Hand,
				Margin = new Padding(0, UITheme.Scale(2), UITheme.Scale(6), 0),
				Font = UITheme.ControlSmallFix
			};
			btn.FlatAppearance.BorderSize = 0;
			btn.Click += (s, e) =>
			{
				chkRegex.Checked = true;
				ModernTextBox activeBox = (searchInputVar.Focused) ? searchInputVar : searchInput;
				int cursor = activeBox.SelectionStart;
				activeBox.Text = activeBox.Text.Insert(cursor, appendValue);
				activeBox.SelectionStart = cursor + appendValue.Length;
				activeBox.Focus();
			};
			return btn;
		}

		#endregion

		private static string ConvertToWildcardRegex(string input)
		{
			return Regex.Escape(input).Replace("\\*", ".*").Replace("\\?", ".");
		}

		public static bool SmartPatternMatch(string src, string pat, bool matchCase)
		{
			if (string.IsNullOrEmpty(pat)) return true;
			if (string.IsNullOrEmpty(src)) return false;

			RegexOptions options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;

			bool isSimpleWildcard = (pat.Contains('*') || pat.Contains('?')) &&
									!pat.Any(c => "^$+\\[]{}()|#".Contains(c));

			if (isSimpleWildcard)
			{
				string safeRegex = ConvertToWildcardRegex(pat);
				try { return Regex.IsMatch(src, safeRegex, options); } catch { return false; }
			}
			else
			{
				try { return Regex.IsMatch(src, pat, options); } catch { return false; }
			}
		}

		internal void UpdatePakList(List<Pak> paks, Func<string, string>? projectResolver = null)
		{
			loadedPaks = paks;
			bool hasPaks = loadedPaks.Count > 0;

			btnSearch.Enabled = hasPaks;
			searchInput.Enabled = hasPaks;
			searchInputVar.Enabled = hasPaks;

			statusLabel.Text = hasPaks ? $"Ready. Loaded archives: {loadedPaks.Count}" : "Ready. Load an archive first to begin scanning.";

#if ENABLE_GRAPH_PLUGIN
			if (hasPaks)
			{
				_GraphPlugin?.UpdatePaks(loadedPaks, projectResolver);
			}
#endif
		}

		#region Search Execution Logic

		private async void BtnSearch_Click(object? sender, EventArgs e)
		{
			if (_isSearching)
			{
				_searchCts?.Cancel();
				btnSearch.Text = "🛑 Stopping...";
				btnSearch.Enabled = false;
				return;
			}

			if (loadedPaks == null || loadedPaks.Count == 0)
			{
				MessageBox.Show("Please load at least one .pak archive in the File Explorer tab first.", "No Archives", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			string query1 = searchInput.Text.Trim();
			string query2 = searchInputVar.Text.Trim();

			if (string.IsNullOrWhiteSpace(query1))
			{
				_currentPopupText = "Please enter a valid search criteria!";
				toolTipHelp.Show(_currentPopupText, searchInput, 0, UITheme.Scale(-30), 2000);
				searchInput.Focus();
				return;
			}

			if (!string.IsNullOrWhiteSpace(query1))
			{
				_searchHistory.Remove(query1);
				_searchHistory.Insert(0, query1);
				if (_searchHistory.Count > 50) _searchHistory.RemoveAt(_searchHistory.Count - 1);
				_historyIndex = -1;
				PlayerPrefs.SetString("DeepSearch_History", string.Join("|||", _searchHistory));
			}

			if (!string.IsNullOrWhiteSpace(query2))
			{
				_searchHistoryVar.Remove(query2);
				_searchHistoryVar.Insert(0, query2);
				if (_searchHistoryVar.Count > 50) _searchHistoryVar.RemoveAt(_searchHistoryVar.Count - 1);
				_historyIndexVar = -1;
				PlayerPrefs.SetString("DeepSearch_HistoryVar", string.Join("|||", _searchHistoryVar));
			}

#if ENABLE_GRAPH_PLUGIN
			await _GraphPlugin?.CloseOverlayAsync()!;
#endif

			PrepareSearchUI(query1, query2);
			await ExecuteSearchTaskAsync(query1, query2);
		}

		public partial class DarkListView : ListView
		{
			public Color EmptyAreaColor { get; set; } = UITheme.BgPanel;
			public Color HeaderBackColor { get; set; } = UITheme.BgDark;

			private NativeHeaderWindow? _headerWindow;

			public DarkListView()
			{
				this.DoubleBuffered = true;
				this.OwnerDraw = true;
				this.View = View.Details;
			}

			protected override void OnHandleCreated(EventArgs e)
			{
				base.OnHandleCreated(e);
				IntPtr headerHandle = SendMessage(this.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
				if (headerHandle != IntPtr.Zero)
				{
					_headerWindow = new NativeHeaderWindow(this, headerHandle);
				}
			}

			protected override void OnPaintBackground(PaintEventArgs e)
			{
				e.Graphics.Clear(EmptyAreaColor);
			}

			protected override void WndProc(ref Message m)
			{
				const int WM_ERASEBKGND = 0x0014;

				if (m.Msg == WM_ERASEBKGND)
				{
					using Graphics g = Graphics.FromHdc(m.WParam);
					g.Clear(EmptyAreaColor);
					m.Result = (IntPtr)1;
					return;
				}

				base.WndProc(ref m);
			}

			private const int LVM_GETHEADER = 0x101F;

			private class NativeHeaderWindow : NativeWindow
			{
				private readonly DarkListView _parent;

				public NativeHeaderWindow(DarkListView parent, IntPtr handle)
				{
					_parent = parent;
					this.AssignHandle(handle);
				}

				protected override void WndProc(ref Message m)
				{
					const int WM_PAINT = 0x000F;
					const int WM_ERASEBKGND = 0x0014;

					if (m.Msg == WM_ERASEBKGND)
					{
						using Graphics g = Graphics.FromHdc(m.WParam);
						using SolidBrush brush = new(_parent.HeaderBackColor);
						g.FillRectangle(brush, new Rectangle(0, 0, _parent.Width, _parent.Height));
						m.Result = (IntPtr)1;
						return;
					}

					base.WndProc(ref m);

					if (m.Msg == WM_PAINT)
					{
						using Graphics g = Graphics.FromHwnd(this.Handle);
						int totalColWidth = 0;
						foreach (ColumnHeader col in _parent.Columns)
						{
							totalColWidth += col.Width;
						}

						if (totalColWidth < _parent.Width)
						{
							using SolidBrush brush = new(_parent.HeaderBackColor);
							g.FillRectangle(brush, new Rectangle(totalColWidth, 0, _parent.Width - totalColWidth, _parent.Height));
						}
					}
				}
			}
		}

		private void PrepareSearchUI(string query1, string query2)
		{
			_isSearching = true;
			_searchCts = new();

			btnSearch.Text = "🛑 Cancel Search";
			btnSearch.BackColor = UITheme.ActionDestructive;
			resultsList.Columns.Clear();
			resultsList.Columns.Add("File Relative Path", UITheme.Scale(450));
			resultsList.Columns.Add("Line", UITheme.Scale(80));
			resultsList.Columns.Add("Match Content / Context Snippet", UITheme.Scale(650));

			resultsList.Items.Clear();
			_previewController.Clear();

			bool isMultiLine = chkMultiLine.Checked;
			bool logEnabled = AppSettings.Current.EnableLog && !AppSettings.Current.LiteMode;

			_previewController.ConfigureForSearchLog(logEnabled);

			if (logEnabled)
			{
				StringBuilder sb = new();
				sb.Append("=================================================================================\n");
				sb.Append("  DEEP CONTENT SEARCH :: MULTI-THREADED SMART SCANNER ACTIVATED\n");
				sb.Append($"  QUERY 1: '{query1}'\n");
				if (isMultiLine) sb.Append($"  QUERY 2 (CONDITION): '{query2}'\n");
				sb.Append($"  ARCHIVES LOADED: {loadedPaks.Count}\n");
				sb.Append("=================================================================================\n\n");
				_previewController.AppendBatchText(sb.ToString());
			}

			statusLabel.Text = $" Scanning in progress... Reading {loadedPaks.Count} archives...";

#if ENABLE_GRAPH_PLUGIN
			_GraphPlugin?.SetGraphButtonEnabled(false);
#endif
		}

		private async Task ExecuteSearchTaskAsync(string query1, string query2)
		{
			var token = _searchCts!.Token;
			int matchCount = 0;
			int scannedCount = 0;
			ConcurrentBag<ListViewItem> globalResultBuffer = [];

			bool matchCase = chkMatchCase.Checked;
			bool wholeWord = chkWholeWord.Checked && !chkRegex.Checked;
			bool isMultiLine = chkMultiLine.Checked;
			bool resolveST = chkResolveST.Checked;
			bool logEnabled = AppSettings.Current.EnableLog && !AppSettings.Current.LiteMode;

			string[] allowedExtensions = [.. txtExtensions.Text
				.Split(_splitExts, StringSplitOptions.RemoveEmptyEntries)
				.Select(ext => ext == "*.*" ? ext : (ext.StartsWith('.') ? ext : '.' + ext).ToLowerInvariant())];

			string[] excludes = [.. txtExclude.Text
				.Split(_splitExcludes, StringSplitOptions.RemoveEmptyEntries)
				.Select(ex => ex.Trim())];

			try
			{
				await Task.Run(() =>
				{
					string actualQuery1 = query1;
					string actualQuery2 = query2;
					string resolvedMsg = "";
					StringBuilder consoleBuffer = new();
					Stopwatch logTimer = Stopwatch.StartNew();

					void flushConsole() { FlushConsoleBuffer(consoleBuffer, token); }
					void flushResults() { FlushResultBuffer(globalResultBuffer, token); }

					ResolveStringTableQueryIfNeeded(ref actualQuery1, ref resolvedMsg, matchCase, wholeWord, resolveST, token);

					bool isSmartRegex = chkRegex.Checked ||
										(resolveST && actualQuery1.Contains('|')) ||
										query1.Contains('*') || query1.Contains('?') ||
										(isMultiLine && (query2.Contains('*') || query2.Contains('?')));

					var itemsToProcess = GatherTargetFiles(ref actualQuery1, allowedExtensions, excludes);

					StringComparison fastComp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
					ParallelOptions po = new() { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount };

					Parallel.ForEach(itemsToProcess, po, pair =>
					{
						po.CancellationToken.ThrowIfCancellationRequested();

						var pak = pair.pak;
						var entry = pair.entry;
						string ext = System.IO.Path.GetExtension(entry.name).ToLowerInvariant();

						Interlocked.Increment(ref scannedCount);

						if (logEnabled && scannedCount % 15 == 0)
						{
							lock (consoleBuffer)
							{
								consoleBuffer.AppendLine($"[SCAN] 0x{entry.offset:X8} -> {entry.name}");
								if (logTimer.ElapsedMilliseconds > 30) { flushConsole(); logTimer.Restart(); }
							}
						}

						byte[] data = pak.GetFileBytes(entry);
						if (data == null || data.Length == 0) return;

						if (!isMultiLine && !isSmartRegex)
						{
							bool isHandled = EnfusionSmartAnalyzer.TryAnalyze(
								ext, data, actualQuery1, fastComp, pak, entry,
								CreateResultItemDynamic,
								item => {
									Interlocked.Increment(ref matchCount);
									globalResultBuffer.Add(item);
									if (globalResultBuffer.Count >= 50) flushResults();
								},
								token
							);
							if (isHandled) return;
						}

						if (isMultiLine)
						{
							ProcessMultiLineSearch(data, actualQuery1, actualQuery2, matchCase, pak, entry, ref matchCount, globalResultBuffer, flushResults);
							return;
						}

						ProcessSingleLineSearch(data, actualQuery1, matchCase, isSmartRegex, wholeWord, fastComp, pak, entry, ref matchCount, globalResultBuffer, flushResults, token);
					});

					flushResults();

					if (logEnabled && !token.IsCancellationRequested)
					{
						lock (consoleBuffer)
						{
							consoleBuffer.AppendLine("\n=================================================================================");
							consoleBuffer.AppendLine(" [SYSTEM] Search operation completed successfully.");
							consoleBuffer.AppendLine($" [SYSTEM] Scanned file count: {scannedCount}");
							consoleBuffer.AppendLine($" [SYSTEM] Total structural matches: {matchCount}");
							consoleBuffer.AppendLine("=================================================================================");
							flushConsole();
						}
					}

					this.Invoke(new Action(() => statusLabel.Text = $" Search complete{resolvedMsg}. Files scanned: {scannedCount}. Hit count: {matchCount}"));
				}, token);

#if ENABLE_GRAPH_PLUGIN
				_GraphPlugin?.SetGraphButtonEnabled(matchCount > 0);
#endif
			}
			catch (OperationCanceledException)
			{
				statusLabel.Text = $" Search cancelled by user. Files processed: {scannedCount}. Hits found: {matchCount}";
#if ENABLE_GRAPH_PLUGIN
				_GraphPlugin?.SetGraphButtonEnabled(matchCount > 0);
#endif
			}
			finally
			{
				_isSearching = false;
				btnSearch.Text = "🚀 Start Search";
				btnSearch.BackColor = UITheme.Accent;
				btnSearch.Enabled = true;
				if (_searchCts != null) { _searchCts.Dispose(); _searchCts = null; }
			}
		}

		private void FlushConsoleBuffer(StringBuilder consoleBuffer, CancellationToken token)
		{
			if (consoleBuffer.Length > 0 && !token.IsCancellationRequested)
			{
				string textToAppend = consoleBuffer.ToString().TrimEnd('\r', '\n');

				consoleBuffer.Clear();
				this.BeginInvoke((MethodInvoker)delegate {
					_previewController.AppendBatchText(textToAppend);
				});
			}
		}

		private void FlushResultBuffer(ConcurrentBag<ListViewItem> globalResultBuffer, CancellationToken token)
		{
			if (!globalResultBuffer.IsEmpty && !token.IsCancellationRequested)
			{
				List<ListViewItem> itemsToDraw = [];
				while (globalResultBuffer.TryTake(out var item)) itemsToDraw.Add(item);
				this.BeginInvoke((MethodInvoker)delegate {
					resultsList.BeginUpdate();
					resultsList.Items.AddRange([.. itemsToDraw]);

					if (sortColumn >= 0 && sortColumn < resultsList.Columns.Count)
					{
						resultsList.ListViewItemSorter = new ListViewItemComparer(sortColumn, resultsList.Sorting);
						resultsList.Sort();
					}

					resultsList.EndUpdate();
				});
			}
		}

		private static volatile bool _cacheBuilt = false;
		private static readonly object _cacheLock = new();

		private static Dictionary<string, HashSet<string>>? _wordToIdsCase;
		private static Dictionary<string, HashSet<string>>? _wordToIdsLower;

		private static List<StringTableEntry>? _allEntries;

		private readonly struct StringTableEntry(string id, string text)
		{
			public readonly string Id = id;
			public readonly string Text = text;
		}

		public static void ResetStringTableCache()
		{
			lock (_cacheLock)
			{
				_cacheBuilt = false;
				_wordToIdsCase = null;
				_wordToIdsLower = null;
				_allEntries = null;
			}
		}

		private void BuildStringTableCacheIfNeeded(CancellationToken token)
		{
			if (_cacheBuilt) return;

			lock (_cacheLock)
			{
				if (_cacheBuilt) return;

				var localWordToIdsCase = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
				var localWordToIdsLower = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
				var localAllEntries = new List<StringTableEntry>(8192);

				foreach (var pak in loadedPaks)
				{
					if (token.IsCancellationRequested) return;

					foreach (var entry in pak.entries)
					{
						if (token.IsCancellationRequested) return;
						if (!entry.name.EndsWith(".conf", StringComparison.OrdinalIgnoreCase)) continue;

						byte[] data = pak.GetFileBytes(entry);
						if (data == null || data.Length == 0) continue;

						string content = Encoding.UTF8.GetString(data);

						if (!content.Contains("StringTableDefinition", StringComparison.Ordinal) &&
							!content.Contains("Localization", StringComparison.Ordinal)) continue;
						if (!content.Contains("Ids {", StringComparison.Ordinal) ||
							!content.Contains("Texts {", StringComparison.Ordinal)) continue;

						ParseStringTableIntoCache(
							content,
							localAllEntries,
							localWordToIdsCase,
							localWordToIdsLower);
					}
				}

				if (token.IsCancellationRequested) return;

				_wordToIdsCase = localWordToIdsCase;
				_wordToIdsLower = localWordToIdsLower;
				_allEntries = localAllEntries;

				_cacheBuilt = true;
			}
		}

		private static void ParseStringTableIntoCache(
			string content,
			List<StringTableEntry> allEntries,
			Dictionary<string, HashSet<string>> wordToIdsCase,
			Dictionary<string, HashSet<string>> wordToIdsLower)
		{
			List<string> ids = new(4096);
			List<string> texts = new(4096);

			ReadOnlySpan<char> span = content.AsSpan();
			int currentSection = 0;
			bool inMultiLine = false;

			StringBuilder multiLineBuilder = new(512);

			int pos = 0;
			while (pos < span.Length)
			{
				int nextLineBreak = span[pos..].IndexOfAny('\r', '\n');
				int lineLen = (nextLineBreak == -1) ? span.Length - pos : nextLineBreak;
				ReadOnlySpan<char> lineSpan = span.Slice(pos, lineLen).Trim();

				if (nextLineBreak == -1) pos = span.Length;
				else
				{
					pos += lineLen;
					if (pos < span.Length && span[pos] == '\r') pos++;
					if (pos < span.Length && span[pos] == '\n') pos++;
				}

				if (lineSpan.Length == 0) continue;

				if (lineSpan.StartsWith("Ids {".AsSpan()) || lineSpan.StartsWith("Ids\t{".AsSpan()))
				{
					currentSection = 1; continue;
				}
				if (lineSpan.StartsWith("Texts {".AsSpan()) || lineSpan.StartsWith("Texts\t{".AsSpan()))
				{
					currentSection = 2; continue;
				}
				if (currentSection != 0 && lineSpan[0] == '}')
				{
					currentSection = 0; inMultiLine = false; multiLineBuilder.Clear(); continue;
				}

				if (currentSection == 1)
				{
					ids.Add(CleanAndRemoveQuotesFromSpan(lineSpan));
				}
				else if (currentSection == 2)
				{
					if (lineSpan.SequenceEqual("\" \"".AsSpan()))
					{
						texts.Add(string.Empty); continue;
					}

					bool hasSlash = lineSpan.Length > 0 && lineSpan[^1] == '\\';
					ReadOnlySpan<char> cleanedSpan = hasSlash ? lineSpan[..^1].TrimEnd() : lineSpan;
					string cleaned = CleanAndRemoveQuotesFromSpan(cleanedSpan);

					if (!inMultiLine)
					{
						multiLineBuilder.Clear();
						multiLineBuilder.Append(cleaned);
					}
					else
					{
						multiLineBuilder.Append(' ').Append(cleaned);
					}

					if (hasSlash)
					{
						inMultiLine = true;
					}
					else
					{
						texts.Add(multiLineBuilder.ToString());
						inMultiLine = false;
					}
				}
			}

			int count = Math.Min(ids.Count, texts.Count);
			for (int i = 0; i < count; i++)
			{
				if (string.IsNullOrEmpty(ids[i])) continue;

				string fullId = "#" + ids[i];
				string text = texts[i] ?? string.Empty;

				allEntries.Add(new StringTableEntry(fullId, text));

				IndexWordsFromText(fullId, text, wordToIdsCase, wordToIdsLower);
			}
		}

		private static void IndexWordsFromText(
			string fullId,
			string text,
			Dictionary<string, HashSet<string>> wordToIdsCase,
			Dictionary<string, HashSet<string>> wordToIdsLower)
		{
			if (string.IsNullOrEmpty(text)) return;
			ReadOnlySpan<char> span = text.AsSpan();

			int start = -1;
			for (int i = 0; i < span.Length; i++)
			{
				char c = span[i];
				if (char.IsLetterOrDigit(c) || c == '_')
				{
					if (start == -1) start = i;
				}
				else
				{
					if (start != -1)
					{
						AddWordToInvertedIndex(span[start..i], fullId, wordToIdsCase, wordToIdsLower);
						start = -1;
					}
				}
			}
			if (start != -1)
			{
				AddWordToInvertedIndex(span[start..], fullId, wordToIdsCase, wordToIdsLower);
			}
		}

		private static void AddWordToInvertedIndex(
			ReadOnlySpan<char> wordSpan,
			string fullId,
			Dictionary<string, HashSet<string>> wordToIdsCase,
			Dictionary<string, HashSet<string>> wordToIdsLower)
		{
			string wordCase = wordSpan.ToString();
			string wordLower = wordCase.ToLowerInvariant();

			if (!wordToIdsCase.TryGetValue(wordCase, out var setCase))
			{
				setCase = new HashSet<string>(StringComparer.Ordinal);
				wordToIdsCase[wordCase] = setCase;
			}
			setCase.Add(fullId);

			if (!wordToIdsLower.TryGetValue(wordLower, out var setLower))
			{
				setLower = new HashSet<string>(StringComparer.Ordinal);
				wordToIdsLower[wordLower] = setLower;
			}
			setLower.Add(fullId);
		}

		private static HashSet<string> ResolveFromCache(string query, bool matchCase, bool wholeWord)
		{
			HashSet<string> result = new(StringComparer.Ordinal);

			if (wholeWord)
			{
				var wordDict = matchCase ? _wordToIdsCase : _wordToIdsLower;
				string searchKey = matchCase ? query : query.ToLowerInvariant();

				if (wordDict != null && wordDict.TryGetValue(searchKey, out var matchingIds))
				{
					foreach (var id in matchingIds) result.Add(id);
				}
			}
			else
			{
				var compOpt = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

				if (_allEntries != null)
				{
					foreach (var entry in _allEntries)
					{
						if (entry.Text.Contains(query, compOpt))
						{
							result.Add(entry.Id);
						}
					}
				}
			}

			return result;
		}

		public void ResolveStringTableQueryIfNeeded(ref string actualQuery1, ref string resolvedMsg, bool matchCase, bool wholeWord, bool resolveST, CancellationToken token)
		{
			if (actualQuery1.StartsWith('#') || !resolveST) return;

			this.Invoke(new Action(() => statusLabel.Text = " Resolving Stringtable (brutal optimized cache)..."));

			BuildStringTableCacheIfNeeded(token);

			if (token.IsCancellationRequested) return;

			var resolvedIds = ResolveFromCache(actualQuery1, matchCase, wholeWord);

			if (resolvedIds.Count > 0)
			{
				actualQuery1 = string.Join("|", resolvedIds.Select(id => @"(?<!\w)" + Regex.Escape(id) + @"(?!\w)"));
				resolvedMsg = $" (Resolved text to {resolvedIds.Count} IDs)";

				string displayQuery = resolvedIds.Count == 1 ? resolvedIds.First() : $"{resolvedIds.Count} multi-IDs";
				this.Invoke(new Action(() => statusLabel.Text = $" Searching usages for resolved ID {displayQuery}..."));
			}
		}

		private static string CleanAndRemoveQuotesFromSpan(ReadOnlySpan<char> input)
		{
			if (input.Length == 0) return string.Empty;
			if (input.SequenceEqual("\" \"".AsSpan())) return string.Empty;

			int start = 0;
			int len = input.Length;

			if (len >= 2 && input[0] == '"' && input[len - 1] == '"')
			{
				if (input[len - 2] != '\\')
				{
					start = 1; len -= 2;
				}
			}
			else if (len > 0 && input[0] == '"')
			{
				start = 1; len -= 1;
			}

			if (len <= 0) return string.Empty;
			ReadOnlySpan<char> sliced = input[start..(start + len)];

			if (sliced.IndexOf("\\\"".AsSpan()) != -1)
			{
				return sliced.ToString().Replace("\\\"", "\"");
			}

			return sliced.ToString();
		}

		private List<(Pak pak, PakEntryFile entry)> GatherTargetFiles(ref string actualQuery1, string[] allowedExtensions, string[] excludes)
		{
			List<(Pak pak, PakEntryFile entry)> itemsToProcess = [];
			foreach (var pak in loadedPaks)
			{
				foreach (var entry in pak.entries)
				{
					if (excludes.Length > 0 && excludes.Any(ex => entry.name.Contains(ex, StringComparison.OrdinalIgnoreCase))) continue;

#if ENABLE_GRAPH_PLUGIN
					if (!EnfusionAssetIndex.FilterByQueryUpgrade(entry.name, actualQuery1, out string _)) continue;
#endif

					string ext = System.IO.Path.GetExtension(entry.name).ToLower();
					if (allowedExtensions.Contains("*.*") || allowedExtensions.Contains(ext))
					{
						itemsToProcess.Add((pak, entry));
					}
				}
			}

#if ENABLE_GRAPH_PLUGIN
					EnfusionAssetIndex.FilterByQueryUpgrade("", actualQuery1, out string tempQuery);
					actualQuery1 = tempQuery;
#endif
			return itemsToProcess;
		}

		private void ProcessMultiLineSearch(byte[] data, string actualQuery1, string actualQuery2, bool matchCase, Pak pak, PakEntryFile entry, ref int matchCount, ConcurrentBag<ListViewItem> globalResultBuffer, Action flushResults)
		{
			string fullContent = Encoding.UTF8.GetString(data);
			string combinedPattern;
			if (chkRegex.Checked)
			{
				combinedPattern = actualQuery1 + @".*?\r?\n.*?" + actualQuery2;
			}
			else
			{
				string esc1 = actualQuery1.Contains('*') || actualQuery1.Contains('?') ? ConvertToWildcardRegex(actualQuery1) : Regex.Escape(actualQuery1);
				string esc2 = string.IsNullOrEmpty(actualQuery2) ? "" : (actualQuery2.Contains('*') || actualQuery2.Contains('?') ? ConvertToWildcardRegex(actualQuery2) : Regex.Escape(actualQuery2));
				combinedPattern = string.IsNullOrEmpty(esc2) ? esc1 : esc1 + @".*?\r?\n.*?" + esc2;
			}

			RegexOptions opts = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
			try
			{
				foreach (Match m in Regex.Matches(fullContent, combinedPattern, opts))
				{
					int lineNum = 1;
					for (int idx = 0; idx < m.Index; idx++) if (fullContent[idx] == '\n') lineNum++;

					int endLineNum = lineNum;
					for (int idx = m.Index; idx < m.Index + m.Length; idx++) if (fullContent[idx] == '\n') endLineNum++;

					string[] matchLines = m.Value.Split(_splitCharsNewLines, StringSplitOptions.RemoveEmptyEntries);
					string displaySnippet = matchLines.Length > 1
						? $"{matchLines.First().Trim()}  ➔  {matchLines.Last().Trim()}"
						: matchLines.FirstOrDefault()?.Trim() ?? "";

					Interlocked.Increment(ref matchCount);
					var listItem = CreateResultItemDynamic(pak, entry, lineNum, endLineNum, actualQuery1, [displaySnippet]);
					if (listItem.Tag is SearchResultInfo info) info.SearchTermVar = actualQuery2;

					globalResultBuffer.Add(listItem);
					if (globalResultBuffer.Count >= 50) flushResults();
				}
			}
			catch { }
		}

		private void ProcessSingleLineSearch(byte[] data, string actualQuery1, bool matchCase, bool isSmartRegex, bool wholeWord, StringComparison fastComp, Pak pak, PakEntryFile entry, ref int matchCount, ConcurrentBag<ListViewItem> globalResultBuffer, Action flushResults, CancellationToken token)
		{
			LazyStringContext normContext = new(data);
			for (int i = 0; i < normContext.Lines.Count; i++)
			{
				token.ThrowIfCancellationRequested();
				var lineSpan = normContext.GetSpan(i);
				bool isMatch = false;

				if (isSmartRegex)
				{
					string lineStr = normContext.GetString(i);
					if (SmartPatternMatch(lineStr, actualQuery1, matchCase)) isMatch = true;
				}
				else if (!wholeWord)
				{
					isMatch = lineSpan.IndexOf(actualQuery1.AsSpan(), fastComp) >= 0;
				}
				else
				{
					int index = 0;
					while (true)
					{
						int found = lineSpan[index..].IndexOf(actualQuery1.AsSpan(), fastComp);
						if (found < 0) break;
						int absFound = index + found;
						bool isStartBoundary = (absFound == 0 || IsBoundaryChar(lineSpan[absFound - 1]));
						bool isEndBoundary = (absFound + actualQuery1.Length == lineSpan.Length || IsBoundaryChar(lineSpan[absFound + actualQuery1.Length]));

						if (isStartBoundary && isEndBoundary) { isMatch = true; break; }
						index = absFound + actualQuery1.Length;
					}
				}

				if (isMatch)
				{
					Interlocked.Increment(ref matchCount);
					string lineStrTrim = normContext.GetString(i).Trim();
					globalResultBuffer.Add(CreateResultItemDynamic(pak, entry, i + 1, -1, actualQuery1, [lineStrTrim]));
					if (globalResultBuffer.Count >= 50) flushResults();
				}
			}
		}

		#endregion

		private static bool IsSpanWhiteSpace(ReadOnlySpan<char> span)
		{
			foreach (char c in span) if (!char.IsWhiteSpace(c)) return false;
			return true;
		}

		private ListViewItem CreateResultItemDynamic(Pak sourcePak, PakEntryFile entry, int lineStart, int lineEnd, string term, string[] dynamicCols)
		{
			string displayPath = $"📦 [{System.IO.Path.GetFileName(sourcePak.name)}] -> {entry.name}";

			ListViewItem item = new(displayPath);
			string rangeStr = lineEnd > lineStart ? $"{lineStart}-{lineEnd}" : lineStart.ToString();
			item.SubItems.Add(rangeStr);

			foreach(string colData in dynamicCols)
			{
				item.SubItems.Add(colData);
			}

			item.Tag = new SearchResultInfo {
				SourcePak = sourcePak,
				EntryFile = entry,
				LineNumber = lineStart,
				LineNumberEnd = lineEnd,
				SearchTerm = term,
				FileName = System.IO.Path.GetFileName(entry.name)
			};
			return item;
		}

		#region Context Window Preview Rendering

		private void ResultsList_SelectedIndexChanged(object? sender, EventArgs e)
		{
			bool hasSelection = resultsList.SelectedItems.Count > 0;

#if ENABLE_GRAPH_PLUGIN
			_GraphPlugin?.UpdateGraphButtonState(hasSelection);
#endif

			if (!hasSelection)
			{
				_previewController.Clear();
				return;
			}

			var selectedItem = resultsList.SelectedItems[0];
			if (selectedItem.Tag is SearchResultInfo info)
			{
				try
				{
					byte[] data = info.SourcePak.GetFileBytes(info.EntryFile);
					if (data != null)
					{
						string text = Encoding.UTF8.GetString(data);

						bool matchCase = chkMatchCase.Checked;
						bool multiLine = chkMultiLine.Checked;
						bool regexMode = chkRegex.Checked;
						bool wholeWord = chkWholeWord.Checked;
						bool useSyntaxHighlighting = AppSettings.Current.EnableSyntaxHighlighting && !AppSettings.Current.LiteMode;

						_previewController.ShowContextWindow(info, text, matchCase, multiLine, regexMode, wholeWord, useSyntaxHighlighting);
					}
					else
					{
						_previewController.Clear();
						_previewController.AppendBatchText("[READ ERROR: Source file is corrupted or unreadable]");
					}
				}
				catch (Exception ex)
				{
					_previewController.Clear();
					_previewController.AppendBatchText($"[EXCEPTION ENGAGED IN PREVIEW MATRIX: {ex.Message}]");
				}
			}
		}

		private void ResultsList_DoubleClick(object? sender, EventArgs e)
		{
			if (resultsList.SelectedItems.Count == 0) return;
			var selectedItem = resultsList.SelectedItems[0];
			if (selectedItem.Tag is SearchResultInfo info)
			{
				try
				{
					byte[] data = info.SourcePak.GetFileBytes(info.EntryFile);
					if (data == null) return;

					string cacheDir = System.IO.Path.Combine(Application.StartupPath, "WorkbenchExplorer_Cache");
					if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

					string safeFileName = info.FileName;
					foreach (char c in System.IO.Path.GetInvalidFileNameChars()) safeFileName = safeFileName.Replace(c, '_');

					string tempFilePath = System.IO.Path.Combine(cacheDir, safeFileName);
					System.IO.File.WriteAllBytes(tempFilePath, data);

					Process.Start(new ProcessStartInfo { FileName = tempFilePath, UseShellExecute = true });
					statusLabel.Text = $" File sent to external editor: {safeFileName}";
				}
				catch (Exception ex)
				{
					MessageBox.Show($"Failed to launch external editor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private void ResultsList_ColumnClick(object? sender, ColumnClickEventArgs e)
		{
			if (e.Column == sortColumn) resultsList.Sorting = resultsList.Sorting == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
			else { sortColumn = e.Column; resultsList.Sorting = SortOrder.Ascending; }

			PlayerPrefs.SetInt("DeepSearch_SortCol", sortColumn);
			PlayerPrefs.SetInt("DeepSearch_SortDir", (int)resultsList.Sorting);

			resultsList.ListViewItemSorter = new ListViewItemComparer(e.Column, resultsList.Sorting);
			resultsList.Sort();
		}

		private static bool IsBoundaryChar(char c)
		{
			return !(char.IsLetterOrDigit(c) || c == '_');
		}
		#endregion

		#region Tokenizer Structures (0-Alloc Engine)

		private enum TokenType { None, Identifier, String, BraceOpen, BraceClose, EOF }

		private ref struct ConfTokenizer
		{
			private readonly ReadOnlySpan<char> _text;
			private int _pos;

			public ConfTokenizer(ReadOnlySpan<char> text)
			{
				_text = text;
				_pos = 0;
			}

			public TokenType Next(out ReadOnlySpan<char> value)
			{
				value = default;

				while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
				if (_pos >= _text.Length) return TokenType.EOF;

				char c = _text[_pos];

				if (c == '{') { _pos++; return TokenType.BraceOpen; }
				if (c == '}') { _pos++; return TokenType.BraceClose; }

				if (c == '"')
				{
					_pos++;
					int start = _pos;
					while (_pos < _text.Length)
					{
						if (_text[_pos] == '"' && _text[_pos - 1] != '\\') break;
						_pos++;
					}
					value = _text[start.._pos];
					if (_pos < _text.Length) _pos++;
					return TokenType.String;
				}

				int idStart = _pos;
				while (_pos < _text.Length && !char.IsWhiteSpace(_text[_pos]) && _text[_pos] != '{' && _text[_pos] != '}' && _text[_pos] != '"')
				{
					_pos++;
				}
				value = _text[idStart.._pos];
				return TokenType.Identifier;
			}
		}

		#endregion
	}

	public class ListViewItemComparer(int column, SortOrder order) : IComparer
	{
		private readonly int col = column;
		private readonly SortOrder order = order;

		public int Compare(object? x, object? y)
		{
			if (x == null && y == null) return 0;
			if (x == null) return order == SortOrder.Ascending ? -1 : 1;
			if (y == null) return order == SortOrder.Ascending ? 1 : -1;

			string textX = ((ListViewItem)x).SubItems[col].Text;
			string textY = ((ListViewItem)y).SubItems[col].Text;
			int returnVal;

			if (col == 1)
			{
				if (textX.Contains('-')) textX = textX.Split('-')[0];
				if (textY.Contains('-')) textY = textY.Split('-')[0];
				_ = int.TryParse(textX, out int numX);
				_ = int.TryParse(textY, out int numY);
				returnVal = numX.CompareTo(numY);
			}
			else returnVal = string.Compare(textX, textY, StringComparison.OrdinalIgnoreCase);

			if (order == SortOrder.Descending) returnVal *= -1;
			return returnVal;
		}
	}
}