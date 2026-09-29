#define USE_XFUSION

using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Drawing.Text;
using PakWorkbench.src;
using PakWorkbench.src.ext.Workbench;
using PakWorkbench.src.ext.Preview;
using PakWorkbench.src.ext.Preview.Xfusion;

using System.Net.Http;
using System.Net.WebSockets;
using Microsoft.Web.WebView2.WinForms;

namespace PakWorkbench.src.ext
{
	#region Helper Types & UI Theme

	public static partial class UITheme
	{
		private static readonly PrivateFontCollection _fontCollection = new();
		private static FontFamily _fontSegoeFamily = null!;

		public const string FontSegoe = "Segoe UI";
		public const string FontMono = "Consolas";
		public const string EmojiFontFamily = "Segoe UI Emoji";

		public const float StandardFontSize = 9F;

		public static float CurrentScale { get; private set; } = 1.0f;

		public static Font MainFont { get; set; } = new Font(FontSegoe, StandardFontSize);
		public static Font MainFontBold { get; private set; } = null!;
		public static Font TitleFont { get; private set; } = null!;
		public static Font SidebarFont { get; private set; } = null!;
		public static Font CodeFont { get; private set; } = null!;
		public static Font HeaderFont { get; private set; } = null!;
		public static Font ToolTipFont { get; private set; } = null!;
		public static Font ToolTipFontBold { get; private set; } = null!;

		public static Font TextTitleFix { get; private set; } = null!;
		public static Font TextBodyFix { get; private set; } = null!;
		public static Font TextBoldFix { get; private set; } = null!;

		public static Font ControlBoldFix { get; private set; } = null!;
		public static Font ControlSmallFix { get; private set; } = null!;

		static UITheme()
		{
			LoadEmbeddedRobotoFromMemory();
			InitializeFonts();
		}

