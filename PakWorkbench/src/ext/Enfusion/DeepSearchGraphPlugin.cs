using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PakWorkbench.src;

namespace PakWorkbench.src.ext.Enfusion
{
	public class DeepSearchGraphPlugin
	{
		#region UI Components & State

		private Panel? graphOverlayPanel;
		private EnfusionGraphViewer? integratedGraphViewer;
		private Button? btnOpenGraph;
		private ProgressBar? indexingProgressBar;

		private int _currentDepth = 5;
		private System.Windows.Forms.Timer? debounceTimer;

		private Action<string>? _updateStatus;
		private Func<DeepSearchControl.SearchResultInfo?>? _getSelectedItem;
		private List<Pak> _loadedPaks = [];
		private Func<string, string>? _projectResolver;
		private bool _isIndexed = false;

		private bool _hasSearchResults = false;

		#endregion

		#region Initialization & Setup

		public void InitializeUI(Control mainControl, FlowLayoutPanel toolbarPanel, ContextMenuStrip contextMenu, Func<DeepSearchControl.SearchResultInfo?> getSelectedItem, Action<string> updateStatus)
		{
			_getSelectedItem = getSelectedItem;
			_updateStatus = updateStatus;

			float dpiScale = GetDpiScale(mainControl);

			SetupToolbarButton(toolbarPanel, dpiScale);
			SetupProgressBar(toolbarPanel, dpiScale);
			SetupContextMenu(contextMenu);
			SetupOverlayPanel(mainControl);
			SetupTimer();
		}

		private static float GetDpiScale(Control mainControl)
		{
			using Graphics g = mainControl.CreateGraphics();
			float scale = g.DpiX / 96.0f;
			return scale < 1.0f ? 1.0f : scale;
		}

		private void SetupToolbarButton(FlowLayoutPanel toolbarPanel, float dpiScale)
		{
			btnOpenGraph = new() {
				Text = "🌐 Graph View",
				Width = (int)(110 * dpiScale),
				Height = (int)(28 * dpiScale),
				BackColor = UITheme.Accent,
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat,
				Cursor = Cursors.Hand,
				Margin = new Padding((int)(5 * dpiScale), 0, 0, 0),
				Font = UITheme.MainFontBold,
				Enabled = false
			};
			btnOpenGraph.FlatAppearance.BorderSize = 0;
			btnOpenGraph.Click += async (s, e) => {
				if (btnOpenGraph != null) btnOpenGraph.Enabled = false;
				await EnsureIndexedAsync();
				await OpenGlobalGraphAsync();
				UpdateGraphButtonState(_getSelectedItem?.Invoke() != null);
			};
			toolbarPanel.Controls.Add(btnOpenGraph);
		}

		private void SetupProgressBar(FlowLayoutPanel toolbarPanel, float dpiScale)
		{
			indexingProgressBar = new() {
				Width = (int)(120 * dpiScale),
				Height = (int)(22 * dpiScale),
				Style = ProgressBarStyle.Continuous,
				Visible = false,
				Margin = new Padding((int)(10 * dpiScale), (int)(3 * dpiScale), 0, 0)
			};
			toolbarPanel.Controls.Add(indexingProgressBar);
		}

		private void SetupContextMenu(ContextMenuStrip contextMenu)
		{
			var viewGraphMenuItem = new ToolStripMenuItem("🌐 Visual Graph View For Selection");
			viewGraphMenuItem.Click += async (s, e) => {
				var info = _getSelectedItem?.Invoke();
				if (info != null) {
					await EnsureIndexedAsync();
					await OpenSelectionGraphAsync(info);
				}
			};
			contextMenu.Items.Add(viewGraphMenuItem);
		}

		private void SetupOverlayPanel(Control mainControl)
		{
			graphOverlayPanel = new() {
				Dock = DockStyle.Fill,
				Visible = false,
				BackColor = UITheme.BgPanel
			};

			integratedGraphViewer = new() { Dock = DockStyle.Fill };
			integratedGraphViewer.OnWebEvent += HandleWebEvent;

			graphOverlayPanel.Controls.Add(integratedGraphViewer);
			mainControl.Controls.Add(graphOverlayPanel);
			graphOverlayPanel.BringToFront();
		}

