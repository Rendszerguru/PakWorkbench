using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PakWorkbench;

namespace PakWorkbench.src.ext.Enfusion
{
	#region Models
	public class WebEventMessage
	{
		public string Event { get; set; } = "";
		public string Value { get; set; } = "";
	}
	#endregion

	public partial class EnfusionGraphViewer : UserControl
	{
		#region Fields & Properties
		// ==========================================
		// Fields & Properties
		// ==========================================
		private WebView2 webView = null!;
		private bool isWebViewReady = false;
		private string? delayedJsonData = null;

		private static readonly JsonSerializerOptions _jsonOptions = new()
		{
			PropertyNameCaseInsensitive = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};

		public event EventHandler<WebEventMessage>? OnWebEvent;
		#endregion

		#region Constructor & Initialization
		// ==========================================
		// Constructor & Initialization
		// ==========================================
		public EnfusionGraphViewer()
		{
			this.AutoScaleMode = AutoScaleMode.Dpi;
			InitializeComponent();
			_ = InitializeWebViewAsync();
		}

		private void InitializeComponent()
		{
			this.webView = new WebView2();
			((System.ComponentModel.ISupportInitialize)(this.webView)).BeginInit();
			this.SuspendLayout();

			this.webView.Dock = DockStyle.Fill;
			this.webView.Name = "webView";

			this.Controls.Add(this.webView);
			this.Name = "EnfusionGraphViewer";

			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.Size = new System.Drawing.Size(800, 600);

			((System.ComponentModel.ISupportInitialize)(this.webView)).EndInit();
			this.ResumeLayout(false);
		}

		private async Task InitializeWebViewAsync()
		{
			try
			{
				await webView.EnsureCoreWebView2Async(null);
				AttachWebViewEvents();
				LoadHtmlContent();
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error initializing WebView: {ex.Message}", "Init Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}
		#endregion

		#region Event Handlers
		// ==========================================
		// Event Handlers
		// ==========================================
		private void AttachWebViewEvents()
		{
			webView.CoreWebView2.WebMessageReceived += HandleWebMessageReceived;

			webView.NavigationCompleted += (s, e) =>
			{
				System.Diagnostics.Debug.WriteLine("WebView NavigationCompleted fired (Waiting for JS READY signal...).");
			};
		}

		private void HandleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
		{
			try
			{
				string? rawStr = null;
				try { rawStr = e.TryGetWebMessageAsString(); } catch { }

				if (rawStr == "READY")
				{
					HandleReadySignal();
					return;
				}

				ProcessJsonWebMessage(e.WebMessageAsJson);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("Error processing WebMessage: " + ex.Message);
			}
		}

		private async void HandleReadySignal()
		{
			System.Diagnostics.Debug.WriteLine("C# -> WebMessageReceived: READY received. JS is fully loaded!");
			isWebViewReady = true;

			if (delayedJsonData != null)
			{
				System.Diagnostics.Debug.WriteLine("C# -> Rendering delayed JSON data...");
				await RenderJsonData(delayedJsonData);
				delayedJsonData = null;
			}
			OnWebEvent?.Invoke(this, new WebEventMessage { Event = "ready" });
		}

		private void ProcessJsonWebMessage(string json)
		{
			if (string.IsNullOrEmpty(json)) return;

			WebEventMessage? msg = null;
			try
			{
				if (json.Length > 1 && json[0] == '"' && json[^1] == '"')
				{
					json = JsonSerializer.Deserialize<string>(json, _jsonOptions) ?? json;
				}

				msg = JsonSerializer.Deserialize<WebEventMessage>(json, _jsonOptions);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("WebMessage parse error: " + ex.Message);
			}

			if (msg != null)
			{
				if (string.Equals(msg.Event, "ready", StringComparison.OrdinalIgnoreCase))
				{
					HandleReadySignal();
				}
				OnWebEvent?.Invoke(this, msg);
			}
		}
		#endregion

		#region Public API
		// ==========================================
		// Public API
		// ==========================================
		public async Task SetHtmlDepth(int depth)
		{
			if (!isWebViewReady) return;

			if (this.InvokeRequired)
			{
				this.BeginInvoke(new Action(async () => await SetHtmlDepth(depth)));
				return;
			}

			try
			{
				await webView.ExecuteScriptAsync($"setDepth({depth});");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"SetHtmlDepth Error: {ex.Message}");
			}
		}

		public void LoadAndRenderGraph(dynamic graph, List<string>? allProjects = null)
		{
			System.Diagnostics.Debug.WriteLine("C# -> LoadAndRenderGraph called!");

			if (graph?.Nodes == null)
			{
				System.Diagnostics.Debug.WriteLine("C# WARNING: graph or graph.Nodes is NULL!");
				return;
			}

			var nodesMap = (ConcurrentDictionary<string, AssetNode>)graph.Nodes;
			System.Diagnostics.Debug.WriteLine($"C# -> Processing graph with {nodesMap.Count} nodes...");

			var elements = new List<object>();
			if (!nodesMap.IsEmpty)
			{
				var nodeIds = new HashSet<string>();
				var addedMissingNodes = new HashSet<string>();

				elements.AddRange(ProcessNodes(nodesMap, nodeIds));
				elements.AddRange(ProcessEdges(nodesMap, nodeIds, addedMissingNodes));
			}

			var payload = new
			{
				nodes = elements,
				allProjects = (object?)allProjects ?? Array.Empty<string>()
			};

			string jsonData = JsonSerializer.Serialize(payload, _jsonOptions);
			HandlePayloadDelivery(jsonData);
		}
		#endregion

		#region Graph Processing Helpers
		// ==========================================
		// Graph Processing Helpers
		// ==========================================
		private static List<object> ProcessNodes(ConcurrentDictionary<string, AssetNode> nodesMap, HashSet<string> nodeIds)
		{
			var nodes = new List<object>();
			foreach (var node in nodesMap.Values)
			{
				if (node is null) continue;

				string safeId = node.Id ?? string.Empty;
				string safeName = node.Name ?? string.Empty;
				string safeType = node.Type ?? "Unknown";

				nodeIds.Add(safeId);

				bool isSearchTarget = false;
				bool isTargetWav = false;
				string nodeOrigin = string.Empty;
				string nodeProjectId = "unknown";

				try { isSearchTarget = node.IsSearchTarget; } catch { }
				try { isTargetWav = node.IsTargetWav; } catch { }
				try { nodeOrigin = node.Origin ?? string.Empty; } catch { }
				try { nodeProjectId = node.ProjectId ?? "unknown"; } catch { }

				int borderWidth = isSearchTarget || isTargetWav ? 4 : 1;
				string borderColor = isSearchTarget ? "#FF3366" : (isTargetWav ? "#00FF88" : "#222222");

				string[] nodeTags = ExtractTags(node);
				string[] safeEmbeddedElements = ExtractEmbeddedElements(node);

				string categoryString = node.Category.ToString();
				nodes.Add(new
				{
					data = new
					{
						id = safeId,
						name = safeName,
						label = $"{safeName}\n[{safeType}]",
						type = safeType,
						category = categoryString,
						color = GetColorForNodeType(safeType),
						borderWidth,
						borderColor,
						isSearchTarget,
						isTargetWav,
						tags = nodeTags,
						embeddedElements = safeEmbeddedElements,
						origin = nodeOrigin,
						projectId = nodeProjectId
					}
				});
			}
			return nodes;
		}

		private static List<object> ProcessEdges(ConcurrentDictionary<string, AssetNode> nodesMap, HashSet<string> nodeIds, HashSet<string> addedMissingNodes)
		{
			var edges = new List<object>();
			foreach (var node in nodesMap.Values)
			{
				if (node?.Edges == null) continue;

				foreach (var edge in node.Edges)
				{
					if (edge == null) continue;

					string targetId = (string?)edge.Target?.Id ?? string.Empty;

					if (string.IsNullOrEmpty(targetId)) continue;

					if (!nodeIds.Contains(targetId) && !addedMissingNodes.Contains(targetId))
					{
						addedMissingNodes.Add(targetId);
						edges.Add(CreateMissingNode(targetId));
					}

					bool isGlowing = false;
					if (nodesMap.TryGetValue(targetId, out AssetNode? targetNode) && targetNode is not null)
					{
						try { isGlowing = targetNode.IsSearchTarget || targetNode.IsTargetWav; } catch { }
					}

					string safeRelation = (string?)edge.Relation ?? "Unknown";

					edges.Add(new
					{
						data = new
						{
							id = $"{node.Id}_{targetId}_{safeRelation}",
							source = node.Id,
							target = targetId,
							label = safeRelation,
							isGlowing
						}
					});
				}
			}
			return edges;
		}

		private static object CreateMissingNode(string targetId)
		{
			return new
			{
				data = new
				{
					id = targetId,
					name = $"MISSING ({targetId})",
					label = $"MISSING\n[{targetId}]",
					type = "MISSING",
					category = "Unknown",
					color = "#111111",
					borderWidth = 2,
					borderColor = "#FF1744",
					isSearchTarget = false,
					isTargetWav = false,
					tags = new string[] { "MISSING", "BROKEN" },
					embeddedElements = Array.Empty<object>(),
					origin = "Unknown",
					projectId = "unknown"
				}
			};
		}

		private static string[] ExtractTags(AssetNode node)
		{
			if (node.Tags != null)
			{
				object lockTarget = (object)node.Tags;
				lock (lockTarget)
				{
					return [.. ((IEnumerable)node.Tags).Cast<object>().Select(x => x?.ToString() ?? string.Empty)];
				}
			}
			return [];
		}

		private static string[] ExtractEmbeddedElements(AssetNode node)
		{
			if (node.EmbeddedElements != null)
			{
				object lockTarget = (object)node.EmbeddedElements;
				lock (lockTarget)
				{
					if (node.EmbeddedElements is IEnumerable<string> stringEnum)
					{
						return [.. stringEnum];
					}
					else if (node.EmbeddedElements is IEnumerable genericEnum)
					{
						var list = new List<string>();
						foreach (var item in genericEnum)
						{
							if (item != null)
							{
								list.Add(item.ToString() ?? string.Empty);
							}
						}
						return [.. list];
					}
				}
			}
			return [];
		}

