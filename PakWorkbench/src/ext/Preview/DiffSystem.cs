using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using PakWorkbench.src;
using PakWorkbench.src.ext.Preview.Xfusion;

namespace PakWorkbench.src.ext.Preview
{
	#region UI Constants & Styling
	internal static class DiffUI
	{
		public static Font NavArrowFont => UITheme.TitleFont;
		public static readonly Font CloseButtonFont = new("Segoe UI", 11F, FontStyle.Bold);

		public static Color TextDarkGray => UITheme.TextDarkGray;

		public static readonly Color TextWhite = Color.White;
		public static readonly Color TextLightGray = Color.LightGray;
		public static readonly Color TextGainsboro = Color.Gainsboro;
		public static readonly Color TextTransparent = Color.Transparent;

		public static readonly Color BtnFileFolderBg = Color.FromArgb(50, 50, 50);
		public static readonly Color BorderBottom = Color.FromArgb(55, 55, 55);
		public static readonly Color HoverBg = Color.FromArgb(55, 55, 55);
		public static readonly Color CloseBtnHover = Color.FromArgb(196, 43, 28);
		public static readonly Color SplitterBg = Color.FromArgb(30, 30, 30);

		public static readonly Color LeftHeaderBg = Color.FromArgb(45, 25, 25);
		public static readonly Color RightHeaderBg = Color.FromArgb(25, 45, 25);

		public static readonly Color DiffAddedText = Color.FromArgb(126, 231, 135);
		public static readonly Color DiffChangedText = Color.FromArgb(121, 192, 255);
		public static readonly Color DiffDeletedText = Color.FromArgb(255, 123, 114);

		public static readonly Color DiffAddedLineBg = Color.FromArgb(40, 63, 185, 80);
		public static readonly Color DiffAddedWordBg = Color.FromArgb(100, 63, 185, 80);
		public static readonly Color DiffDeletedLineBg = Color.FromArgb(50, 228, 76, 76);
		public static readonly Color DiffDeletedWordBg = Color.FromArgb(120, 228, 76, 76);
		public static readonly Color DiffEmptyBg = Color.FromArgb(20, 20, 20);
	}
	#endregion

	#region Data Models
	public enum DiffType { Added, Changed, Deleted }

	public sealed class DiffResult : IDisposable
	{
		public DiffType Type { get; set; }
		public string InternalPath { get; set; } = string.Empty;

		public bool IsBasePhysical { get; set; }
		public PakEntryFile? BasePakEntry { get; set; }
		public string BasePhysicalPath { get; set; } = string.Empty;
		public Pak? BaseSourcePak { get; set; }

		public bool IsModPhysical { get; set; }
		public PakEntryFile? ModPakEntry { get; set; }
		public string ModPhysicalPath { get; set; } = string.Empty;
		public Pak? ModSourcePak { get; set; }

		public void Dispose()
		{
			BaseSourcePak?.Dispose();
			ModSourcePak?.Dispose();
			GC.SuppressFinalize(this);
		}
	}
	#endregion

	#region Comparison Logic
	public static class BuildComparer
	{
		private sealed class FileInfoNode
		{
			public bool IsPhysical { get; set; }
			public string PhysicalPath { get; set; } = string.Empty;
			public PakEntryFile? PakEntry { get; set; }
			public Pak? SourcePak { get; set; }
			public long Size { get; set; }
		}