		private void SetupTimer()
		{
			debounceTimer = new() { Interval = 400 };
			debounceTimer.Tick += async (s, ev) => {
				debounceTimer.Stop();
				var info = _getSelectedItem?.Invoke();
				if (info != null && graphOverlayPanel != null && graphOverlayPanel.Visible) {
					await OpenSelectionGraphAsync(info);
				}
			};
		}

		#endregion

		#region Event Handlers & State Management

		private void HandleWebEvent(object? sender, WebEventMessage msg)
		{
			if (msg.Event.Equals("depthChanged", StringComparison.OrdinalIgnoreCase))
			{
				if (int.TryParse(msg.Value, out int newDepth))
				{
					_currentDepth = newDepth;
					debounceTimer?.Stop();
					debounceTimer?.Start();
				}
			}
			else if (msg.Event.Equals("close", StringComparison.OrdinalIgnoreCase))
			{
				_ = CloseOverlayAsync();
			}
		}

		public async Task CloseOverlayAsync()
		{
			if (graphOverlayPanel == null) return;

			if (graphOverlayPanel.InvokeRequired)
			{
				graphOverlayPanel.Invoke(() => graphOverlayPanel.Visible = false);
			}
			else
			{
				graphOverlayPanel.Visible = false;
			}

			if (integratedGraphViewer != null)
			{
				await integratedGraphViewer.ExecuteScriptAsync("if (typeof closeSidebar === 'function') { closeSidebar(); }");
			}
		}

		public void UpdatePaks(List<Pak> paks, Func<string, string>? projectResolver = null)
		{
			_loadedPaks = paks;
			_projectResolver = projectResolver;
			_isIndexed = false;
		}

		public void SetGraphButtonEnabled(bool isEnabled)
		{
			_hasSearchResults = isEnabled;
			UpdateGraphButtonState(_getSelectedItem?.Invoke() != null);
		}

		public void UpdateGraphButtonState(bool hasSelection)
		{
			bool shouldEnable = _hasSearchResults && hasSelection;
			if (btnOpenGraph != null)
			{
				if (btnOpenGraph.InvokeRequired)
				{
					btnOpenGraph.Invoke((MethodInvoker)delegate { btnOpenGraph.Enabled = shouldEnable; });
				}
				else
				{
					btnOpenGraph.Enabled = shouldEnable;
				}
			}
		}

		private void ProjectIds()
		{
			if (EnfusionAssetIndex.GlobalGraph?.Nodes == null || _projectResolver == null) return;

			foreach (var node in EnfusionAssetIndex.GlobalGraph.Nodes.Values)
			{
				if (string.IsNullOrEmpty(node.ProjectId) || node.ProjectId.Equals("unknown", StringComparison.OrdinalIgnoreCase))
				{
					if (!string.IsNullOrEmpty(node.Origin))
					{
						string projId = _projectResolver(node.Origin);
						if (!string.IsNullOrEmpty(projId))
						{
							node.ProjectId = projId;
						}
					}
				}
			}
		}

		private async Task EnsureIndexedAsync()
		{
			if (_isIndexed || _loadedPaks == null || _loadedPaks.Count == 0) return;

			if (btnOpenGraph != null) btnOpenGraph.Enabled = false;
			if (indexingProgressBar != null)
			{
				indexingProgressBar.Visible = true;
				indexingProgressBar.Value = 0;
			}

			try
			{
				await EnfusionAssetIndex.BuildIndexAsync(_loadedPaks, (status, current, total) =>
				{
					if (btnOpenGraph?.InvokeRequired == true)
						btnOpenGraph.Invoke((MethodInvoker)UpdateAction);
					else
						UpdateAction();

					void UpdateAction()
					{
						_updateStatus?.Invoke(" " + status);
						if (indexingProgressBar != null && total > 0)
						{
							int percent = (int)((double)current / total * 100);
							indexingProgressBar.Value = Math.Max(0, Math.Min(100, percent));
						}
					}
				}, ProjectIds);
				_isIndexed = true;
			}
			catch (Exception ex)
			{
				if (btnOpenGraph?.InvokeRequired == true)
					btnOpenGraph.Invoke((MethodInvoker)ErrorAction);
				else
					ErrorAction();

				void ErrorAction() => _updateStatus?.Invoke(" Graph Plugin Error: " + ex.Message);
			}
			finally
			{
				if (btnOpenGraph?.InvokeRequired == true)
					btnOpenGraph.Invoke((MethodInvoker)CleanupAction);
				else
					CleanupAction();

				void CleanupAction()
				{
					UpdateGraphButtonState(_getSelectedItem?.Invoke() != null);
					if (indexingProgressBar != null) indexingProgressBar.Visible = false;
				}
			}
		}