		private static void LoadEmbeddedRobotoFromMemory()
		{
			try
			{
				var assembly = typeof(UITheme).Assembly;
				const string resourceName = "PakWorkbench.src.ext.res.Roboto-Regular.ttf";

				using Stream? stream = assembly.GetManifestResourceStream(resourceName);
				if (stream != null)
				{
					int length = (int)stream.Length;
					byte[] fontData = new byte[length];

					stream.ReadExactly(fontData, 0, length);

					IntPtr fontPtr = Marshal.AllocCoTaskMem(length);
					try
					{
						Marshal.Copy(fontData, 0, fontPtr, length);
						_fontCollection.AddMemoryFont(fontPtr, length);

						uint dummy = 0;
						AddFontMemResourceEx(fontPtr, (uint)length, IntPtr.Zero, ref dummy);
					}
					finally
					{
						Marshal.FreeCoTaskMem(fontPtr);
					}

					if (_fontCollection.Families.Length > 0)
					{
						_fontSegoeFamily = _fontCollection.Families[0];
						return;
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to load embedded font: {ex.Message}");
			}

			_fontSegoeFamily = new FontFamily(FontSegoe);
		}

		[LibraryImport("gdi32.dll")]
		private static partial IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

		private static void InitializeFonts()
		{
			MainFont?.Dispose();
			MainFontBold?.Dispose();
			TitleFont?.Dispose();
			SidebarFont?.Dispose();
			CodeFont?.Dispose();
			HeaderFont?.Dispose();
			ToolTipFont?.Dispose();
			ToolTipFontBold?.Dispose();
			TextTitleFix?.Dispose();
			TextBodyFix?.Dispose();
			ControlBoldFix?.Dispose();
			ControlSmallFix?.Dispose();

			MainFont = new Font(_fontSegoeFamily, 9F);
			MainFontBold = new Font(_fontSegoeFamily, 9F, FontStyle.Bold);
			TitleFont = new Font(_fontSegoeFamily, 10F, FontStyle.Bold);
			SidebarFont = new Font(_fontSegoeFamily, 15F);

			CodeFont = new Font(FontMono, 10.5F);

			HeaderFont = new Font(_fontSegoeFamily, 12F, FontStyle.Bold);
			ToolTipFont = new Font(_fontSegoeFamily, StandardFontSize, FontStyle.Regular);
			ToolTipFontBold = new Font(_fontSegoeFamily, StandardFontSize, FontStyle.Bold);

			TextTitleFix = new Font(_fontSegoeFamily, StandardFontSize, FontStyle.Bold);
			TextBodyFix = new Font(_fontSegoeFamily, StandardFontSize, FontStyle.Regular);
			TextBoldFix = TextTitleFix;

			ControlBoldFix = new Font(_fontSegoeFamily, 8.5F, FontStyle.Bold);
			ControlSmallFix = new Font(_fontSegoeFamily, 8F, FontStyle.Regular);

			System.Diagnostics.Debug.WriteLine($"Current font: {_fontSegoeFamily.Name} (Scale: {CurrentScale})");
		}

		public static void InitScale(float scale)
		{
			CurrentScale = scale;
			InitializeFonts();
		}

		public static int Scale(int value)
		{
			return (int)(value * CurrentScale);
		}

		public static readonly Color BgMain = Color.FromArgb(18, 18, 18);
		public static readonly Color BgPanel = Color.FromArgb(24, 24, 24);
		public static readonly Color BgDark = Color.FromArgb(32, 32, 32);
		public static readonly Color BgDarker = Color.FromArgb(15, 15, 15);
		public static readonly Color BgSelected = Color.FromArgb(45, 45, 48);

		public static readonly Color TextMain = Color.FromArgb(182, 189, 204);
		public static readonly Color TextMuted = Color.FromArgb(150, 150, 150);
		public static readonly Color TextDarkGray = Color.Gray;
		public static readonly Color TextAccent = Color.FromArgb(180, 220, 240);

		public static readonly Color Accent = Color.FromArgb(0, 122, 204);
		public static readonly Color AccentHover = Color.FromArgb(0, 140, 230);
		public static readonly Color AccentPressed = Color.FromArgb(0, 100, 180);

		public static readonly Color ToggleActive = Color.FromArgb(16, 185, 129);
		public static readonly Color ToggleInactive = Color.FromArgb(60, 60, 65);
		public static readonly Color ToggleThumb = Color.FromArgb(240, 240, 240);
		public static readonly Color ToggleDisabled = Color.FromArgb(40, 40, 42);

		public static readonly Color ToolTipBg = Color.FromArgb(28, 28, 30);
		public static readonly Color BorderDefault = Color.FromArgb(65, 65, 70);

		public static readonly Color SyntaxKeyword = Color.FromArgb(89, 166, 233);
		public static readonly Color SyntaxType = Color.FromArgb(64, 181, 172);
		public static readonly Color SyntaxString = Color.FromArgb(193, 120, 221);
		public static readonly Color SyntaxComment = Color.FromArgb(89, 170, 89);
		public static readonly Color SyntaxHighlight = Color.FromArgb(97, 175, 239);
		public static readonly Color SyntaxBuiltin = Color.FromArgb(220, 220, 170);

		public static readonly Color SyntaxNumber = Color.FromArgb(213, 223, 240);
		public static readonly Color SyntaxOperator = Color.FromArgb(213, 223, 240);
		public static readonly Color SyntaxPreprocessor = Color.FromArgb(212, 253, 149);
		public static readonly Color SyntaxFunction = Color.FromArgb(243, 173, 88);

		public static readonly Color LogSuccess = Color.FromArgb(152, 195, 121);
		public static readonly Color LogWarning = Color.FromArgb(229, 192, 123);
		public static readonly Color LogError = Color.FromArgb(184, 34, 0);
		public static readonly Color LogInfo = Color.FromArgb(0, 255, 65);

		public static readonly Color BgPreview = Color.FromArgb(38, 40, 43);
		public static readonly Color ActionDestructive = Color.FromArgb(180, 50, 50);

		public static readonly Color SideIcon = Color.FromArgb(80, 186, 238);
		public static readonly Color FolderIcon = Color.FromArgb(87, 134, 169);

		public static readonly Color BgHover = Color.FromArgb(40, 40, 45);
	}

	public class VirtualNode
	{
		public string Name { get; set; } = string.Empty;
		public object? Tag { get; set; }
		public Dictionary<string, VirtualNode> SubDirs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
		public List<VirtualNode> Files { get; set; } = [];
	}

	public class PakFileWrapper
	{
		public PakEntryFile Entry { get; set; } = null!;
		public Pak SourcePak { get; set; } = null!;
	}

	public class ModernButton : Button
	{
		private readonly Color _defaultColor;
		private readonly Color _hoverColor;
		private readonly Color _pressedColor;
		private bool _isHovered;

		public ModernButton(Color defaultColor, Color hoverColor, Color pressedColor)
		{
			_defaultColor = defaultColor;
			_hoverColor = hoverColor;
			_pressedColor = pressedColor;

			this.FlatStyle = FlatStyle.Flat;
			this.FlatAppearance.BorderSize = 0;
			this.BackColor = _defaultColor;
			this.ForeColor = Color.White;
			this.Font = UITheme.TitleFont;
			this.Cursor = Cursors.Hand;

			this.AutoSize = true;
			this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			this.MinimumSize = new Size(UITheme.Scale(160), UITheme.Scale(40));
			this.Padding = new Padding(UITheme.Scale(10), UITheme.Scale(5), UITheme.Scale(10), UITheme.Scale(5));

			this.MouseEnter += (s, e) => { _isHovered = true; this.BackColor = _hoverColor; };
			this.MouseLeave += (s, e) => { _isHovered = false; this.BackColor = _defaultColor; };
			this.MouseDown += (s, e) => { this.BackColor = _pressedColor; };
			this.MouseUp += (s, e) => { this.BackColor = _isHovered ? _hoverColor : _defaultColor; };
		}
	}

	#endregion

	public partial class ViewerForm : Form
	{
		#region Win32 Imports & Regex

		[LibraryImport("user32.dll")]
		private static partial int SendMessage(IntPtr hWnd, int wMsg, [MarshalAs(UnmanagedType.Bool)] bool wParam, int lParam);
		private const int WM_SETREDRAW = 11;

		[LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
		private static partial int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

		[GeneratedRegex(@"ID\s+""([^""]+)""", RegexOptions.IgnoreCase)]
		private static partial Regex ProjectIdRegex();

		#endregion

		#region Fields & Properties

		public const PreviewEngine ActivePreviewEngine = PreviewEngine.XFusion;

		private readonly float _dpiScale = 1.0f;

		private CustomTreeView treeView = null!;
		private XFusionPreviewController previewBox = null!;

		internal readonly List<Pak> loadedPaks = [];
		private readonly List<string> pakFilePaths = [];

		private TextBox searchBox = null!;
		private System.Windows.Forms.Timer searchTimer = null!;

		private StatusStrip statusStrip = null!;
		private ToolStripStatusLabel statusLblState = null!;
		private ToolStripDropDownButton statusLblArchives = null!;
		private ToolStripStatusLabel statusLblFiles = null!;
		private ToolStripStatusLabel statusLblSelected = null!;
		private ToolStripProgressBar progressBar = null!;
		private ToolStripButton btnCancelProcess = null!;

		private ToolStripButton btnExtractAll = null!;
		private ToolStripButton btnExtractSelected = null!;
		private ToolStripButton btnLoadVanilla = null!;
		private CheckBox chkLoadVanilla = null!;
		private ToolStripMenuItem ctxMenuExtractSelected = null!;
		private ToolStripMenuItem ctxMenuExtractAll = null!;
		private ToolStripMenuItem ctxMenuCompareWithDisk = null!;
		private ToolStripMenuItem ctxMenuRemoveProject = null!;
		private ToolStripMenuItem ctxMenuOpenInWorkbench = null!;

		private ToolStripMenuItem ctxMenuViewDependencies = null!;

		private ToolStripButton btnAppendToggle = null!;

		private Button btnToggleWrap = null!;
		private bool logEnabled = true;
		private bool currentWrapMode = false;
		private bool isUserResizingSplitter = false;

		private CancellationTokenSource? searchCts;
		private CancellationTokenSource? _diffCts;
		private CancellationTokenSource? _extractCts;

		private SplitContainer split = null!;
		private DeepSearchControl deepSearchControl = null!;

		private Panel _previewContainerPanel = null!;
		private DiffManagerControl? _diffControl;

		private Panel sidebarPanel = null!;
		private Panel mainContentContainer = null!;
		private Panel explorerViewPanel = null!;
		private Panel deepSearchViewPanel = null!;
		private Panel welcomePanel = null!;

		private Button btnNavExplorer = null!;
		private Button btnNavSearch = null!;
		private Button btnNavSettings = null!;

		private Button btnNavWorkbenchSync = null!;
		private Panel workbenchSyncViewPanel = null!;
		private WorkbenchSyncControl workbenchSyncControl = null!;

		private Button btnNavMoDiscover = null!;
		private Panel modiscoverViewPanel = null!;
		private MoDiscoverControl modiscoverControl = null!;

		private ToolTip globalToolTip = null!;

		private int totalFilesCount = 0;
		private int totalMatchedCount = 0;
		private bool isSearchActive = false;

		private float _splitterRatio = 0.25f;
		private bool _isSettingSplitterProgrammatically = false;

		private readonly Dictionary<string, string> folderToProjectIdCache = new(StringComparer.OrdinalIgnoreCase);

		private readonly DiffOrchestrator _diffOrchestrator;

		private ToolStripMenuItem ctxMenuExpandSelected = null!;
		private ToolStripMenuItem ctxMenuCollapseSelected = null!;

		private static readonly char[] _pipeSeparator = ['|'];
		private static readonly char[] _spaceSeparator = [' '];
		private static readonly char[] _commaSeparator = [','];
		private static readonly char[] _pathSeparators = ['/', '\\'];

		private static readonly Bitmap _redCrossIcon = CreateRedCrossImage();

		#endregion

		#region Constructor

		public ViewerForm(List<string> initialPakPaths)
		{
			_dpiScale = this.DeviceDpi / 96f;
			if (_dpiScale < 1.0f) _dpiScale = 1.0f;
			UITheme.InitScale(_dpiScale);

			_diffOrchestrator = new DiffOrchestrator(this, _dpiScale);

			PakIndex.Initialize();
			InitializeBaseForm();
			InitializeSidebar();
			InitializeMainContentArea();
			InitializeStatusStrip();
			InitializeExplorerView();
			this.FormClosing += (sender, e) => SaveRootFoldersState();
			SetupSplitter();
			SetupFormLifecycleEvents(initialPakPaths);
		}

		#endregion

		#region Initialization Methods

		private void InitializeBaseForm()
		{
			this.AutoScaleMode = AutoScaleMode.None;
			this.Text = AppInfo.Title;

			try
			{
				var assembly = System.Reflection.Assembly.GetExecutingAssembly();
				using var stream = assembly.GetManifestResourceStream("PakWorkbench.src.ext.res.icon.ico");
				if (stream != null)
				{
					this.Icon = new Icon(stream);
				}
			}
			catch
			{
			}

			this.Size = new Size(UITheme.Scale(1280), UITheme.Scale(800));
			this.StartPosition = FormStartPosition.CenterScreen;
			this.BackColor = UITheme.BgMain;
			this.ForeColor = UITheme.TextMain;

			ToolStripManager.Renderer = new DarkToolStripRenderer();

			globalToolTip = new ToolTip { UseAnimation = true, UseFading = true, OwnerDraw = true };

			globalToolTip.Draw += (s, e) =>
			{
				e.Graphics.Clear(UITheme.ToolTipBg);

				using var pen = new Pen(UITheme.BorderDefault);
				e.Graphics.DrawRectangle(pen, new Rectangle(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1));

				using var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
				using var brush = new SolidBrush(UITheme.TextMain);
				Rectangle textRect = new(6, 0, e.Bounds.Width - 12, e.Bounds.Height);
				e.Graphics.DrawString(e.ToolTipText, UITheme.ToolTipFont, brush, textRect, sf);
			};

			globalToolTip.Popup += (s, e) =>
			{
				if (e.AssociatedControl != null)
				{
					string text = globalToolTip.GetToolTip(e.AssociatedControl) ?? string.Empty;

					if (!string.IsNullOrEmpty(text))
					{
						using var g = this.CreateGraphics();
						SizeF size = g.MeasureString(text, UITheme.ToolTipFont);
						e.ToolTipSize = new Size((int)Math.Ceiling(size.Width) + 12, (int)Math.Ceiling(size.Height) + 8);
					}
				}
			};
		}

		private void InitializeSidebar()
		{
			sidebarPanel = new Panel
			{
				Dock = DockStyle.Left,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				MinimumSize = new Size(UITheme.Scale(50), 0),
				BackColor = UITheme.BgPanel,
				Padding = new Padding(0, UITheme.Scale(10), 0, UITheme.Scale(10))
			};

			btnNavExplorer = CreateSidebarButton("📁", "File Explorer");
			btnNavSearch = CreateSidebarButton("🔍", "Deep Search");
			btnNavWorkbenchSync = CreateSidebarButton("🔄", "Workbench NET Sync");
			btnNavMoDiscover = CreateSidebarButton("⊕", "MoDiscover");
			btnNavSettings = CreateSidebarButton("⚙️", "Settings");

			btnNavExplorer.Dock = DockStyle.Top;
			btnNavSearch.Dock = DockStyle.Top;
			btnNavWorkbenchSync.Dock = DockStyle.Top;

			btnNavSettings.Dock = DockStyle.Bottom;
			btnNavMoDiscover.Dock = DockStyle.Bottom;

			btnNavExplorer.Click += (s, e) => SwitchView(explorerViewPanel, btnNavExplorer);
			btnNavSearch.Click += BtnNavSearch_Click;
			btnNavWorkbenchSync.Click += (s, e) => SwitchView(workbenchSyncViewPanel, btnNavWorkbenchSync);
			btnNavMoDiscover.Click += (s, e) => SwitchView(modiscoverViewPanel, btnNavMoDiscover);
			btnNavSettings.Click += BtnSettings_Click;

			sidebarPanel.Controls.Add(btnNavMoDiscover);
			sidebarPanel.Controls.Add(btnNavSettings);

			sidebarPanel.Controls.Add(btnNavWorkbenchSync);
			sidebarPanel.Controls.Add(btnNavSearch);
			sidebarPanel.Controls.Add(btnNavExplorer);
		}

		private void InitializeMainContentArea()
		{
			mainContentContainer = new Panel
			{
				Dock = DockStyle.Fill,
				BackColor = UITheme.BgMain
			};

			Panel middleContainer = new() { Dock = DockStyle.Fill };
			middleContainer.Controls.Add(sidebarPanel);
			middleContainer.Controls.Add(mainContentContainer);
			mainContentContainer.BringToFront();
			this.Controls.Add(middleContainer);

			welcomePanel = BuildWelcomePanel();
			mainContentContainer.Controls.Add(welcomePanel);

			explorerViewPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
			deepSearchViewPanel = new Panel { Dock = DockStyle.Fill, Visible = false };

			modiscoverViewPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
			modiscoverControl = new MoDiscoverControl { Dock = DockStyle.Fill };
			modiscoverViewPanel.Controls.Add(modiscoverControl);

			this.Load += (s, e) =>
			{
				modiscoverControl.Initialize(this, sidebarPanel, btnNavWorkbenchSync, treeView, btnNavMoDiscover);
			};

			workbenchSyncViewPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
			workbenchSyncControl = new WorkbenchSyncControl { Dock = DockStyle.Fill };
			workbenchSyncViewPanel.Controls.Add(workbenchSyncControl);

			deepSearchControl = new DeepSearchControl();
			deepSearchViewPanel.Controls.Add(deepSearchControl);

			mainContentContainer.Controls.Add(explorerViewPanel);
			mainContentContainer.Controls.Add(deepSearchViewPanel);
			mainContentContainer.Controls.Add(workbenchSyncViewPanel);
			mainContentContainer.Controls.Add(modiscoverViewPanel);
			mainContentContainer.BringToFront();

			searchTimer = new System.Windows.Forms.Timer { Interval = 400 };
			searchTimer.Tick += SearchTimer_Tick;
		}

		private void InitializeStatusStrip()
		{
			statusStrip = new StatusStrip
			{
				BackColor = UITheme.Accent,
				ForeColor = Color.White,
				SizingGrip = false,
				ShowItemToolTips = false,
				Font = UITheme.MainFont,
				ImageScalingSize = new Size(UITheme.Scale(16), UITheme.Scale(16))
			};

			statusLblState = new ToolStripStatusLabel(" Ready. Load files or a folder to display the contents.")
			{
				Spring = true,
				TextAlign = ContentAlignment.MiddleLeft
			};

			statusLblArchives = new ToolStripDropDownButton("Archives: 0")
			{
				Padding = new Padding(UITheme.Scale(10), 0, UITheme.Scale(10), 0),
				DropDownDirection = ToolStripDropDownDirection.AboveRight,
				ShowDropDownArrow = true
			};

			statusLblArchives.Paint += (s, e) =>
			{
				using var pen = new Pen(SystemColors.ActiveBorder);
				e.Graphics.DrawLine(pen, 0, 0, 0, statusLblArchives.Height);
			};

			statusLblArchives.DropDown.BackColor = UITheme.BgSelected;
			statusLblArchives.DropDown.ForeColor = UITheme.TextMain;
			statusLblArchives.DropDown.Renderer = new DarkToolStripRenderer();
			if (statusLblArchives.DropDown is ToolStripDropDownMenu mainDropdownMenu)
			{
				mainDropdownMenu.ShowImageMargin = true;
				mainDropdownMenu.ShowCheckMargin = false;
				mainDropdownMenu.ShowItemToolTips = false;
			}

			statusLblFiles = new ToolStripStatusLabel("Files: 0")
			{
				BorderSides = ToolStripStatusLabelBorderSides.Left,
				Padding = new Padding(UITheme.Scale(10), 0, UITheme.Scale(10), 0)
			};

			statusLblSelected = new ToolStripStatusLabel("Selected: 0")
			{
				BorderSides = ToolStripStatusLabelBorderSides.Left,
				Padding = new Padding(UITheme.Scale(10), 0, UITheme.Scale(10), 0)
			};

			progressBar = new ToolStripProgressBar
			{
				Visible = false,
				AutoSize = false,
				Size = new Size(UITheme.Scale(100), UITheme.Scale(18)),
				Style = ProgressBarStyle.Continuous,
				Margin = new Padding(UITheme.Scale(10), UITheme.Scale(3), UITheme.Scale(10), UITheme.Scale(3))
			};

			btnCancelProcess = new ToolStripButton("❌ Abort")
			{
				Visible = false,
				BackColor = Color.DarkRed,
				ForeColor = Color.White,
				Margin = new Padding(UITheme.Scale(10), 0, UITheme.Scale(10), 0),
				Padding = new Padding(UITheme.Scale(5), UITheme.Scale(2), UITheme.Scale(5), UITheme.Scale(2))
			};
			btnCancelProcess.Click += (s, e) =>
			{
				_diffCts?.Cancel();
				_extractCts?.Cancel();
				this.Cursor = Cursors.Default;
			};

			statusStrip.Items.Add(statusLblState);
			statusStrip.Items.Add(progressBar);
			statusStrip.Items.Add(btnCancelProcess);
			statusStrip.Items.Add(statusLblArchives);
			statusStrip.Items.Add(statusLblFiles);
			statusStrip.Items.Add(statusLblSelected);

			AttachGlobalTooltips(statusStrip.Items, statusStrip, 5, -25);

			this.Controls.Add(statusStrip);
		}

		private void InitializeExplorerView()
		{
			SetupToolStrip();

			split = new SplitContainer
			{
				Dock = DockStyle.Fill,
				BackColor = UITheme.BgDark,
				SplitterWidth = UITheme.Scale(4)
			};

			SetupTreeViewAndFilter();
			SetupPreviewBox();

			Panel mainPanel = new() { Dock = DockStyle.Fill, Padding = new Padding(0) };
			mainPanel.Controls.Add(split);

			explorerViewPanel.Controls.Add(mainPanel);
			mainPanel.BringToFront();

			UpdateUIState();
		}

		private void SetupToolStrip()
		{
			ToolStrip toolStrip = new()
			{
				BackColor = UITheme.BgDark,
				ForeColor = Color.White,
				GripStyle = ToolStripGripStyle.Hidden,
				Padding = new Padding(UITheme.Scale(8)),
				Renderer = new DarkToolStripRenderer(),
				Font = UITheme.MainFont,
				ImageScalingSize = new Size(UITheme.Scale(16), UITheme.Scale(16)),
				AutoSize = true,
				ShowItemToolTips = false
			};

			int marginSm = UITheme.Scale(4);
			int marginLg = UITheme.Scale(8);

			ToolStripButton btnOpenFiles = new("📂 Open PAK", null, BtnOpenFiles_Click)
			{
				Margin = new Padding(0, 0, marginSm, 0),
				ToolTipText = "Open one or more .pak files"
			};

			ToolStripButton btnOpenFolder = new("📁 Scan Folder", null, BtnOpenFolder_Click)
			{
				Margin = new Padding(0, 0, marginLg, 0),
				ToolTipText = "Scan an entire directory for .pak files"
			};

			btnAppendToggle = new ToolStripButton("☐ Append Mode", null, (s, e) => {
				btnAppendToggle.Text = btnAppendToggle.Checked ? "☑ Append Mode" : "☐ Append Mode";
				btnAppendToggle.BackColor = btnAppendToggle.Checked ? UITheme.Accent : Color.Transparent;
			})
			{
				CheckOnClick = true,
				Margin = new Padding(marginLg, 0, marginSm, 0),
				ToolTipText = "If enabled, opened PAK files or folders are added to the existing list instead of overwriting it."
			};

			btnLoadVanilla = new ToolStripButton("☐ Auto-Load Vanilla", null, (s, e) => {
				SetVanillaLoadState(!btnLoadVanilla.Checked);

				if (btnLoadVanilla.Checked)
				{
					BtnLoadVanilla_Click(s!, e);
				}
				else
				{
					RemoveVanillaFiles();
				}
			})
			{
				CheckOnClick = false,
				Margin = new Padding(0, 0, marginLg, 0),
				ToolTipText = "Automatically load game base (vanilla) assets alongside custom mods."
			};

			btnExtractSelected = new ToolStripButton("📦 Extract Selected", null, BtnExtractSelected_Click)
			{
				Margin = new Padding(marginLg, 0, marginSm, 0),
				Enabled = false,
				ToolTipText = "Extract currently selected items to disk"
			};

			btnExtractAll = new ToolStripButton("📤 Extract All", null, BtnExtractAll_Click)
			{
				Margin = new Padding(0, 0, marginSm, 0),
				Enabled = false,
				ToolTipText = "Extract all loaded files to disk"
			};

			toolStrip.Items.AddRange(
			[
				btnOpenFiles,
				btnOpenFolder,
				new ToolStripSeparator() { Margin = new Padding(marginSm, 0, marginSm, 0) },
				btnAppendToggle,
				btnLoadVanilla,
				new ToolStripSeparator() { Margin = new Padding(marginSm, 0, marginSm, 0) },
				btnExtractSelected,
				btnExtractAll
			]);

			AttachGlobalTooltips(toolStrip.Items, toolStrip, 10, 25);

			explorerViewPanel.Controls.Add(toolStrip);
		}

		private void SetupTreeViewAndFilter()
		{
			Panel leftPanel = new() { Dock = DockStyle.Fill, BackColor = UITheme.BgPanel };

			Panel filterPanel = new()
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				MinimumSize = new Size(0, UITheme.Scale(36)),
				BackColor = UITheme.BgDark,
				Padding = new Padding(UITheme.Scale(8))
			};

			Label lblSearchIcon = new() { Text = "🔍", Dock = DockStyle.Left, AutoSize = true, MinimumSize = new Size(UITheme.Scale(25), 0), ForeColor = UITheme.TextDarkGray, TextAlign = ContentAlignment.MiddleCenter, Font = UITheme.TitleFont, Padding = new Padding(0, UITheme.Scale(2), 0, 0) };

			Button btnClearFilter = new()
			{
				Text = "❌",
				Dock = DockStyle.Right,
				AutoSize = true,
				MinimumSize = new Size(UITheme.Scale(25), 0),
				FlatStyle = FlatStyle.Flat,
				ForeColor = UITheme.ActionDestructive,
				Cursor = Cursors.Hand,
				Font = UITheme.ControlSmallFix,
			};
			btnClearFilter.FlatAppearance.BorderSize = 0;
			btnClearFilter.FlatAppearance.MouseOverBackColor = UITheme.ToggleInactive;
			btnClearFilter.FlatAppearance.MouseDownBackColor = UITheme.BgDark;

			searchBox = new() { Dock = DockStyle.Fill, Font = UITheme.CodeFont, BackColor = UITheme.BgDark, ForeColor = UITheme.TextDarkGray, BorderStyle = BorderStyle.None, Text = "Filter items by name...", Margin = new Padding(UITheme.Scale(5)), Enabled = false };
			searchBox.Enter += SearchBox_Enter;
			searchBox.Leave += SearchBox_Leave;
			searchBox.TextChanged += SearchBox_TextChanged;

			btnClearFilter.Click += BtnClearFilter_Click;

			Panel searchBoxContainer = new() { Dock = DockStyle.Fill, Padding = new Padding(UITheme.Scale(4), UITheme.Scale(2), 0, 0) };
			searchBoxContainer.Controls.Add(searchBox);

			filterPanel.Controls.Add(searchBoxContainer);
			filterPanel.Controls.Add(btnClearFilter);
			filterPanel.Controls.Add(lblSearchIcon);

			treeView = new() { Dock = DockStyle.Fill, ItemHeight = UITheme.MainFont.Height + UITheme.Scale(4) };

			treeView.AfterSelect += TreeView_AfterSelect;
			treeView.NodeMouseClick += TreeView_NodeMouseClick;
			treeView.BeforeExpand += TreeView_BeforeExpand;
			treeView.SelectionChanged += TreeView_SelectionChanged;

			leftPanel.Controls.Add(treeView);
			leftPanel.Controls.Add(filterPanel);

			SetupTreeViewContextMenu();

			split.Panel1.Controls.Add(leftPanel);
		}

		private void SetupTreeViewContextMenu()
		{
			ContextMenuStrip ctxMenu = new()
			{
				Renderer = new DarkToolStripRenderer(),
				BackColor = UITheme.BgSelected,
				ForeColor = UITheme.TextMain,
				Font = UITheme.MainFont,
				ShowItemToolTips = false
			};

			ToolStripMenuItem ctxMenuSendToSync = new("🎯 Set as Workbench Sync Target", null, (s, e) => {
				if (treeView.SelectedNode?.Tag is PakFileWrapper wrapper && workbenchSyncViewPanel != null && workbenchSyncControl != null && WorkbenchSyncControl.IsSyncSupported(wrapper.Entry.name))
				{
					try
					{
						string tempDir = Path.Combine(Path.GetTempPath(), "PakWorkbenchSync");
						Directory.CreateDirectory(tempDir);

						string physicalPath = Path.Combine(tempDir, Path.GetFileName(wrapper.Entry.name));

						byte[] fileData = wrapper.SourcePak.GetPreviewBytes(wrapper.Entry, (int)wrapper.Entry.originalSize);

						if (fileData != null && fileData.Length > 0)
						{
							File.WriteAllBytes(physicalPath, fileData);

							workbenchSyncControl.SetTargetScript(physicalPath, wrapper.Entry.name);
							SwitchView(workbenchSyncViewPanel, btnNavWorkbenchSync);
						}
						else
						{
							MessageBox.Show(this, "Failed to read file contents from the PAK archive.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
						}
					}
					catch (Exception ex)
					{
						MessageBox.Show(this, $"An error occurred while extracting the file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
					}
				}
			});

			ToolStripSeparator syncSeparator = new();

			ctxMenuOpenInWorkbench = new ToolStripMenuItem("🛠 Open in Workbench", null, BtnOpenInWorkbench_Click) { Enabled = false };
			ctxMenuCompareWithDisk = new ToolStripMenuItem("📄 Compare with disk...", null, BtnCompareWithDisk_Click) { Enabled = false };
			ctxMenuExtractSelected = new ToolStripMenuItem("Extract Selected", null, BtnExtractSelected_Click) { Enabled = false };

			ctxMenuViewDependencies = new ToolStripMenuItem("🕸 View Dependencies / Graph", null, modiscoverControl.BtnViewDependencies_Click) { Enabled = false, Visible = false };

			ctxMenu.Items.Add(ctxMenuSendToSync);
			ctxMenu.Items.Add(syncSeparator);
			ctxMenu.Items.Add(ctxMenuOpenInWorkbench);
			ctxMenu.Items.Add(ctxMenuCompareWithDisk);
			ctxMenu.Items.Add(ctxMenuExtractSelected);
			ctxMenu.Items.Add(ctxMenuViewDependencies);

			ctxMenu.Items.Add(new ToolStripSeparator());

			ctxMenuExpandSelected = new ToolStripMenuItem("Expand Selected Node", null, (s, e) => {
				if (treeView.SelectedNode != null)
				{
					treeView.BeginUpdate();
					ForceDeepVirtualLoad(treeView.SelectedNode.Nodes);
					ExpandNodeFully(treeView.SelectedNode);
					treeView.EndUpdate();
				}
			});

			ctxMenuCollapseSelected = new ToolStripMenuItem("Collapse Selected Node", null, (s, e) => {
				if (treeView.SelectedNode != null)
				{
					treeView.BeginUpdate();
					treeView.SelectedNode.Collapse();
					treeView.EndUpdate();
				}
			});

			ctxMenu.Items.Add(ctxMenuExpandSelected);
			ctxMenu.Items.Add(ctxMenuCollapseSelected);
			ctxMenu.Items.Add(new ToolStripMenuItem("Expand All", null, (s, e) => {
				treeView.BeginUpdate(); ForceDeepVirtualLoad(treeView.Nodes); treeView.ExpandAll(); treeView.EndUpdate();
			}));
			ctxMenu.Items.Add(new ToolStripMenuItem("Collapse All", null, (s, e) => {
				treeView.BeginUpdate(); treeView.CollapseAll(); treeView.EndUpdate();
			}));

			ctxMenu.Items.Add(new ToolStripSeparator());

			ctxMenuExtractAll = new ToolStripMenuItem("Extract All", null, BtnExtractAll_Click) { Enabled = false };
			ctxMenu.Items.Add(ctxMenuExtractAll);

			ctxMenuRemoveProject = new ToolStripMenuItem("Remove Project/PAK from Session", null, BtnRemoveProject_Click)
			{
				Visible = false,
				ForeColor = UITheme.TextMain,
				Image = _redCrossIcon
			};
			ctxMenu.Items.Add(new ToolStripSeparator());
			ctxMenu.Items.Add(ctxMenuRemoveProject);

			ctxMenu.Opening += (s, e) => {
				bool isSupported = treeView.SelectedNode?.Tag is PakFileWrapper wrapper &&
								   workbenchSyncControl != null &&
								   WorkbenchSyncControl.IsSyncSupported(wrapper.Entry.name);

				ctxMenuSendToSync.Visible = isSupported;
				syncSeparator.Visible = isSupported;
			};

			AttachGlobalTooltips(ctxMenu.Items, ctxMenu, 150, 0);

			treeView.ContextMenuStrip = ctxMenu;
		}

		private void SetupPreviewBox()
		{
			_previewContainerPanel = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.BgMain, Padding = new Padding(0) };

			previewBox = new XFusionPreviewController(UITheme.CodeFont);

			Panel previewHeader = new() {
				Dock = DockStyle.Top,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				MinimumSize = new Size(0, UITheme.Scale(32)),
				BackColor = UITheme.BgDark,
				Padding = new Padding(UITheme.Scale(8), UITheme.Scale(4), UITheme.Scale(8), UITheme.Scale(4))
			};

			Label lblHeaderTitle = new() {
				Text = "📄 FILE PREVIEW / ENGINE LOG",
				Font = UITheme.MainFontBold,
				ForeColor = UITheme.TextMain,
				Dock = DockStyle.Left,
				TextAlign = ContentAlignment.MiddleLeft,
				AutoSize = true,
				Padding = new Padding(0, UITheme.Scale(4), 0, 0)
			};

			btnToggleWrap = new Button {
				Text = "Word Wrap: OFF",
				Font = new Font(UITheme.FontSegoe, 8F, FontStyle.Bold),
				AutoSize = true,
				Dock = DockStyle.Right,
				FlatStyle = FlatStyle.Flat,
				Cursor = Cursors.Hand,
				ForeColor = UITheme.TextMain
			};
			btnToggleWrap.FlatAppearance.BorderSize = 1;
			btnToggleWrap.FlatAppearance.BorderColor = UITheme.ToggleInactive;
			btnToggleWrap.FlatAppearance.MouseOverBackColor = UITheme.BgSelected;
			btnToggleWrap.FlatAppearance.MouseDownBackColor = UITheme.BgDark;
			btnToggleWrap.Click += BtnToggleWrap_Click;

			previewHeader.Controls.Add(lblHeaderTitle);
			previewHeader.Controls.Add(btnToggleWrap);

			_previewContainerPanel.Controls.Add(previewBox.UIControl);
			_previewContainerPanel.Controls.Add(previewHeader);

			split.Panel2.Controls.Add(_previewContainerPanel);
		}

		private void SetupSplitter()
		{
			split.Resize += (s, e) =>
			{
				if (split.Width > UITheme.Scale(100))
				{
					_isSettingSplitterProgrammatically = true;
					try { split.SplitterDistance = (int)(split.Width * _splitterRatio); } catch { }
					_isSettingSplitterProgrammatically = false;
				}
			};

			split.SplitterMoving += (s, e) =>
			{
				if (split.Width > 0)
				{
					int pct = (int)Math.Round((double)e.SplitX / split.Width * 100);

					Point localCursorPos = split.PointToClient(Cursor.Position);
					localCursorPos.Offset(UITheme.Scale(15), UITheme.Scale(5));

					globalToolTip.Show($"{pct}%", split, localCursorPos);
				}
			};

			split.DoubleClick += (s, e) =>
			{
				if (split.Width > 0)
				{
					_splitterRatio = 0.30f;

					_isSettingSplitterProgrammatically = true;
					split.SplitterDistance = (int)(split.Width * _splitterRatio);
					_isSettingSplitterProgrammatically = false;

					PlayerPrefs.SetInt("SplitterRatio", (int)(_splitterRatio * 100));

					int pct = (int)Math.Round(_splitterRatio * 100);
					Point localCursorPos = split.PointToClient(Cursor.Position);
					localCursorPos.Offset(UITheme.Scale(15), UITheme.Scale(5));
					globalToolTip.Show($"Reset: {pct}%", split, localCursorPos, 1500);
				}
			};

			split.SplitterMoved += (s, e) =>
			{
				if (isUserResizingSplitter && !_isSettingSplitterProgrammatically && split.Width > 0)
				{
					_splitterRatio = (float)split.SplitterDistance / split.Width;
					PlayerPrefs.SetInt("SplitterRatio", (int)(_splitterRatio * 100));
					globalToolTip.Hide(split);
				}
			};
		}

		private void SetupFormLifecycleEvents(List<string> initialPakPaths)
		{
			this.Load += (s, e) => HandleFormLoad(initialPakPaths);

			this.Shown += (s, e) =>
			{
				isUserResizingSplitter = true;
			};

			this.FormClosing += (s, e) =>
			{
				workbenchSyncControl?.Cleanup();

				PlayerPrefs.SetBool("WordWrapEnabled", currentWrapMode);

				if (btnAppendToggle != null)
				{
					PlayerPrefs.SetBool("AppendModeEnabled", btnAppendToggle.Checked);
				}

				_diffOrchestrator.CloseCurrentDiff();

				_diffControl?.Dispose();
				previewBox?.Dispose();
			};
		}

		private Panel BuildWelcomePanel()
		{
			TableLayoutPanel mainLayout = new() {
				Dock = DockStyle.Fill,
				BackColor = UITheme.BgMain,
				ColumnCount = 3,
				RowCount = 3
			};
			mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
			mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

			TableLayoutPanel centerPnl = new() {
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 1,
				RowCount = 4,
				Margin = new Padding(0)
			};
			centerPnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

			Label lblTitle = new() {
				Text = "PakWorkbench",
				Font = new Font("Segoe UI", 28F, FontStyle.Bold),
				ForeColor = Color.White,
				AutoSize = true,
				Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
				TextAlign = ContentAlignment.MiddleCenter,
				Margin = new Padding(0, UITheme.Scale(20), 0, 0)
			};

			Label lblSub = new() {
				Text = "Open a PAK archive or scan a folder to begin browsing assets.",
				Font = UITheme.HeaderFont,
				ForeColor = UITheme.TextMuted,
				AutoSize = true,
				Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
				TextAlign = ContentAlignment.MiddleCenter,
				Margin = new Padding(0, UITheme.Scale(16), 0, UITheme.Scale(32))
			};

			TableLayoutPanel btnPanel = new() {
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 2,
				RowCount = 1,
				Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
			};
			btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

			ModernButton btnOpen = new(UITheme.Accent, UITheme.AccentHover, UITheme.AccentPressed) { Text = "📂 Open PAK File", Margin = new Padding(UITheme.Scale(10)), Anchor = AnchorStyles.Right };
			ModernButton btnScan = new(
				UITheme.BgSelected,
				UITheme.BorderDefault,
				UITheme.BgDark
			)
			{
				Text = "📁 Scan Folder",
				Margin = new Padding(UITheme.Scale(10)),
				Anchor = AnchorStyles.Left
			};
			btnOpen.Click += BtnOpenFiles_Click;
			btnScan.Click += BtnOpenFolder_Click;

			btnPanel.Controls.Add(btnOpen, 0, 0);
			btnPanel.Controls.Add(btnScan, 1, 0);

			chkLoadVanilla = new CheckBox {
				Text = "Always load Vanilla assets (Auto-Load)",
				Font = UITheme.HeaderFont,
				ForeColor = UITheme.TextMuted,
				AutoSize = true,
				Anchor = AnchorStyles.Top,
				Cursor = Cursors.Hand,
				Margin = new Padding(0, UITheme.Scale(10), 0, 0),
				Checked = PlayerPrefs.GetBool("AutoLoadVanilla", false)
			};

			chkLoadVanilla.CheckedChanged += ChkLoadVanilla_CheckedChanged;

			centerPnl.Controls.Add(lblTitle, 0, 0);
			centerPnl.Controls.Add(lblSub, 0, 1);
			centerPnl.Controls.Add(btnPanel, 0, 2);
			centerPnl.Controls.Add(chkLoadVanilla, 0, 3);

			mainLayout.Controls.Add(centerPnl, 1, 1);

			Panel pnl = new() { Dock = DockStyle.Fill, BackColor = UITheme.BgMain };
			pnl.Controls.Add(mainLayout);

			return pnl;
		}

		#endregion

		#region Event Handlers & Lifecycle

		private async void HandleFormLoad(List<string> initialPakPaths)
		{
			int savedRatio = PlayerPrefs.GetInt("SplitterRatio", 25);
			if (savedRatio < 10 || savedRatio > 90) savedRatio = 25;
			_splitterRatio = savedRatio / 100f;

			if (split.Width > UITheme.Scale(100))
			{
				_isSettingSplitterProgrammatically = true;
				try { split.SplitterDistance = (int)(split.Width * _splitterRatio); } catch { }
				_isSettingSplitterProgrammatically = false;
			}

			currentWrapMode = PlayerPrefs.GetBool("WordWrapEnabled", false);
			UpdateWrapButtonUi();

			if (btnAppendToggle != null)
			{
				btnAppendToggle.Checked = PlayerPrefs.GetBool("AppendModeEnabled", false);
				btnAppendToggle.Text = btnAppendToggle.Checked ? "☑ Append Mode" : "☐ Append Mode";
				btnAppendToggle.ForeColor = UITheme.TextMain;
			}

			bool autoLoadVanilla = PlayerPrefs.GetBool("AutoLoadVanilla", false);
			SetVanillaLoadState(autoLoadVanilla);

			ApplySettings();

			previewBox.Initialize();
			previewBox.WordWrap = currentWrapMode;

			await LoadPreviousSessionOrInitial(initialPakPaths);

			if (autoLoadVanilla)
			{
				string vanillaPath = WorkbenchLauncherModule.GetReforgerVanillaPath();
				if (!string.IsNullOrEmpty(vanillaPath))
				{
					string addonsDir = Path.Combine(vanillaPath, "addons");
					if (Directory.Exists(addonsDir))
					{
						await ScanFolderAsync(addonsDir);
					}
				}
			}
		}

		private async Task LoadPreviousSessionOrInitial(List<string> initialPakPaths)
		{
			bool autoLoad = AppSettings.Current.AutoLoadLast;
			string lastLoadedType = PlayerPrefs.GetString("LastLoadedType", "");
			string lastLoadedPaths = PlayerPrefs.GetString("LastLoadedPaths", "");

			if (autoLoad && !string.IsNullOrEmpty(lastLoadedPaths))
			{
				if (lastLoadedType == "FOLDER")
				{
					await ScanFolderAsync(lastLoadedPaths);
				}
				else if (lastLoadedType == "FILES")
				{
					var paths = lastLoadedPaths.Split(_pipeSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();

					if (paths.Count > 0)
					{
						await AddPaksToViewAsync(paths);
					}
				}
			}
			else if (initialPakPaths != null && initialPakPaths.Count > 0)
			{
				await AddPaksToViewAsync(initialPakPaths);
			}
			else
			{
				SwitchView(welcomePanel, btnNavExplorer);
			}
		}

		private void ApplySettings()
		{
			treeView.DrawMode = AppSettings.Current.EnableCustomIcons && !AppSettings.Current.LiteMode ? TreeViewDrawMode.OwnerDrawAll : TreeViewDrawMode.Normal;
			treeView.ShowLines = !AppSettings.Current.EnableCustomIcons || AppSettings.Current.LiteMode;

			btnNavSearch.Visible = AppSettings.Current.EnableSearch && !AppSettings.Current.LiteMode;
			btnNavMoDiscover.Visible = AppSettings.Current.EnableMoDiscover && !AppSettings.Current.LiteMode;

			btnNavWorkbenchSync.Visible = AppSettings.Current.EnableWorkbenchSync && !AppSettings.Current.LiteMode;

			logEnabled = AppSettings.Current.EnableLog && !AppSettings.Current.LiteMode;
			treeView.Invalidate();
		}

		private void BtnSettings_Click(object? sender, EventArgs e)
		{
			using var sf = new SettingsForm();
			if (sf.ShowDialog() == DialogResult.OK)
			{
				ApplySettings();
			}
		}

		#endregion

		#region Navigation & Session UI State

		private Button CreateSidebarButton(string icon, string tooltip)
		{
			Button btn = new()
			{
				Text = icon,
				Font = UITheme.SidebarFont,
				Dock = DockStyle.Top,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				MinimumSize = new Size(UITheme.Scale(50), UITheme.Scale(50)),
				FlatStyle = FlatStyle.Flat,
				ForeColor = UITheme.TextMuted,
				BackColor = UITheme.BgPanel,
				Cursor = Cursors.Hand,
				Margin = new Padding(0),
				Padding = new Padding(0)
			};
			btn.FlatAppearance.BorderSize = 0;
			btn.FlatAppearance.MouseOverBackColor = UITheme.BgSelected;
			btn.FlatAppearance.MouseDownBackColor = UITheme.BgDark;

			btn.MouseEnter += (s, e) => {
				if (btn.BackColor != UITheme.BgSelected)
					btn.ForeColor = Color.White;
			};

			btn.MouseLeave += (s, e) => {
				if (btn.BackColor != UITheme.BgSelected)
					btn.ForeColor = UITheme.TextMuted;
			};

			globalToolTip.SetToolTip(btn, tooltip);
			return btn;
		}

		internal void SwitchView(Panel newView, Button? activeButton)
		{
			if (loadedPaks.Count == 0 && newView == explorerViewPanel)
			{
				newView = welcomePanel;
			}

			mainContentContainer.SuspendLayout();

			explorerViewPanel.Visible = (newView == explorerViewPanel);
			deepSearchViewPanel.Visible = (newView == deepSearchViewPanel);
			workbenchSyncViewPanel.Visible = (newView == workbenchSyncViewPanel);
			welcomePanel.Visible = (newView == welcomePanel);
			modiscoverViewPanel.Visible = (newView == modiscoverViewPanel);

			modiscoverControl.ShowMoDiscoverNavigationButtons(newView == modiscoverViewPanel);

			btnNavExplorer.BackColor = UITheme.BgPanel;
			btnNavSearch.BackColor = UITheme.BgPanel;
			btnNavWorkbenchSync.BackColor = UITheme.BgPanel;
			btnNavMoDiscover.BackColor = UITheme.BgPanel;

			btnNavExplorer.ForeColor = UITheme.TextMuted;
			btnNavSearch.ForeColor = UITheme.TextMuted;
			btnNavWorkbenchSync.ForeColor = UITheme.TextMuted;
			btnNavMoDiscover.ForeColor = UITheme.TextMuted;

			if (activeButton != null)
			{
				activeButton.BackColor = UITheme.BgSelected;
				activeButton.ForeColor = UITheme.SideIcon;
			}

			mainContentContainer.ResumeLayout(true);

			if (newView == modiscoverViewPanel)
			{
				statusLblState.Text = " MoDiscover: Browsing online database...";
			}
			else if (newView == workbenchSyncViewPanel)
			{
				statusLblState.Text = " Workbench NET Sync: Ready to sync scripts.";
			}
			else if (newView == deepSearchViewPanel)
			{
				statusLblState.Text = " Deep Search: Ready to query.";
			}
			else if (newView == welcomePanel)
			{
				statusLblState.Text = " Ready. Load files to begin.";
			}
			else if (newView == explorerViewPanel)
			{
				if (treeView.SelectedNode != null)
				{
					string cleanName = treeView.SelectedNode.Text.Replace("📁 ", "").Replace("📄 ", "").Trim();

					if (treeView.SelectedNode.Tag is PakFileWrapper)
					{
						statusLblState.Text = $" File: {cleanName}";
					}
					else if (treeView.SelectedNode.Parent == null)
					{
						statusLblState.Text = $" Pak: {cleanName}";
					}
					else
					{
						statusLblState.Text = $" Directory: {cleanName}";
					}
				}
				else
				{
					statusLblState.Text = isSearchActive ? $" Filter applied. ({totalMatchedCount} matches)" : " Ready.";
				}
			}
		}

		private void BtnNavSearch_Click(object? sender, EventArgs e)
		{
			if (loadedPaks.Count == 0) return;

			deepSearchControl.UpdatePakList(loadedPaks, FindProjectIdForPak);
			SwitchView(deepSearchViewPanel, btnNavSearch);
		}

		private void UpdateSessionView()
		{
			if (statusStrip.InvokeRequired)
			{
				statusStrip.Invoke(new Action(UpdateSessionView));
				return;
			}

			statusLblArchives.DropDownItems.Clear();

			var paksSnapshot = loadedPaks.ToList();
			if (paksSnapshot.Count == 0) return;

			var groupedPaks = paksSnapshot
				.GroupBy(pak => GetResolvedProjectId(pak.name))
				.OrderBy(g => g.Key);

			foreach (var group in groupedPaks)
			{
				string projName = group.Key;
				var paksInGroup = group.ToList();

				var projectFolderMenu = new ToolStripMenuItem($"Remove '{projName}' from Session")
				{
					Font = UITheme.MainFont,
					ForeColor = UITheme.TextMain,
					Image = _redCrossIcon
				};

				projectFolderMenu.DropDown.Renderer = new DarkToolStripRenderer();
				projectFolderMenu.DropDown.BackColor = UITheme.BgSelected;
				projectFolderMenu.DropDown.ForeColor = UITheme.TextMain;

				if (projectFolderMenu.DropDown is ToolStripDropDownMenu subDropDownMenu)
				{
					subDropDownMenu.ShowImageMargin = true;
					subDropDownMenu.ShowCheckMargin = false;
					subDropDownMenu.ShowItemToolTips = false;
				}

				var removeAllBtn = new ToolStripMenuItem($"Remove ALL ({paksInGroup.Count} PAKs)")
				{
					Font = UITheme.MainFont,
					ForeColor = UITheme.TextMain,
					Image = _redCrossIcon
				};

				removeAllBtn.Click += async (s, e) => await RemovePaksAsync(paksInGroup);

				projectFolderMenu.DropDownItems.Add(removeAllBtn);
				projectFolderMenu.DropDownItems.Add(new ToolStripSeparator());

				foreach (var pak in paksInGroup)
				{
					var pakBtn = new ToolStripMenuItem($"Remove: {Path.GetFileName(pak.name)}")
					{
						ToolTipText = $"Click to remove {pak.name} from current session\nPath: {pak.name}",
						Font = UITheme.MainFont,
						ForeColor = UITheme.TextMain,
						Image = _redCrossIcon
					};

					pakBtn.MouseEnter += (s, e) => {
						if (!string.IsNullOrEmpty(pakBtn.ToolTipText)) {
							Point mousePos = projectFolderMenu.DropDown.PointToClient(Control.MousePosition);
							mousePos.Offset(15, 15);
							globalToolTip.Show(pakBtn.ToolTipText, projectFolderMenu.DropDown, mousePos.X, mousePos.Y);
						}
					};
					pakBtn.MouseLeave += (s, e) => {
						globalToolTip.Hide(projectFolderMenu.DropDown);
					};

					var currentPak = pak;
					pakBtn.Click += async (s, e) => await RemovePaksAsync([currentPak]);

					projectFolderMenu.DropDownItems.Add(pakBtn);
				}

				statusLblArchives.DropDownItems.Add(projectFolderMenu);
			}
		}

		private void UpdateUIState()
		{
			bool hasPaks = loadedPaks.Count > 0;
			bool hasSelection = treeView.SelectedNodes.Count > 0;

			if (hasPaks && welcomePanel.Visible)
			{
				welcomePanel.Visible = false;
				SwitchView(explorerViewPanel, btnNavExplorer);
			}
			else if (!hasPaks && !welcomePanel.Visible)
			{
				explorerViewPanel.Visible = false;
				deepSearchViewPanel.Visible = false;
				welcomePanel.Visible = true;
				welcomePanel.BringToFront();
			}

			btnExtractAll.Enabled = hasPaks;
			ctxMenuExtractAll.Enabled = hasPaks;
			searchBox.Enabled = hasPaks;

			btnExtractSelected.Enabled = hasSelection;
			ctxMenuExtractSelected.Enabled = hasSelection;

			ctxMenuCompareWithDisk.Enabled = hasSelection && (treeView.SelectedNode?.Tag is PakFileWrapper || treeView.SelectedNode?.Text.Contains("📁") == true);

			statusLblArchives.Text = $"Archives: {loadedPaks.Count}";
			statusLblFiles.Text = isSearchActive ? $"Matches: {totalMatchedCount}" : $"Files: {totalFilesCount:N0}";
			statusLblSelected.Text = $"Selected: {treeView.SelectedNodes.Count}";

			if (!hasPaks) statusLblState.Text = " Ready. Load files to begin.";

			UpdateSessionView();
		}

		#endregion

		#region Tree View Logic & Events

		private static TreeNode CreateFolderNode(VirtualNode dirNode, bool addDummy = false)
		{
			TreeNode node = new($"📁 {dirNode.Name}")
			{
				Name = dirNode.Name,
				Tag = dirNode
			};
			if (addDummy)
			{
				node.Nodes.Add(new TreeNode("...") { Tag = "DUMMY" });
			}
			return node;
		}

		private static TreeNode CreateFileNode(VirtualNode fileNode)
		{
			return new TreeNode($"📄 {fileNode.Name}")
			{
				Name = fileNode.Name,
				Tag = fileNode.Tag
			};
		}

		private static void PopulateNodeWithVirtualData(TreeNode parentNode, VirtualNode vNode, bool forceFullLoad)
		{
			foreach (var subDir in vNode.SubDirs.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
			{
				TreeNode dirUiNode = CreateFolderNode(subDir, !forceFullLoad);
				if (forceFullLoad)
				{
					PopulateNodeWithVirtualData(dirUiNode, subDir, true);
				}
				parentNode.Nodes.Add(dirUiNode);
			}

			foreach (var file in vNode.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
			{
				parentNode.Nodes.Add(CreateFileNode(file));
			}
		}

		private void TreeView_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
		{
			TreeNode? expandingNode = e.Node;
			if (expandingNode == null) return;

			if (expandingNode.Nodes.Count == 1 && expandingNode.Nodes[0].Tag?.ToString() == "DUMMY")
			{
				treeView.BeginUpdate();
				expandingNode.Nodes.Clear();

				if (expandingNode.Tag is VirtualNode vNode)
				{
					PopulateNodeWithVirtualData(expandingNode, vNode, false);
				}
				treeView.EndUpdate();
			}
		}

		private static void ForceDeepVirtualLoad(TreeNodeCollection nodes)
		{
			foreach (TreeNode node in nodes)
			{
				if (node.Nodes.Count == 1 && node.Nodes[0].Tag?.ToString() == "DUMMY")
				{
					node.Nodes.Clear();
					if (node.Tag is VirtualNode vNode)
					{
						PopulateNodeWithVirtualData(node, vNode, false);
					}
				}
				if (node.Nodes.Count > 0)
				{
					ForceDeepVirtualLoad(node.Nodes);
				}
			}
		}

		private static void ExpandNodeFully(TreeNode node)
		{
			node.Expand();
			foreach (TreeNode child in node.Nodes)
			{
				ExpandNodeFully(child);
			}
		}

		private void TreeView_SelectionChanged(object? sender, EventArgs e)
		{
			UpdateUIState();
		}

		private void TreeView_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
		{
			if (e.Button == MouseButtons.Right)
			{
				treeView.SelectedNode = e.Node;

				bool isRootNode = (e.Node?.Parent == null);

				if (treeView.ContextMenuStrip != null)
				{
					foreach (ToolStripItem item in treeView.ContextMenuStrip.Items)
					{
						item.Visible = true;
					}

					string projName = e.Node != null ? e.Node.Text.Replace("📁 ", "").Trim() : "";

					string actualProjectId = string.Empty;
					if (e.Node != null)
					{
						var relatedPak = loadedPaks.FirstOrDefault(p => {
							return string.Equals(GetResolvedProjectId(p.name), projName, StringComparison.OrdinalIgnoreCase);
						});

						if (relatedPak != null)
						{
							actualProjectId = FindProjectIdForPak(relatedPak.name);
						}
					}

					bool isBlacklistedForDeps =
						string.Equals(projName, "ArmaReforger", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(projName, "core", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(actualProjectId, "58D0FB3206B6F859", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(actualProjectId, "5614BBCCBB55ED1C", StringComparison.OrdinalIgnoreCase);

					ctxMenuRemoveProject.Visible = isRootNode;

					bool canShowDeps = isRootNode && !isBlacklistedForDeps;
					ctxMenuViewDependencies.Visible = canShowDeps;
					ctxMenuViewDependencies.Enabled = canShowDeps;

					if (isRootNode)
					{
						ctxMenuRemoveProject.Text = $"Remove '{projName}' from Session";
						ctxMenuRemoveProject.DropDownItems.Clear();

						var paksInProject = loadedPaks.Where(p => {
							return string.Equals(GetResolvedProjectId(p.name), projName, StringComparison.OrdinalIgnoreCase);
						}).ToList();

						if (paksInProject.Count > 1)
						{
							var removeAllBtn = new ToolStripMenuItem($"Remove ALL ({paksInProject.Count} PAKs)")
							{
								ForeColor = UITheme.TextMain,
								Image = _redCrossIcon
							};
							removeAllBtn.Click += (s, ev) => BtnRemoveProject_Click(s, ev);
							ctxMenuRemoveProject.DropDownItems.Add(removeAllBtn);
							ctxMenuRemoveProject.DropDownItems.Add(new ToolStripSeparator());

							foreach (var pak in paksInProject)
							{
								var pakBtn = new ToolStripMenuItem($"Remove: {Path.GetFileName(pak.name)}")
								{
									ForeColor = UITheme.TextMain,
									Image = _redCrossIcon
								};
								var currentPak = pak;
								pakBtn.Click += async (s, ev) => await RemovePaksAsync([currentPak]);
								ctxMenuRemoveProject.DropDownItems.Add(pakBtn);
							}
						}
					}
				}

				if (e.Node != null)
				{
					bool isFile = e.Node.Tag is PakFileWrapper;

					if (ctxMenuExpandSelected != null) ctxMenuExpandSelected.Visible = !isFile;
					if (ctxMenuCollapseSelected != null) ctxMenuCollapseSelected.Visible = !isFile;

					if (ctxMenuCompareWithDisk != null)
					{
						if (isFile)
						{
							var wrapper = (PakFileWrapper)e.Node.Tag!;
							ctxMenuCompareWithDisk.Text = "📄 Compare file...";
							ctxMenuCompareWithDisk.Enabled = true;

							string ext = Path.GetExtension(wrapper.Entry.name);
							bool isSupported = WorkbenchLauncherModule.IsWorkbenchSupported(ext);

							ctxMenuOpenInWorkbench.Enabled = isSupported;
							ctxMenuOpenInWorkbench.Visible = isSupported;
						}
						else
						{
							ctxMenuCompareWithDisk.Text = "📁 Compare folder...";
							ctxMenuCompareWithDisk.Enabled = true;
							ctxMenuOpenInWorkbench.Enabled = false;
							ctxMenuOpenInWorkbench.Visible = false;
						}
					}
				}
			}
		}

		private void TreeView_AfterSelect(object? sender, TreeViewEventArgs e)
		{
			if (_diffControl != null && _diffControl.Visible)
			{
				_diffControl.Visible = false;
				if (_previewContainerPanel != null) _previewContainerPanel.Visible = true;
			}

			if (e.Node?.Tag is PakFileWrapper wrapper)
			{
				Pak? sourcePak = wrapper.SourcePak;
				PakEntryFile entry = wrapper.Entry;
				if (sourcePak != null)
				{
					statusLblState.Text = $" File: {Path.GetFileName(sourcePak.name)} | Selected: {entry.name}";
				}

				previewBox.Clear();
				ShowPreview(sourcePak, entry);
				previewBox.Refresh();
			}
			else if (e.Node != null)
			{
				previewBox.ConfigureForSearchLog(false);
				previewBox.EnableSyntaxHighlighting = false;
				previewBox.Clear();

				bool isRootPak = e.Node.Parent == null;
				string cleanName = e.Node.Text.Replace("📁 ", "").Replace("📦 ", "").Trim();

				statusLblState.Text = isRootPak
					? $" Pak: {cleanName}"
					: $" Directory: {cleanName}";

				StringBuilder sb = new();
				string headerIcon = isRootPak ? "📦" : "📁";
				string headerType = isRootPak ? "Pak Contents" : "Directory Contents";

				sb.AppendLine($"\n {headerIcon} {headerType}: {cleanName}");
				sb.AppendLine(" ======================================================================");

				if (e.Node.Tag is VirtualNode vNode)
				{
					int count = 0;
					foreach (var dir in vNode.SubDirs.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
					{
						sb.AppendLine($"   📁 {dir.Name}");
						count++;
					}
					foreach (var file in vNode.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
					{
						sb.AppendLine($"     📄 {file.Name}");
						count++;
					}
					if (count == 0) sb.AppendLine("   (Empty directory)");
				}
				else
				{
					int count = 0;
					foreach (TreeNode child in e.Node.Nodes)
					{
						if (child.Tag?.ToString() == "DUMMY") continue;
						sb.AppendLine($"   {child.Text}");
						count++;
					}
					if (count == 0 && e.Node.Nodes.Count == 0) sb.AppendLine("   (Empty directory)");
					else if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag?.ToString() == "DUMMY") sb.AppendLine("   (Expand to load contents...)");
				}

				previewBox.AppendBatchText(sb.ToString());
				previewBox.Refresh();
				previewBox.ResetScroll();
			}
			else
			{
				previewBox.Clear();
				previewBox.ShowEmptyState();
				previewBox.Refresh();
				statusLblState.Text = " Ready.";
			}
		}

		private void SaveRootFoldersState()
		{
			if (isSearchActive) return;

			List<string> expandedNames = [];

			CollectExpandedNodesRecursive(treeView.Nodes, expandedNames);

			PlayerPrefs.SetString("ExpandedRootNodes", string.Join(',', expandedNames));
		}

		private static void CollectExpandedNodesRecursive(TreeNodeCollection nodes, List<string> expandedNames)
		{
			foreach (TreeNode node in nodes)
			{
				if (node.IsExpanded && node.Text.Contains("📁"))
				{
					string cleanName = node.Text.Replace("📁 ", "").Trim();
					expandedNames.Add(cleanName);
				}

				if (node.Nodes.Count > 0)
				{
					CollectExpandedNodesRecursive(node.Nodes, expandedNames);
				}
			}
		}

		#endregion

		#region File Loading & Folder Scanning

		private void UncheckVanillaToggle()
		{
			SetVanillaLoadState(false);
		}

		private async void BtnLoadVanilla_Click(object sender, EventArgs e)
		{
			string vanillaPath = WorkbenchLauncherModule.GetReforgerVanillaPath();
			if (!string.IsNullOrEmpty(vanillaPath))
			{
				string addonsDir = Path.Combine(vanillaPath, "addons");
				if (Directory.Exists(addonsDir))
				{
					await ScanFolderAsync(addonsDir);
					return;
				}
			}

			UIHelper.CustomMessageBox(this, "Could not find Arma Reforger Vanilla addons directory in the registry.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		private void ChkLoadVanilla_CheckedChanged(object? sender, EventArgs e)
		{
			SetVanillaLoadState(chkLoadVanilla.Checked);

			if (chkLoadVanilla.Checked)
			{
				BtnLoadVanilla_Click(this, EventArgs.Empty);
			}
			else
			{
				RemoveVanillaFiles();
			}
		}

		private void RemoveVanillaFiles()
		{
			string vanillaPath = WorkbenchLauncherModule.GetReforgerVanillaPath();
			if (!string.IsNullOrEmpty(vanillaPath))
			{
				string addonsDir = Path.Combine(vanillaPath, "addons");
				var paksToRemove = loadedPaks.Where(p => p.name.StartsWith(addonsDir, StringComparison.OrdinalIgnoreCase)).ToList();

				if (paksToRemove.Count > 0)
				{
					_ = RemovePaksAsync(paksToRemove);
				}
			}
		}

		private static string ParseGprojProjectId(string filePath)
		{
			try
			{
				if (File.Exists(filePath))
				{
					string content = File.ReadAllText(filePath);
					Match match = ProjectIdRegex().Match(content);
					if (match.Success)
					{
						return match.Groups[1].Value;
					}
				}
			}
			catch { }
			return string.Empty;
		}

		private string FindProjectIdForPak(string pakPath)
		{
			try
			{
				string? currentDir = Path.GetDirectoryName(pakPath);
				if (string.IsNullOrEmpty(currentDir) || !Directory.Exists(currentDir)) return string.Empty;

				if (folderToProjectIdCache.TryGetValue(currentDir, out string? cachedId))
				{
					return cachedId;
				}

				string checkDir = currentDir;
				for (int depth = 0; depth < 4; depth++)
				{
					if (string.IsNullOrEmpty(checkDir) || !Directory.Exists(checkDir)) break;

					string[] gprojFiles = Directory.GetFiles(checkDir, "*.gproj", SearchOption.TopDirectoryOnly);
					if (gprojFiles.Length > 0)
					{
						string projId = ParseGprojProjectId(gprojFiles[0]);
						if (!string.IsNullOrEmpty(projId))
						{
							folderToProjectIdCache[currentDir] = projId;
							return projId;
						}
					}

					checkDir = Path.GetDirectoryName(checkDir)!;
				}
			}
			catch { }
			return string.Empty;
		}

		internal string GetResolvedProjectId(string pakPath)
		{
			string pProj = FindProjectIdForPak(pakPath);
			return string.IsNullOrEmpty(pProj) ? "RootDirectory" : pProj;
		}

		private void SetVanillaLoadState(bool enabled)
		{
			PlayerPrefs.SetBool("AutoLoadVanilla", enabled);

			if (btnLoadVanilla != null)
			{
				btnLoadVanilla.Checked = enabled;
				btnLoadVanilla.Text = enabled ? "☑ Auto-Load Vanilla" : "☐ Auto-Load Vanilla";
				btnLoadVanilla.ForeColor = UITheme.TextMain;
			}

			if (chkLoadVanilla != null && chkLoadVanilla.Checked != enabled)
			{
				chkLoadVanilla.CheckedChanged -= ChkLoadVanilla_CheckedChanged;
				chkLoadVanilla.Checked = enabled;
				chkLoadVanilla.CheckedChanged += ChkLoadVanilla_CheckedChanged;
			}
		}

		private void UpdateLastLoadedState()
		{
			if (loadedPaks.Count == 0)
			{
				PlayerPrefs.SetString("LastLoadedPaths", "");
				return;
			}

			PlayerPrefs.SetString("LastLoadedType", "FILES");
			var currentPaths = loadedPaks.Select(p => p.name).Distinct();
			PlayerPrefs.SetString("LastLoadedPaths", string.Join('|', currentPaths));
		}

		private async void BtnOpenFiles_Click(object? sender, EventArgs e)
		{
			using var ofd = new OpenFileDialog
			{
				Filter = "Pak files (*.pak)|*.pak",
				Title = "Select .pak file(s) to open",
				Multiselect = true,
				RestoreDirectory = false
			};

			string lastPakDir = PlayerPrefs.GetString("LastPakDir", "");
			if (!string.IsNullOrEmpty(lastPakDir) && Directory.Exists(lastPakDir))
			{
				ofd.InitialDirectory = lastPakDir;
			}

			if (ofd.ShowDialog() == DialogResult.OK)
			{
				string? selectedDir = Path.GetDirectoryName(ofd.FileNames[0]);
				if (!string.IsNullOrEmpty(selectedDir) && Directory.Exists(selectedDir))
				{
					PlayerPrefs.SetString("LastPakDir", selectedDir);
				}

				bool shouldAppend = btnAppendToggle != null && btnAppendToggle.Checked;

				if (!shouldAppend)
				{
					UncheckVanillaToggle();

					pakFilePaths.Clear();
					foreach (var oldPak in loadedPaks) oldPak?.Dispose();
					loadedPaks.Clear();
					treeView.Nodes.Clear();
					folderToProjectIdCache.Clear();
				}

				await AddPaksToViewAsync([.. ofd.FileNames]);
			}
		}

		private async void BtnRemoveProject_Click(object? sender, EventArgs e)
		{
			var selectedNode = treeView.SelectedNode;
			if (selectedNode?.Parent != null) return;

			string projName = selectedNode!.Text.Replace("📁 ", "").Trim();

			var paksToRemove = loadedPaks.Where(p =>
			{
				return string.Equals(GetResolvedProjectId(p.name), projName, StringComparison.OrdinalIgnoreCase);
			}).ToList();

			if (paksToRemove.Count > 0)
			{
				await RemovePaksAsync(paksToRemove);
			}
		}

		private async Task RemovePakAsync(Pak pak)
		{
			await RemovePaksAsync([pak]);
		}

		private async Task RemovePaksAsync(IEnumerable<Pak> paksToRemove)
		{
			var list = paksToRemove.ToList();
			if (list.Count == 0) return;

			this.Cursor = Cursors.WaitCursor;

			foreach (var pak in list)
			{
				loadedPaks.Remove(pak);
				pakFilePaths.Remove(pak.name);
				pak.Dispose();
			}

			UpdateSessionView();

			if (loadedPaks.Count == 0)
			{
				treeView.Nodes.Clear();
				UpdateUIState();
				previewBox.Clear();
			}
			else
			{
				await RenderTreeViewAsync(searchBox.Text);
			}

			UpdateLastLoadedState();
			this.Cursor = Cursors.Default;
		}

		private async void BtnOpenFolder_Click(object? sender, EventArgs e)
		{
			using var fbd = new FolderBrowserDialog
			{
				Description = "Select a directory to scan for .pak files:",
				ShowNewFolderButton = false
			};

			string lastFolderDir = PlayerPrefs.GetString("LastFolderDir", "");
			if (!string.IsNullOrEmpty(lastFolderDir) && Directory.Exists(lastFolderDir))
			{
				fbd.SelectedPath = lastFolderDir;
				fbd.InitialDirectory = lastFolderDir;
			}

			if (fbd.ShowDialog() == DialogResult.OK)
			{
				PlayerPrefs.SetString("LastFolderDir", fbd.SelectedPath);

				await ScanFolderAsync(fbd.SelectedPath);
			}
		}

		private async Task ScanFolderAsync(string folderPath)
		{
			statusLblState.Text = " Scanning directory for .pak files...";
			progressBar.Visible = true;
			progressBar.Style = ProgressBarStyle.Marquee;

			bool shouldAppend = btnAppendToggle != null && btnAppendToggle.Checked;

			string vanillaPath = WorkbenchLauncherModule.GetReforgerVanillaPath();
			string vanillaAddons = !string.IsNullOrEmpty(vanillaPath) ? Path.Combine(vanillaPath, "addons") : "";
			bool isScanningVanilla = !string.IsNullOrEmpty(vanillaAddons) && folderPath.StartsWith(vanillaAddons, StringComparison.OrdinalIgnoreCase);

			if (!shouldAppend && !isScanningVanilla)
			{
				UncheckVanillaToggle();

				folderToProjectIdCache.Clear();
				pakFilePaths.Clear();

				foreach (var oldPak in loadedPaks)
				{
					oldPak?.Dispose();
				}
				loadedPaks.Clear();

				treeView.Nodes.Clear();
			}

			string[] foundFiles = await Task.Run(() => Directory.GetFiles(folderPath, "*.pak", SearchOption.AllDirectories));

			if (foundFiles.Length > 0)
			{
				await AddPaksToViewAsync([.. foundFiles]);
			}
			else
			{
				UIHelper.CustomMessageBox(this, "No .pak files found in the selected folder.", "No Files Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
				progressBar.Visible = false;
				UpdateUIState();
			}
		}

		private async Task AddPaksToViewAsync(IEnumerable<string> fileNames)
		{
			var newPaths = fileNames as IReadOnlyList<string> ?? [.. fileNames];
			if (newPaths.Count == 0) return;

			void InitUi()
			{
				progressBar.Style = ProgressBarStyle.Continuous;
				progressBar.Visible = true;
				progressBar.Value = 0;
				progressBar.Maximum = newPaths.Count;
				statusLblState.Text = $" Loading {newPaths.Count} files...";
			}

			if (this.InvokeRequired) this.Invoke((MethodInvoker)InitUi);
			else InitUi();

			int dbHits = 0;
			List<Pak> newlyLoadedPaks = [];
			List<string> newlyAddedPaths = [];

			try
			{
				await Task.Run(() =>
				{
					for (int i = 0; i < newPaths.Count; i++)
					{
						string path = newPaths[i];

						if (!pakFilePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
						{
							try
							{
								FileInfo fi = new(path);
								long size = fi.Length;
								long mtime = fi.LastWriteTimeUtc.Ticks;

								Pak pak;
								if (PakIndex.NeedsUpdate(path, size, mtime))
								{
									pak = new Pak(path);
									PakIndex.SavePakToDb(pak, size, mtime);
								}
								else
								{
									pak = PakIndex.LoadPakFromDb(path);
									dbHits++;
								}

								newlyAddedPaths.Add(path);
								newlyLoadedPaks.Add(pak);
							}
							catch (Exception ex)
							{
								this.BeginInvoke((MethodInvoker)delegate {
									UIHelper.CustomMessageBox(this, $"Error reading {Path.GetFileName(path)}: {ex.Message}", "Loading Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
								});
							}
						}

						int currentStep = i + 1;
						this.BeginInvoke((MethodInvoker)delegate {
							if (!this.IsDisposed && progressBar.Visible)
							{
								progressBar.Value = Math.Min(currentStep, progressBar.Maximum);
								statusLblState.Text = $" Processing files... ({currentStep}/{newPaths.Count}) - Cache: {dbHits}";
							}
						});
					}
				});

				if (newlyLoadedPaks.Count > 0)
				{
					pakFilePaths.AddRange(newlyAddedPaths);
					loadedPaks.AddRange(newlyLoadedPaks);

					this.Text = AppInfo.Title;
					deepSearchControl.UpdatePakList(loadedPaks, FindProjectIdForPak);
					await RenderTreeViewAsync("");
					UpdateLastLoadedState();
				}
			}
			catch (Exception ex)
			{
				UIHelper.CustomMessageBox(this, $"An unexpected error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				progressBar.Visible = false;
				UpdateUIState();
			}
		}

		#endregion

		#region Search & Tree Rendering

		private const string PlaceholderText = "Filter items by name...";

		private void BtnClearFilter_Click(object? sender, EventArgs e)
		{
			searchBox.TextChanged -= SearchBox_TextChanged;
			searchBox.Text = PlaceholderText;
			searchBox.ForeColor = UITheme.TextDarkGray;
			searchBox.TextChanged += SearchBox_TextChanged;
			searchTimer?.Stop();

			searchCts?.Cancel();
			searchCts?.Dispose();
			searchCts = null;
			_ = RenderTreeViewAsync("");
		}

		private void SearchBox_Enter(object? sender, EventArgs e)
		{
			if (searchBox.Text == PlaceholderText)
			{
				searchBox.TextChanged -= SearchBox_TextChanged;
				searchBox.Text = "";
				searchBox.ForeColor = Color.White;
				searchBox.TextChanged += SearchBox_TextChanged;
			}
		}

		private void SearchBox_Leave(object? sender, EventArgs e)
		{
			if (string.IsNullOrWhiteSpace(searchBox.Text))
			{
				searchBox.TextChanged -= SearchBox_TextChanged;
				searchBox.Text = PlaceholderText;
				searchBox.ForeColor = UITheme.TextDarkGray;
				searchBox.TextChanged += SearchBox_TextChanged;

				searchTimer.Stop();

				searchCts?.Cancel();
				searchCts?.Dispose();
				searchCts = null;

				_ = RenderTreeViewAsync("");
			}
		}

		private void SearchBox_TextChanged(object? sender, EventArgs e)
		{
			if (searchBox.Text == PlaceholderText) return;

			searchCts?.Cancel();
			searchCts?.Dispose();
			searchCts = null;

			searchTimer.Stop();
			searchTimer.Start();
		}

		private void SearchTimer_Tick(object? sender, EventArgs e)
		{
			searchTimer.Stop();
			_ = RenderTreeViewAsync(searchBox.Text);
		}

		private async Task RenderTreeViewAsync(string filter)
		{
			searchCts?.Cancel();
			searchCts?.Dispose();
			searchCts = new();

			var token = searchCts.Token;

			PrepareTreeViewUIForRender(filter);

			totalFilesCount = 0;
			totalMatchedCount = 0;

			VirtualNode globalVirtualContainer = new() { Name = "SYSTEM_CONTAINER", Tag = "DIR" };

			string trimmedFilter = isSearchActive ? filter.Trim() : "";
			string[] filterTerms = isSearchActive
				? trimmedFilter.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				: [];

			await BuildVirtualFileTreeAsync(globalVirtualContainer, filterTerms, token);

			if (token.IsCancellationRequested) return;

			List<TreeNode> uiRootNodes = await BuildUITreeNodesAsync(globalVirtualContainer, token);
			if (token.IsCancellationRequested) return;

			FinalizeTreeViewRender(uiRootNodes);
		}

		private void PrepareTreeViewUIForRender(string filter)
		{
			progressBar.Style = ProgressBarStyle.Marquee;
			progressBar.Visible = true;

			isSearchActive = !string.IsNullOrWhiteSpace(filter) && filter != PlaceholderText;

			if (isSearchActive) statusLblState.Text = $" Applying filter: '{filter}'...";
			else statusLblState.Text = " Building file tree view...";

			if (previewBox != null)
			{
				previewBox.ConfigureForSearchLog(logEnabled);
				previewBox.ShowLogHeader(filter, isSearchActive, logEnabled);
			}
		}

		private async Task BuildVirtualFileTreeAsync(VirtualNode globalVirtualContainer, string[] filterTerms, CancellationToken token)
		{
			await Task.Run(() =>
			{
				StringBuilder consoleBuffer = new();
				Stopwatch logTimer = Stopwatch.StartNew();

				void FlushConsole()
				{
					if (consoleBuffer.Length > 0 && !token.IsCancellationRequested)
					{
						string textToAppend = consoleBuffer.ToString().TrimEnd('\r', '\n');
						consoleBuffer.Clear();

						try
						{
							this.BeginInvoke(() =>
							{
								if (token.IsCancellationRequested) return;
								previewBox.AppendBatchText(textToAppend);
							});
						}
						catch (ObjectDisposedException) { }
					}
				}

				foreach (Pak pak in loadedPaks)
				{
					if (token.IsCancellationRequested) return;

					if (logEnabled)
					{
						consoleBuffer.AppendLine($"[INDEX] Processing: {Path.GetFileName(pak.name)}");
						if (logTimer.ElapsedMilliseconds > 30) { FlushConsole(); logTimer.Restart(); }
					}

					totalFilesCount += pak.entries.Count;

					string projectRootName = GetResolvedProjectId(pak.name);

					if (!globalVirtualContainer.SubDirs.TryGetValue(projectRootName, out VirtualNode? projectRootNode))
					{
						projectRootNode = new VirtualNode { Name = projectRootName, Tag = "DIR" };
						globalVirtualContainer.SubDirs[projectRootName] = projectRootNode;
					}

					foreach (var entry in pak.entries)
					{
						if (token.IsCancellationRequested) return;

						if (isSearchActive)
						{
							bool isMatch = CheckFileAgainstFilter(entry.name, filterTerms);
							if (!isMatch) continue;
						}

						totalMatchedCount++;

						ReadOnlySpan<char> pathSpan = entry.name.AsSpan();
						VirtualNode currentVirtual = projectRootNode;

						int start = 0;
						for (int i = 0; i <= pathSpan.Length; i++)
						{
							if (i == pathSpan.Length || pathSpan[i] == '/' || pathSpan[i] == '\\')
							{
								if (i > start)
								{
									ReadOnlySpan<char> segmentSpan = pathSpan[start..i];
									bool isLast = (i == pathSpan.Length);

									if (isLast)
									{
										string fileName = entry.name[start..i];
										currentVirtual.Files.Add(new VirtualNode
										{
											Name = fileName,
											Tag = new PakFileWrapper { Entry = entry, SourcePak = pak }
										});
									}
									else
									{
										VirtualNode? nextSub = null;

										foreach (var pair in currentVirtual.SubDirs)
										{
											if (segmentSpan.Equals(pair.Key.AsSpan(), StringComparison.Ordinal))
											{
												nextSub = pair.Value;
												break;
											}
										}

										if (nextSub == null)
										{
											string dirName = segmentSpan.ToString();
											nextSub = new VirtualNode { Name = dirName, Tag = "DIR" };
											currentVirtual.SubDirs[dirName] = nextSub;
										}

										currentVirtual = nextSub;
									}
								}
								start = i + 1;
							}
						}
					}
				}

				if (!token.IsCancellationRequested)
				{
					FlushConsole();
					try
					{
						this.BeginInvoke(() =>
						{
							if (token.IsCancellationRequested) return;
							previewBox.ShowLogFooter(totalFilesCount, totalMatchedCount, isSearchActive, logEnabled);
							previewBox.Refresh();
						});
					}
					catch (ObjectDisposedException) { }
				}
			}, token);
		}

		private static bool CheckFileAgainstFilter(string entryName, string[] filterTerms)
		{
			ReadOnlySpan<char> entrySpan = entryName.AsSpan();

			foreach (string term in filterTerms)
			{
				if (term.StartsWith('.'))
				{
					ReadOnlySpan<char> ext = Path.GetExtension(entrySpan);

					if (ext.IsEmpty)
					{
						return false;
					}

					if (term.Length < ext.Length)
					{
						if (term.Length <= 2 || !ext.StartsWith(term, StringComparison.OrdinalIgnoreCase))
						{
							return false;
						}
					}
					else
					{
						if (!ext.StartsWith(term, StringComparison.OrdinalIgnoreCase))
						{
							return false;
						}
					}
				}
				else
				{
					if (!entrySpan.Contains(term, StringComparison.OrdinalIgnoreCase))
					{
						return false;
					}
				}
			}

			return true;
		}

		private async Task<List<TreeNode>> BuildUITreeNodesAsync(VirtualNode globalVirtualContainer, CancellationToken token)
		{
			int activeCount = isSearchActive ? totalMatchedCount : totalFilesCount;
			bool forceFullLoad = activeCount < 5000 || isSearchActive;
			List<TreeNode> uiRootNodes = [];

			await Task.Run(() =>
			{
				foreach (var projectNode in globalVirtualContainer.SubDirs.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
				{
					if (token.IsCancellationRequested) return;

					if (isSearchActive && projectNode.SubDirs.Count == 0 && projectNode.Files.Count == 0)
						continue;

					TreeNode projectUiNode = CreateFolderNode(projectNode, false);

					foreach (var subDir in projectNode.SubDirs.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
					{
						TreeNode dirUiNode = CreateFolderNode(subDir, !forceFullLoad);
						if (forceFullLoad)
						{
							PopulateNodeWithVirtualData(dirUiNode, subDir, true);
						}
						projectUiNode.Nodes.Add(dirUiNode);
					}

					foreach (var file in projectNode.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
					{
						projectUiNode.Nodes.Add(CreateFileNode(file));
					}

					uiRootNodes.Add(projectUiNode);
				}
			}, token);

			return uiRootNodes;
		}

		private void FinalizeTreeViewRender(List<TreeNode> uiRootNodes)
		{
			treeView.BeginUpdate();
			treeView.AfterSelect -= TreeView_AfterSelect;
			treeView.Nodes.Clear();

			if (uiRootNodes.Count == 0 && isSearchActive)
			{
				treeView.Nodes.Add(new TreeNode("🚫 No matching files found."));
			}
			else
			{
				treeView.Nodes.AddRange([.. uiRootNodes]);
			}

			statusLblState.Text = isSearchActive
				? $" Filter applied. {totalMatchedCount} matches found."
				: $" Loading done. {totalFilesCount:N0} files mapped.";

			if (isSearchActive && uiRootNodes.Count > 0)
			{
				treeView.ExpandAll();
			}
			else
			{
				string expandedRootsStr = PlayerPrefs.GetString("ExpandedRootNodes", "");
				if (!string.IsNullOrEmpty(expandedRootsStr))
				{
					HashSet<string> expandedRoots = expandedRootsStr
						.Split(_commaSeparator, StringSplitOptions.RemoveEmptyEntries)
						.Select(r => r.Trim())
						.ToHashSet(StringComparer.OrdinalIgnoreCase);

					if (expandedRoots.Count > 0)
					{
						RestoreExpandedStateRecursive(treeView.Nodes, expandedRoots);
					}
				}
				else if (treeView.Nodes.Count > 0)
				{
					treeView.Nodes[0].Expand();
				}
			}

			if (treeView.Nodes.Count > 0)
				treeView.SelectedNode = treeView.Nodes[0];

			treeView.AfterSelect += TreeView_AfterSelect;
			treeView.EndUpdate();

			progressBar.Visible = false;
			UpdateUIState();
		}

		private static void RestoreExpandedStateRecursive(TreeNodeCollection nodes, HashSet<string> expandedRoots)
		{
			foreach (TreeNode node in nodes)
			{
				string cleanName = node.Text.Replace("📁 ", "").Trim();

				if (expandedRoots.Contains(cleanName))
				{
					if (node.Tag is VirtualNode virtualDir)
					{
						bool isLazyPlaceholder = node.Nodes.Count == 0 ||
												 (node.Nodes.Count == 1 && node.Nodes[0].Text == "...");

						if (isLazyPlaceholder)
						{
							node.Nodes.Clear();

							PopulateNodeWithVirtualData(node, virtualDir, false);
						}
					}

					node.IsExpanded = true;
				}

				if (node.Nodes.Count > 0)
				{
					RestoreExpandedStateRecursive(node.Nodes, expandedRoots);
				}
			}
		}

		#endregion

		#region Preview Operations

		private void BtnToggleWrap_Click(object? sender, EventArgs e)
		{
			currentWrapMode = !currentWrapMode;
			UpdateWrapButtonUi();
			previewBox.WordWrap = currentWrapMode;
		}

		private void UpdateWrapButtonUi()
		{
			if (currentWrapMode)
			{
				btnToggleWrap.Text = "Word Wrap: ON";
				btnToggleWrap.ForeColor = UITheme.LogSuccess;
				btnToggleWrap.FlatAppearance.BorderColor = UITheme.LogSuccess;
			}
			else
			{
				btnToggleWrap.Text = "Word Wrap: OFF";
				btnToggleWrap.ForeColor = UITheme.TextMuted;
				btnToggleWrap.FlatAppearance.BorderColor = UITheme.ToggleInactive;
			}
		}

		private void ShowPreview(Pak? sourcePak, PakEntryFile entry)
		{
			if (sourcePak == null) return;

			int maxReasonableSize = 10 * 1024 * 1024;
			int bytesToRead = (entry.originalSize > maxReasonableSize) ? maxReasonableSize : (int)entry.originalSize;
			if (bytesToRead <= 0) bytesToRead = 4096;

			byte[] data = sourcePak.GetPreviewBytes(entry, bytesToRead);
			string? textPreview = DecodeText(data);

			previewBox.ShowFilePreview(sourcePak, entry, data, textPreview, maxReasonableSize);
		}

		private static string? DecodeText(byte[] data)
		{
			if (data == null || data.Length == 0) return null;
			int nullCount = 0;
			int nonAsciiCount = 0;

			int analyzeLength = Math.Min(data.Length, 4096);
			for (int i = 0; i < analyzeLength; i++)
			{
				if (data[i] == 0) nullCount++;
				if (data[i] > 127) nonAsciiCount++;
			}

			if (nullCount > 2 || (analyzeLength > 0 && (double)nonAsciiCount / analyzeLength > 0.3))
				return null;

			try { return Encoding.UTF8.GetString(data); }
			catch { return null; }
		}

		private void PreparePreviewForLog(string actionName)
		{
			previewBox.ConfigureForSearchLog(true);
			previewBox.ShowExtractionLogHeader(actionName);
		}

		#endregion

		#region Extraction & Workbench Operations

		private void BtnOpenInWorkbench_Click(object? sender, EventArgs e)
		{
			var node = treeView.SelectedNode;
			if (node == null) return;

			if (node.Tag is PakFileWrapper wrapper && wrapper.SourcePak != null)
			{
				byte[] data = wrapper.SourcePak.GetPreviewBytes(wrapper.Entry, (int)wrapper.Entry.originalSize);
				if (data != null && data.Length > 0)
				{
					WorkbenchLauncherModule.OpenInWorkbench(wrapper.Entry.name, data, (path) => {
						string cleanSearchPath = path.Replace('\\', '/').TrimStart('/');

						var targetEntry = wrapper.SourcePak.entries
							.FirstOrDefault(e => e.name.Replace('\\', '/').TrimStart('/')
							.Equals(cleanSearchPath, StringComparison.OrdinalIgnoreCase));

						if (targetEntry != null)
						{
							return wrapper.SourcePak.GetFileBytes(targetEntry);
						}
						return null;
					});
				}
				else
				{
					UIHelper.CustomMessageBox(this, "Failed to read file data for Workbench.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private async void BtnExtractAll_Click(object? sender, EventArgs e)
		{
			if (loadedPaks.Count == 0) return;

			DialogResult res = UIHelper.CustomMessageBox(this, $"Are you sure you want to extract all ({loadedPaks.Count}) archives?",
				"Confirm Action", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

			if (res != DialogResult.Yes) return;

			await RunExtractionTaskAsync(
				title: "ALL ARCHIVES",
				totalCount: loadedPaks.Count,
				getStatusText: current => $" Extracting archive {current} of {loadedPaks.Count}...",
				extractionWork: (progress, token) =>
				{
					int current = 0;
					var uiRefreshTimer = Stopwatch.StartNew();

					foreach (Pak pak in loadedPaks)
					{
						if (token.IsCancellationRequested) break;

						string? outputDir = Path.ChangeExtension(pak.name, null);
						pak.ExtractDataBlock(outputDir!);

						current++;

						if (uiRefreshTimer.ElapsedMilliseconds > 30 || current == loadedPaks.Count)
						{
							progress.Report(current);
							uiRefreshTimer.Restart();
						}
					}
					return Task.FromResult(current);
				},
				getSuccessDialogText: (extracted, elapsed) =>
					$"=== Extraction Finished Successfully ===\n\n" +
					$"Total Archives: {loadedPaks.Count}\n" +
					$"Total Files: {totalFilesCount:N0}\n" +
					$"Elapsed Time: {elapsed}\n\n" +
					$"All directory structures generated.",
				getCancelledDialogText: (extracted, elapsed) =>
					$"=== Extraction Cancelled ===\n\n" +
					$"Elapsed Time: {elapsed}\n\n" +
					$"Partial directory structures may have been generated."
			);
		}

		private async void BtnExtractSelected_Click(object? sender, EventArgs e)
		{
			if (loadedPaks.Count == 0) return;

			List<TreeNode> filesToExtract = GetSelectedFilesForExtraction();

			if (filesToExtract.Count == 0)
			{
				UIHelper.CustomMessageBox(this, "Nothing is selected. Please select items from the list first (Use Shift or Ctrl).", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			await RunExtractionTaskAsync(
				title: filesToExtract.Count > 1 ? "MULTIPLE ITEMS" : "SELECTED ITEM",
				totalCount: filesToExtract.Count,
				getStatusText: current => $" Extracting file {current} of {filesToExtract.Count}...",
				extractionWork: (progress, token) => ExtractFilesAsync(filesToExtract, progress, token),
				getSuccessDialogText: (extracted, elapsed) =>
					$"=== Selected Extraction Done ===\n\n" +
					$"Successfully extracted: {extracted} file(s).\n" +
					$"Elapsed Time: {elapsed}",
				getCancelledDialogText: (extracted, elapsed) =>
					$"=== Selected Extraction Cancelled ===\n\n" +
					$"Successfully extracted: {extracted} file(s) before cancellation.\n" +
					$"Elapsed Time: {elapsed}"
			);
		}

		private List<TreeNode> GetSelectedFilesForExtraction()
		{
			HashSet<TreeNode> filesToExtract = [];

			treeView.BeginUpdate();
			foreach (TreeNode selectedNode in treeView.SelectedNodes)
			{
				if (selectedNode.Tag is PakFileWrapper)
				{
					filesToExtract.Add(selectedNode);
				}
				else if (selectedNode.Tag is VirtualNode || selectedNode.Nodes.Count > 0)
				{
					CollectAllFileNodesUnder(selectedNode, filesToExtract);
				}
			}
			treeView.EndUpdate();

			return [.. filesToExtract];
		}

		private static void CollectAllFileNodesUnder(TreeNode startNode, HashSet<TreeNode> result)
		{
			if (startNode.Nodes.Count == 1 && string.Equals(startNode.Nodes[0].Tag?.ToString(), "DUMMY", StringComparison.Ordinal))
			{
				startNode.Nodes.Clear();
				if (startNode.Tag is VirtualNode vNode)
				{
					PopulateNodeWithVirtualData(startNode, vNode, false);
				}
			}

			foreach (TreeNode child in startNode.Nodes)
			{
				if (child.Tag is PakFileWrapper)
				{
					result.Add(child);
				}

				if (child.Nodes.Count > 0 || (child.Nodes.Count == 1 && string.Equals(child.Nodes[0].Tag?.ToString(), "DUMMY", StringComparison.Ordinal)))
				{
					CollectAllFileNodesUnder(child, result);
				}
			}
		}

		private static async Task<int> ExtractFilesAsync(List<TreeNode> filesToExtract, IProgress<int> progress, CancellationToken cancellationToken)
		{
			int totalExtracted = 0;

			var filesToProcess = filesToExtract
				.Where(n => n?.Tag is PakFileWrapper)
				.Select(n => (PakFileWrapper)n.Tag!)
				.ToList();

			await Task.Run(() =>
			{
				var uiRefreshTimer = Stopwatch.StartNew();

				foreach (var wrapper in filesToProcess)
				{
					if (cancellationToken.IsCancellationRequested) break;

					PakEntryFile fileEntry = wrapper.Entry;
					Pak? selectedPak = wrapper.SourcePak;
					if (selectedPak != null)
					{
						string? outputDir = Path.ChangeExtension(selectedPak.name, null);
						selectedPak.ExtractSingleEntry(fileEntry, outputDir!);

						totalExtracted++;

						if (uiRefreshTimer.ElapsedMilliseconds > 30 || totalExtracted == filesToProcess.Count)
						{
							progress.Report(totalExtracted);
							uiRefreshTimer.Restart();
						}
					}
				}
			}, cancellationToken);

			return totalExtracted;
		}

		private async Task RunExtractionTaskAsync(
			string title,
			int totalCount,
			Func<int, string> getStatusText,
			Func<IProgress<int>, CancellationToken, Task<int>> extractionWork,
			Func<int, string, string> getSuccessDialogText,
			Func<int, string, string> getCancelledDialogText)
		{
			this.Cursor = Cursors.WaitCursor;
			PreparePreviewForLog($"Extracting {title}");

			progressBar.Style = ProgressBarStyle.Continuous;
			progressBar.Minimum = 0;
			progressBar.Maximum = totalCount;
			progressBar.Value = 0;
			progressBar.Visible = true;
			btnCancelProcess.Visible = true;

			_extractCts?.Cancel();
			_extractCts?.Dispose();
			_extractCts = new CancellationTokenSource();

			var progress = new Progress<int>(currentValue =>
			{
				if (progressBar.Visible && !this.IsDisposed)
				{
					progressBar.Value = Math.Min(currentValue, totalCount);
					statusLblState.Text = getStatusText(currentValue);
				}
			});

			TextWriter oldOut = Console.Out;
			TextWriter dynamicWriter = previewBox.CreateTextWriter();
			Console.SetOut(dynamicWriter);

			Stopwatch sw = Stopwatch.StartNew();

			int extractedCount = 0;

			try
			{
				extractedCount = await extractionWork(progress, _extractCts.Token);
				sw.Stop();

				string elapsedTimeFormatted = sw.Elapsed.ToString("mm\\:ss\\.ff");
				Console.WriteLine();

				if (_extractCts.Token.IsCancellationRequested)
				{
					Console.WriteLine($"[INFO] Extraction cancelled by user! Elapsed time: {elapsedTimeFormatted}");
					string logMsg = getCancelledDialogText(extractedCount, elapsedTimeFormatted);
					previewBox.AppendBatchText("\n" + logMsg);
					previewBox.Refresh();
					statusLblState.Text = " Operation cancelled.";
					UIHelper.CustomMessageBox(this, logMsg, "Operation Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
				else
				{
					Console.WriteLine($"[INFO] Extraction complete! Elapsed time: {elapsedTimeFormatted}");
					string logMsg = getSuccessDialogText(extractedCount, elapsedTimeFormatted);
					previewBox.AppendBatchText("\n" + logMsg);
					previewBox.Refresh();
					statusLblState.Text = " Extraction completed.";
					UIHelper.CustomMessageBox(this, logMsg, "Operation Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			}
			catch (OperationCanceledException)
			{
				sw.Stop();
				string elapsedTimeFormatted = sw.Elapsed.ToString("mm\\:ss\\.ff");
				string logMsg = getCancelledDialogText(extractedCount, elapsedTimeFormatted);
				previewBox.AppendBatchText("\n" + logMsg);
				previewBox.Refresh();
				statusLblState.Text = " Operation cancelled.";
				UIHelper.CustomMessageBox(this, logMsg, "Operation Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			catch (Exception ex)
			{
				UIHelper.CustomMessageBox(this, $"An error occurred during extraction: {ex.Message}", "Extraction Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				btnCancelProcess.Visible = false;
				_extractCts?.Dispose();
				_extractCts = null;

				Console.SetOut(oldOut);
				dynamicWriter.Dispose();
				this.Cursor = Cursors.Default;
				progressBar.Visible = false;
				statusLblState.Text = " Ready.";
				previewBox.WordWrap = currentWrapMode;
				previewBox.ScrollToBottom();
			}
		}

		private async void BtnCompareWithDisk_Click(object? sender, EventArgs e)
		{
			var node = treeView.SelectedNode;
			if (node == null) return;

			if (node.Tag is PakFileWrapper wrapper && wrapper.SourcePak != null)
			{
				string internalFileName = wrapper.Entry.name;
				byte[] data = wrapper.SourcePak.GetPreviewBytes(wrapper.Entry, (int)wrapper.Entry.originalSize);

				if (_diffOrchestrator.TryPrepareAutoCompare(internalFileName, data, out string? archiveText, out string? externalText, out string targetExtFile))
				{
					string safeArchiveText = archiveText ?? string.Empty;
					string safeExternalText = externalText ?? string.Empty;

					string normalizedArchive = safeArchiveText.Replace("\r\n", "\n");
					string normalizedExternal = safeExternalText.Replace("\r\n", "\n");

					if (string.Equals(normalizedArchive, normalizedExternal, StringComparison.Ordinal))
					{
						UIHelper.CustomMessageBox(this, "No differences found! The files are identical.", "No Differences", MessageBoxButtons.OK, MessageBoxIcon.Information);
						return;
					}

					ShowDiffControl();
					_diffControl!.LoadDirectText(internalFileName, safeArchiveText, safeExternalText, targetExtFile);
				}
			}
			else
			{
				List<string> parts = [];
				TreeNode? current = node;
				while (current != null && current.Parent != null)
				{
					parts.Add(current.Text.Replace("📁 ", "").Trim());
					current = current.Parent;
				}
				parts.Reverse();
				string internalPath = string.Join('/', parts);

				string selectedModPath = string.Empty;
				using (var setupDlg = new CompareSetupDialog(internalPath))
				{
					if (setupDlg.ShowDialog(this) == DialogResult.OK)
					{
						selectedModPath = setupDlg.ModPath;
					}
					else
					{
						return;
					}
				}

				this.Cursor = Cursors.WaitCursor;
				progressBar.Style = ProgressBarStyle.Marquee;
				progressBar.Visible = true;
				btnCancelProcess.Visible = true;

				_diffCts?.Cancel();
				_diffCts?.Dispose();
				_diffCts = new CancellationTokenSource();
				statusLblState.Text = string.IsNullOrEmpty(internalPath) ? " Comparing full loaded content with target..." : $" Analyzing area '{internalPath}'...";

				try
				{
					var changes = await _diffOrchestrator.ExecuteDiffSessionAsync(loadedPaks, selectedModPath, internalPath, _diffCts.Token);

					if (changes != null)
					{
						ShowDiffControl();
						_diffControl!.LoadDiffSession(changes);
						if (changes.Count == 0) UIHelper.CustomMessageBox(this, "No differences found!", "No Differences", MessageBoxButtons.OK, MessageBoxIcon.Information);
					}
				}
				catch (OperationCanceledException) { statusLblState.Text = " Comparison was cancelled by the user."; }
				catch (Exception ex) { UIHelper.CustomMessageBox(this, $"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
				finally
				{
					this.Cursor = Cursors.Default;
					progressBar.Visible = false;
					btnCancelProcess.Visible = false;
					if (statusLblState.Text.Contains("Analyzing") || statusLblState.Text.Contains("Comparing")) statusLblState.Text = " Ready.";
					_diffCts?.Dispose(); _diffCts = null;
				}
			}
		}

		private void ShowDiffControl()
		{
			if (_diffControl == null)
			{
				_diffControl = new DiffManagerControl { Dock = DockStyle.Fill };
				_diffControl.OnCloseRequested += (s, ev) => {
					if (_previewContainerPanel != null) _previewContainerPanel.Visible = true;
					if (_diffControl != null) { split.Panel2.Controls.Remove(_diffControl); _diffControl.Dispose(); _diffControl = null; }
					split.Panel2.PerformLayout(); split.Panel2.Invalidate();
				};
				split.Panel2.Controls.Add(_diffControl);
			}
			if (_previewContainerPanel != null) _previewContainerPanel.Visible = false;
			_diffControl.Visible = true;
			_diffControl.BringToFront();
			split.Panel2.PerformLayout();
		}

		#endregion

		#region Helper Methods

		private static Bitmap CreateRedCrossImage()
		{
			Bitmap bmp = new(16, 16);
			using Graphics g = Graphics.FromImage(bmp);

			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

			using Pen pen = new(UITheme.ActionDestructive, 2.5f);
			g.DrawLine(pen, 3, 3, 13, 13);
			g.DrawLine(pen, 3, 13, 13, 3);

			return bmp;
		}

		private void AttachGlobalTooltips(ToolStripItemCollection items, Control owner, int xOffset, int yOffset)
		{
			foreach (ToolStripItem item in items)
			{
				if (item is ToolStripSeparator) continue;
				item.MouseEnter += (s, e) => {
					if (!string.IsNullOrEmpty(item.ToolTipText)) {
						globalToolTip.Show(item.ToolTipText, owner, item.Bounds.Left + UITheme.Scale(xOffset), item.Bounds.Top + UITheme.Scale(yOffset));
					}
				};
				item.MouseLeave += (s, e) => {
					globalToolTip.Hide(owner);
				};
			}
		}

		private static string GetFullPath(TreeNode node)
		{
			List<string> parts = [];
			TreeNode? current = node;

			while (current != null)
			{
				parts.Add(current.Text.Replace("📁 ", "").Replace("📄 ", "").Trim());
				current = current.Parent;
			}

			parts.Reverse();
			return string.Join('\\', parts);
		}

		#endregion
	}

	public static class PlayerPrefs
	{
		private static readonly string FilePath = Path.Combine(
			AppContext.BaseDirectory,
			"prefs.json"
		);

		private static Dictionary<string, string> _prefs = new(StringComparer.OrdinalIgnoreCase);
		private static readonly object _fileLock = new();

		static PlayerPrefs()
		{
			Load();
		}

		private static void Load()
		{
			lock (_fileLock)
			{
				try
				{
					if (!File.Exists(FilePath)) return;

					string json = File.ReadAllText(FilePath);

					try
					{
						var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
						if (data != null) _prefs = new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
					}
					catch
					{
						var oldData = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
						if (oldData != null)
						{
							_prefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
							foreach (var (key, value) in oldData)
							{
								_prefs[key] = value.ToString();
							}
						}
					}
				}
				catch
				{
				}
			}
		}

		private static void Save()
		{
			lock (_fileLock)
			{
				try
				{
					string? dir = Path.GetDirectoryName(FilePath);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					{
						Directory.CreateDirectory(dir);
					}

					string json = JsonSerializer.Serialize(_prefs);
					File.WriteAllText(FilePath, json);
				}
				catch
				{
				}
			}
		}

		public static void SetString(string key, string value)
		{
			lock (_fileLock)
			{
				_prefs[key] = value;
			}
			Save();
		}

		public static string GetString(string key, string defaultValue)
		{
			lock (_fileLock)
			{
				return _prefs.TryGetValue(key, out string? value) ? value : defaultValue;
			}
		}

		public static void SetInt(string key, int value) => SetString(key, value.ToString());

		public static int GetInt(string key, int defaultValue)
		{
			lock (_fileLock)
			{
				return _prefs.TryGetValue(key, out string? val) && int.TryParse(val, out int res)
					? res
					: defaultValue;
			}
		}

		public static void SetBool(string key, bool value) => SetString(key, value ? "1" : "0");

		public static bool GetBool(string key, bool defaultValue)
		{
			lock (_fileLock)
			{
				if (_prefs.TryGetValue(key, out string? val))
				{
					return val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
				}

				return defaultValue;
			}
		}
	}
}