		public static List<DiffResult> CompareTargets(List<Pak> activePaks, string modPath, string filterPath = "", CancellationToken token = default)
		{
			var baseFiles = new Dictionary<string, FileInfoNode>(StringComparer.OrdinalIgnoreCase);

			filterPath = filterPath.Replace('\\', '/').TrimStart('/');
			if (!string.IsNullOrEmpty(filterPath) && !filterPath.EndsWith('/'))
			{
				filterPath += "/";
			}

			foreach (var pak in activePaks)
			{
				token.ThrowIfCancellationRequested();
				foreach (var entry in pak.entries)
				{
					token.ThrowIfCancellationRequested();
					string cleanName = entry.name.Replace('\\', '/').TrimStart('/');

					if (!string.IsNullOrEmpty(filterPath) && !cleanName.StartsWith(filterPath, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					baseFiles[cleanName] = new FileInfoNode
					{
						IsPhysical = false,
						PakEntry = entry,
						SourcePak = pak,
						Size = entry.originalSize
					};
				}
			}

			var modFiles = new Dictionary<string, FileInfoNode>(StringComparer.OrdinalIgnoreCase);

			if (File.Exists(modPath) && modPath.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
			{
				var pak = new Pak(modPath);
				foreach (var entry in pak.entries)
				{
					token.ThrowIfCancellationRequested();
					string cleanName = entry.name.Replace('\\', '/').TrimStart('/');

					if (!string.IsNullOrEmpty(filterPath) && !cleanName.StartsWith(filterPath, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					modFiles[cleanName] = new FileInfoNode
					{
						IsPhysical = false,
						PakEntry = entry,
						SourcePak = pak,
						Size = entry.originalSize
					};
				}
			}
			else if (Directory.Exists(modPath))
			{
				string[] pakFiles = Directory.GetFiles(modPath, "*.pak", SearchOption.TopDirectoryOnly);
				if (pakFiles.Length > 0)
				{
					foreach (var pakFile in pakFiles)
					{
						token.ThrowIfCancellationRequested();
						var pak = new Pak(pakFile);
						foreach (var entry in pak.entries)
						{
							token.ThrowIfCancellationRequested();
							string cleanName = entry.name.Replace('\\', '/').TrimStart('/');

							if (!string.IsNullOrEmpty(filterPath) && !cleanName.StartsWith(filterPath, StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}

							modFiles[cleanName] = new FileInfoNode
							{
								IsPhysical = false,
								PakEntry = entry,
								SourcePak = pak,
								Size = entry.originalSize
							};
						}
					}
				}
				else
				{
					string[] files = Directory.GetFiles(modPath, "*.*", SearchOption.AllDirectories);
					foreach (string file in files)
					{
						token.ThrowIfCancellationRequested();
						string subPath = file[modPath.Length..].Replace('\\', '/').TrimStart('/');
						string cleanName = string.IsNullOrEmpty(filterPath) ? subPath : filterPath + subPath;

						if (!string.IsNullOrEmpty(filterPath) && !cleanName.StartsWith(filterPath, StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}

						modFiles[cleanName] = new FileInfoNode
						{
							IsPhysical = true,
							PhysicalPath = file,
							Size = new FileInfo(file).Length
						};
					}
				}
			}

			var results = new List<DiffResult>(Math.Max(baseFiles.Count, modFiles.Count));

			foreach (var (relPath, modNode) in modFiles)
			{
				token.ThrowIfCancellationRequested();

				if (!baseFiles.TryGetValue(relPath, out var baseNode))
				{
					results.Add(new DiffResult
					{
						Type = DiffType.Added,
						InternalPath = relPath,
						IsModPhysical = modNode.IsPhysical,
						ModPhysicalPath = modNode.PhysicalPath,
						ModPakEntry = modNode.PakEntry,
						ModSourcePak = modNode.SourcePak
					});
				}
				else
				{
					bool isDifferent = baseNode.Size != modNode.Size;
					if (isDifferent)
					{
						results.Add(new DiffResult
						{
							Type = DiffType.Changed,
							InternalPath = relPath,
							IsBasePhysical = baseNode.IsPhysical,
							BasePhysicalPath = baseNode.PhysicalPath,
							BasePakEntry = baseNode.PakEntry,
							BaseSourcePak = baseNode.SourcePak,
							IsModPhysical = modNode.IsPhysical,
							ModPhysicalPath = modNode.PhysicalPath,
							ModPakEntry = modNode.PakEntry,
							ModSourcePak = modNode.SourcePak
						});
					}
					baseFiles.Remove(relPath);
				}
			}

			foreach (var (relPath, baseNode) in baseFiles)
			{
				token.ThrowIfCancellationRequested();
				results.Add(new DiffResult
				{
					Type = DiffType.Deleted,
					InternalPath = relPath,
					IsBasePhysical = baseNode.IsPhysical,
					BasePhysicalPath = baseNode.PhysicalPath,
					BasePakEntry = baseNode.PakEntry,
					BaseSourcePak = baseNode.SourcePak
				});
			}

			return [.. results.OrderBy(r => r.InternalPath)];
		}
	}
	#endregion

	#region Dialogs
	public sealed class CompareSetupDialog : Form
	{
		private readonly float _dpiScale;
		private readonly TextBox txtMod;
		private readonly Button btnStart;

		public string ModPath { get; private set; } = string.Empty;

		public CompareSetupDialog(string sourceItemPath = "")
		{
			_dpiScale = Math.Max(1.0f, DeviceDpi / 96f);

			Text = "Select Modified Target (Folder or File)";
			Size = new Size((int)(600 * _dpiScale), (int)(190 * _dpiScale));
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			BackColor = UITheme.BgMain;
			ForeColor = DiffUI.TextWhite;

			string infoText = "Select the modified source (a matching .pak file, a folder containing paks, or an extracted folder structure).";
			if (!string.IsNullOrEmpty(sourceItemPath))
			{
				infoText += $"\nComparing: {Path.GetFileName(sourceItemPath)}";
			}

			Label lblInfo = new()
			{
				Text = infoText,
				Dock = DockStyle.Top,
				Height = (int)(55 * _dpiScale),
				TextAlign = ContentAlignment.MiddleCenter,
				Font = UITheme.MainFontBold
			};
			Controls.Add(lblInfo);

			btnStart = new Button
			{
				Text = "Start Comparison",
				Dock = DockStyle.Bottom,
				Height = (int)(32 * _dpiScale),
				BackColor = UITheme.Accent,
				FlatStyle = FlatStyle.Flat,
				Font = UITheme.TitleFont,
				Cursor = Cursors.Hand
			};
			btnStart.FlatAppearance.BorderSize = 0;
			btnStart.Click += OnStartClicked;

			Panel pnlMod = CreateInputPanel("MODIFIED (New Version / Folder / PAK):", out txtMod, (int)(75 * _dpiScale), () => btnStart.PerformClick());

			Controls.Add(pnlMod);
			Controls.Add(btnStart);

			Shown += (s, e) => txtMod.Focus();
		}

		private void OnStartClicked(object? sender, EventArgs e)
		{
			if (string.IsNullOrWhiteSpace(txtMod.Text))
			{
				UIHelper.CustomMessageBox(this, "Please provide a valid path!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			if (!File.Exists(txtMod.Text) && !Directory.Exists(txtMod.Text))
			{
				UIHelper.CustomMessageBox(this, "The specified file or folder does not exist!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			ModPath = txtMod.Text;
			DialogResult = DialogResult.OK;
			Close();
		}

		private Panel CreateInputPanel(string title, out TextBox txtOut, int top, Action onAutoSubmit)
		{
			Panel pnl = new() { Location = new Point((int)(20 * _dpiScale), top), Size = new Size((int)(540 * _dpiScale), (int)(48 * _dpiScale)) };
			Label lbl = new() { Text = title, Dock = DockStyle.Top, Height = (int)(15 * _dpiScale), ForeColor = DiffUI.TextLightGray };

			txtOut = new TextBox
			{
				Dock = DockStyle.Bottom,
				Height = (int)(23 * _dpiScale),
				BackColor = UITheme.BgDark,
				ForeColor = DiffUI.TextWhite,
				Font = UITheme.CodeFont,
				BorderStyle = BorderStyle.FixedSingle
			};

			TextBox captureTxt = txtOut;
			captureTxt.KeyDown += (s, e) =>
			{
				if (e.KeyCode == Keys.Enter)
				{
					e.SuppressKeyPress = true;
					onAutoSubmit?.Invoke();
				}
			};

			Panel btnPnl = new() { Dock = DockStyle.Right, Width = (int)(180 * _dpiScale), Height = (int)(23 * _dpiScale) };
			Button btnFile = new() { Text = "📄 File/PAK", Dock = DockStyle.Left, Width = (int)(90 * _dpiScale), FlatStyle = FlatStyle.Flat, BackColor = DiffUI.BtnFileFolderBg, ForeColor = DiffUI.TextWhite, Height = txtOut.Height };
			Button btnDir = new() { Text = "📁 Folder", Dock = DockStyle.Right, Width = (int)(90 * _dpiScale), FlatStyle = FlatStyle.Flat, BackColor = DiffUI.BtnFileFolderBg, ForeColor = DiffUI.TextWhite, Height = txtOut.Height };

			btnFile.FlatAppearance.BorderSize = 1;
			btnFile.FlatAppearance.BorderColor = UITheme.TextDarkGray;
			btnDir.FlatAppearance.BorderSize = 1;
			btnDir.FlatAppearance.BorderColor = UITheme.TextDarkGray;

			btnFile.Click += (s, e) =>
			{
				using var ofd = new OpenFileDialog { Filter = "All Files (*.*)|*.*|Pak Files (*.pak)|*.pak", RestoreDirectory = false };

				string currentPath = captureTxt.Text.Trim();
				string? startDir = null;

				if (!string.IsNullOrEmpty(currentPath))
				{
					startDir = File.Exists(currentPath) ? Path.GetDirectoryName(currentPath) : (Directory.Exists(currentPath) ? currentPath : null);
				}

				if (string.IsNullOrEmpty(startDir))
				{
					startDir = PlayerPrefs.GetString("LastExtDiffDir", "");
				}

				if (!string.IsNullOrEmpty(startDir) && Directory.Exists(startDir))
				{
					ofd.InitialDirectory = startDir;
				}

				if (ofd.ShowDialog() == DialogResult.OK)
				{
					captureTxt.Text = ofd.FileName;
					PlayerPrefs.SetString("LastExtDiffDir", Path.GetDirectoryName(ofd.FileName) ?? "");
					onAutoSubmit?.Invoke();
				}
			};

			btnDir.Click += (s, e) =>
			{
				using var fbd = new FolderBrowserDialog { Description = "Select a modified folder structure" };

				string currentPath = captureTxt.Text.Trim();
				string? startDir = Directory.Exists(currentPath) ? currentPath : (File.Exists(currentPath) ? Path.GetDirectoryName(currentPath) : null);

				if (string.IsNullOrEmpty(startDir))
				{
					startDir = PlayerPrefs.GetString("LastExtDiffDir", "");
				}

				if (!string.IsNullOrEmpty(startDir) && Directory.Exists(startDir))
				{
					fbd.SelectedPath = startDir;
					fbd.InitialDirectory = startDir;
				}

				if (fbd.ShowDialog() == DialogResult.OK)
				{
					captureTxt.Text = fbd.SelectedPath;
					PlayerPrefs.SetString("LastExtDiffDir", fbd.SelectedPath);
					onAutoSubmit?.Invoke();
				}
			};

			btnPnl.Controls.Add(btnFile);
			btnPnl.Controls.Add(btnDir);
			pnl.Controls.Add(txtOut);
			pnl.Controls.Add(lbl);
			pnl.Controls.Add(btnPnl);

			return pnl;
		}
	}
	#endregion

	#region User Controls
	public sealed class DiffManagerControl : UserControl
	{
		private readonly float _dpiScale;

		private Panel customTabBar = null!;
		private Button btnTabList = null!;
		private Button btnTabDiff = null!;
		private Button btnClose = null!;
		private Button btnNext = null!;
		private Button btnPrev = null!;
		private Button btnSyncScroll = null!;

		private Panel pnlMainContent = null!;
		private Panel pnlList = null!;
		private Panel pnlDiff = null!;

		private ListView lvChanges = null!;
		private SplitContainer codeSplit = null!;
		private XFusion oldCodeBox = null!;
		private XFusion newCodeBox = null!;
		private Label lblBaseFileName = null!;
		private Label lblModFileName = null!;

		private HScrollBar? _hScrollOld;
		private HScrollBar? _hScrollNew;

		private readonly List<int> _diffLineIndices = [];
		private bool _isSyncing;
		private bool _isScrollSyncEnabled = true;
		private int _currentDiffIndex = -1;

		public event EventHandler? OnCloseRequested;

		public DiffManagerControl()
		{
			_dpiScale = Math.Max(1.0f, DeviceDpi / 96f);

			Dock = DockStyle.Fill;
			BackColor = UITheme.BgMain;

			InitializeCustomTabBar();
			InitializeTabsAndContent();

			customTabBar.SendToBack();
		}

		private static string? DecodeBytesToText(byte[] bytes)
		{
			try
			{
				if (bytes.Take(100).Contains((byte)0))
					return null;

				return Encoding.UTF8.GetString(bytes);
			}
			catch
			{
				return null;
			}
		}

		private void InitializeCustomTabBar()
		{
			customTabBar = new Panel { Dock = DockStyle.Top, Height = (int)(40 * _dpiScale), BackColor = UITheme.BgDarker };
			Panel bottomBorder = new() { Dock = DockStyle.Bottom, Height = (int)Math.Max(1, 1 * _dpiScale), BackColor = DiffUI.BorderBottom };
			customTabBar.Controls.Add(bottomBorder);

			btnTabList = new Button { Text = "📄 Differences", Dock = DockStyle.Left, Width = (int)(150 * _dpiScale), FlatStyle = FlatStyle.Flat, Font = UITheme.MainFontBold, ForeColor = DiffUI.TextWhite, BackColor = UITheme.BgPanel, Cursor = Cursors.Hand };
			btnTabList.FlatAppearance.BorderSize = 0;

			btnTabDiff = new Button { Text = "⚖️ Compare Content", Dock = DockStyle.Left, Width = (int)(180 * _dpiScale), FlatStyle = FlatStyle.Flat, Font = UITheme.MainFontBold, ForeColor = UITheme.TextDarkGray, BackColor = UITheme.BgDarker, Cursor = Cursors.Hand };
			btnTabDiff.FlatAppearance.BorderSize = 0;

			FlowLayoutPanel pnlRightButtons = new()
			{
				Dock = DockStyle.Right,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				FlowDirection = FlowDirection.RightToLeft,
				WrapContents = false,
				BackColor = DiffUI.TextTransparent,
				Margin = Padding.Empty,
				Padding = Padding.Empty
			};

			int btnHeight = customTabBar.Height;

			btnSyncScroll = new Button
			{
				Text = "🔒 Sync Scroll",
				Width = (int)(110 * _dpiScale),
				Height = btnHeight,
				FlatStyle = FlatStyle.Flat,
				Font = UITheme.MainFontBold,
				ForeColor = DiffUI.DiffChangedText,
				Cursor = Cursors.Hand,
				Visible = false,
				Margin = Padding.Empty
			};
			btnSyncScroll.FlatAppearance.BorderSize = 0;
			btnSyncScroll.FlatAppearance.MouseOverBackColor = DiffUI.HoverBg;
			btnSyncScroll.Click += (s, e) => ToggleScrollSync();

			btnPrev = new Button
			{
				Text = "▲",
				Width = (int)(40 * _dpiScale),
				Height = btnHeight,
				FlatStyle = FlatStyle.Flat,
				Font = DiffUI.NavArrowFont,
				ForeColor = DiffUI.TextLightGray,
				Cursor = Cursors.Hand,
				Visible = false,
				Margin = Padding.Empty
			};
			btnPrev.FlatAppearance.BorderSize = 0;
			btnPrev.FlatAppearance.MouseOverBackColor = DiffUI.HoverBg;
			btnPrev.Click += (s, e) => NavigateDiff(-1);

			btnNext = new Button
			{
				Text = "▼",
				Width = (int)(40 * _dpiScale),
				Height = btnHeight,
				FlatStyle = FlatStyle.Flat,
				Font = DiffUI.NavArrowFont,
				ForeColor = DiffUI.TextLightGray,
				Cursor = Cursors.Hand,
				Visible = false,
				Margin = Padding.Empty
			};
			btnNext.FlatAppearance.BorderSize = 0;
			btnNext.FlatAppearance.MouseOverBackColor = DiffUI.HoverBg;
			btnNext.Click += (s, e) => NavigateDiff(1);

			btnClose = new Button
			{
				Text = "✕",
				Width = (int)(45 * _dpiScale),
				Height = btnHeight,
				FlatStyle = FlatStyle.Flat,
				Font = DiffUI.CloseButtonFont,
				ForeColor = UITheme.TextDarkGray,
				Cursor = Cursors.Hand,
				Margin = Padding.Empty
			};
			btnClose.FlatAppearance.BorderSize = 0;
			btnClose.FlatAppearance.MouseOverBackColor = DiffUI.CloseBtnHover;
			btnClose.MouseEnter += (s, e) => btnClose.ForeColor = DiffUI.TextWhite;
			btnClose.MouseLeave += (s, e) => btnClose.ForeColor = UITheme.TextDarkGray;
			btnClose.Click += (s, e) => OnCloseRequested?.Invoke(this, EventArgs.Empty);

			btnTabList.Click += (s, e) => SwitchToTab(pnlList);
			btnTabDiff.Click += (s, e) => SwitchToTab(pnlDiff);

			pnlRightButtons.Controls.Add(btnClose);
			pnlRightButtons.Controls.Add(btnNext);
			pnlRightButtons.Controls.Add(btnPrev);
			pnlRightButtons.Controls.Add(btnSyncScroll);

			ToolTip toolTip = new();
			toolTip.SetToolTip(btnPrev, "Previous Difference");
			toolTip.SetToolTip(btnNext, "Next Difference");
			toolTip.SetToolTip(btnClose, "Close");
			toolTip.SetToolTip(btnSyncScroll, "Sync Scroll");

			customTabBar.Controls.Add(btnTabDiff);
			customTabBar.Controls.Add(btnTabList);
			customTabBar.Controls.Add(pnlRightButtons);

			Controls.Add(customTabBar);
		}

		private void ToggleScrollSync()
		{
			_isScrollSyncEnabled = !_isScrollSyncEnabled;
			if (_isScrollSyncEnabled)
			{
				btnSyncScroll.Text = "🔒 Sync Scroll";
				btnSyncScroll.ForeColor = DiffUI.DiffChangedText;
				SynchronizeScrollBars();
			}
			else
			{
				btnSyncScroll.Text = "🔓 Free Scroll";
				btnSyncScroll.ForeColor = DiffUI.DiffDeletedText;
			}
		}

		private void SwitchToTab(Panel targetPanel)
		{
			bool isList = targetPanel == pnlList;

			pnlList.Visible = isList;
			pnlDiff.Visible = !isList;

			btnNext.Visible = !isList;
			btnPrev.Visible = !isList;
			btnSyncScroll.Visible = !isList;

			btnTabList.BackColor = isList ? UITheme.BgPanel : UITheme.BgDarker;
			btnTabList.ForeColor = isList ? DiffUI.TextWhite : UITheme.TextDarkGray;

			btnTabDiff.BackColor = isList ? UITheme.BgDarker : UITheme.BgPanel;
			btnTabDiff.ForeColor = isList ? UITheme.TextDarkGray : DiffUI.TextWhite;

			pnlMainContent.Invalidate();
			pnlMainContent.Update();
		}

		private void InitializeTabsAndContent()
		{
			pnlMainContent = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.BgMain };
			pnlList = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.BgMain };
			pnlDiff = new Panel { Dock = DockStyle.Fill, BackColor = UITheme.BgMain, Visible = false };

			pnlMainContent.Controls.Add(pnlList);
			pnlMainContent.Controls.Add(pnlDiff);
			Controls.Add(pnlMainContent);

			InitializeListView();
			InitializeDiffEditor();
		}

		private void InitializeListView()
		{
			lvChanges = new ListView
			{
				Dock = DockStyle.Fill,
				View = View.Details,
				FullRowSelect = true,
				GridLines = false,
				BorderStyle = BorderStyle.None,
				BackColor = UITheme.BgPanel,
				ForeColor = DiffUI.TextGainsboro,
				Font = UITheme.MainFont,
				OwnerDraw = true
			};

			lvChanges.Columns.Add("State", (int)(120 * _dpiScale));
			lvChanges.Columns.Add("Internal Path", (int)(800 * _dpiScale));

			lvChanges.DrawColumnHeader += (s, e) =>
			{
				e.Graphics.FillRectangle(new SolidBrush(UITheme.BgDarker), e.Bounds);
				if (e.Header != null)
				{
					TextRenderer.DrawText(e.Graphics, e.Header.Text, UITheme.MainFontBold, e.Bounds, UITheme.TextDarkGray, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.LeftAndRightPadding);
				}
			};

			lvChanges.DrawItem += (s, e) => e.DrawDefault = false;

			lvChanges.DrawSubItem += (s, e) =>
			{
				if (e.Item == null || e.SubItem == null) return;

				if (e.Item.Selected)
				{
					e.Graphics.FillRectangle(SystemBrushes.Highlight, e.Bounds);
					TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.Item.Font, e.Bounds, SystemColors.HighlightText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.LeftAndRightPadding);
				}
				else
				{
					using var brush = new SolidBrush(lvChanges.BackColor);
					e.Graphics.FillRectangle(brush, e.Bounds);
					TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.Item.Font, e.Bounds, e.Item.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.LeftAndRightPadding);
				}
			};

			lvChanges.SelectedIndexChanged += LvChanges_SelectedIndexChanged;

			Panel paddingPanel = new() { Dock = DockStyle.Fill, Padding = new Padding((int)(10 * _dpiScale)) };
			paddingPanel.Controls.Add(lvChanges);
			pnlList.Controls.Add(paddingPanel);
		}

		private void InitializeDiffEditor()
		{
			codeSplit = new SplitContainer
			{
				Dock = DockStyle.Fill,
				Orientation = Orientation.Vertical,
				BackColor = DiffUI.SplitterBg,
				SplitterWidth = (int)(6 * _dpiScale)
			};

			codeSplit.Resize += (s, e) => { if (codeSplit.Width > 0) codeSplit.SplitterDistance = codeSplit.Width / 2; };

			int headerHeight = (int)(32 * _dpiScale);

			// --- LEFT SIDE ---
			Panel pnlLeft = new() { Dock = DockStyle.Fill, BackColor = UITheme.BgPanel };
			Panel hLeft = new() { Dock = DockStyle.Top, Height = headerHeight, BackColor = DiffUI.LeftHeaderBg };

			Label lLeftTitle = new() { Text = " ⛔ BASE", Dock = DockStyle.Left, Width = (int)(80 * _dpiScale), ForeColor = DiffUI.DiffDeletedText, Font = UITheme.MainFontBold, TextAlign = ContentAlignment.MiddleLeft };
			lblBaseFileName = new Label { Text = "", Dock = DockStyle.Fill, ForeColor = DiffUI.TextDarkGray, Font = UITheme.MainFont, TextAlign = ContentAlignment.MiddleLeft };

			hLeft.Controls.Add(lblBaseFileName);
			hLeft.Controls.Add(lLeftTitle);

			oldCodeBox = CreateEditorControl();
			oldCodeBox.Dock = DockStyle.Fill;

			pnlLeft.Controls.Add(oldCodeBox);
			pnlLeft.Controls.Add(hLeft);

			hLeft.SendToBack();
			oldCodeBox.BringToFront();

			// --- RIGHT SIDE ---
			Panel pnlRight = new() { Dock = DockStyle.Fill, BackColor = UITheme.BgPanel };
			Panel hRight = new() { Dock = DockStyle.Top, Height = headerHeight, BackColor = DiffUI.RightHeaderBg };

			Label lRightTitle = new() { Text = " ✅ MODIFIED", Dock = DockStyle.Left, Width = (int)(100 * _dpiScale), ForeColor = DiffUI.DiffAddedText, Font = UITheme.MainFontBold, TextAlign = ContentAlignment.MiddleLeft };
			lblModFileName = new Label { Text = "", Dock = DockStyle.Fill, ForeColor = DiffUI.TextDarkGray, Font = UITheme.MainFont, TextAlign = ContentAlignment.MiddleLeft };

			hRight.Controls.Add(lblModFileName);
			hRight.Controls.Add(lRightTitle);

			newCodeBox = CreateEditorControl();
			newCodeBox.Dock = DockStyle.Fill;

			pnlRight.Controls.Add(newCodeBox);
			pnlRight.Controls.Add(hRight);

			hRight.SendToBack();
			newCodeBox.BringToFront();

			codeSplit.Panel1.Controls.Add(pnlLeft);
			codeSplit.Panel2.Controls.Add(pnlRight);

			pnlDiff.Controls.Add(codeSplit);

			SetupScrollSync();
		}

		private void SetupScrollSync()
		{
			oldCodeBox.VisualScrollPositionChanged -= XfOld_ScrollChanged;
			newCodeBox.VisualScrollPositionChanged -= XfNew_ScrollChanged;

			oldCodeBox.VisualScrollPositionChanged += XfOld_ScrollChanged;
			newCodeBox.VisualScrollPositionChanged += XfNew_ScrollChanged;

			SetupHScrollSync(oldCodeBox, newCodeBox);
		}

		private void SetupHScrollSync(XFusion xfOld, XFusion xfNew)
		{
			if (_hScrollOld != null)
			{
				_hScrollOld.Scroll -= HScrollOld_Scroll;
				_hScrollOld.ValueChanged -= HScrollOld_Scroll;
			}
			if (_hScrollNew != null)
			{
				_hScrollNew.Scroll -= HScrollNew_Scroll;
				_hScrollNew.ValueChanged -= HScrollNew_Scroll;
			}

			_hScrollOld = GetHScrollBar(xfOld);
			_hScrollNew = GetHScrollBar(xfNew);

			if (_hScrollOld != null)
			{
				_hScrollOld.Scroll += HScrollOld_Scroll;
				_hScrollOld.ValueChanged += HScrollOld_Scroll;
			}
			if (_hScrollNew != null)
			{
				_hScrollNew.Scroll += HScrollNew_Scroll;
				_hScrollNew.ValueChanged += HScrollNew_Scroll;
			}
		}

		private static HScrollBar? GetHScrollBar(XFusion ctrl)
		{
			if (ctrl == null) return null;

			HScrollBar? found = FindControlRecursive<HScrollBar>(ctrl);
			if (found != null) return found;

			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			foreach (var field in ctrl.GetType().GetFields(flags))
			{
				if (typeof(HScrollBar).IsAssignableFrom(field.FieldType))
				{
					if (field.GetValue(ctrl) is HScrollBar hsb) return hsb;
				}
			}
			foreach (var prop in ctrl.GetType().GetProperties(flags))
			{
				if (typeof(HScrollBar).IsAssignableFrom(prop.PropertyType))
				{
					if (prop.GetValue(ctrl) is HScrollBar hsb) return hsb;
				}
			}
			return null;
		}

		private static T? FindControlRecursive<T>(Control parent) where T : Control
		{
			foreach (Control child in parent.Controls)
			{
				if (child is T typed) return typed;
				var found = FindControlRecursive<T>(child);
				if (found != null) return found;
			}
			return null;
		}

		private void HScrollOld_Scroll(object? sender, EventArgs e)
		{
			if (_isSyncing || !_isScrollSyncEnabled) return;
			if (_hScrollOld != null && _hScrollNew != null)
			{
				_isSyncing = true;
				try
				{
					SetHScrollValue(newCodeBox, _hScrollNew, _hScrollOld.Value);
					oldCodeBox.Update();
					newCodeBox.Update();
				}
				finally
				{
					_isSyncing = false;
				}
			}
		}

		private void HScrollNew_Scroll(object? sender, EventArgs e)
		{
			if (_isSyncing || !_isScrollSyncEnabled) return;
			if (_hScrollOld != null && _hScrollNew != null)
			{
				_isSyncing = true;
				try
				{
					SetHScrollValue(oldCodeBox, _hScrollOld, _hScrollNew.Value);
					newCodeBox.Update();
					oldCodeBox.Update();
				}
				finally
				{
					_isSyncing = false;
				}
			}
		}

		private static void SetHScrollValue(XFusion ctrl, HScrollBar hBar, int val)
		{
			int clamped = Math.Clamp(val, hBar.Minimum, hBar.Maximum);
			if (hBar.Value != clamped)
			{
				hBar.Value = clamped;
			}

			var type = ctrl.GetType();
			var setMethod = type.GetMethod("SetHorizontalScrollPosition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
			?? type.GetMethod("SetHScrollPosition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
			?? type.GetMethod("SetScrollPosH", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

			if (setMethod != null)
			{
				try { setMethod.Invoke(ctrl, [clamped]); } catch { }
			}
			else
			{
				var prop = type.GetProperty("HorizontalScrollPosition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
				?? type.GetProperty("HScrollPosition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (prop != null && prop.CanWrite)
				{
					try { prop.SetValue(ctrl, clamped); } catch { }
				}
			}

			ctrl.Invalidate();
			ctrl.Update();
		}

		private void XfOld_ScrollChanged(object? sender, int scrollLine)
		{
			if (_isSyncing || !_isScrollSyncEnabled) return;
			_isSyncing = true;
			try
			{
				newCodeBox.SetScrollPosition(scrollLine);
				newCodeBox.Update();
				oldCodeBox.Update();
			}
			finally
			{
				_isSyncing = false;
			}
		}

		private void XfNew_ScrollChanged(object? sender, int scrollLine)
		{
			if (_isSyncing || !_isScrollSyncEnabled) return;
			_isSyncing = true;
			try
			{
				oldCodeBox.SetScrollPosition(scrollLine);
				oldCodeBox.Update();
				newCodeBox.Update();
			}
			finally
			{
				_isSyncing = false;
			}
		}

		private void SynchronizeScrollBars()
		{
			// --- Vertical Sync ---
			VScrollBar? scrollOld = oldCodeBox.Controls.OfType<VScrollBar>().FirstOrDefault();
			VScrollBar? scrollNew = newCodeBox.Controls.OfType<VScrollBar>().FirstOrDefault();

			if (scrollOld != null && scrollNew != null)
			{
				int max = Math.Max(scrollOld.Maximum, scrollNew.Maximum);
				scrollOld.Maximum = max;
				scrollNew.Maximum = max;

				scrollOld.Minimum = Math.Min(scrollOld.Minimum, scrollNew.Minimum);
				scrollNew.Minimum = scrollOld.Minimum;

				int targetValue = Math.Min(scrollOld.Value, scrollOld.Maximum);
				scrollOld.Value = targetValue;
				scrollNew.Value = targetValue;

				oldCodeBox.SetScrollPosition(targetValue);
				newCodeBox.SetScrollPosition(targetValue);
			}

			// --- Horizontal Sync ---
			SetupHScrollSync(oldCodeBox, newCodeBox);
			if (_hScrollOld != null && _hScrollNew != null)
			{
				int hMax = Math.Max(_hScrollOld.Maximum, _hScrollNew.Maximum);
				_hScrollOld.Maximum = hMax;
				_hScrollNew.Maximum = hMax;

				int targetHVal = Math.Min(_hScrollOld.Value, _hScrollOld.Maximum);
				SetHScrollValue(oldCodeBox, _hScrollOld, targetHVal);
				SetHScrollValue(newCodeBox, _hScrollNew, targetHVal);
			}

			oldCodeBox.Update();
			newCodeBox.Update();
		}

		private static XFusion CreateEditorControl()
		{
			var xf = new XFusion { Font = UITheme.CodeFont };
			DisableWordWrap(xf);
			return xf;
		}

		private static void DisableWordWrap(XFusion xf)
		{
			try
			{
				var prop = typeof(XFusion).GetProperty("WordWrap");
				if (prop != null && prop.CanWrite)
				{
					prop.SetValue(xf, false);
				}
			}
			catch { }
		}

		private void NavigateDiff(int direction)
		{
			if (_diffLineIndices.Count == 0) return;

			if (_currentDiffIndex < 0 || _currentDiffIndex >= _diffLineIndices.Count)
			{
				int currentLine = 0;
				var vBar = oldCodeBox.Controls.OfType<VScrollBar>().FirstOrDefault();
				if (vBar != null) currentLine = vBar.Value;

				if (direction > 0)
				{
					_currentDiffIndex = _diffLineIndices.FindIndex(l => l >= currentLine);
					if (_currentDiffIndex == -1) _currentDiffIndex = 0;
				}
				else
				{
					_currentDiffIndex = _diffLineIndices.FindLastIndex(l => l <= currentLine);
					if (_currentDiffIndex == -1) _currentDiffIndex = _diffLineIndices.Count - 1;
				}
			}
			else
			{
				_currentDiffIndex += direction;
				if (_currentDiffIndex >= _diffLineIndices.Count) _currentDiffIndex = 0;
				else if (_currentDiffIndex < 0) _currentDiffIndex = _diffLineIndices.Count - 1;
			}

			int targetLine = _diffLineIndices[_currentDiffIndex];
			int targetScroll = Math.Max(0, targetLine - 3);

			oldCodeBox.ScrollToLine(targetScroll);
			newCodeBox.ScrollToLine(targetScroll);
		}

		public void LoadDiffSession(List<DiffResult> diffs)
		{
			_currentDiffIndex = -1;

			SwitchToTab(pnlList);
			lvChanges.Items.Clear();

			oldCodeBox.Clear();
			newCodeBox.Clear();

			lblBaseFileName.Text = string.Empty;
			lblModFileName.Text = string.Empty;

			foreach (var d in diffs)
			{
				var item = new ListViewItem(d.Type.ToString()) { Tag = d };

				item.ForeColor = d.Type switch
				{
					DiffType.Added => DiffUI.DiffAddedText,
					DiffType.Changed => DiffUI.DiffChangedText,
					DiffType.Deleted => DiffUI.DiffDeletedText,
					_ => item.ForeColor
				};

				item.SubItems.Add(d.InternalPath);
				lvChanges.Items.Add(item);
			}

			if (lvChanges.Items.Count > 0)
			{
				lvChanges.Items[0].Selected = true;
			}
		}

		public enum DiffSide { Left, Right }

		public void LoadDirectText(string internalPath, string baseText, string modText, string targetExternalFile)
		{
			_currentDiffIndex = -1;

			SwitchToTab(pnlDiff);

			oldCodeBox.Clear();
			newCodeBox.Clear();

			lblBaseFileName.Text = $"—  [Base] // {internalPath}";
			lblModFileName.Text = !string.IsNullOrEmpty(targetExternalFile)
				? $"—  [{Path.GetFileName(targetExternalFile)}] // {internalPath}"
				: $"—  [Modified] // {internalPath}";

			_diffLineIndices.Clear();
			var dpx = new SideBySideDiffBuilder(new Differ());
			var model = dpx.BuildDiffModel(baseText ?? string.Empty, modText ?? string.Empty);

			RenderXFusionDiff(oldCodeBox, model.OldText, DiffSide.Left, true);
			RenderXFusionDiff(newCodeBox, model.NewText, DiffSide.Right, false);

			SynchronizeScrollBars();

			oldCodeBox.ScrollToLine(0);
			newCodeBox.ScrollToLine(0);
		}

		private async void LvChanges_SelectedIndexChanged(object? sender, EventArgs e)
		{
			if (lvChanges.SelectedItems.Count == 0 || lvChanges.SelectedItems[0].Tag is not DiffResult diff)
				return;

			_currentDiffIndex = -1;

			string? baseText = string.Empty;
			string? modText = string.Empty;
			string baseFileName = diff.InternalPath;

			lblBaseFileName.Text = "⏳ Loading base...";
			lblModFileName.Text = "⏳ Loading modified...";

			try
			{
				await Task.Run(() =>
				{
					if (diff.Type != DiffType.Added)
					{
						if (diff.IsBasePhysical && File.Exists(diff.BasePhysicalPath))
						{
							baseText = DecodeBytesToText(File.ReadAllBytes(diff.BasePhysicalPath));
						}
						else if (diff.BaseSourcePak != null && diff.BasePakEntry != null)
						{
							baseText = DecodeBytesToText(diff.BaseSourcePak.GetPreviewBytes(diff.BasePakEntry, (int)diff.BasePakEntry.originalSize));
						}
					}

					if (diff.Type != DiffType.Deleted)
					{
						if (diff.IsModPhysical && File.Exists(diff.ModPhysicalPath))
						{
							modText = DecodeBytesToText(File.ReadAllBytes(diff.ModPhysicalPath));
						}
						else if (diff.ModSourcePak != null && diff.ModPakEntry != null)
						{
							modText = DecodeBytesToText(diff.ModSourcePak.GetPreviewBytes(diff.ModPakEntry, (int)diff.ModPakEntry.originalSize));
						}
					}
				});

				baseText ??= "[ BINARY DATA - CANNOT RENDER TEXT DIFF ]";
				modText ??= "[ BINARY DATA - CANNOT RENDER TEXT DIFF ]";

				lblBaseFileName.Text = diff.Type != DiffType.Added ? $"— [Base] // {baseFileName}" : "— [New File / Not in Base]";
				lblModFileName.Text = diff.Type != DiffType.Deleted ? $"— [Modified] // {baseFileName}" : "— [Deleted File / Not in Modified]";

				_diffLineIndices.Clear();
				var dpx = new SideBySideDiffBuilder(new Differ());
				var model = dpx.BuildDiffModel(baseText, modText);

				RenderXFusionDiff(oldCodeBox, model.OldText, DiffSide.Left, true);
				RenderXFusionDiff(newCodeBox, model.NewText, DiffSide.Right, false);

				SynchronizeScrollBars();

				SwitchToTab(pnlDiff);

				oldCodeBox.ScrollToLine(0);
				newCodeBox.ScrollToLine(0);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error loading diff text: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void RenderXFusionDiff(XFusion xf, DiffPaneModel textSide, DiffSide side, bool trackChanges)
		{
			xf.BeginUpdate();
			xf.Clear();

			using var scope = xf.CreateForceHighlightScope();

			Color bgAddedLine = DiffUI.DiffAddedLineBg;
			Color bgAddedWord = DiffUI.DiffAddedWordBg;
			Color bgDeletedLine = DiffUI.DiffDeletedLineBg;
			Color bgDeletedWord = DiffUI.DiffDeletedWordBg;
			Color bgEmpty = DiffUI.DiffEmptyBg;

			int lineIndex = 0;
			bool inBlock = false;

			foreach (var line in textSide.Lines)
			{
				bool isChanged = line.Type != ChangeType.Unchanged;

				if (isChanged && trackChanges)
				{
					if (!inBlock)
					{
						_diffLineIndices.Add(lineIndex);
						inBlock = true;
					}
				}
				else if (line.Type is ChangeType.Unchanged or ChangeType.Imaginary)
				{
					inBlock = false;
				}

				Color? bgColor = line.Type switch
				{
					ChangeType.Inserted => bgAddedLine,
					ChangeType.Deleted => bgDeletedLine,
					ChangeType.Modified => side == DiffSide.Left ? bgDeletedLine : bgAddedLine,
					ChangeType.Imaginary => bgEmpty,
					_ => null
				};

				xf.AppendLine(line.Text ?? string.Empty, bgColor, line.Type == ChangeType.Imaginary ? null : line.Position);

				if (isChanged && line.SubPieces is { Count: > 0 })
				{
					int currentPos = 0;
					foreach (var piece in line.SubPieces)
					{
						if (piece.Text != null)
						{
							if (piece.Type is not (ChangeType.Unchanged or ChangeType.Imaginary))
							{
								Color wordColor = piece.Type switch
								{
									ChangeType.Inserted => bgAddedWord,
									ChangeType.Deleted => bgDeletedWord,
									_ => side == DiffSide.Left ? bgDeletedWord : bgAddedWord
								};

								xf.AddHighlight(lineIndex, currentPos, piece.Text.Length, wordColor);
							}
							currentPos += piece.Text.Length;
						}
					}
				}
				lineIndex++;
			}
			xf.EndUpdate();

			var vBar = xf.Controls.OfType<VScrollBar>().FirstOrDefault();
			if (vBar != null)
			{
				int extraPadding = vBar.LargeChange > 0 ? vBar.LargeChange : 10;
				vBar.Maximum = Math.Max(0, textSide.Lines.Count + extraPadding);
			}
		}

		protected override void Dispose(bool disposing)
		{ base.Dispose(disposing); }
	}
	#endregion

	#region Orchestrator
	public sealed class DiffOrchestrator(Form ownerForm, float dpiScale)
	{
		private readonly Form _ownerForm = ownerForm;
		private readonly float _dpiScale = dpiScale;
		private List<DiffResult>? _currentDiffResults;

		public void CloseCurrentDiff()
		{
			if (_currentDiffResults != null)
			{
				foreach (var diff in _currentDiffResults)
				{
					diff.Dispose();
				}
				_currentDiffResults.Clear();
				_currentDiffResults = null;
			}
		}

		public static string? DecodeText(byte[] data)
		{
			if (data is not { Length: > 0 }) return null;

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

		public bool TryPrepareAutoCompare(string internalPath, byte[] archiveBytes, out string archiveTextOut, out string externalTextOut, out string targetExternalFileOut)
		{
			archiveTextOut = string.Empty;
			externalTextOut = string.Empty;
			targetExternalFileOut = string.Empty;

			bool autoCompareEnabled = PlayerPrefs.GetBool("AutoCompareEnabled", true);

			string externalRoot = PlayerPrefs.GetString("AutoDiffExternalRoot", "");
			string targetExternalFile = string.Empty;

			if (autoCompareEnabled && !string.IsNullOrEmpty(externalRoot) && Directory.Exists(externalRoot))
			{
				string normalizedPath = internalPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
				string potentialPath = Path.Combine(externalRoot, normalizedPath);
				if (File.Exists(potentialPath)) targetExternalFile = potentialPath;
			}

			if (string.IsNullOrEmpty(targetExternalFile))
			{
				string selectedPath = string.Empty;
				bool isFolderSelected = false;

				using (var setupDlg = new CompareSetupDialog(internalPath))
				{
					setupDlg.Text = "Auto-pair failed. Select modified source:";

					if (setupDlg.ShowDialog(_ownerForm) == DialogResult.OK)
					{
						selectedPath = setupDlg.ModPath;
						isFolderSelected = Directory.Exists(selectedPath);
					}
					else
					{
						return false;
					}
				}

				if (isFolderSelected)
				{
					string normalizedPath = internalPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
					string directFileCheck = Path.Combine(selectedPath, Path.GetFileName(internalPath));
					string structuralFileCheck = Path.Combine(selectedPath, normalizedPath);

					string calculatedRoot = selectedPath;
					string normalizedInternal = internalPath.Replace('\\', '/').TrimStart('/');
					int slashCount = normalizedInternal.Count(c => c == '/');

					if (File.Exists(structuralFileCheck))
					{
						targetExternalFile = structuralFileCheck;
						PlayerPrefs.SetString("LastExtDiffDir", selectedPath);
					}
					else if (File.Exists(directFileCheck))
					{
						targetExternalFile = directFileCheck;
						PlayerPrefs.SetString("LastExtDiffDir", selectedPath);
						try
						{
							for (int i = 0; i < slashCount; i++) calculatedRoot = Path.GetDirectoryName(calculatedRoot) ?? calculatedRoot;
						}
						catch { }
					}
					else
					{
						UIHelper.CustomMessageBox(_ownerForm, $"The file '{Path.GetFileName(internalPath)}' could not be found inside the selected folder structure!\n\nTried paths:\n1. {structuralFileCheck}\n2. {directFileCheck}", "File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
						return false;
					}

					string effectiveExternalRoot = autoCompareEnabled ? externalRoot : "";

					if (Directory.Exists(calculatedRoot) && calculatedRoot != effectiveExternalRoot)
					{
						var dr = UIHelper.CustomMessageBox(
							_ownerForm,
							$"Would you like to set this folder as the global comparison source?\n\nRoot: {calculatedRoot}\n\nIf yes, next time the program will AUTOMATICALLY pair files on right-click!\n\n(Note: You can turn this automatic behavior off anytime in the Settings.)",
							"Set Auto-Matching", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

						if (dr == DialogResult.Yes)
						{
							PlayerPrefs.SetString("AutoDiffExternalRoot", calculatedRoot);
							PlayerPrefs.SetBool("AutoCompareEnabled", true);
						}
					}
				}
				else
				{
					targetExternalFile = selectedPath;
					PlayerPrefs.SetString("LastExtDiffDir", Path.GetDirectoryName(targetExternalFile) ?? "");

					string detectedDir = Path.GetDirectoryName(targetExternalFile) ?? "";
					string normalizedInternal = internalPath.Replace('\\', '/').TrimStart('/');
					int slashCount = normalizedInternal.Count(c => c == '/');
					string calculatedRoot = detectedDir;

					try
					{
						for (int i = 0; i < slashCount; i++) calculatedRoot = Path.GetDirectoryName(calculatedRoot) ?? calculatedRoot;

						string effectiveExternalRoot = autoCompareEnabled ? externalRoot : "";

						if (Directory.Exists(calculatedRoot) && calculatedRoot != effectiveExternalRoot)
						{
							var dr = UIHelper.CustomMessageBox(
								_ownerForm,
								$"Would you like to set this folder as the global comparison source?\n\nRoot: {calculatedRoot}\n\nIf yes, next time the program will AUTOMATICALLY pair files on right-click!\n\n(Note: You can turn this automatic behavior off anytime in the Settings.)",
								"Set Auto-Matching", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

							if (dr == DialogResult.Yes)
							{
								PlayerPrefs.SetString("AutoDiffExternalRoot", calculatedRoot);
								PlayerPrefs.SetBool("AutoCompareEnabled", true);
							}
						}
					}
					catch { }
				}
			}

			try
			{
				string? archiveText = DecodeText(archiveBytes);
				if (archiveText == null)
				{
					UIHelper.CustomMessageBox(_ownerForm, "This is a binary file! Text comparison is not supported for binary formats.", "Binary File Detected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return false;
				}

				byte[] externalBytes = File.ReadAllBytes(targetExternalFile);
				string? externalText = DecodeText(externalBytes);
				if (externalText == null)
				{
					UIHelper.CustomMessageBox(_ownerForm, "The target external file is binary! Comparison aborted.", "Binary File Detected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return false;
				}

				archiveTextOut = archiveText;
				externalTextOut = externalText;
				targetExternalFileOut = targetExternalFile;
				return true;
			}
			catch (Exception ex)
			{
				UIHelper.CustomMessageBox(_ownerForm, $"Error reading file:\n{ex.Message}", "Diff Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
		}

		public async Task<List<DiffResult>?> ExecuteDiffSessionAsync(List<Pak> loadedPaks, string modPath, string targetInternalPath, CancellationToken token)
		{
			if (loadedPaks is not { Count: > 0 })
			{
				UIHelper.CustomMessageBox(_ownerForm, "No PAK file is loaded! Please open at least one archive in the workbench to serve as the BASE.", "No Base", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return null;
			}

			CloseCurrentDiff();

			var changes = await Task.Run(() => BuildComparer.CompareTargets(loadedPaks, modPath, targetInternalPath, token));
			_currentDiffResults = changes;
			return changes;
		}
	}
	#endregion
}