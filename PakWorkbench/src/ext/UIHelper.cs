using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PakWorkbench.src.ext
{
	public static class UIHelper
	{
		// ==========================================
		// GLOBAL (CustomMessageBox)
		// ==========================================
		public static DialogResult CustomMessageBox(IWin32Window? owner, string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
		{
			using Form msgForm = new();
			float dpiScale = 1.0f;
			if (owner is Control ownerControl)
			{
				dpiScale = ownerControl.DeviceDpi / 96f;
			}
			else
			{
				using Form tempForm = new();
				dpiScale = tempForm.DeviceDpi / 96f;
			}
			if (dpiScale < 1.0f) dpiScale = 1.0f;

			msgForm.Text = title;
			msgForm.StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
			msgForm.FormBorderStyle = FormBorderStyle.FixedDialog;
			msgForm.MaximizeBox = false;
			msgForm.MinimizeBox = false;
			msgForm.BackColor = UITheme.BgMain;
			msgForm.ForeColor = Color.White;
			msgForm.ShowInTaskbar = false;
			msgForm.TopMost = true;

			string iconPrefix = "";
			if (icon == MessageBoxIcon.Error) iconPrefix = "❌ ";
			else if (icon == MessageBoxIcon.Warning) iconPrefix = "⚠️ ";
			else if (icon == MessageBoxIcon.Question) iconPrefix = "❓ ";
			else if (icon == MessageBoxIcon.Information) iconPrefix = "ℹ️ ";

			Label lblText = new() {
				Text = iconPrefix + text,
				Location = new Point((int)(25 * dpiScale), (int)(20 * dpiScale)),
				MaximumSize = new Size((int)(400 * dpiScale), 0),
				AutoSize = true,
				ForeColor = Color.LightGray,
				Font = UITheme.MainFont
			};
			msgForm.Controls.Add(lblText);

			int minButtonY = (int)(95 * dpiScale);
			int buttonY = Math.Max(lblText.Bottom + (int)(20 * dpiScale), minButtonY);

			DialogResult result = DialogResult.OK;

			if (buttons == MessageBoxButtons.YesNo)
			{
				Button btnYes = new() { Text = "Yes", Location = new Point((int)(110 * dpiScale), buttonY), Size = new Size((int)(110 * dpiScale), (int)(32 * dpiScale)), BackColor = UITheme.BgDark, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
				Button btnNo = new() { Text = "No", Location = new Point((int)(240 * dpiScale), buttonY), Size = new Size((int)(110 * dpiScale), (int)(32 * dpiScale)), BackColor = UITheme.BgDark, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
				btnYes.FlatAppearance.BorderColor = Color.Gray;
				btnNo.FlatAppearance.BorderColor = Color.Gray;

				btnYes.Click += (s, e) => { result = DialogResult.Yes; msgForm.DialogResult = DialogResult.Yes; msgForm.Close(); };
				btnNo.Click += (s, e) => { result = DialogResult.No; msgForm.DialogResult = DialogResult.No; msgForm.Close(); };

				msgForm.Controls.Add(btnYes);
				msgForm.Controls.Add(btnNo);
			}
			else
			{
				Button btnOk = new() { Text = "OK", Location = new Point((int)(175 * dpiScale), buttonY), Size = new Size((int)(110 * dpiScale), (int)(32 * dpiScale)), BackColor = UITheme.BgDark, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
				btnOk.FlatAppearance.BorderColor = Color.Gray;
				btnOk.Click += (s, e) => { result = DialogResult.OK; msgForm.DialogResult = DialogResult.OK; msgForm.Close(); };
				msgForm.Controls.Add(btnOk);
			}

			msgForm.ClientSize = new Size((int)(460 * dpiScale), buttonY + (int)(52 * dpiScale));

			if (owner != null) msgForm.ShowDialog(owner);
			else msgForm.ShowDialog();

			return result;
		}
	}

	// ==========================================
	// DARK THEME RENDERING
	// ==========================================
	public class DarkToolStripRenderer : ToolStripProfessionalRenderer
	{
		public DarkToolStripRenderer() : base(new DarkColorTable()) { }
	}

	public class DarkColorTable : ProfessionalColorTable
	{
		public override Color ToolStripBorder => Color.FromArgb(30, 30, 32);
		public override Color ToolStripGradientBegin => Color.FromArgb(30, 30, 32);
		public override Color ToolStripGradientEnd => Color.FromArgb(30, 30, 32);
		public override Color MenuBorder => Color.FromArgb(45, 45, 48);
		public override Color MenuItemBorder => Color.FromArgb(0, 122, 204);
		public override Color MenuItemSelected => Color.FromArgb(60, 60, 62);
		public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 62);
		public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 62);
		public override Color ToolStripDropDownBackground => Color.FromArgb(40, 40, 42);
		public override Color ImageMarginGradientBegin => Color.FromArgb(40, 40, 42);
		public override Color ImageMarginGradientMiddle => Color.FromArgb(40, 40, 42);
		public override Color ImageMarginGradientEnd => Color.FromArgb(40, 40, 42);

		public override Color ButtonSelectedGradientBegin => Color.FromArgb(60, 60, 62);
		public override Color ButtonSelectedGradientMiddle => Color.FromArgb(60, 60, 62);
		public override Color ButtonSelectedGradientEnd => Color.FromArgb(60, 60, 62);
		public override Color ButtonSelectedHighlight => Color.FromArgb(60, 60, 62);
		public override Color ButtonSelectedBorder => Color.FromArgb(0, 122, 204);

		public override Color ButtonPressedGradientBegin => Color.FromArgb(0, 122, 204);
		public override Color ButtonPressedGradientMiddle => Color.FromArgb(0, 122, 204);
		public override Color ButtonPressedGradientEnd => Color.FromArgb(0, 122, 204);
		public override Color ButtonPressedHighlight => Color.FromArgb(0, 122, 204);
		public override Color ButtonPressedBorder => Color.FromArgb(0, 122, 204);

		public override Color ButtonCheckedGradientBegin => Color.FromArgb(45, 45, 48);
		public override Color ButtonCheckedGradientMiddle => Color.FromArgb(45, 45, 48);
		public override Color ButtonCheckedGradientEnd => Color.FromArgb(45, 45, 48);
		public override Color ButtonCheckedHighlight => Color.FromArgb(45, 45, 48);
		public override Color ButtonCheckedHighlightBorder => Color.FromArgb(0, 122, 204);
		public override Color CheckBackground => Color.FromArgb(45, 45, 48);
		public override Color CheckPressedBackground => Color.FromArgb(0, 122, 204);
		public override Color CheckSelectedBackground => Color.FromArgb(60, 60, 62);
	}
}