		private static string GetColorForNodeType(string type)
		{
			return type switch
			{
				"ET" => "#4FC3F7",
				"CT" => "#0288D1",
				"CONF" => "#26A69A",
				"ENTITY" => "#B2DFDB",

				"XOB" => "#4CAF50",
				"EMAT" => "#8BC34A",
				"SHADER" => "#CDDC39",
				"EDDS" => "#1B5E20",

				"SOUND" => "#9C27B0",
				"WAV" => "#BA68C8",
				"ACP" => "#E1BEE7",

				"ANM" => "#FF9800",
				"PAP" => "#FFA726",
				"ASI" => "#FFB74D",
				"BT" => "#FFCC80",
				"PTC" => "#FFE082",
				"SIGA" => "#FFD700",

				"LAYOUT" => "#FF1744",
				"WIDGET" => "#E040FB",
				"IMAGESET" => "#FF8A80",

				_ => "#505054"
			};
		}
		#endregion

		#region JS Execution & Delivery
		// ==========================================
		// JS Execution & Delivery
		// ==========================================
		private async void HandlePayloadDelivery(string jsonData)
		{
			if (!isWebViewReady)
			{
				System.Diagnostics.Debug.WriteLine("C# WARNING: WebView not ready yet. Storing data payload in delayed cache.");
				delayedJsonData = jsonData;
			}
			else
			{
				System.Diagnostics.Debug.WriteLine("C# SUCCESS: WebView is active. Pushing data payload now...");
				await RenderJsonData(jsonData);
			}
		}

		private void LoadHtmlContent()
		{
			webView.NavigateToString(WebAssets.HtmlContent);
		}

		private async Task RenderJsonData(string jsonData)
		{
			if (this.InvokeRequired)
			{
				this.BeginInvoke(new Action(async () => await RenderJsonData(jsonData)));
				return;
			}

			try
			{
				string scriptToExecute = WebAssets.RenderJsonScriptTemplate.Replace("{jsonData}", jsonData);
				await webView.ExecuteScriptAsync(scriptToExecute);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"RenderJsonData Error: {ex.Message}");
			}
		}

		public async Task ExecuteScriptAsync(string script)
		{
			if (webView != null && webView.CoreWebView2 != null)
			{
				await webView.CoreWebView2.ExecuteScriptAsync(script);
			}
		}
		#endregion

		#region Static Web Assets (HTML/JS Constants)
		// ==========================================
		// Static Web Assets (HTML/JS Constants)
		// ==========================================
		private static class WebAssets
		{
			public const string HtmlContent = @"
		<!DOCTYPE html>
		<html>
		<head>
			<script src='https://cdnjs.cloudflare.com/ajax/libs/cytoscape/3.26.0/cytoscape.min.js'></script>

			<!-- Layout Extensions -->
			<script src='https://unpkg.com/dagre@0.7.4/dist/dagre.js'></script>
			<script src='https://cdn.jsdelivr.net/npm/cytoscape-dagre@2.5.0/cytoscape-dagre.min.js'></script>

			<!-- Radial Context Menu (cxtmenu) Extension -->
			<script src='https://cdn.jsdelivr.net/npm/cytoscape-cxtmenu@3.6.0/cytoscape-cxtmenu.min.js'></script>

			<style>
				body {
					margin: 0;
					padding: 0;
					background-color: #0b0c10;
					background-image:
						linear-gradient(rgba(255, 255, 255, 0.04) 1px, transparent 1px),
						linear-gradient(90deg, rgba(255, 255, 255, 0.04) 1px, transparent 1px),
						linear-gradient(rgba(255, 255, 255, 0.01) 1px, transparent 1px),
						linear-gradient(90deg, rgba(255, 255, 255, 0.01) 1px, transparent 1px);
					background-size: 100px 100px, 100px 100px, 20px 20px, 20px 20px;
					color: white;
					font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
					overflow: hidden;
				}

				#cy { width: 100%; height: 100%; position: absolute; top: 0; left: 0; z-index: 1; }

				.toolbar {
					position: absolute; top: 1rem; right: 1rem; z-index: 10;
					background: rgba(15, 15, 18, 0.75); backdrop-filter: blur(12px);
					padding: 6px 12px; border-radius: 10px;
					border: 1px solid rgba(255, 255, 255, 0.08);
					display: flex; gap: 10px; align-items: center;
					box-shadow: 0 8px 32px rgba(0,0,0,0.6);
					transition: all 0.3s ease;
				}
				.toolbar.collapsed {
					padding: 4px;
					background: rgba(15, 15, 18, 0.4);
				}
				.toolbar.collapsed .toolbar-content {
					display: none !important;
				}
				.toolbar-content {
					display: flex; gap: 10px; align-items: center;
				}

