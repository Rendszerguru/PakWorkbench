using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using PakWorkbench.src.ext.Workbench;

namespace PakWorkbench.src.ext
{
	public partial class MoDiscoverControl : UserControl
	{
		#region MoDiscover Controls & State

		public WebView2 ModiscoverBrowser { get; private set; } = null!;

		private Button btnWebBack = null!;
		private Button btnWebForward = null!;

		private bool _hasClickedConsent = false;

		private ViewerForm _viewerForm = null!;
		private Control _sidebarPanel = null!;
		private Control _btnNavWorkbenchSync = null!;
		private CustomTreeView _treeView = null!;
		private Button _btnNavMoDiscover = null!;

		#endregion

		public MoDiscoverControl()
		{
			ModiscoverBrowser = new WebView2 { Dock = DockStyle.Fill };
			this.Controls.Add(ModiscoverBrowser);
		}

		public void Initialize(ViewerForm viewerForm, Control sidebarPanel, Control btnNavWorkbenchSync, CustomTreeView treeView, Button btnNavMoDiscover)
		{
			_viewerForm = viewerForm;
			_sidebarPanel = sidebarPanel;
			_btnNavWorkbenchSync = btnNavWorkbenchSync;
			_treeView = treeView;
			_btnNavMoDiscover = btnNavMoDiscover;

			InitializeWebView();
		}

		#region WebView2 Initialization & Navigation Controls

		private async void InitializeWebView()
		{
			CreateNavigationButtons();

			await ModiscoverBrowser.EnsureCoreWebView2Async();

			ModiscoverBrowser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;

			ModiscoverBrowser.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
			ModiscoverBrowser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

			ModiscoverBrowser.Source = new Uri("https://modiscover.eu");
		}

		private void CreateNavigationButtons()
		{
			Control targetContainer = _sidebarPanel;

			int btnWidth = this.LogicalToDeviceUnits(18);
			int btnHeight = this.LogicalToDeviceUnits(32);
			int spacing = this.LogicalToDeviceUnits(2);

			int marginFromSyncBtn = this.LogicalToDeviceUnits(8);

			int totalWidth = (btnWidth * 2) + spacing;
			int startX = (targetContainer.Width - totalWidth) / 2;

			int startY = (_btnNavWorkbenchSync != null)
				? _btnNavWorkbenchSync.Bottom + marginFromSyncBtn
				: this.LogicalToDeviceUnits(170);

			void SetupNavButton(Button btn, string text)
			{
				btn.Text = text;
				btn.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
				btn.Size = new Size(btnWidth, btnHeight);
				btn.Enabled = false;
				btn.Visible = false;
				btn.FlatStyle = FlatStyle.Flat;
				btn.BackColor = UITheme.BgDark;
				btn.ForeColor = UITheme.ToggleInactive;
				btn.Cursor = Cursors.Hand;
				btn.FlatAppearance.BorderSize = 1;
				btn.FlatAppearance.BorderColor = UITheme.BgDark;

				btn.Paint += (s, pe) =>
				{
					var b = (Button)s!;
					pe.Graphics.Clear(b.BackColor);

					using var pen = new Pen(b.FlatAppearance.BorderColor);
					pe.Graphics.DrawRectangle(pen, 0, 0, b.Width - 1, b.Height - 1);

					using var brush = new SolidBrush(b.ForeColor);
					var sf = new StringFormat
					{
						Alignment = StringAlignment.Center,
						LineAlignment = StringAlignment.Center
					};
					pe.Graphics.DrawString(b.Text, b.Font, brush, new RectangleF(0, 0, b.Width, b.Height), sf);
				};
			}

			btnWebBack = new Button { Location = new Point(startX, startY) };
			SetupNavButton(btnWebBack, "‹");
			btnWebBack.Click += BtnWebBack_Click;

			btnWebForward = new Button { Location = new Point(startX + btnWidth + spacing, startY) };
			SetupNavButton(btnWebForward, "›");
			btnWebForward.Click += BtnWebForward_Click;

			targetContainer.Controls.Add(btnWebBack);
			targetContainer.Controls.Add(btnWebForward);
			btnWebBack.BringToFront();
			btnWebForward.BringToFront();
		}

