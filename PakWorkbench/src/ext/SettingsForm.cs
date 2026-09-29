using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PakWorkbench.src.ext
{
	public class SettingsForm : Form
	{
		#region Shared Fonts & Colors
		// ==========================================
		// SHARED FONTS & COLORS
		// ==========================================
		private static readonly Color _colorLiteBg = UITheme.BgDarker;
		private static readonly Color _colorSectionTitle = Color.FromArgb(120, 120, 125);
		private static readonly Color _colorBtnCancelText = Color.FromArgb(180, 180, 185);
		private static readonly Color _colorBtnCancelHover = Color.FromArgb(50, 50, 55);

		private static readonly Font _fontLiteIcon = new Font(UITheme.FontSegoe, 12f);
		private static readonly Font _fontLiteText = new Font(UITheme.FontSegoe, 9.5f, FontStyle.Bold);
		private static readonly Font _fontSectionHeader = new Font(UITheme.FontMono, 9f, FontStyle.Bold);
		private static readonly Font _fontMoDiscoverIcon = new Font(UITheme.FontSegoe, 15.0f, FontStyle.Bold);
		private static readonly Font _fontBtnSave = new Font(UITheme.FontSegoe, 9.5f, FontStyle.Bold);
		private static readonly Font _fontBtnCancel = new Font(UITheme.FontSegoe, 9.5f);
		private static readonly Font _fontIconLabel = new Font(UITheme.FontSegoe, 10.5f);
		private static readonly Font _fontTextLabel = new Font(UITheme.FontSegoe, 9.5f, FontStyle.Regular);
		#endregion

		#region Nested Controls
		public class ModernToggleSwitch : Control
		{
			private static readonly Color _colorThumbDisabled = Color.FromArgb(90, 90, 95);
			private bool _checked;

			public bool Checked
			{
				get => _checked;
				set { _checked = value; Invalidate(); }
			}

			public event EventHandler? CheckedChanged;

			public ModernToggleSwitch()
			{
				this.Size = new Size(UITheme.Scale(45), UITheme.Scale(22));
				this.Cursor = Cursors.Hand;
				this.DoubleBuffered = true;
			}

			protected override void OnMouseClick(MouseEventArgs e)
			{
				base.OnMouseClick(e);
				if (!this.Enabled) return;
				Checked = !Checked;
				CheckedChanged?.Invoke(this, EventArgs.Empty);
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				Graphics g = e.Graphics;
				g.SmoothingMode = SmoothingMode.AntiAlias;
				g.Clear(this.Parent?.BackColor ?? UITheme.BgMain);

				Color bg = this.Enabled ? (Checked ? UITheme.ToggleActive : UITheme.ToggleInactive) : UITheme.ToggleDisabled;

				using GraphicsPath path = new GraphicsPath();
				int offset1 = UITheme.Scale(1);
				int offset2 = UITheme.Scale(2);
				int d = this.Height - offset2;

				path.AddArc(offset1, offset1, d, d, 90, 180);
				path.AddArc(this.Width - d - offset1, offset1, d, d, 270, 180);
				path.CloseAllFigures();

				using SolidBrush bgBrush = new SolidBrush(bg);
				g.FillPath(bgBrush, path);

				int thumbPad = UITheme.Scale(4);
				int thumbOffset = UITheme.Scale(6);
				int thumbSize = this.Height - thumbOffset;

				int thumbX = Checked ? (this.Width - thumbSize - thumbPad) : thumbPad;
				int thumbY = UITheme.Scale(3);

				using SolidBrush thumbBrush = new SolidBrush(this.Enabled ? UITheme.ToggleThumb : _colorThumbDisabled);
				g.FillEllipse(thumbBrush, thumbX, thumbY, thumbSize, thumbSize);
			}
		}
		#endregion

		#region Private Fields & Theme Colors
		private ModernToggleSwitch swLiteMode;
		private ModernToggleSwitch swCustomIcons;
		private ModernToggleSwitch swSyntaxHighlighting;
		private ModernToggleSwitch swLog;
		private ModernToggleSwitch swSearch;
		private ModernToggleSwitch swMoDiscover;
		private ModernToggleSwitch swWorkbenchSync;
		private ModernToggleSwitch swAutoLoad;
		private ModernToggleSwitch swAutoCompare;

		private Label lblLiteIcon, lblLiteText;
		private Label lblIconsIcon, lblIconsText;
		private Label lblSyntaxIcon, lblSyntaxText;
		private Label lblLogIcon, lblLogText;
		private Label lblSearchIcon, lblSearchText;
		private Label lblMoDiscoverIcon, lblMoDiscoverText;
		private Label lblWorkbenchSyncIcon, lblWorkbenchSyncText;
		private Label lblAutoLoadIcon, lblAutoLoadText;
		private Label lblAutoCompareIcon, lblAutoCompareText;

		private Button btnSave;
		private Button btnCancel;

		private readonly Color _colorLite = UITheme.LogWarning;
		private readonly Color _colorIcons = UITheme.SyntaxHighlight;
		private readonly Color _colorSyntax = UITheme.SyntaxString;
		private readonly Color _colorLog = UITheme.ToggleActive;
		private readonly Color _colorSearch = Color.FromArgb(86, 182, 194);
		private readonly Color _colorMoDiscover = Color.FromArgb(52, 152, 219);
		private readonly Color _colorWorkbenchSync = Color.FromArgb(142, 68, 173);
		private readonly Color _colorAutoLoad = UITheme.SyntaxBuiltin;
		private readonly Color _colorAutoCompare = Color.FromArgb(255, 123, 114);

		private readonly Color _textActive = UITheme.ToggleThumb;
		private readonly Color _textDisabled = Color.FromArgb(85, 85, 88);
		#endregion

		#region Constructor
		public SettingsForm()
		{
			this.Text = "Settings";
			this.StartPosition = FormStartPosition.CenterParent;
			this.BackColor = UITheme.BgMain;
			this.ForeColor = _textActive;
			this.FormBorderStyle = FormBorderStyle.FixedSingle;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.ShowIcon = false;

			InitializeCustomLayout();
			LoadCurrentSettings();
			UpdateDynamicUI();

			swLiteMode.CheckedChanged += (s, e) => UpdateDynamicUI();
			swSyntaxHighlighting.CheckedChanged += (s, e) => UpdateDynamicUI();
		}
		#endregion

		#region UI Layout & Construction
		private void InitializeCustomLayout()
		{
			Panel litePanel = new Panel
			{
				Location = new Point(0, 0),
				Size = new Size(UITheme.Scale(470), UITheme.Scale(85)),
				BackColor = _colorLiteBg,
			};

			Panel topBar = new Panel {
				Location = new Point(0, 0),
				Size = new Size(UITheme.Scale(470), UITheme.Scale(3)),
				BackColor = _colorLite
			};
			litePanel.Controls.Add(topBar);

			swLiteMode = new ModernToggleSwitch { Location = new Point(UITheme.Scale(25), UITheme.Scale(33)) };

			lblLiteIcon = new Label {
				Text = "\u2699\uFE0F",
				Location = new Point(UITheme.Scale(85), UITheme.Scale(26)),
				AutoSize = true,
				Font = _fontLiteIcon,
				ForeColor = _colorLite
			};

			lblLiteText = new Label
			{
				Text = "Lite Mode\nDisables advanced UI features to improve performance.",
				Location = new Point(UITheme.Scale(115), UITheme.Scale(26)),
				Size = new Size(UITheme.Scale(320), UITheme.Scale(40)),
				Font = _fontLiteText,
				ForeColor = _textActive
			};

			litePanel.Controls.AddRange(new Control[] { swLiteMode, lblLiteIcon, lblLiteText });
			this.Controls.Add(litePanel);

			int currentY = UITheme.Scale(105);

			AddSeparatorLine(ref currentY);
			currentY += UITheme.Scale(10);

			// ==========================================
			// 1. APPEARANCE & VIEW
			// ==========================================
			AddSectionHeader("APPEARANCE & VIEW", ref currentY);

			swCustomIcons = new ModernToggleSwitch();
			lblIconsIcon = CreateIconLabel("\U0001F4C1", _colorIcons);
			lblIconsText = CreateTextLabel("Enable Custom TreeView Node Icons");
			AddSettingRow(swCustomIcons, lblIconsIcon, lblIconsText, ref currentY);

			swSyntaxHighlighting = new ModernToggleSwitch();
			lblSyntaxIcon = CreateIconLabel("\U0001F4BB", _colorSyntax);
			lblSyntaxText = CreateTextLabel("Enable Syntax Highlighting");
			AddSettingRow(swSyntaxHighlighting, lblSyntaxIcon, lblSyntaxText, ref currentY);

			swLog = new ModernToggleSwitch();
			lblLogIcon = CreateIconLabel("\u263A", _colorLog);
			lblLogText = CreateTextLabel("Enable Search Log");
			AddSettingRow(swLog, lblLogIcon, lblLogText, ref currentY);

			currentY += UITheme.Scale(10);
			AddSeparatorLine(ref currentY);
			currentY += UITheme.Scale(15);

			// ==========================================
			// 2. SIDEBAR TOOLS
			// ==========================================
			AddSectionHeader("SIDEBAR TOOLS", ref currentY);

			swSearch = new ModernToggleSwitch();
			lblSearchIcon = CreateIconLabel("\U0001F50D", _colorSearch);
			lblSearchText = CreateTextLabel("Enable Content Search");
			AddSettingRow(swSearch, lblSearchIcon, lblSearchText, ref currentY);

			swMoDiscover = new ModernToggleSwitch();
			lblMoDiscoverIcon = CreateIconLabel("\u2295", _colorMoDiscover);
			lblMoDiscoverIcon.Font = _fontMoDiscoverIcon;
			lblMoDiscoverText = CreateTextLabel("Enable MoDiscover");
			AddSettingRow(swMoDiscover, lblMoDiscoverIcon, lblMoDiscoverText, ref currentY);
			lblMoDiscoverIcon.Top -= UITheme.Scale(2);

			swWorkbenchSync = new ModernToggleSwitch();
			lblWorkbenchSyncIcon = CreateIconLabel("\U0001F504", _colorWorkbenchSync);
			lblWorkbenchSyncText = CreateTextLabel("Enable Workbench NET Sync");
			AddSettingRow(swWorkbenchSync, lblWorkbenchSyncIcon, lblWorkbenchSyncText, ref currentY);

			currentY += UITheme.Scale(10);
			AddSeparatorLine(ref currentY);
			currentY += UITheme.Scale(15);

			// ==========================================
			// 3. WORKFLOW AUTOMATION
			// ==========================================
			AddSectionHeader("WORKFLOW AUTOMATION", ref currentY);

			swAutoLoad = new ModernToggleSwitch();
			lblAutoLoadIcon = CreateIconLabel("\u21BA", _colorAutoLoad);
			lblAutoLoadText = CreateTextLabel("Auto-load last loaded PAK or Folder on startup");
			AddSettingRow(swAutoLoad, lblAutoLoadIcon, lblAutoLoadText, ref currentY);

			swAutoCompare = new ModernToggleSwitch();
			lblAutoCompareIcon = CreateIconLabel("\u2696\uFE0F", _colorAutoCompare);
			lblAutoCompareText = CreateTextLabel("Auto-match and compare with external root folder");
			AddSettingRow(swAutoCompare, lblAutoCompareIcon, lblAutoCompareText, ref currentY);

			currentY += UITheme.Scale(20);

			btnSave = new Button
			{
				Text = "Save Changes",
				Location = new Point(UITheme.Scale(170), currentY),
				Size = new Size(UITheme.Scale(140), UITheme.Scale(32)),
				BackColor = UITheme.Accent,
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat,
				Font = _fontBtnSave,
				Cursor = Cursors.Hand
			};
			btnSave.FlatAppearance.BorderSize = 0;
			btnSave.FlatAppearance.MouseOverBackColor = UITheme.AccentHover;
			btnSave.Click += BtnSave_Click;

			btnCancel = new Button
			{
				Text = "Cancel",
				Location = new Point(UITheme.Scale(325), currentY),
				Size = new Size(UITheme.Scale(110), UITheme.Scale(32)),
				BackColor = UITheme.BgDark,
				ForeColor = _colorBtnCancelText,
				FlatStyle = FlatStyle.Flat,
				Font = _fontBtnCancel,
				Cursor = Cursors.Hand
			};
			btnCancel.FlatAppearance.BorderSize = 1;
			btnCancel.FlatAppearance.BorderColor = UITheme.BorderDefault;
			btnCancel.FlatAppearance.MouseOverBackColor = _colorBtnCancelHover;
			btnCancel.Click += (s, e) => this.Close();

			this.Controls.AddRange(new Control[] {
				swCustomIcons, lblIconsIcon, lblIconsText,
				swSyntaxHighlighting, lblSyntaxIcon, lblSyntaxText,
				swLog, lblLogIcon, lblLogText,
				swSearch, lblSearchIcon, lblSearchText,
				swMoDiscover, lblMoDiscoverIcon, lblMoDiscoverText,
				swWorkbenchSync, lblWorkbenchSyncIcon, lblWorkbenchSyncText,
				swAutoLoad, lblAutoLoadIcon, lblAutoLoadText,
				swAutoCompare, lblAutoCompareIcon, lblAutoCompareText,
				btnSave, btnCancel
			});

			this.Size = new Size(UITheme.Scale(470), currentY + UITheme.Scale(80));
		}

		private void AddSectionHeader(string title, ref int currentY)
		{
			Label lblSectionTitle = new Label
			{
				Text = title,
				Location = new Point(UITheme.Scale(22), currentY),
				AutoSize = true,
				Font = _fontSectionHeader,
				ForeColor = _colorSectionTitle
			};
			this.Controls.Add(lblSectionTitle);
			currentY += UITheme.Scale(30);
		}

		private void AddSettingRow(ModernToggleSwitch toggle, Label iconLabel, Label textLabel, ref int currentY)
		{
			toggle.Location = new Point(UITheme.Scale(25), currentY);

			int iconY = currentY + (toggle.Height - iconLabel.PreferredSize.Height) / 2;
			int textY = currentY + (toggle.Height - textLabel.PreferredSize.Height) / 2;

			iconLabel.Location = new Point(UITheme.Scale(85), iconY);
			textLabel.Location = new Point(UITheme.Scale(115), textY);

			currentY += UITheme.Scale(40);
		}

		private void AddSeparatorLine(ref int currentY)
		{
			Panel separator = new Panel {
				Location = new Point(UITheme.Scale(20), currentY),
				Size = new Size(UITheme.Scale(415), UITheme.Scale(1)),
				BackColor = UITheme.BgSelected
			};
			this.Controls.Add(separator);
			currentY += UITheme.Scale(5);
		}

		private Label CreateIconLabel(string icon, Color iconColor)
		{
			return new Label
			{
				Text = icon,
				AutoSize = true,
				Font = _fontIconLabel,
				ForeColor = iconColor,
				Tag = iconColor
			};
		}

		private Label CreateTextLabel(string text)
		{
			return new Label
			{
				Text = text,
				AutoSize = true,
				Font = _fontTextLabel,
				ForeColor = _textActive
			};
		}
		#endregion

		#region Settings Logic & Persistence
		private void LoadCurrentSettings()
		{
			swLiteMode.Checked = AppSettings.Current.LiteMode;
			swCustomIcons.Checked = AppSettings.Current.EnableCustomIcons;
			swSyntaxHighlighting.Checked = AppSettings.Current.EnableSyntaxHighlighting;
			swLog.Checked = AppSettings.Current.EnableLog;
			swSearch.Checked = AppSettings.Current.EnableSearch;
			swMoDiscover.Checked = AppSettings.Current.EnableMoDiscover;
			swWorkbenchSync.Checked = AppSettings.Current.EnableWorkbenchSync;

			swAutoLoad.Checked = AppSettings.Current.AutoLoadLast;
			swAutoCompare.Checked = AppSettings.Current.AutoCompareEnabled;
		}

		private void UpdateDynamicUI()
		{
			bool enableOthers = !swLiteMode.Checked;

			swCustomIcons.Enabled = enableOthers;
			swSyntaxHighlighting.Enabled = enableOthers;
			swLog.Enabled = enableOthers;
			swSearch.Enabled = enableOthers;
			swMoDiscover.Enabled = enableOthers;
			swWorkbenchSync.Enabled = enableOthers;

			lblIconsIcon.ForeColor = enableOthers ? (Color)lblIconsIcon.Tag! : _textDisabled;
			lblSyntaxIcon.ForeColor = enableOthers ? (Color)lblSyntaxIcon.Tag! : _textDisabled;
			lblLogIcon.ForeColor = enableOthers ? (Color)lblLogIcon.Tag! : _textDisabled;
			lblSearchIcon.ForeColor = enableOthers ? (Color)lblSearchIcon.Tag! : _textDisabled;
			lblMoDiscoverIcon.ForeColor = enableOthers ? (Color)lblMoDiscoverIcon.Tag! : _textDisabled;
			lblWorkbenchSyncIcon.ForeColor = enableOthers ? (Color)lblWorkbenchSyncIcon.Tag! : _textDisabled;

			lblIconsText.ForeColor = enableOthers ? _textActive : _textDisabled;
			lblSyntaxText.ForeColor = enableOthers ? _textActive : _textDisabled;
			lblLogText.ForeColor = enableOthers ? _textActive : _textDisabled;
			lblSearchText.ForeColor = enableOthers ? _textActive : _textDisabled;
			lblMoDiscoverText.ForeColor = enableOthers ? _textActive : _textDisabled;
			lblWorkbenchSyncText.ForeColor = enableOthers ? _textActive : _textDisabled;
		}

		private void BtnSave_Click(object? sender, EventArgs e)
		{
			AppSettings.Current.LiteMode = swLiteMode.Checked;
			AppSettings.Current.EnableCustomIcons = swCustomIcons.Checked;
			AppSettings.Current.EnableSyntaxHighlighting = swSyntaxHighlighting.Checked;
			AppSettings.Current.EnableLog = swLog.Checked;
			AppSettings.Current.EnableSearch = swSearch.Checked;
			AppSettings.Current.EnableMoDiscover = swMoDiscover.Checked;
			AppSettings.Current.EnableWorkbenchSync = swWorkbenchSync.Checked;

			AppSettings.Current.AutoLoadLast = swAutoLoad.Checked;
			AppSettings.Current.AutoCompareEnabled = swAutoCompare.Checked;

			AppSettings.Save();

			this.DialogResult = DialogResult.OK;
			this.Close();
		}
		#endregion
	}
}