				.search-wrapper {
					display: flex; align-items: center; background: rgba(0,0,0,0.45);
					border-radius: 6px; padding: 0 8px; border: 1px solid transparent;
					transition: border-color 0.2s;
				}
				.search-wrapper:focus-within { border-color: #007ACC; }
				.search-wrapper span { color: #888; font-size: 12px; margin-right: 4px; }

				#nodeSearch {
					background: transparent; border: none; color: #E0E0E0;
					width: 90px; font-size: 12px; padding: 6px 0; outline: none;
					transition: width 0.3s cubic-bezier(0.4, 0, 0.2, 1);
				}
				#nodeSearch:focus { width: 180px; }
				#nodeSearch::placeholder { color: #666; }

				.depth-container {
					display: flex;
					align-items: center;
					gap: 6px;
					background: rgba(0, 0, 0, 0.45);
					padding: 4px 8px;
					border-radius: 6px;
					border: 1px solid rgba(255, 255, 255, 0.03);
				}
				.control-title {
					font-size: 10px;
					color: #888;
					font-weight: 700;
					text-transform: uppercase;
					letter-spacing: 0.5px;
				}
				.step-btn {
					background: rgba(255, 255, 255, 0.05);
					border: 1px solid rgba(255, 255, 255, 0.1);
					color: #E0E0E0;
					border-radius: 4px;
					width: 20px;
					height: 20px;
					display: flex;
					align-items: center;
					justify-content: center;
					cursor: pointer;
					font-size: 12px;
					font-weight: bold;
					transition: all 0.15s;
					outline: none;
				}
				.step-btn:hover {
					background: #007ACC;
					color: #fff;
					border-color: #0098FF;
				}
				.slider-wrapper {
					display: flex;
					flex-direction: column;
					align-items: center;
					position: relative;
					width: 100px;
				}
				#depthRange {
					-webkit-appearance: none;
					width: 100%;
					height: 4px;
					border-radius: 2px;
					background: rgba(255, 255, 255, 0.15);
					outline: none;
					margin: 8px 0;
				}
				#depthRange::-webkit-slider-thumb {
					-webkit-appearance: none;
					appearance: none;
					width: 12px;
					height: 12px;
					border-radius: 50%;
					background: #00FF88;
					cursor: pointer;
					box-shadow: 0 0 10px #00FF88;
					transition: transform 0.1s, background-color 0.1s;
				}
				#depthRange::-webkit-slider-thumb:hover {
					transform: scale(1.25);
					background: #fff;
				}
				.ticks {
					display: flex;
					justify-content: space-between;
					width: 100%;
					padding: 0 2px;
					margin-top: -3px;
					margin-bottom: 2px;
				}
				.ticks span {
					font-size: 7.5px;
					color: #555;
					font-family: monospace;
					position: relative;
				}
				.ticks span::before {
					content: '';
					display: block;
					width: 1px;
					height: 3px;
					background: rgba(255, 255, 255, 0.2);
					margin: 0 auto 1px auto;
				}
				.depth-value {
					font-family: 'Consolas', monospace;
					font-size: 12px;
					font-weight: bold;
					color: #00FF88;
					min-width: 16px;
					text-align: center;
					text-shadow: 0 0 8px rgba(0, 255, 136, 0.4);
				}

				.select-wrapper {
					display: flex;
					align-items: center;
					gap: 6px;
					background: rgba(0, 0, 0, 0.45);
					padding: 4px 8px;
					border-radius: 6px;
					border: 1px solid rgba(255, 255, 255, 0.03);
				}
				select {
					background: transparent; border: none; color: #E0E0E0;
					font-size: 11px; font-weight: 600; padding: 2px 4px; outline: none; cursor: pointer;
				}
				select option { background: #1e1e24; color: #fff; }

				.multi-select-container {
					position: relative;
					display: inline-block;
				}
				.multi-select-btn {
					background: transparent;
					color: #E0E0E0;
					border: none;
					font-size: 11px;
					cursor: pointer;
					outline: none;
					font-weight: 600;
					padding: 2px 4px;
					display: flex;
					align-items: center;
				}
				.multi-select-btn:hover { color: #00FF88; }
				.multi-select-menu {
					position: absolute;
					top: 100%;
					left: 0;
					margin-top: 8px;
					background: rgba(15, 15, 18, 0.95);
					backdrop-filter: blur(12px);
					border: 1px solid rgba(255, 255, 255, 0.1);
					border-radius: 6px;
					padding: 8px;
					display: flex;
					flex-direction: column;
					gap: 6px;
					z-index: 1000;
					min-width: 160px;
					box-shadow: 0 8px 24px rgba(0,0,0,0.8);
					max-height: 320px;
					overflow-y: auto;
				}
				.multi-select-menu label {
					font-size: 11px;
					color: #E0E0E0;
					display: flex;
					align-items: center;
					gap: 8px;
					cursor: pointer;
					padding: 2px 0;
				}
				.multi-select-menu label:hover {
					color: #00FF88;
				}
				.multi-select-menu input[type='checkbox'] {
					cursor: pointer;
					margin: 0;
					accent-color: #007ACC;
				}
				.multi-select-menu::-webkit-scrollbar { width: 6px; }
				.multi-select-menu::-webkit-scrollbar-track { background: rgba(0,0,0,0.2); border-radius: 3px;}
				.multi-select-menu::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.2); border-radius: 3px;}
				.multi-select-menu::-webkit-scrollbar-thumb:hover { background: #007ACC; }

				.divider { width: 1px; height: 18px; background: rgba(255,255,255,0.12); margin: 0 2px; }

				.icon-btn {
					background: transparent; border: none; color: #bbb;
					font-size: 16px; width: 28px; height: 28px; border-radius: 4px;
					cursor: pointer; display: flex; align-items: center; justify-content: center;
					transition: all 0.2s; padding: 0; outline: none;
				}
				.icon-btn:hover { background: rgba(255,255,255,0.1); color: #fff; }

				.close-btn:hover {
					background: rgba(220, 50, 50, 0.95) !important;
					color: #fff !important;
					box-shadow: 0 0 10px rgba(220, 50, 50, 0.5);
				}

				.debug-icon {
					position: absolute; bottom: 1.5rem; left: 1.5rem; z-index: 1000;
					width: 32px; height: 32px; border-radius: 50%;
					background: rgba(15, 15, 18, 0.8); backdrop-filter: blur(5px);
					border: 1px solid rgba(255, 255, 255, 0.1); color: #666;
					cursor: pointer; display: flex; align-items: center; justify-content: center;
					box-shadow: 0 4px 10px rgba(0,0,0,0.5); transition: all 0.2s; font-size: 14px;
					outline: none;
				}
				.debug-icon:hover { border-color: #00FF88; color: #00FF88; transform: scale(1.1); }

				#navigator {
					position: absolute;
					bottom: 1.5rem;
					right: 1.5rem;
					width: 180px;
					height: 180px;
					z-index: 800;
					background: #141419;
					border: 1px solid #444;
					border-radius: 8px;
					overflow: hidden;
					box-shadow: 0 10px 30px rgba(0,0,0,0.8);
					cursor: pointer;
				}

				#miniMapCanvas {
					display: block;
					width: 100%;
					height: 100%;
				}

				#debugLog {
					display: none;
					position: absolute;
					bottom: 4rem;
					left: 1.5rem;
					width: 350px;
					max-height: 250px;
					z-index: 9999;
					background: rgba(10, 10, 12, 0.9);
					backdrop-filter: blur(5px);
					border: 1px solid #333;
					border-radius: 8px;
					padding: 10px;
					color: #00FF88;
					font-family: 'Consolas', 'Courier New', monospace;
					font-size: 11px;
					overflow-y: scroll;
					scrollbar-width: thin;
					scrollbar-color: #555 transparent;
					pointer-events: auto;
					box-shadow: 0 10px 30px rgba(0,0,0,0.8);
				}

				.missing-toggle-btn {
					background: rgba(255, 51, 102, 0.1);
					border: 1px solid rgba(255, 51, 102, 0.4);
					color: #FF3366;
					border-radius: 4px;
					padding: 2px 6px;
					font-size: 11px;
					font-weight: bold;
					cursor: pointer;
					display: flex;
					align-items: center;
					gap: 4px;
					transition: all 0.2s;
				}
				.missing-toggle-btn:hover, .missing-toggle-btn.active {
					background: rgba(255, 51, 102, 0.8);
					color: #fff;
				}

				/* --- PROPERTIES / DETAILS SIDEBAR STYLES --- */
				.sidebar {
					position: absolute;
					top: 0;
					right: -380px;
					width: 360px;
					height: 100%;
					background: rgba(15, 15, 20, 0.92);
					backdrop-filter: blur(16px);
					border-left: 1px solid rgba(255, 255, 255, 0.12);
					box-shadow: -8px 0 32px rgba(0, 0, 0, 0.7);
					z-index: 900;
					transition: right 0.3s cubic-bezier(0.16, 1, 0.3, 1);
					display: flex;
					flex-direction: column;
					overflow: hidden;
				}

				.sidebar.open {
					right: 0;
				}

				.sidebar-header {
					padding: 16px;
					background: rgba(0, 0, 0, 0.4);
					border-bottom: 1px solid rgba(255, 255, 255, 0.08);
					display: flex;
					justify-content: space-between;
					align-items: flex-start;
				}

				.sidebar-title-container {
					display: flex;
					flex-direction: column;
					gap: 6px;
					max-width: 290px;
				}

				.node-title {
					margin: 0;
					font-size: 15px;
					font-weight: 700;
					color: #FFFFFF;
					word-break: break-word;
					line-height: 1.3;
				}

				.badges-container {
					display: flex;
					gap: 6px;
					flex-wrap: wrap;
					align-items: center;
				}

				.type-badge {
					display: inline-block;
					padding: 2px 8px;
					border-radius: 4px;
					font-size: 10px;
					font-weight: 700;
					text-transform: uppercase;
					background: rgba(0, 122, 204, 0.3);
					color: #4FC3F7;
					border: 1px solid rgba(79, 195, 247, 0.4);
				}

				.category-badge {
					display: inline-block;
					padding: 2px 8px;
					border-radius: 4px;
					font-size: 10px;
					font-weight: 700;
					text-transform: uppercase;
					background: rgba(255, 215, 0, 0.2);
					color: #FFD700;
					border: 1px solid rgba(255, 215, 0, 0.5);
				}

				.sidebar-close-btn {
					background: transparent;
					border: none;
					color: #888;
					font-size: 18px;
					cursor: pointer;
					padding: 2px 6px;
					border-radius: 4px;
					transition: all 0.2s;
				}

				.sidebar-close-btn:hover {
					color: #FFF;
					background: rgba(255, 255, 255, 0.1);
				}

				.sidebar-content {
					flex: 1;
					padding: 16px;
					overflow-y: auto;
					display: flex;
					flex-direction: column;
					gap: 16px;
				}

				.sidebar-content::-webkit-scrollbar { width: 6px; }
				.sidebar-content::-webkit-scrollbar-track { background: rgba(0,0,0,0.2); }
				.sidebar-content::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.2); border-radius: 3px; }
				.sidebar-content::-webkit-scrollbar-thumb:hover { background: #007ACC; }

				.sidebar-section {
					background: rgba(255, 255, 255, 0.03);
					border: 1px solid rgba(255, 255, 255, 0.06);
					border-radius: 8px;
					padding: 12px;
				}

				.sidebar-section-title {
					font-size: 11px;
					font-weight: 700;
					text-transform: uppercase;
					letter-spacing: 0.8px;
					color: #00FF88;
					margin-bottom: 10px;
					display: flex;
					justify-content: space-between;
					align-items: center;
				}

				.treeview {
					list-style: none;
					padding-left: 0;
					margin: 0;
					font-size: 12px;
				}

				.treeview-item {
					padding: 5px 0;
					border-bottom: 1px solid rgba(255, 255, 255, 0.03);
					display: flex;
					align-items: center;
					gap: 6px;
					color: #CCCCCC;
					word-break: break-word;
				}

				.treeview-item:last-child {
					border-bottom: none;
				}

				.treeview-icon {
					color: #56B6C2;
					font-size: 11px;
				}

				.prop-row {
					display: flex;
					justify-content: space-between;
					align-items: center;
					padding: 4px 0;
					font-size: 12px;
					border-bottom: 1px solid rgba(255, 255, 255, 0.03);
				}

				.prop-label {
					color: #888888;
				}

				.prop-val {
					color: #EEEEEE;
					font-family: 'Consolas', monospace;
					font-size: 11px;
					text-align: right;
					word-break: break-all;
					max-width: 200px;
				}

				.interactive-node-link {
					color: #4FC3F7;
					cursor: pointer;
					text-decoration: underline;
					transition: color 0.15s;
				}

				.interactive-node-link:hover {
					color: #00FF88;
				}
			</style>
		</head>
		<body>
			<div class='toolbar' id='mainToolbar'>

				<button onclick='toggleToolbar()' class='icon-btn' title='Collapse / Expand Menu'>☰</button>

				<div class='toolbar-content'>
					<div class='divider'></div>

					<button id='btnShowMissing' class='missing-toggle-btn' style='display: none;' onclick='toggleMissingFilter()' title='Show missing/broken dependencies'>
						🚨 Missing
					</button>

					<div class='divider'></div>

					<div class='multi-select-container'>
						<button class='multi-select-btn' onclick='toggleProjectMenu(event)' style='color: #4FC3F7;'>🌍 Projects ▼</button>
						<div class='multi-select-menu' id='projectMenu' style='display: none;' onclick='event.stopPropagation()'>
							<label><input type='checkbox' id='selectAllProjects' value='ALL' checked onchange='toggleAllProjects(this)'> <b>Select All</b></label>
							<div style='height:1px; background:rgba(255,255,255,0.1); margin: 4px 0;'></div>
							<!-- Dynamic projects will be inserted here -->
						</div>
					</div>

					<div class='divider'></div>

					<div class='search-wrapper'>
						<span>🔍</span>

						<div class='multi-select-container'>
							<button class='multi-select-btn' onclick='toggleTypeMenu(event)'>Types ▼</button>
							<div class='multi-select-menu' id='typeMenu' style='display: none;' onclick='event.stopPropagation()'>
								<label><input type='checkbox' value='ALL' checked onchange='toggleAllTypes(this)'> <b>Select All</b></label>
								<div style='height:1px; background:rgba(255,255,255,0.1); margin: 4px 0;'></div>

								<!-- Kernel & Configuration -->
								<label><input type='checkbox' value='ET' class='type-cb' checked onchange='filterNodes()'> ET (Entity)</label>
								<label><input type='checkbox' value='CT' class='type-cb' checked onchange='filterNodes()'> CT (Component)</label>
								<label><input type='checkbox' value='CONF' class='type-cb' checked onchange='filterNodes()'> CONF</label>
								<div style='height:1px; background:rgba(255,255,255,0.05); margin: 3px 0;'></div>

								<!-- 3D Graphics & Materials -->
								<label><input type='checkbox' value='XOB' class='type-cb' checked onchange='filterNodes()'> XOB (Model)</label>
								<label><input type='checkbox' value='EMAT' class='type-cb' checked onchange='filterNodes()'> EMAT (Material)</label>
								<label><input type='checkbox' value='SHADER' class='type-cb' checked onchange='filterNodes()'> SHADER</label>
								<label><input type='checkbox' value='EDDS' class='type-cb' checked onchange='filterNodes()'> EDDS (Texture)</label>
								<div style='height:1px; background:rgba(255,255,255,0.05); margin: 3px 0;'></div>

								<!-- Animation, Effect & AI -->
								<label><input type='checkbox' value='ANM' class='type-cb' checked onchange='filterNodes()'> ANM (Animation)</label>
								<label><input type='checkbox' value='PAP' class='type-cb' checked onchange='filterNodes()'> PAP (Anim Project)</label>
								<label><input type='checkbox' value='ASI' class='type-cb' checked onchange='filterNodes()'> ASI</label>
								<label><input type='checkbox' value='BT' class='type-cb' checked onchange='filterNodes()'> BT (Behavior Tree)</label>
								<label><input type='checkbox' value='PTC' class='type-cb' checked onchange='filterNodes()'> PTC (Particle)</label>
								<label><input type='checkbox' value='SIGA' class='type-cb' checked onchange='filterNodes()'> SIGA</label>
								<div style='height:1px; background:rgba(255,255,255,0.05); margin: 3px 0;'></div>

								<!-- UI and Interface (2D Overlay) -->
								<label><input type='checkbox' value='LAYOUT' class='type-cb' checked onchange='filterNodes()'> LAYOUT</label>
								<label><input type='checkbox' value='WIDGET' class='type-cb' checked onchange='filterNodes()'> WIDGET</label>
								<label><input type='checkbox' value='IMAGESET' class='type-cb' checked onchange='filterNodes()'> IMAGESET</label>
								<div style='height:1px; background:rgba(255,255,255,0.05); margin: 3px 0;'></div>

								<!-- Audio -->
								<label><input type='checkbox' value='SOUND' class='type-cb' checked onchange='filterNodes()'> SOUND</label>
								<label><input type='checkbox' value='WAV' class='type-cb' checked onchange='filterNodes()'> WAV</label>
								<label><input type='checkbox' value='ACP' class='type-cb' checked onchange='filterNodes()'> ACP (Audio Project)</label>

								<!-- Missing References -->
								<div style='height:1px; background:rgba(255,255,255,0.05); margin: 3px 0;'></div>
								<label><input type='checkbox' value='MISSING' class='type-cb' checked onchange='filterNodes()'> <span style='color:#FF3366'>MISSING (Broken)</span></label>
							</div>
						</div>

						<div style='width: 1px; height: 14px; background: rgba(255,255,255,0.15); margin-right: 6px; margin-left: 4px;'></div>
						<input type='text' id='nodeSearch' placeholder='Search ID/Tag...' onkeyup='filterNodes()' />
					</div>

					<div class='divider'></div>

					<div class='depth-container'>
						<span class='control-title'>Depth:</span>
						<button class='step-btn' onclick='stepDepth(-1)' title='Decrease Depth'>-</button>
						<div class='slider-wrapper'>
							<input type='range' id='depthRange' min='1' max='20' value='5' oninput='onDepthSliderInput(this.value)' onchange='onDepthSliderChange(this.value)'>
							<div class='ticks'>
								<span>1</span>
								<span>5</span>
								<span>10</span>
								<span>15</span>
								<span>20</span>
							</div>
						</div>
						<button class='step-btn' onclick='stepDepth(1)' title='Increase Depth'>+</button>
						<span id='depthVal' class='depth-value'>5</span>
					</div>

					<div class='divider'></div>

					<div class='select-wrapper'>
						<span class='control-title'>Layout:</span>
						<select id='layoutSelect' onchange='runLayout()' title='Layout Engine'>
							<option value='dagre'>Hierarchical</option>
							<option value='cose'>Physics</option>
							<option value='circle'>Circle</option>
							<option value='grid'>Grid</option>
						</select>

						<button onclick='runLayout()' class='icon-btn' style='width:24px; height:24px; margin-left:4px;' title='Relayout (Újrarendezés)'>♻</button>
					</div>

					<div class='divider'></div>

					<div class='select-wrapper'>
						<span class='control-title'>Edges:</span>
						<select id='edgeStyleSelect' onchange='updateEdgeStyle()' title='Edge Routing Type'>
							<option value='taxi'>Orthogonal</option>
							<option value='bezier'>Smooth</option>
							<option value='straight'>Straight</option>
						</select>
					</div>

					<div class='divider'></div>

					<button onclick='if(typeof cy !== ""undefined"") cy.fit(null, 40)' class='icon-btn' title='Fit Screen to Window'>⛶</button>
					<div class='divider'></div>
					<button onclick='closeGraph()' class='icon-btn close-btn' title='Close Graph overlay'>✕</button>
				</div>
			</div>

			<!-- PROPERTIES / DETAILS SIDEBAR PANEL -->
			<div id='detailsSidebar' class='sidebar'>
				<div class='sidebar-header'>
					<div class='sidebar-title-container'>
						<div class='badges-container'>
							<span id='sidebarTypeBadge' class='type-badge'>TYPE</span>
							<span id='sidebarCategoryBadge' class='category-badge' style='display:none;'>CATEGORY</span>
						</div>
						<h3 id='sidebarNodeName' class='node-title'>Node Details</h3>
					</div>
					<button class='sidebar-close-btn' onclick='closeSidebar()' title='Close details panel'>✕</button>
				</div>
				<div class='sidebar-content' id='sidebarContent'>
				</div>
			</div>

			<button onclick='toggleDebug()' class='debug-icon' title='Toggle Debug Diagnostics'>🪲</button>
			<div id='debugLog'><b>[DIAGNOSTIC SYSTEM ONLINE]</b><br/></div>

			<div id='cy'></div>
			<div id='navigator'>
				<canvas id='miniMapCanvas' width='180' height='180'></canvas>
			</div>

			<script>
				var cy;
				var isDraggingMiniMap = false;
				var showingMissingOnly = false;
				var glowingEdgesCache = null;

				function updateGlowingEdgesCache() {
					if (cy) {
						glowingEdgesCache = cy.edges('[?isGlowing]');
					}
				}

				function toggleSection(headerEl) {
					var content = headerEl.nextElementSibling;
					if (content) {
						if (content.style.display === 'none') {
							content.style.display = '';
							var icon = headerEl.querySelector('.toggle-icon');
							if (icon) icon.innerText = '▼';
						} else {
							content.style.display = 'none';
							var icon = headerEl.querySelector('.toggle-icon');
							if (icon) icon.innerText = '▶';
						}
					}
				}

				function toggleToolbar() {
					var tb = document.getElementById('mainToolbar');
					if (tb) tb.classList.toggle('collapsed');
				}

				function toggleTypeMenu(e) {
					if(e) e.stopPropagation();
					var menu = document.getElementById('typeMenu');
					menu.style.display = menu.style.display === 'none' ? 'flex' : 'none';
					var pMenu = document.getElementById('projectMenu');
					if(pMenu) pMenu.style.display = 'none';
				}

				function toggleProjectMenu(e) {
					if(e) e.stopPropagation();
					var menu = document.getElementById('projectMenu');
					menu.style.display = menu.style.display === 'none' ? 'flex' : 'none';
					var tMenu = document.getElementById('typeMenu');
					if(tMenu) tMenu.style.display = 'none';
				}

				function toggleAllTypes(cb) {
					var checkboxes = document.querySelectorAll('.type-cb');
					checkboxes.forEach(function(c) { c.checked = cb.checked; });
					filterNodes();
				}

				function updateSelectAllProjects() {
					var allCb = document.getElementById('selectAllProjects');
					var projCbs = document.querySelectorAll('.proj-cb');
					if (allCb && projCbs.length > 0) {
						var checkedCount = 0;
						var indetCount = 0;
						projCbs.forEach(function(cb) {
							if (cb.checked) checkedCount++;
							if (cb.indeterminate) indetCount++;
						});

						if (checkedCount === projCbs.length) {
							allCb.checked = true;
							allCb.indeterminate = false;
						} else if (checkedCount === 0 && indetCount === 0) {
							allCb.checked = false;
							allCb.indeterminate = false;
						} else {
							allCb.checked = false;
							allCb.indeterminate = true;
						}
					}
				}

				function updateProjectCheckboxState(projCb, paksContainer) {
					var childPaks = paksContainer.querySelectorAll('.pak-cb');
					if (childPaks.length === 0) return;
					var checkedCount = 0;
					childPaks.forEach(function(cp) { if (cp.checked) checkedCount++; });

					if (checkedCount === 0) {
						projCb.checked = false;
						projCb.indeterminate = false;
					} else if (checkedCount === childPaks.length) {
						projCb.checked = true;
						projCb.indeterminate = false;
					} else {
						projCb.checked = false;
						projCb.indeterminate = true;
					}
				}

				function toggleAllProjects(cb) {
					var isChecked = cb.checked;
					cb.indeterminate = false;
					var projCheckboxes = document.querySelectorAll('.proj-cb');
					projCheckboxes.forEach(function(c) {
						c.checked = isChecked;
						c.indeterminate = false;
					});
					var pakCheckboxes = document.querySelectorAll('.pak-cb');
					pakCheckboxes.forEach(function(c) { c.checked = isChecked; });
					filterNodes();
				}

				function toggleMissingFilter() {
					showingMissingOnly = !showingMissingOnly;
					var btn = document.getElementById('btnShowMissing');
					if (showingMissingOnly) {
						btn.classList.add('active');
					} else {
						btn.classList.remove('active');
					}
					filterNodes();
				}

				window.addEventListener('click', function(e) {
					var tMenu = document.getElementById('typeMenu');
					if (tMenu && tMenu.style.display === 'flex' && !e.target.closest('.multi-select-container')) {
						tMenu.style.display = 'none';
					}
					var pMenu = document.getElementById('projectMenu');
					if (pMenu && pMenu.style.display === 'flex' && !e.target.closest('.multi-select-container')) {
						pMenu.style.display = 'none';
					}
				});

				function stepDepth(amount) {
					var slider = document.getElementById('depthRange');
					var val = parseInt(slider.value) + amount;
					if (val >= 1 && val <= 20) {
						slider.value = val;
						onDepthSliderInput(val);
						onDepthSliderChange(val);
					}
				}

				function onDepthSliderInput(val) {
					document.getElementById('depthVal').innerText = val;
				}

				function onDepthSliderChange(val) {
					dLog('Depth changed via JS: ' + val);
					try {
						if (window.chrome && window.chrome.webview) {
							var payload = { event: 'depthChanged', value: val.toString() };
							window.chrome.webview.postMessage(payload);
						}
					} catch(e) {
						dLog('Error sending depth change: ' + e.message);
					}
				}

				function setDepth(val) {
					var slider = document.getElementById('depthRange');
					if (slider) {
						slider.value = val;
						onDepthSliderInput(val);
					}
				}

				function closeGraph() {
					try {
						if (window.chrome && window.chrome.webview) {
							window.chrome.webview.postMessage({ event: 'close' });
						}
					} catch(e) {
						dLog('Error posting close event: ' + e.message);
					}
				}

				function toggleDebug() {
					var logDiv = document.getElementById('debugLog');
					if (logDiv.style.display === 'none' || logDiv.style.display === '') {
						logDiv.style.display = 'block';
						logDiv.scrollTop = logDiv.scrollHeight;
					} else {
						logDiv.style.display = 'none';
					}
				}

				function dLog(msg) {
					var logDiv = document.getElementById('debugLog');
					if(logDiv) {
						var time = new Date().toLocaleTimeString();
						logDiv.innerHTML += '[' + time + '] ' + msg + '<br/>';
						logDiv.scrollTop = logDiv.scrollHeight;
					}
					console.log(msg);
				}

				window.addEventListener('load', function() {
					dLog('WebView loaded. Initializing Core Cytoscape...');
					cy = cytoscape({
						container: document.getElementById('cy'),
						wheelSensitivity: 0.15,
						textureOnViewport: true,
						hideEdgesOnViewport: true,
						hideLabelsOnViewport: true,
						motionBlur: false,
						pixelRatio: window.devicePixelRatio || 1,
						style: [
							{
								selector: 'node',
								style: {
									'background-color': 'data(color)',
									'label': 'data(label)',
									'color': '#FFF',
									'text-valign': 'center',
									'text-halign': 'center',
									'font-size': '11px',
									'width': 'label',
									'height': 'label',
									'padding': '14px',
									'shape': 'round-rectangle',
									'text-wrap': 'wrap',
									'text-max-width': '180px',
									'border-width': 'data(borderWidth)',
									'border-color': 'data(borderColor)',
									'shadow-blur': 6,
									'shadow-color': 'data(color)',
									'shadow-opacity': 0.4,
									'transition-property': 'opacity',
									'transition-duration': '0.3s'
								}
							},
							{
								selector: 'node:selected',
								style: {
									'border-width': '4px',
									'border-color': '#00FF88',
									'shadow-blur': 12,
									'shadow-color': '#00FF88',
									'shadow-opacity': 0.9
								}
							},
							{
								selector: 'node[type=""MISSING""]',
								style: {
									'background-color': '#111',
									'border-width': '2px',
									'border-color': '#FF1744',
									'border-style': 'dashed',
									'shape': 'octagon',
									'color': '#FF1744',
									'font-weight': 'bold',
									'shadow-color': '#FF1744',
									'shadow-blur': 10,
									'shadow-opacity': 0.8
								}
							},
							{
								selector: 'node[?isSearchTarget]',
								style: {
									'border-width': '4px',
									'border-color': '#FF3366',
									'shadow-blur': 12,
									'shadow-color': '#FF3366',
									'shadow-opacity': 0.9,
									'font-weight': 'bold'
								}
							},
							{
								selector: 'node[?isTargetWav]',
								style: {
									'border-width': '4px',
									'border-color': '#00FF88',
									'shadow-blur': 12,
									'shadow-color': '#00FF88',
									'shadow-opacity': 0.9,
									'font-weight': 'bold'
								}
							},
							{
								selector: 'edge',
								style: {
									'width': 2.5,
									'line-color': '#44444a',
									'target-arrow-color': '#44444a',
									'target-arrow-shape': 'triangle',
									'label': 'data(label)',
									'font-size': '10px',
									'color': '#aaa',
									'text-rotation': 'autorotate',
									'text-background-color': '#0b0c10',
									'text-background-opacity': 1,
									'text-background-padding': '4px',
									'line-style': 'dashed',
									'line-dash-pattern': [6, 4],
									'transition-property': 'opacity',
									'transition-duration': '0.3s',
									'source-distance-from-node': '5px',
									'target-distance-from-node': '5px'
								}
							},
							{
								selector: 'edge.edge-taxi',
								style: {
									'curve-style': 'taxi',
									'taxi-direction': 'auto',
									'taxi-turn': '30px',
									'taxi-turn-min-distance': '15px'
								}
							},
							{
								selector: 'edge.edge-bezier',
								style: {
									'curve-style': 'unbundled-bezier',
									'control-point-distances': 40,
									'control-point-weights': 0.5,
									'edge-distances': 'node-position'
								}
							},
							{
								selector: 'edge.edge-straight',
								style: {
									'curve-style': 'straight'
								}
							},
							{
								selector: 'edge[?isGlowing]',
								style: {
									'width': 3,
									'line-color': '#00FF88',
									'target-arrow-color': '#00FF88',
									'text-background-color': '#0b0c10',
									'color': '#00FF88',
									'shadow-blur': 4,
									'shadow-color': '#00FF88',
									'shadow-opacity': 0.8
								}
							},
							{
								selector: '.dimmed',
								style: {
									'opacity': 0.15,
									'transition-property': 'opacity',
									'transition-duration': '0.2s'
								}
							},
							{
								selector: '.filtered',
								style: {
									'opacity': 0.1,
									'transition-property': 'opacity',
									'transition-duration': '0.2s'
								}
							},
							{
								selector: '.hidden-type',
								style: {
									'display': 'none'
								}
							}
						],
						elements: []
					});

					var miniMapRequested = false;
					cy.on('pan zoom resize', function() {
						if (!miniMapRequested) {
							miniMapRequested = true;
							requestAnimationFrame(function() {
								updateMiniMap();
								miniMapRequested = false;
							});
						}
					});

					var zoomTimeout = null;
					cy.on('zoom', function() {
						clearTimeout(zoomTimeout);
						zoomTimeout = setTimeout(function() {
							var currentZoom = cy.zoom();
							var nativeRatio = window.devicePixelRatio || 1;
							var targetRatio = nativeRatio;

							if (currentZoom > 2.5) {
								targetRatio = Math.min(nativeRatio * 2, 3);
							} else if (currentZoom > 1.5) {
								targetRatio = Math.min(nativeRatio * 1.5, 2.5);
							}

							if (cy.renderer().pixelRatio !== targetRatio) {
								cy.renderer().pixelRatio = targetRatio;
							}
						}, 150);
					});

					cy.on('drag', 'node', function(e) {
						var draggedNode = e.target;
						var visibleNodes = cy.nodes(':not(.hidden-type)');
						var count = visibleNodes.length;
						if (count <= 1) return;

						var padding = 25;
						var iterations = 6;

						var items = new Array(count);
						var draggedIndex = -1;

						for (var i = 0; i < count; i++) {
							var node = visibleNodes[i];
							var pos = node.position();
							var bb = node.boundingBox({ includeLabels: true });

							var isDragged = (node === draggedNode);
							if (isDragged) draggedIndex = i;

							items[i] = {
								node: node,
								x: pos.x,
								y: pos.y,
								cx: (bb.x1 + bb.x2) / 2,
								cy: (bb.y1 + bb.y2) / 2,
								hw: bb.w / 2 + padding / 2,
								hh: bb.h / 2 + padding / 2,
								isDragged: isDragged,
								distSq: 0
							};
						}

						if (draggedIndex !== -1) {
							var dragCx = items[draggedIndex].cx;
							var dragCy = items[draggedIndex].cy;

							for (var d = 0; d < count; d++) {
								var dcx = items[d].cx - dragCx;
								var dcy = items[d].cy - dragCy;
								items[d].distSq = dcx * dcx + dcy * dcy;
							}
						}

						for (var iter = 0; iter < iterations; iter++) {
							var movedAny = false;

							for (var a = 0; a < count; a++) {
								var itemA = items[a];

								for (var b = a + 1; b < count; b++) {
									var itemB = items[b];

									var dx = itemB.cx - itemA.cx;
									var absDx = Math.abs(dx);
									var minHw = itemA.hw + itemB.hw;
									if (absDx >= minHw) continue;

									var dy = itemB.cy - itemA.cy;
									var absDy = Math.abs(dy);
									var minHh = itemA.hh + itemB.hh;
									if (absDy >= minHh) continue;

									var overlapX = minHw - absDx;
									var overlapY = minHh - absDy;

									if (overlapX > 0 && overlapY > 0) {
										movedAny = true;

										var normOverlapX = overlapX / minHw;
										var normOverlapY = overlapY / minHh;

										var moveX = 0;
										var moveY = 0;

										if (normOverlapX > normOverlapY) {
											var dirY = (dy > 0) ? 1 : ((dy < 0) ? -1 : 1);
											moveY = overlapY * dirY;
										} else {
											var dirX = (dx > 0) ? 1 : ((dx < 0) ? -1 : 1);
											moveX = overlapX * dirX;
										}

										if (itemA.isDragged) {
											itemB.x += moveX;
											itemB.y += moveY;
											itemB.cx += moveX;
											itemB.cy += moveY;
										} else if (itemB.isDragged) {
											itemA.x -= moveX;
											itemA.y -= moveY;
											itemA.cx -= moveX;
											itemA.cy -= moveY;
										} else {
											if (itemA.distSq < itemB.distSq) {
												itemB.x += moveX;
												itemB.y += moveY;
												itemB.cx += moveX;
												itemB.cy += moveY;
											} else if (itemB.distSq < itemA.distSq) {
												itemA.x -= moveX;
												itemA.y -= moveY;
												itemA.cx -= moveX;
												itemA.cy -= moveY;
											} else {
												var halfMx = moveX * 0.5;
												var halfMy = moveY * 0.5;

												itemB.x += halfMx;
												itemB.y += halfMy;
												itemB.cx += halfMx;
												itemB.cy += halfMy;

												itemA.x -= halfMx;
												itemA.y -= halfMy;
												itemA.cx -= halfMx;
												itemA.cy -= halfMy;
											}
										}
									}
								}
							}

							if (!movedAny) break;
						}

						cy.batch(function() {
							for (var k = 0; k < count; k++) {
								var item = items[k];
								if (!item.isDragged) {
									item.node.position({
										x: item.x,
										y: item.y
									});
								}
							}
						});
						updateMiniMap();
					});

					cy.on('mouseover', 'node', function(e) {
						var sel = e.target;
						var relatives = sel.outgoers().union(sel.incomers());
						var others = cy.elements().difference(relatives).not(sel);
						others.addClass('dimmed');

						var d = sel.data();
						var tooltipText = d.name + ' [' + d.type + ']';
						if (d.embeddedElements && d.embeddedElements.length > 0) {
							tooltipText += '\n\nWidgets / Elements:\n- ' + d.embeddedElements.join('\n- ');
						}
						document.getElementById('cy').setAttribute('title', tooltipText);
					});

					cy.on('mouseout', 'node', function(e) {
						cy.elements().removeClass('dimmed');
						filterNodes();

						document.getElementById('cy').removeAttribute('title');
					});

					cy.on('tap', 'node', function(e) {
						var node = e.target;
						openSidebarForNode(node);
					});

					cy.on('tap', function(e) {
						if (e.target === cy) {
							closeSidebar();
						}
					});

					initRadialMenu();
					startAnimation();
					initCustomMiniMapControls();
					dLog('Core Initialized. Waiting for data payload...');

					try {
						if (window.chrome && window.chrome.webview) {
							window.chrome.webview.postMessage('READY');
							dLog('System READY signal sent to host.');
						}
					} catch(e) {
						dLog('Error sending READY signal: ' + e.message);
					}
				});

				function openSidebarForNode(node) {
					var data = node.data();
					var sidebar = document.getElementById('detailsSidebar');
					if (!sidebar) return;

					document.getElementById('sidebarNodeName').innerText = data.name || data.id;

					var typeBadge = document.getElementById('sidebarTypeBadge');
					if (data.type && data.type.trim() !== '') {
						typeBadge.innerText = data.type;
						if (data.color) {
							typeBadge.style.backgroundColor = data.color + '44';
							typeBadge.style.borderColor = data.color;
							typeBadge.style.color = '#FFF';
						}
						typeBadge.style.display = 'inline-block';
					} else {
						typeBadge.style.display = 'none';
					}

					var catBadge = document.getElementById('sidebarCategoryBadge');
					if (data.category && data.category !== 'Unknown') {
						catBadge.innerText = data.category;
						catBadge.style.display = 'inline-block';
					} else {
						catBadge.style.display = 'none';
					}

					var content = document.getElementById('sidebarContent');
					var html = '';

					html += '<div class=""sidebar-section"">';
					html += '  <div class=""sidebar-section-title""><span>ℹ Properties</span></div>';
					html += '  <div class=""prop-row""><span class=""prop-label"">ID:</span><span class=""prop-val"">' + escapeHtml(data.id) + '</span></div>';

					if (data.type && data.type.trim() !== '') {
						html += '  <div class=""prop-row""><span class=""prop-label"">Type:</span><span class=""prop-val"">' + escapeHtml(data.type) + '</span></div>';
					}

					if (data.category && data.category !== 'Unknown') {
						html += '  <div class=""prop-row""><span class=""prop-label"">Category:</span><span class=""prop-val"" style=""color:#FFD700;"">' + escapeHtml(data.category) + '</span></div>';
					}

					if (data.origin && data.origin.trim() !== '') {
						html += '  <div class=""prop-row""><span class=""prop-label"">Origin:</span><span class=""prop-val"">' + escapeHtml(data.origin) + '</span></div>';
					}

					if (data.projectId && data.type !== 'MISSING') {
						html += '  <div class=""prop-row""><span class=""prop-label"">Project:</span><span class=""prop-val"" style=""color:#4FC3F7; font-weight:bold;"">' + escapeHtml(data.projectId) + '</span></div>';
					}

					var tags = data.tags || [];
					if (tags.length > 0) {
						html += '  <div class=""prop-row""><span class=""prop-label"">Tags:</span><span class=""prop-val"" style=""color:#00FF88;"">' + escapeHtml(tags.join(', ')) + '</span></div>';
					}

					var outEdges = node.outgoers('edge');
					var inEdges = node.incomers('edge');
					html += '  <div class=""prop-row""><span class=""prop-label"">Outgoing Links:</span><span class=""prop-val"">' + outEdges.length + '</span></div>';
					html += '  <div class=""prop-row""><span class=""prop-label"">Incoming Links:</span><span class=""prop-val"">' + inEdges.length + '</span></div>';
					html += '</div>';

					var embedded = data.embeddedElements || data.EmbeddedElements || [];
					if (embedded.length > 0) {
						html += '<div class=""sidebar-section"">';
						html += '  <div class=""sidebar-section-title"" style=""cursor:pointer;"" onclick=""toggleSection(this)"">';
						html += '    <span>🧩 Embedded Elements (' + embedded.length + ')</span><span class=""toggle-icon"">▶</span>';
						html += '  </div>';
						html += '  <ul class=""treeview"" style=""display: none;"">';
						for (var i = 0; i < embedded.length; i++) {
							html += '    <li class=""treeview-item""><span class=""treeview-icon"">▪</span> <span>' + escapeHtml(embedded[i]) + '</span></li>';
						}
						html += '  </ul>';
						html += '</div>';
					}

					if (outEdges.length > 0) {
						html += '<div class=""sidebar-section"">';
						html += '  <div class=""sidebar-section-title"" style=""cursor:pointer;"" onclick=""toggleSection(this)"">';
						html += '    <span>🔽 Dependencies (' + outEdges.length + ')</span><span class=""toggle-icon"">▼</span>';
						html += '  </div>';
						html += '  <ul class=""treeview"">';
						outEdges.forEach(function(edge) {
							var targetNode = edge.target();
							var targetData = targetNode.data();
							var rel = edge.data('label') || 'references';
							html += '    <li class=""treeview-item"" style=""justify-content:space-between;"">';
							html += '      <span><span class=""treeview-icon"">➡</span> <span class=""interactive-node-link"" onclick=""focusAndSelectNode(\'' + escapeJsString(targetData.id) + '\')"">' + escapeHtml(targetData.name || targetData.id) + '</span></span>';
							html += '      <span style=""font-size:10px; color:#888;"">[' + escapeHtml(rel) + ']</span>';
							html += '    </li>';
						});
						html += '  </ul>';
						html += '</div>';
					}

					if (inEdges.length > 0) {
						html += '<div class=""sidebar-section"">';
						html += '  <div class=""sidebar-section-title"" style=""cursor:pointer;"" onclick=""toggleSection(this)"">';
						html += '    <span>🔼 Used By (' + inEdges.length + ')</span><span class=""toggle-icon"">▼</span>';
						html += '  </div>';
						html += '  <ul class=""treeview"">';
						inEdges.forEach(function(edge) {
							var sourceNode = edge.source();
							var sourceData = sourceNode.data();
							var rel = edge.data('label') || 'references';
							html += '    <li class=""treeview-item"" style=""justify-content:space-between;"">';
							html += '      <span><span class=""treeview-icon"">⬅</span> <span class=""interactive-node-link"" onclick=""focusAndSelectNode(\'' + escapeJsString(sourceData.id) + '\')"">' + escapeHtml(sourceData.name || sourceData.id) + '</span></span>';
							html += '      <span style=""font-size:10px; color:#888;"">[' + escapeHtml(rel) + ']</span>';
							html += '    </li>';
						});
						html += '  </ul>';
						html += '</div>';
					}

					content.innerHTML = html;
					sidebar.classList.add('open');
				}

				function closeSidebar() {
					var sidebar = document.getElementById('detailsSidebar');
					if (sidebar) sidebar.classList.remove('open');
				}

				function focusAndSelectNode(nodeId) {
					if (!cy) return;
					var targetNode = cy.getElementById(nodeId);
					if (targetNode && targetNode.length > 0) {
						cy.animate({ center: { eles: targetNode }, zoom: 1.4, duration: 400 });
						cy.nodes().unselect();
						targetNode.select();
						openSidebarForNode(targetNode);
					}
				}

				function escapeHtml(str) {
					if (!str) return '';
					return String(str)
						.replace(/&/g, '&amp;')
						.replace(/</g, '&lt;')
						.replace(/>/g, '&gt;')
						.replace(/\""/g, '&quot;')
						.replace(/'/g, '&#039;');
				}

				function escapeJsString(str) {
					if (!str) return '';
					return String(str).replace(/\\/g, '\\\\').replace(/'/g, ""\\'"");
				}

				function updateEdgeStyle() {
					if (!cy) return;
					var styleType = document.getElementById('edgeStyleSelect').value;
					dLog('Applying Edge Style class: ' + styleType);
					cy.batch(function() {
						cy.edges().removeClass('edge-taxi edge-bezier edge-straight');
						cy.edges().addClass('edge-' + styleType);
					});
				}

				function initRadialMenu() {
					if (typeof cy.cxtmenu === 'function') {
						cy.cxtmenu({
							selector: 'node',
							commands: [
								{
									content: '<div style=""text-align:center;""><span style=""font-size: 16px;"">🎯</span><br/><span style=""font-size: 10px;"">Focus</span></div>',
									select: function(ele){
										cy.animate({ center: { eles: ele }, zoom: 1.5, duration: 400 });
									}
								},
								{
									content: '<div style=""text-align:center;""><span style=""font-size: 16px;"">🔽</span><br/><span style=""font-size: 10px;"">Depends On</span></div>',
									select: function(ele){
										cy.elements().addClass('dimmed');
										var outgoers = ele.successors();
										ele.removeClass('dimmed');
										outgoers.removeClass('dimmed');
									}
								},
								{
									content: '<div style=""text-align:center;""><span style=""font-size: 16px;"">🔼</span><br/><span style=""font-size: 10px;"">Impact (Used By)</span></div>',
									select: function(ele){
										cy.elements().addClass('dimmed');
										var incomers = ele.predecessors();
										ele.removeClass('dimmed');
										incomers.removeClass('dimmed');
									}
								},
								{
									content: '<div style=""text-align:center;""><span style=""font-size: 16px;"">🔄</span><br/><span style=""font-size: 10px;"">Reset</span></div>',
									select: function(ele){
										cy.elements().removeClass('dimmed');
										filterNodes();
									}
								}
							],
							menuRadius: 75,
							activePadding: 8,
							indicatorSize: 18,
							separatorWidth: 3,
							spotlightPadding: 4,
							itemColor: '#E0E0E0',
							itemFill: 'rgba(25, 25, 30, 0.95)',
							activeItemFill: '#007ACC'
						});
					} else {
						dLog('WARNING: cxtmenu extension is missing.');
					}
				}

				function updateMiniMap() {
					if (!cy) return;
					var canvas = document.getElementById('miniMapCanvas');
					if (!canvas) return;
					var ctx = canvas.getContext('2d');
					if (!ctx) return;

					var cw = canvas.width;
					var ch = canvas.height;

					ctx.fillStyle = '#141419';
					ctx.fillRect(0, 0, cw, ch);

					var bounds = cy.elements(':not(.hidden-type)').boundingBox();
					if (bounds.w === 0 || bounds.h === 0) return;

					var padding = 15;
					var scaleX = (cw - padding * 2) / bounds.w;
					var scaleY = (ch - padding * 2) / bounds.h;
					var scale = Math.min(scaleX, scaleY);

					var offsetX = padding - bounds.x1 * scale + (cw - padding * 2 - bounds.w * scale) / 2;
					var offsetY = padding - bounds.y1 * scale + (ch - padding * 2 - bounds.h * scale) / 2;

					function toMapX(val) { return val * scale + offsetX; }
					function toMapY(val) { return val * scale + offsetY; }

					ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)';
					ctx.lineWidth = 1;
					cy.edges().forEach(function(edge) {
						if (edge.hasClass('hidden-type')) return;
						var source = edge.source().position();
						var target = edge.target().position();
						if (source && target) {
							ctx.beginPath();
							ctx.moveTo(toMapX(source.x), toMapY(source.y));
							ctx.lineTo(toMapX(target.x), toMapY(target.y));
							ctx.stroke();
						}
					});

					cy.nodes().forEach(function(node) {
						if (node.hasClass('hidden-type')) return;
						var pos = node.position();
						var col = node.data('color') || '#888';
						ctx.fillStyle = col;

						if (node.data('type') === 'MISSING') {
							ctx.strokeStyle = '#FF1744';
							ctx.lineWidth = 2;
							ctx.beginPath();
							ctx.arc(toMapX(pos.x), toMapY(pos.y), 4, 0, Math.PI * 2);
							ctx.fill();
							ctx.stroke();
						} else if (node.data('isSearchTarget') || node.data('isTargetWav')) {
							ctx.strokeStyle = '#FF3366';
							ctx.lineWidth = 2;
							ctx.beginPath();
							ctx.arc(toMapX(pos.x), toMapY(pos.y), 4, 0, Math.PI * 2);
							ctx.fill();
							ctx.stroke();
						} else {
							ctx.beginPath();
							ctx.arc(toMapX(pos.x), toMapY(pos.y), 2.5, 0, Math.PI * 2);
							ctx.fill();
						}
					});

					var extent = cy.extent();
					var vx1 = toMapX(extent.x1);
					var vy1 = toMapY(extent.y1);
					var vx2 = toMapX(extent.x2);
					var vy2 = toMapY(extent.y2);

					var vw = vx2 - vx1;
					var vh = vy2 - vy1;

					ctx.fillStyle = 'rgba(0, 0, 0, 0.4)';
					ctx.fillRect(0, 0, cw, Math.max(0, vy1));
					ctx.fillRect(0, Math.min(ch, vy2), cw, Math.max(0, ch - vy2));
					ctx.fillRect(0, Math.max(0, vy1), Math.max(0, vx1), Math.min(vh, ch - vy1));
					ctx.fillRect(Math.min(cw, vx2), Math.max(0, vy1), Math.max(0, cw - vx2), Math.min(vh, ch - vy1));

					ctx.strokeStyle = '#00FF88';
					ctx.lineWidth = 1.5;
					ctx.strokeRect(vx1, vy1, vw, vh);

					ctx.fillStyle = 'rgba(0, 255, 136, 0.05)';
					ctx.fillRect(vx1, vy1, vw, vh);
				}

				function initCustomMiniMapControls() {
					var canvas = document.getElementById('miniMapCanvas');
					if (!canvas) return;

					function handleMiniMapInteraction(e) {
						if (!cy) return;
						var rect = canvas.getBoundingClientRect();
						var clickX = e.clientX - rect.left;
						var clickY = e.clientY - rect.top;

						var bounds = cy.elements(':not(.hidden-type)').boundingBox();
						if (bounds.w === 0 || bounds.h === 0) return;

						var cw = canvas.width;
						var ch = canvas.height;
						var padding = 15;
						var scaleX = (cw - padding * 2) / bounds.w;
						var scaleY = (ch - padding * 2) / bounds.h;
						var scale = Math.min(scaleX, scaleY);

						var offsetX = padding - bounds.x1 * scale + (cw - padding * 2 - bounds.w * scale) / 2;
						var offsetY = padding - bounds.y1 * scale + (ch - padding * 2 - bounds.h * scale) / 2;

						var targetX = (clickX - offsetX) / scale;
						var targetY = (clickY - offsetY) / scale;

						cy.pan({
							x: cy.width() / 2 - targetX * cy.zoom(),
							y: cy.height() / 2 - targetY * cy.zoom()
						});
					}

					canvas.addEventListener('mousedown', function(e) {
						isDraggingMiniMap = true;
						handleMiniMapInteraction(e);
					});

					window.addEventListener('mousemove', function(e) {
						if (isDraggingMiniMap) handleMiniMapInteraction(e);
					});

					window.addEventListener('mouseup', function() {
						isDraggingMiniMap = false;
					});
				}

				var animOffsetFast = 0;
				var animationFrameId = null;

				function startAnimation() {
					if (animationFrameId) cancelAnimationFrame(animationFrameId);
					updateGlowingEdgesCache();

					function tick() {
						animOffsetFast = (animOffsetFast - 1.2) % 40;
						if (glowingEdgesCache && glowingEdgesCache.length > 0) {
							cy.batch(function() {
								glowingEdgesCache.style('line-dash-offset', animOffsetFast);
							});
						}
						animationFrameId = requestAnimationFrame(tick);
					}
					animationFrameId = requestAnimationFrame(tick);
				}

				function runLayout() {
					if(!cy) return;
					var layoutType = document.getElementById('layoutSelect').value;
					var layoutOpts = {};

					if (layoutType === 'dagre') {
						layoutOpts = { name: 'dagre', rankDir: 'LR', align: 'UL', nodesep: 100, edgesep: 40, ranksep: 250, padding: 60, animate: true, animationDuration: 600, animationEasing: 'ease-out-expo' };
					} else if (layoutType === 'cose') {
						layoutOpts = { name: 'cose', animate: true, animationDuration: 800, randomize: true, idealEdgeLength: 200, nodeOverlap: 80, nodeRepulsion: function(n){ return 800000; }, edgeElasticity: function(e){ return 50; }, nestingFactor: 5, gravity: 50, numIter: 2500, initialTemp: 300, coolingFactor: 0.98, minTemp: 1.0, padding: 60 };
					} else if (layoutType === 'circle') {
						layoutOpts = { name: 'circle', padding: 30, spacingFactor: 0.5, animate: true, animationDuration: 800, animationEasing: 'ease-out-expo', avoidOverlap: true, nodeDimensionsIncludeLabels: true };
					} else if (layoutType === 'grid') {
						layoutOpts = { name: 'grid', padding: 60, animate: true, animationDuration: 800, animationEasing: 'ease-out-expo', avoidOverlap: true, nodeDimensionsIncludeLabels: true };
					}

					var layout = cy.elements(':not(.hidden-type)').layout(layoutOpts);
					layout.run();
				}

				function filterNodes() {
					var textVal = document.getElementById('nodeSearch').value.toLowerCase();

					var typeCheckboxes = document.querySelectorAll('.type-cb');
					var disabledTypes = Array.from(typeCheckboxes).filter(cb => !cb.checked).map(cb => cb.value);

					var projCheckboxes = document.querySelectorAll('.proj-cb');
					var pakCheckboxes = document.querySelectorAll('.pak-cb');
					var selectedPaks = Array.from(pakCheckboxes).filter(cb => cb.checked).map(cb => cb.value);

					cy.batch(function(){
						cy.elements().removeClass('dimmed');
						cy.nodes().removeClass('filtered hidden-type');
						cy.edges().removeClass('filtered hidden-type');

						cy.nodes().removeStyle('display');
						cy.edges().removeStyle('display');

						cy.nodes().forEach(function(node){
							var nodeType = node.data('type');
							var nodeProj = node.data('projectId');
							var nodeOrigin = node.data('origin');

							var tags = node.data('tags') || [];
							var labelStr = node.data('label').toLowerCase();
							var tagsStr = tags.join(' ').toLowerCase();
							var matchesText = !textVal || labelStr.includes(textVal) || tagsStr.includes(textVal);

							var isExplicitlyDisabledType = disabledTypes.includes(nodeType);

							var pId = (nodeProj && nodeProj !== 'unknown') ? nodeProj : 'unknown';
							var oId = (nodeOrigin && nodeOrigin !== 'unknown') ? nodeOrigin.trim() : 'unknown';

							var isNodeActive = true;
							if (pId !== 'unknown') {
								if (oId !== 'unknown') {
									var combinedKey = pId + '|' + oId;
									isNodeActive = selectedPaks.includes(combinedKey);
								} else {
									var projCb = Array.from(projCheckboxes).find(function(cb) { return cb.value === pId; });
									if (projCb) {
										isNodeActive = projCb.checked || projCb.indeterminate;
									}
								}
							}

							if (!isNodeActive) {
								node.addClass('hidden-type');
								node.style('display', 'none');
							} else if (showingMissingOnly) {
								if (nodeType !== 'MISSING' && node.successors('node[type=""MISSING""]').length === 0) {
									node.addClass('hidden-type');
									node.style('display', 'none');
								}
							} else if (isExplicitlyDisabledType) {
								node.addClass('hidden-type');
								node.style('display', 'none');
							} else if (!matchesText) {
								node.addClass('filtered');
							} else {
								node.style('display', 'element');
							}
						});

						cy.edges().forEach(function(e){
							if(e.source().hasClass('hidden-type') || e.target().hasClass('hidden-type')) {
								e.addClass('hidden-type');
								e.style('display', 'none');
							} else {
								e.style('display', 'element');
								if (textVal && !showingMissingOnly) {
									e.addClass('filtered');
								}
							}
						});
					});
					updateGlowingEdgesCache();
					updateMiniMap();
				}

				window.addEventListener('resize', function() {
					if(cy) cy.resize();
				});
			</script>
		</body>
		</html>";

		public const string RenderJsonScriptTemplate = @"
					console.log('C# -> WebView2 ExecuteScriptAsync called!');

					try {
						if (typeof dLog !== 'undefined') {
							dLog('RenderJsonData script triggered from C#...');
						}

						if (typeof cy !== 'undefined' && cy !== null) {
							dLog('--- INCOMING DATA ---');
							cy.stop(true, true);
							cy.elements().remove();

							var rawData = {jsonData};
							var newElements = rawData.nodes ? rawData.nodes : rawData;
							var allProjectsList = rawData.allProjects ? rawData.allProjects : [];
							if (newElements && newElements.length > 0) {
								cy.add(newElements);

								dLog('Graph loaded -> Nodes: ' + cy.nodes().length + ', Edges: ' + cy.edges().length);

								var availableTypes = new Set();
								cy.nodes().forEach(function(n) {
									var t = n.data('type');
									if (t) availableTypes.add(t.toUpperCase());
								});

								var menu = document.getElementById('typeMenu');
								var existingCheckboxes = document.querySelectorAll('.type-cb');
								var existingTypes = new Set();
								existingCheckboxes.forEach(function(cb) { existingTypes.add(cb.value.toUpperCase()); });

								var missingDivider = null;
								var childrenArr = Array.from(menu.children);
								for (var i = 0; i < childrenArr.length; i++) {
									if (childrenArr[i].tagName === 'DIV' && i === childrenArr.length - 2) {
										missingDivider = childrenArr[i];
									}
								}

								availableTypes.forEach(function(t) {
									if (!existingTypes.has(t) && t !== 'NODE') {
										var lbl = document.createElement('label');
										var inp = document.createElement('input');
										inp.type = 'checkbox';
										inp.value = t;
										inp.className = 'type-cb';
										inp.checked = true;
										inp.onchange = function() { filterNodes(); };

										lbl.appendChild(inp);
										lbl.appendChild(document.createTextNode(' ' + t));

										if (missingDivider) {
											menu.insertBefore(lbl, missingDivider);
										} else {
											menu.appendChild(lbl);
										}
									}
								});

								var allCheckboxes = document.querySelectorAll('.type-cb');
								allCheckboxes.forEach(function(cb) {
									var label = cb.closest('label');
									if (availableTypes.has(cb.value.toUpperCase())) {
										label.style.display = 'flex';
									} else {
										label.style.display = 'none';
									}
								});

								var children = Array.from(menu.children);
								var visibleSinceLastDivider = 0;
								var lastDivider = null;

								for (var i = 0; i < children.length; i++) {
									var el = children[i];
									if (el.tagName === 'DIV') {
										if (lastDivider && visibleSinceLastDivider === 0) {
											lastDivider.style.display = 'none';
										}
										lastDivider = el;
										visibleSinceLastDivider = 0;
										el.style.display = 'block';
									} else if (el.tagName === 'LABEL' && el.style.display !== 'none') {
										var input = el.querySelector('input');
										if (input && input.value !== 'ALL') {
											visibleSinceLastDivider++;
										}
									}
								}
								if (lastDivider && visibleSinceLastDivider === 0) {
									lastDivider.style.display = 'none';
								}

								var projectMap = new Map();

								if (allProjectsList && allProjectsList.length > 0) {
									allProjectsList.forEach(function(p) {
										var cleanP = String(p).trim();
										if (cleanP !== '') {
											projectMap.set(cleanP, new Set());
										}
									});
								}

								cy.nodes().forEach(function(n) {
									var p = n.data('projectId');
									var o = n.data('origin');
									if (p && p.toLowerCase() !== 'unknown' && p.trim() !== '') {
										var cleanP = p.trim();
										if (!projectMap.has(cleanP)) {
											projectMap.set(cleanP, new Set());
										}
										if (o && o.toLowerCase() !== 'unknown' && o.trim() !== '') {
											projectMap.get(cleanP).add(o.trim());
										}
									}
								});

								var projectMenu = document.getElementById('projectMenu');
								if (projectMenu) {
									projectMenu.innerHTML = `<label><input type='checkbox' id='selectAllProjects' value='ALL' checked onchange='toggleAllProjects(this)'> <b>Select All</b></label>
									<div style='height:1px; background:rgba(255,255,255,0.1); margin: 4px 0;'></div>`;

									var existingProjs = new Set();

									var sortedProjects = Array.from(projectMap.keys()).sort();

									sortedProjects.forEach(function(p) {
										if (!existingProjs.has(p)) {
											var projContainer = document.createElement('div');
											projContainer.style.display = 'flex';
											projContainer.style.flexDirection = 'column';
											projContainer.style.marginBottom = '4px';

											var lbl = document.createElement('label');
											lbl.style.fontWeight = 'bold';
											lbl.style.cursor = 'pointer';
											lbl.style.display = 'flex';
											lbl.style.alignItems = 'center';

											var inp = document.createElement('input');
											inp.type = 'checkbox';
											inp.value = p;
											inp.className = 'proj-cb';
											inp.checked = true;

											lbl.appendChild(inp);
											var boldText = document.createElement('b');
											boldText.textContent = ' ' + p;
											lbl.appendChild(boldText);
											projContainer.appendChild(lbl);

											var paksContainer = document.createElement('div');
											paksContainer.style.display = 'flex';
											paksContainer.style.flexDirection = 'column';
											paksContainer.style.paddingLeft = '18px';
											paksContainer.style.marginTop = '2px';
											paksContainer.style.gap = '2px';

											var paks = Array.from(projectMap.get(p)).sort();
											paks.forEach(function(pak) {
												var pakLbl = document.createElement('label');
												pakLbl.style.fontSize = '10px';
												pakLbl.style.color = '#B0BEC5';
												pakLbl.style.cursor = 'pointer';
												pakLbl.style.display = 'flex';
												pakLbl.style.alignItems = 'center';

												var pakInp = document.createElement('input');
												pakInp.type = 'checkbox';
												pakInp.value = p + '|' + pak;
												pakInp.id = 'cb-' + p.replace(/[^a-zA-Z0-9]/g, '_') + '-' + pak.replace(/[^a-zA-Z0-9]/g, '_');
												pakInp.className = 'pak-cb';
												pakInp.dataset.project = p;
												pakInp.checked = true;

												pakInp.onchange = function() {
													updateProjectCheckboxState(inp, paksContainer);
													updateSelectAllProjects();
													filterNodes();
												};

												pakLbl.appendChild(pakInp);
												pakLbl.appendChild(document.createTextNode(' ' + pak));
												paksContainer.appendChild(pakLbl);
											});

											inp.onchange = function() {
												var isChecked = this.checked;
												this.indeterminate = false;
												var childPaks = paksContainer.querySelectorAll('.pak-cb');
												childPaks.forEach(function(cp) { cp.checked = isChecked; });
												updateSelectAllProjects();
												filterNodes();
											};

											projContainer.appendChild(paksContainer);
											projectMenu.appendChild(projContainer);
											existingProjs.add(p);
										}
									});
								}

								var hasMissing = cy.nodes('[type=""MISSING""]').length > 0;
								var missingBtn = document.getElementById('btnShowMissing');
								if (missingBtn) {
									missingBtn.style.display = hasMissing ? 'flex' : 'none';
								}
							} else {
								dLog('WARNING: No nodes received from JSON.');
							}

							if (!newElements || newElements.length === 0) {
								dLog('SKIP: No data -> minimap not triggered.');
							} else {
								cy.off('layoutstop');
								cy.one('layoutstop', function() {
									dLog('Layout stabilized. Ready.');
									updateEdgeStyle();
									updateGlowingEdgesCache();
									updateMiniMap();
								});

								dLog('Triggering layout engine...');
								runLayout();
							}
						} else {
							var errMsg = 'ERROR: Cytoscape (cy) is not defined yet when injecting JSON!';
							console.error(errMsg);
							if (typeof dLog !== 'undefined') dLog(errMsg);
						}
					} catch(e) {
						console.error('Error in RenderJsonData injection logic:', e);
						if (typeof dLog !== 'undefined') dLog('ERROR in injection: ' + e.message);
					}
				";
		}
		#endregion
	}
}