		public void ShowMoDiscoverNavigationButtons(bool show)
		{
			if (btnWebBack != null && btnWebForward != null)
			{
				if (show && _btnNavWorkbenchSync != null)
				{
					int btnWidth = this.LogicalToDeviceUnits(18);
					int spacing = this.LogicalToDeviceUnits(2);
					int marginFromSyncBtn = this.LogicalToDeviceUnits(8);

					int totalWidth = (btnWidth * 2) + spacing;
					int startX = (_sidebarPanel.Width - totalWidth) / 2;
					int startY = _btnNavWorkbenchSync.Bottom + marginFromSyncBtn;

					btnWebBack.Location = new Point(startX, startY);
					btnWebForward.Location = new Point(startX + btnWidth + spacing, startY);

					btnWebBack.BringToFront();
					btnWebForward.BringToFront();
				}

				btnWebBack.Visible = show;
				btnWebForward.Visible = show;

				if (show)
				{
					CheckPagePaginationStateAsync();
				}
			}
		}

		private void UpdateNavButtonStates(bool canGoBack, bool canGoForward)
		{
			if (btnWebBack == null || btnWebForward == null) return;

			btnWebBack.Enabled = canGoBack;
			btnWebBack.BackColor = canGoBack ? UITheme.BgSelected : UITheme.BgDark;
			btnWebBack.ForeColor = canGoBack ? Color.White : UITheme.ToggleInactive;
			btnWebBack.FlatAppearance.BorderColor = canGoBack ? UITheme.BorderDefault : UITheme.BgDark;

			btnWebForward.Enabled = canGoForward;
			btnWebForward.BackColor = canGoForward ? UITheme.BgSelected : UITheme.BgDark;
			btnWebForward.ForeColor = canGoForward ? Color.White : UITheme.ToggleInactive;
			btnWebForward.FlatAppearance.BorderColor = canGoForward ? UITheme.BorderDefault : UITheme.BgDark;
		}

		private async void CheckPagePaginationStateAsync()
		{
			if (ModiscoverBrowser?.CoreWebView2 == null) return;

			string script = @"
				(function() {
					var buttons = Array.from(document.querySelectorAll('button, a'));
					var canPageBack = false;
					var canPageForward = false;

					var activeBtn = document.querySelector('[aria-current=""page""], .active, button.active');
					var currentPage = activeBtn ? parseInt(activeBtn.textContent.trim(), 10) : null;

					if (currentPage !== null && !isNaN(currentPage)) {
						canPageBack = buttons.some(b => parseInt(b.textContent.trim(), 10) === currentPage - 1);
						canPageForward = buttons.some(b => parseInt(b.textContent.trim(), 10) === currentPage + 1);
					} else {
						if (buttons.some(b => b.textContent.trim() === '1')) {
							canPageForward = buttons.some(b => b.textContent.trim() === '2' || b.textContent.trim() === '>' || b.textContent.trim() === '›');
						}
					}

					return {
						canBack: canPageBack || " + (ModiscoverBrowser.CanGoBack ? "true" : "false") + @",
						canForward: canPageForward || " + (ModiscoverBrowser.CanGoForward ? "true" : "false") + @"
					};
				})();
			";

			try
			{
				string jsonResult = await ModiscoverBrowser.CoreWebView2.ExecuteScriptAsync(script);
				if (!string.IsNullOrEmpty(jsonResult) && jsonResult != "null")
				{
					using var doc = System.Text.Json.JsonDocument.Parse(jsonResult);
					bool canBack = doc.RootElement.GetProperty("canBack").GetBoolean();
					bool canForward = doc.RootElement.GetProperty("canForward").GetBoolean();
					UpdateNavButtonStates(canBack, canForward);
				}
				else
				{
					UpdateNavButtonStates(ModiscoverBrowser.CanGoBack, ModiscoverBrowser.CanGoForward);
				}
			}
			catch
			{
				UpdateNavButtonStates(ModiscoverBrowser.CanGoBack, ModiscoverBrowser.CanGoForward);
			}
		}