		#endregion

		#region Graph Operations

		private List<string> GetAllProjectIds()
		{
			HashSet<string> projs = new(StringComparer.OrdinalIgnoreCase);

			if (EnfusionAssetIndex.GlobalGraph?.Nodes != null)
			{
				foreach (var node in EnfusionAssetIndex.GlobalGraph.Nodes.Values)
				{
					if (!string.IsNullOrEmpty(node.ProjectId) && !node.ProjectId.Equals("unknown", StringComparison.OrdinalIgnoreCase))
					{
						projs.Add(node.ProjectId);
					}
				}
			}

			if (_loadedPaks != null)
			{
				foreach (var pak in _loadedPaks)
				{
					string pakPath = PakUtils.GetPakFilePath(pak);
					string pId = string.Empty;

					if (_projectResolver != null && !string.IsNullOrEmpty(pakPath))
					{
						pId = _projectResolver(pakPath);
					}

					if (string.IsNullOrEmpty(pId) || pId.Equals("unknown", StringComparison.OrdinalIgnoreCase))
					{
						pId = EnfusionAssetIndex.DetermineProjectId(pak, pakPath);
					}

					if (!string.IsNullOrEmpty(pId) && !pId.Equals("unknown", StringComparison.OrdinalIgnoreCase))
					{
						projs.Add(pId);
					}
				}
			}

			return [.. projs];
		}

		private async Task OpenGlobalGraphAsync()
		{
			if (graphOverlayPanel == null || integratedGraphViewer == null) return;

			var info = _getSelectedItem?.Invoke();
			if (info != null && (info.EntryFile != null || !string.IsNullOrEmpty(info.SearchTerm)))
			{
				await OpenSelectionGraphAsync(info);
			}
			else
			{
				graphOverlayPanel.Visible = true;
				graphOverlayPanel.BringToFront();
				integratedGraphViewer.LoadAndRenderGraph(new AssetGraph(), GetAllProjectIds());
				_updateStatus?.Invoke(" ⚠️ Please select a file from the list first to view its deep local connections.");
			}
		}

		private async Task OpenSelectionGraphAsync(DeepSearchControl.SearchResultInfo info)
		{
			if (graphOverlayPanel == null || integratedGraphViewer == null) return;

			graphOverlayPanel.Visible = true;
			graphOverlayPanel.BringToFront();

			integratedGraphViewer.LoadAndRenderGraph(new AssetGraph(), GetAllProjectIds());

			string? fileId = info.EntryFile?.name?.ToLower();
			if (!string.IsNullOrEmpty(fileId))
			{
				await Task.Run(() => EnfusionAssetIndex.EnsureFileIndexed(fileId, _loadedPaks));
			}

			string? targetId = null;
			if (info.EntryFile != null && !string.IsNullOrEmpty(info.EntryFile.name))
				targetId = System.IO.Path.GetFileName(info.EntryFile.name).ToLower();
			else if (!string.IsNullOrEmpty(info.SearchTerm))
				targetId = System.IO.Path.GetFileName(info.SearchTerm).ToLower();

			if (targetId == null)
			{
				integratedGraphViewer.LoadAndRenderGraph(new AssetGraph(), GetAllProjectIds());
				return;
			}

			string? safeSearchTerm = info.SearchTerm;
			int maxDepth = _currentDepth;

			AssetGraph graphToRender = EnfusionAssetIndex.GlobalGraph.BuildSubGraph(targetId, maxDepth, safeSearchTerm);

			if (graphToRender == null || graphToRender.Nodes.IsEmpty)
			{
				integratedGraphViewer.LoadAndRenderGraph(new AssetGraph(), GetAllProjectIds());
				MessageBox.Show($"This asset '{targetId}' has no deep links defined.", "No Graph Data", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			integratedGraphViewer.LoadAndRenderGraph(graphToRender, GetAllProjectIds());
			await integratedGraphViewer.SetHtmlDepth(_currentDepth);
			_updateStatus?.Invoke($" 📂 Displaying clean targeted tree for: {targetId} (Depth: {maxDepth})");
		}

		#endregion
	}

	public class EnfusionWebEventArgs : EventArgs
	{
		public string Event { get; set; } = string.Empty;
		public string Value { get; set; } = string.Empty;
	}
}