		#endregion

		#region Event Handlers & Page Navigation

		private void CoreWebView2_HistoryChanged(object? _, object _1)
		{
			if (btnWebBack == null || !btnWebBack.Visible) return;

			CheckPagePaginationStateAsync();
		}

		private async void BtnWebBack_Click(object? _, EventArgs _1)
		{
			if (ModiscoverBrowser?.CoreWebView2 == null) return;

			string script = @"
				(function() {
					var activeBtn = document.querySelector('[aria-current=""page""], .active, button.active');
					if (activeBtn) {
						var currentPage = parseInt(activeBtn.textContent.trim(), 10);
						if (!isNaN(currentPage) && currentPage > 1) {
							var targetPage = currentPage - 1;
							var buttons = Array.from(document.querySelectorAll('button, a'));
							var targetBtn = buttons.find(b => parseInt(b.textContent.trim(), 10) === targetPage);
							if (targetBtn) {
								targetBtn.click();
								return true;
							}
						}
					}
					return false;
				})();
			";

			string result = await ModiscoverBrowser.CoreWebView2.ExecuteScriptAsync(script);

			if (result == "false" && ModiscoverBrowser.CanGoBack)
			{
				ModiscoverBrowser.GoBack();
			}

			await Task.Delay(200);
			CheckPagePaginationStateAsync();
		}

		private async void BtnWebForward_Click(object? sender, EventArgs e)
		{
			if (ModiscoverBrowser?.CoreWebView2 == null) return;

			string script = @"
				(function() {
					var activeBtn = document.querySelector('[aria-current=""page""], .active, button.active');
					if (activeBtn) {
						var currentPage = parseInt(activeBtn.textContent.trim(), 10);
						if (!isNaN(currentPage)) {
							var targetPage = currentPage + 1;
							var buttons = Array.from(document.querySelectorAll('button, a'));
							var targetBtn = buttons.find(b => parseInt(b.textContent.trim(), 10) === targetPage);
							if (targetBtn) {
								targetBtn.click();
								return true;
							}
						}
					}
					return false;
				})();
			";

			string result = await ModiscoverBrowser.CoreWebView2.ExecuteScriptAsync(script);

			if (result == "false" && ModiscoverBrowser.CanGoForward)
			{
				ModiscoverBrowser.GoForward();
			}

			await Task.Delay(200);
			CheckPagePaginationStateAsync();
		}

		private async void CoreWebView2_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
		{
			CheckPagePaginationStateAsync();

			foreach (int delay in new[] { 500, 1000, 2000, 3000 })
			{
				await Task.Delay(delay);
				CheckPagePaginationStateAsync();
			}

			if (!e.IsSuccess) return;
			if (_hasClickedConsent) return;
			_hasClickedConsent = true;

			string autoClickScript = @"
				(function() {
					function clickConsentButtons() {
						var buttons = Array.from(document.querySelectorAll('button, a, div[role=""button""]'));

						var cookieBtn = buttons.find(b => b.textContent && b.textContent.includes('Understood'));
						if (cookieBtn) cookieBtn.click();

						var loadImgBtn = buttons.find(b => b.textContent && b.textContent.includes('Load Images'));
						if (loadImgBtn) loadImgBtn.click();
					}

					clickConsentButtons();

					var observer = new MutationObserver(function(mutations) {
						clickConsentButtons();
					});

					observer.observe(document.body, { childList: true, subtree: true });

					setTimeout(function() { observer.disconnect(); }, 2000);
				})();
			";

			await ModiscoverBrowser.CoreWebView2.ExecuteScriptAsync(autoClickScript);
		}

		#endregion

		#region Mod Dependencies & Graph Integration

		public void BtnViewDependencies_Click(object? sender, EventArgs e)
		{
			if (_treeView == null || _viewerForm == null) return;

			var selectedNode = _treeView.SelectedNode;
			if (selectedNode == null || selectedNode.Parent != null) return;

			string? modId = GetModIdFromNode(selectedNode);
			if (string.IsNullOrEmpty(modId)) return;

			string targetUrl = $"https://modiscover.eu/mod/{modId}";

			try
			{
				if (this.Parent is Panel parentPanel)
				{
					_viewerForm.SwitchView(parentPanel, _btnNavMoDiscover);
				}

				var webView = ModiscoverBrowser;

				if (webView?.CoreWebView2 is { } coreWebView)
				{
					async void OnNavigationCompleted(object? s, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs ev)
					{
						coreWebView.NavigationCompleted -= OnNavigationCompleted;

						if (!ev.IsSuccess) return;

						string jsCode = @"
					(function() {
						function clickConsentButtons() {
							var buttons = Array.from(document.querySelectorAll('button'));

							var cookieBtn = buttons.find(b => b.textContent.includes('Understood'));
							if (cookieBtn) cookieBtn.click();

							var loadImgBtn = buttons.find(b => b.textContent.includes('Load Images'));
							if (loadImgBtn) loadImgBtn.click();
						}

						function tryClickGraph() {
							var buttons = Array.from(document.querySelectorAll('button, a, div[role=""button""]'));
							var target = buttons.find(b => {
								var txt = b.textContent.toLowerCase();
								return txt.includes('graph') || txt.includes('dependencies');
							});

							if (target) {
								target.click();
								return true;
							}
							return false;
						}

						clickConsentButtons();
						if (tryClickGraph()) return;

						var observer = new MutationObserver(function(mutations, obs) {
							clickConsentButtons();
							if (tryClickGraph()) {
								obs.disconnect();
							}
						});

						observer.observe(document.body, { childList: true, subtree: true });

						setTimeout(function() { observer.disconnect(); }, 10000);
					})();
				";

						try
						{
							await coreWebView.ExecuteScriptAsync(jsCode);
						}
						catch (Exception ex)
						{
							System.Diagnostics.Debug.WriteLine($"JS injection error: {ex.Message}");
						}
					}

					coreWebView.NavigationCompleted += OnNavigationCompleted;
					coreWebView.Navigate(targetUrl);
				}
			}
			catch (Exception ex)
			{
				UIHelper.CustomMessageBox(this, $"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private string? GetModIdFromNode(TreeNode node)
		{
			if (node.Tag is string tagString && !string.IsNullOrEmpty(tagString))
			{
				string? foundInTag = ExtractHex16(tagString);
				if (foundInTag != null) return foundInTag;
			}

			string cleanName = node.Text.Replace("📁 ", "").Trim();

			string? foundInName = ExtractHex16(cleanName);
			if (foundInName != null) return foundInName;

			var loadedPaksEnum = (System.Collections.IEnumerable)_viewerForm.loadedPaks;
			var matchingPak = loadedPaksEnum.Cast<dynamic>().FirstOrDefault(p => {
				return string.Equals((string)_viewerForm.GetResolvedProjectId(p.name), cleanName, StringComparison.OrdinalIgnoreCase);
			});

			if (matchingPak != null)
			{
				string? foundInPath = ExtractHex16((string)matchingPak.name);
				if (foundInPath != null) return foundInPath;
			}

			return null;
		}

		[System.Text.RegularExpressions.GeneratedRegex(@"[0-9A-Fa-f]{16}")]
		private static partial System.Text.RegularExpressions.Regex Hex16Regex();

		private static string? ExtractHex16(string input)
		{
			if (string.IsNullOrEmpty(input)) return null;

			var match = Hex16Regex().Match(input);
			return match.Success ? match.Value : null;
		}

		#endregion
	}
}
