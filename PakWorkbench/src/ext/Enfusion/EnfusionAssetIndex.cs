using PakWorkbench.src;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PakWorkbench.src.ext.Enfusion
{
	#region Models & Enums

	public enum EnfusionAssetCategory
	{
		Unknown,
		Vehicle,
		Weapon,
		Mission,
		Character,
		Prop
	}

	public enum AssetTokenType { Identifier, String, BraceOpen, BraceClose, End }

	public class AssetNode
	{
		public string Id { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
		public string Type { get; set; } = string.Empty;
		public EnfusionAssetCategory Category { get; set; } = EnfusionAssetCategory.Unknown;
		public List<AssetEdge> Edges { get; set; } = [];
		public bool IsSearchTarget { get; set; } = false;
		public bool IsTargetWav { get; set; } = false;
		public string ProjectId { get; set; } = "unknown";
		public string Origin { get; set; } = string.Empty;
		public HashSet<string> EmbeddedElements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
		public List<string> Tags { get; set; } = [];
	}

	public class AssetEdge
	{
		public AssetNode Target { get; set; } = null!;
		public string Relation { get; set; } = string.Empty;
	}

	public class AssetGraph
	{
		public ConcurrentDictionary<string, AssetNode> Nodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

		public AssetGraph BuildSubGraph(string targetId, int maxDepth, string? searchTerm = null)
		{
			var subGraph = new AssetGraph();
			if (string.IsNullOrEmpty(targetId)) return subGraph;

			var globalNodes = this.Nodes;
			if (globalNodes == null || globalNodes.IsEmpty) return subGraph;

			string actualTargetId = targetId.ToLower();

			if (!globalNodes.ContainsKey(actualTargetId))
			{
				var match = globalNodes.Keys.FirstOrDefault(k => k.EndsWith(actualTargetId, StringComparison.OrdinalIgnoreCase) ||
																 k.Contains(actualTargetId, StringComparison.OrdinalIgnoreCase));
				if (match != null) actualTargetId = match;
				else return subGraph;
			}

			var incomingMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var node in globalNodes.Values)
			{
				if (node.Edges == null) continue;
				foreach (var edge in node.Edges)
				{
					if (edge.Target == null || string.IsNullOrEmpty(edge.Target.Id)) continue;
					if (!incomingMap.TryGetValue(edge.Target.Id, out var incomingList))
					{
						incomingList = new List<string>(4);
						incomingMap[edge.Target.Id] = incomingList;
					}
					incomingList.Add(node.Id);
				}
			}

			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var queue = new Queue<(string Id, int Depth)>();
			int maxNodesLimit = 300;

			queue.Enqueue((actualTargetId, 0));
			visited.Add(actualTargetId);

			while (queue.Count > 0)
			{
				if (visited.Count >= maxNodesLimit) break;

				var (currentId, depth) = queue.Dequeue();

				if (!globalNodes.TryGetValue(currentId, out var globalNode)) continue;

				if (depth < maxDepth)
				{
					if (globalNode.Edges != null)
					{
						int expandedSounds = 0;
						foreach (var edge in globalNode.Edges)
						{
							if (edge.Target == null || string.IsNullOrEmpty(edge.Target.Id)) continue;

							if (globalNode.Type == "ACP" && edge.Target.Type == "SOUND")
							{
								bool isMatch = !string.IsNullOrEmpty(searchTerm) &&
											   edge.Target.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);

								if (!isMatch)
								{
									if (expandedSounds >= 1) continue;
									expandedSounds++;
								}
							}

							if (visited.Add(edge.Target.Id))
							{
								queue.Enqueue((edge.Target.Id, depth + 1));
								if (visited.Count >= maxNodesLimit) break;
							}
						}
					}

					if (visited.Count >= maxNodesLimit) break;

					if (incomingMap.TryGetValue(currentId, out var incoming))
					{
						foreach (var parentId in incoming)
						{
							if (visited.Add(parentId))
							{
								queue.Enqueue((parentId, depth + 1));
								if (visited.Count >= maxNodesLimit) break;
							}
						}
					}
				}
			}

			string realTargetNodeId = actualTargetId;
			if (actualTargetId.EndsWith(".acp", StringComparison.OrdinalIgnoreCase))
			{
				if (globalNodes.TryGetValue(actualTargetId, out var acpNode) && acpNode.Edges != null)
				{
					var targetSound = acpNode.Edges.FirstOrDefault(e => e.Target.Type == "SOUND" && !string.IsNullOrEmpty(searchTerm) && e.Target.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
					realTargetNodeId = targetSound != null ? targetSound.Target.Id :
						(acpNode.Edges.FirstOrDefault(e => e.Target.Type == "SOUND")?.Target.Id ?? actualTargetId);
				}
			}

			foreach (var id in visited)
			{
				if (!globalNodes.TryGetValue(id, out var gNode)) continue;

				bool isTarget = id.Equals(realTargetNodeId, StringComparison.OrdinalIgnoreCase) ||
								id.Equals(actualTargetId, StringComparison.OrdinalIgnoreCase);

				bool isSearchMatch = !string.IsNullOrEmpty(searchTerm) &&
									 (gNode.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
									  gNode.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

				bool isWavForTarget = false;
				if (gNode.Type == "WAV" && !isTarget)
				{
					if (globalNodes.TryGetValue(realTargetNodeId, out var rootNode) && rootNode.Edges != null)
					{
						isWavForTarget = rootNode.Edges.Any(e => e.Target != null && e.Target.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
					}
				}

				subGraph.Nodes[id] = new()
				{
					Id = gNode.Id,
					Name = gNode.Name,
					Type = gNode.Type,
					Category = gNode.Category,
					Edges = [],
					IsSearchTarget = isTarget || isSearchMatch,
					IsTargetWav = isWavForTarget,
					EmbeddedElements = new HashSet<string>(gNode.EmbeddedElements, StringComparer.OrdinalIgnoreCase),
					Tags = [.. gNode.Tags],
					ProjectId = gNode.ProjectId,
					Origin = gNode.Origin
				};
			}

			foreach (var id in visited)
			{
				if (!globalNodes.TryGetValue(id, out var gNode) || gNode.Edges == null) continue;
				if (!subGraph.Nodes.TryGetValue(id, out var subNode)) continue;

				foreach (var edge in gNode.Edges)
				{
					if (edge.Target != null && subGraph.Nodes.TryGetValue(edge.Target.Id, out var targetNode))
					{
						subNode.Edges.Add(new()
						{
							Target = targetNode,
							Relation = edge.Relation
						});
					}
				}
			}

			return subGraph;
		}
	}

	#endregion

	#region Tokenizer Core

	public ref struct AssetTokenByte
	{
		public AssetTokenType Type;
		public ReadOnlySpan<byte> Value;
		public readonly string GetString() => Encoding.UTF8.GetString(Value);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public readonly bool EqualsStringIgnoreCase(string text)
		{
			if (Value.Length != text.Length) return false;
			ReadOnlySpan<byte> valSpan = Value;
			for (int i = 0; i < text.Length; i++)
			{
				uint b = valSpan[i];
				uint c = text[i];
				if (b - 'A' <= 'Z' - 'A') b += 32;
				if (c - 'A' <= 'Z' - 'A') c += 32;
				if (b != c) return false;
			}
			return true;
		}
	}

	public ref struct TokenizerByte
	{
		private readonly ReadOnlySpan<byte> _text;
		private int _pos;
		public TokenizerByte(ReadOnlySpan<byte> text) { _text = text; _pos = 0; }

		public AssetTokenByte Next()
		{
			while (_pos < _text.Length && _text[_pos] <= 32) _pos++;
			if (_pos >= _text.Length) return new AssetTokenByte { Type = AssetTokenType.End };
			byte c = _text[_pos];

			if (c == 34)
			{
				_pos++;
				int start = _pos;
				int quoteIdx = _text[_pos..].IndexOf((byte)34);
				if (quoteIdx >= 0)
				{
					var mem = _text.Slice(start, quoteIdx);
					_pos += quoteIdx + 1;
					return new AssetTokenByte { Type = AssetTokenType.String, Value = mem };
				}
				else
				{
					var mem = _text[start..];
					_pos = _text.Length;
					return new AssetTokenByte { Type = AssetTokenType.String, Value = mem };
				}
			}
			if (c == 123) { _pos++; return new AssetTokenByte { Type = AssetTokenType.BraceOpen }; }
			if (c == 125) { _pos++; return new AssetTokenByte { Type = AssetTokenType.BraceClose }; }

			int idStart = _pos;
			while (_pos < _text.Length && _text[_pos] > 32 && _text[_pos] != 123 && _text[_pos] != 125) _pos++;
			return new AssetTokenByte { Type = AssetTokenType.Identifier, Value = _text[idStart.._pos] };
		}
	}

	#endregion

	#region Utilities

	internal static class PakUtils
	{
		public static string GetPakFilePath(Pak pak)
		{
			if (pak == null) return string.Empty;
			try
			{
				var type = pak.GetType();
				return (type.GetProperty("FilePath")?.GetValue(pak) as string) ??
					   (type.GetProperty("FileName")?.GetValue(pak) as string) ??
					   (type.GetField("FilePath")?.GetValue(pak) as string) ??
					   (type.GetField("FileName")?.GetValue(pak) as string) ??
					   (type.GetField("name")?.GetValue(pak) as string) ??
					   (type.GetProperty("name")?.GetValue(pak) as string) ?? string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		}
	}

	#endregion

	public static partial class EnfusionAssetIndex
	{
		#region Global Properties & Constants

		public static readonly ConcurrentDictionary<string, string> SoundMap = new(StringComparer.OrdinalIgnoreCase);
		public static readonly ConcurrentDictionary<string, string> ShaderMap = new(StringComparer.OrdinalIgnoreCase);
		public static readonly ConcurrentDictionary<string, List<string>> EntityTree = new(StringComparer.OrdinalIgnoreCase);
		public static readonly AssetGraph GlobalGraph = new();

		public static readonly ConcurrentDictionary<string, bool> ParsedFiles = new(StringComparer.OrdinalIgnoreCase);
		public static readonly ConcurrentDictionary<string, AssetNode> EmatNodes = new(StringComparer.OrdinalIgnoreCase);
		private static readonly ConcurrentDictionary<string, string> ProjectIdPakCache = new(StringComparer.OrdinalIgnoreCase);

		private static bool _isIndexed = false;
		public static bool IsIndexed => _isIndexed;

		private static readonly object LayoutDebugLock = new();

		[GeneratedRegex(@"\""(\{[0-9A-Fa-f]{16}\}[^\""]+\.[a-zA-Z0-9]+|[^\""]+\.[a-zA-Z0-9]+)\""", RegexOptions.IgnoreCase)]
		private static partial Regex GetScriptPathRegex();

		[GeneratedRegex(@"ID\s+""([^""]+)""", RegexOptions.IgnoreCase)]
		private static partial Regex GetProjectIdRegex();

		private static readonly HashSet<string> SignificantBlocks = new(StringComparer.OrdinalIgnoreCase)
		{
			"RunBT", "Sequence", "State", "AnimSetInstanceSource_Line",
			"SignalInputClass", "SignalOutputClass", "ConnectionClass",
			"TemplateClass", "EntityPrefab",
			"IOPItemInputClass", "Node", "Event"
		};

		private static readonly HashSet<string> TrackedExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".et", ".conf", ".emat", ".xob", ".edds", ".acp", ".pap", ".siga", ".ptc", ".asi", ".bt", ".anm", ".ct", ".imageset", ".c", ".layout", ".styles"
		};

		#endregion

		#region Core Indexing Logic

		public static Task BuildIndexAsync(List<Pak> paks, Action<string, int, int> onStatusUpdate, Action? postProcessingAction = null)
		{
			return Task.Run(async () =>
			{
				_isIndexed = false;
				var cacheDb = new EnfusionSqliteCache();

				if (cacheDb.HasCache())
				{
					onStatusUpdate("💾 Incremental Scan: Loading existing graph from SQLite cache...", 0, 100);
					try
					{
						GlobalGraph.Nodes = await cacheDb.LoadGraphDataAsync(onStatusUpdate);
					}
					catch (Exception ex)
					{
						onStatusUpdate($"⚠️ Cache load failed ({ex.Message}), performing full scan...", 0, 100);
						GlobalGraph.Nodes.Clear();
					}
				}
				else
				{
					GlobalGraph.Nodes.Clear();
				}

				var fileDatesCache = await cacheDb.LoadFileIndexCacheAsync();
				var updatedFileDates = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

				SoundMap.Clear();
				ShaderMap.Clear();
				EntityTree.Clear();
				ParsedFiles.Clear();
				EmatNodes.Clear();

				onStatusUpdate("🌐 Discovery Phase: Scanning for modified files...", 0, 100);

				List<(Pak sourcePak, PakEntryFile entry)> entriesToProcess = [];
				bool cacheNeedsUpdate = false;

				foreach (var pak in paks)
				{
					if (pak?.entries == null) continue;

					string pakPath = PakUtils.GetPakFilePath(pak);
					if (string.IsNullOrEmpty(pakPath)) pakPath = "unknown_pak";

					long pakLastWrite = 0;
					if (File.Exists(pakPath)) pakLastWrite = File.GetLastWriteTimeUtc(pakPath).Ticks;

					DetermineProjectId(pak, pakPath);

					foreach (var entry in pak.entries)
					{
						if (string.IsNullOrEmpty(entry?.name)) continue;
						string ext = Path.GetExtension(entry.name).ToLowerInvariant();

						if (TrackedExtensions.Contains(ext))
						{
							string entryKey = $"{Path.GetFileName(pakPath)}::{entry.name}".ToLowerInvariant();
							updatedFileDates[entryKey] = pakLastWrite;

							if (fileDatesCache.TryGetValue(entryKey, out long cachedTime) && cachedTime >= pakLastWrite)
							{
								continue;
							}

							entriesToProcess.Add((pak, entry));
							cacheNeedsUpdate = true;
						}
					}
				}

				int totalFiles = entriesToProcess.Count;

				if (totalFiles == 0)
				{
					_isIndexed = true;
					onStatusUpdate($"✅ Incremental Scan Complete. Graph Viewer Ready. ({GlobalGraph.Nodes.Count} nodes cached)", 100, 100);
					return;
				}

				onStatusUpdate($"⚡ Incremental Indexing: Processing {totalFiles} modified/new files...", 0, 100);

				foreach (var (sourcePak, entry) in entriesToProcess)
				{
					string fileId = Path.GetFileName(entry.name).ToLowerInvariant();
					if (GlobalGraph.Nodes.TryGetValue(fileId, out var existingNode))
					{
						lock (existingNode.Edges) existingNode.Edges.Clear();
						lock (existingNode.EmbeddedElements) existingNode.EmbeddedElements.Clear();
					}
				}

				int processedCount = 0;
				var threadLocalGraphs = new ThreadLocal<Dictionary<string, AssetNode>>(() => new Dictionary<string, AssetNode>(StringComparer.OrdinalIgnoreCase), true);

				Parallel.ForEach(entriesToProcess, new() { MaxDegreeOfParallelism = Environment.ProcessorCount }, item =>
				{
					var (sourcePak, entry) = item;
					var localNodes = threadLocalGraphs.Value;
					EnsureFileIndexedThreadLocal(entry.name, sourcePak, entry, localNodes!);

					int current = Interlocked.Increment(ref processedCount);
					if (current % 100 == 0 || current == totalFiles)
						onStatusUpdate($"⚡ Parsing modified configurations: {current}/{totalFiles}...", current, totalFiles);
				});

				onStatusUpdate("🔄 Merging updated nodes into global graph...", 95, 100);

				foreach (var localGraph in threadLocalGraphs.Values)
				{
					foreach (var kvp in localGraph)
					{
						var globalNode = GlobalGraph.Nodes.GetOrAdd(kvp.Key, k => kvp.Value);

						MergeLocalNodeToGlobal(globalNode, kvp.Value, kvp.Key);

						foreach (var edge in kvp.Value.Edges)
						{
							if (edge.Target == null) continue;
							var globalTarget = GlobalGraph.Nodes.GetOrAdd(edge.Target.Id, k => edge.Target);
							lock (globalNode.Edges)
							{
								AddUniqueEdge(globalNode, globalTarget, edge.Relation, checkRelation: true);
							}
						}
					}
				}
				threadLocalGraphs.Dispose();

				foreach (var nodeId in GlobalGraph.Nodes.Keys)
				{
					ResolveNodeCrossLinks(nodeId);
				}

				_isIndexed = true;
				onStatusUpdate($"✅ Incremental Scan Complete. Tracked {GlobalGraph.Nodes.Count} global unified nodes.", 100, 100);

				postProcessingAction?.Invoke();

				if (cacheNeedsUpdate)
				{
					_ = Task.Run(async () =>
					{
						try
						{
							await cacheDb.SaveGraphDataAsync(GlobalGraph.Nodes.Values);
							await cacheDb.SaveFileIndexCacheAsync(updatedFileDates);
						}
						catch (Exception ex)
						{
							Console.WriteLine($"⚠️ SQLite Cache Update Error: {ex.Message}");
						}
					});
				}
			});
		}

		public static void EnsureFileIndexed(string path, List<Pak> paks)
		{
			var localNodes = new Dictionary<string, AssetNode>(StringComparer.OrdinalIgnoreCase);
			string fullPathId = path.ToLowerInvariant();
			if (!ParsedFiles.TryAdd(fullPathId, true)) return;

			List<Pak> paksSnapshot;
			lock (paks) paksSnapshot = [.. paks];

			foreach (var pak in paksSnapshot)
			{
				if (pak?.entries == null) continue;
				List<PakEntryFile> entriesSnapshot;
				lock (pak.entries) entriesSnapshot = [.. pak.entries];

				var entry = entriesSnapshot.FirstOrDefault(e => e.name.Equals(fullPathId, StringComparison.OrdinalIgnoreCase));
				if (entry != null)
				{
					EnsureFileIndexedThreadLocal(path, pak, entry, localNodes);

					foreach (var kvp in localNodes)
					{
						var globalNode = GlobalGraph.Nodes.GetOrAdd(kvp.Key, k => kvp.Value);
						MergeLocalNodeToGlobal(globalNode, kvp.Value, kvp.Key);

						foreach (var edge in kvp.Value.Edges)
						{
							if (edge.Target == null) continue;
							var globalTarget = GlobalGraph.Nodes.GetOrAdd(edge.Target.Id, k => edge.Target);
							lock (globalNode.Edges)
							{
								AddUniqueEdge(globalNode, globalTarget, edge.Relation, checkRelation: true);
							}
						}
					}
					break;
				}
			}
		}

		private static void EnsureFileIndexedThreadLocal(string path, Pak targetPak, PakEntryFile targetEntry, Dictionary<string, AssetNode> localNodes)
		{
			string fullPathId = path.ToLowerInvariant();
			ParsedFiles.TryAdd(fullPathId, true);
			string fileNameId = Path.GetFileName(path).ToLowerInvariant();

			string rawPakPath = PakUtils.GetPakFilePath(targetPak);
			string pakPath = string.IsNullOrEmpty(rawPakPath) ? "unknown_pak" : Path.GetFileName(rawPakPath);
			string projectId = DetermineProjectId(targetPak, rawPakPath);

			try
			{
				string ext = Path.GetExtension(fileNameId);
				if (ext == ".edds" || ext == ".xob" || ext == ".anm" || ext == ".pap" || ext == ".wav")
				{
					GetOrAddLocalNode(localNodes, fileNameId, Path.GetFileName(path), ext.TrimStart('.').ToUpperInvariant(), projectId, pakPath);
					return;
				}

				byte[] data = targetPak.GetFileBytes(targetEntry);
				if (data != null && data.Length > 0)
				{
					ReadOnlySpan<byte> spanData = new(data);

					if (ext == ".acp") ParseAcp(fileNameId, spanData, path, localNodes, projectId, pakPath);
					else if (ext == ".emat") ParseEmat(fileNameId, spanData, path, localNodes, projectId, pakPath);
					else if (ext == ".imageset") ParseImageset(fileNameId, spanData, path, localNodes, projectId, pakPath);
					else if (ext == ".c") ParseEnforceScript(fileNameId, spanData, path, localNodes, projectId, pakPath);
					else if (ext == ".layout") ParseLayoutWidgets(fileNameId, spanData, path, localNodes, projectId, pakPath);
					else ParseUniversalEnfusion(fileNameId, spanData, path, localNodes, projectId, pakPath);
				}
			}
			catch { }
		}

		public static string DetermineProjectId(Pak pak, string pakPath)
		{
			if (ProjectIdPakCache.TryGetValue(pakPath, out string? cachedProjectId) && cachedProjectId != null)
				return cachedProjectId;

			string projectId = "unknown";
			try
			{
				if (pak?.entries != null)
				{
					var gprojEntry = pak.entries.FirstOrDefault(e =>
						!string.IsNullOrEmpty(e.name) &&
						e.name.EndsWith(".gproj", StringComparison.OrdinalIgnoreCase));

					if (gprojEntry != null)
					{
						byte[] data = pak.GetFileBytes(gprojEntry);
						if (data != null && data.Length > 0)
						{
							string content = Encoding.UTF8.GetString(data);
							Match match = GetProjectIdRegex().Match(content);
							if (match.Success) projectId = match.Groups[1].Value;
						}
					}
				}

				if (projectId == "unknown" && !string.IsNullOrEmpty(pakPath))
				{
					string? currentDir = Path.GetDirectoryName(Path.GetFullPath(pakPath));

					while (!string.IsNullOrEmpty(currentDir))
					{
						if (Directory.Exists(currentDir))
						{
							var gprojFiles = Directory.GetFiles(currentDir, "*.gproj");
							if (gprojFiles.Length > 0)
							{
								string selectedGproj = gprojFiles[0];
								if (gprojFiles.Length > 1)
								{
									string? bestMatch = gprojFiles.FirstOrDefault(g =>
										pakPath.Contains(Path.GetFileNameWithoutExtension(g), StringComparison.OrdinalIgnoreCase));
									if (bestMatch != null) selectedGproj = bestMatch;
								}

								try
								{
									string content = File.ReadAllText(selectedGproj);
									Match match = GetProjectIdRegex().Match(content);
									if (match.Success)
									{
										projectId = match.Groups[1].Value;
									}
								}
								catch { }

								if (projectId == "unknown")
								{
									projectId = Path.GetFileNameWithoutExtension(selectedGproj) ?? "unknown";
								}

								break;
							}
						}

						DirectoryInfo? parent = Directory.GetParent(currentDir);
						if (parent == null) break;
						currentDir = parent.FullName;
					}
				}
			}
			catch { }

			if (string.IsNullOrEmpty(projectId)) projectId = "unknown";

			ProjectIdPakCache[pakPath] = projectId;
			return projectId;
		}

		#endregion

		#region Format Parsers

		private static void ParseUniversalEnfusion(string fileId, ReadOnlySpan<byte> contentSpan, string fullPath, Dictionary<string, AssetNode> localNodes, string projectId, string origin)
		{
			string quickCheck = Encoding.UTF8.GetString(contentSpan[..Math.Min(contentSpan.Length, 1024)]);
			if (quickCheck.Contains("StringTableRuntime") && quickCheck.Contains("Ids") && quickCheck.Contains("Texts"))
			{
				ParseStringTableConf(fileId, contentSpan, fullPath, localNodes, projectId, origin);
				return;
			}

			var tz = new TokenizerByte(contentSpan);
			string fileExt = Path.GetExtension(fileId).TrimStart('.').ToUpperInvariant();
			if (string.IsNullOrEmpty(fileExt)) fileExt = "CONF";

			var fileNode = GetOrAddLocalNode(localNodes, fileId, Path.GetFileName(fullPath), fileExt, projectId, origin);

			Stack<AssetNode> nodeStack = new();
			nodeStack.Push(fileNode);
			Stack<string> blockStack = new();
			string lastIdentifier = "";
			int subNodeCounter = 0;
			bool expectParentPrefab = false;

			while (true)
			{
				var t = tz.Next();
				if (t.Type == AssetTokenType.End) break;

				if (t.Type == AssetTokenType.Identifier)
				{
					string rawId = t.GetString();

					if (fileNode.Category == EnfusionAssetCategory.Unknown)
					{
						if (rawId == "SCR_MissionHeader")
						{
							fileNode.Category = EnfusionAssetCategory.Mission;
							fileNode.Tags.Add("Mission");
						}
						else if (rawId == "VehicleWheeledSimulation" || rawId == "CarControllerComponent" || rawId == "HelicopterControllerComponent")
						{
							fileNode.Category = EnfusionAssetCategory.Vehicle;
							fileNode.Tags.Add("Vehicle");
						}
						else if (rawId == "WeaponComponent" || rawId == "MuzzleComponent" || rawId == "MagazineComponent")
						{
							fileNode.Category = EnfusionAssetCategory.Weapon;
							fileNode.Tags.Add("Weapon");
						}
						else if (rawId == "SCR_ChimeraCharacter" || rawId == "CharacterAnimationComponent")
						{
							fileNode.Category = EnfusionAssetCategory.Character;
							fileNode.Tags.Add("Character");
						}
						else if (rawId == "StaticModelEntity" || rawId == "SCR_DestructibleEntity")
						{
							fileNode.Category = EnfusionAssetCategory.Prop;
							fileNode.Tags.Add("Prop");
						}
					}

					if (rawId == ":") expectParentPrefab = true;
					else if (rawId.EndsWith(':')) { lastIdentifier = rawId[..^1]; expectParentPrefab = true; }
					else { lastIdentifier = rawId; expectParentPrefab = false; }
				}
				else if (t.Type == AssetTokenType.BraceOpen)
				{
					blockStack.Push(lastIdentifier);
					expectParentPrefab = false;

					if (IsSignificantBlock(lastIdentifier))
					{
						subNodeCounter++;
						string subNodeId = $"{fileId}|{lastIdentifier}_{subNodeCounter}".ToLowerInvariant();
						var subNode = GetOrAddLocalNode(localNodes, subNodeId, lastIdentifier, "NODE", projectId, origin);

						var parentNode = nodeStack.Peek();
						AddUniqueEdge(parentNode, subNode, "contains_block");
						nodeStack.Push(subNode);
					}
					else
					{
						nodeStack.Push(nodeStack.Peek());
					}
				}
				else if (t.Type == AssetTokenType.BraceClose)
				{
					if (blockStack.Count > 0) blockStack.Pop();
					if (nodeStack.Count > 1) nodeStack.Pop();
				}
				else if (t.Type == AssetTokenType.String || t.Type == AssetTokenType.Identifier)
				{
					string val = t.GetString();
					var currentNode = nodeStack.Peek();

					if (currentNode != fileNode && (lastIdentifier.Equals("Name", StringComparison.OrdinalIgnoreCase) || lastIdentifier.Equals("id", StringComparison.OrdinalIgnoreCase) || lastIdentifier.Equals("class", StringComparison.OrdinalIgnoreCase)))
					{
						if (t.Type == AssetTokenType.String) currentNode.Name = $"{blockStack.Peek()}: {val}";
						else if (t.Type == AssetTokenType.Identifier) currentNode.Name = $"{blockStack.Peek()} ID: {val}";
					}

					if (t.Type == AssetTokenType.String)
					{
						if (lastIdentifier.Equals("Name", StringComparison.OrdinalIgnoreCase))
						{
							string activeClass = blockStack.Count > 0 ? blockStack.Peek() : "Entity";
							string entry = $"{val} ({activeClass})";
							currentNode.EmbeddedElements.Add(entry);
						}

						string cleanPath = StripEnfusionGuid(val);
						if (cleanPath.Contains('.') && !cleanPath.Contains(' '))
						{
							string ext = Path.GetExtension(cleanPath).ToLowerInvariant();
							if (IsTrackedExtension(ext))
							{
								string targetId = Path.GetFileName(cleanPath).ToLowerInvariant();
								string targetType = ext.TrimStart('.').ToUpperInvariant();
								var targetNode = GetOrAddLocalNode(localNodes, targetId, Path.GetFileName(cleanPath), targetType, projectId, origin);

								if (expectParentPrefab)
								{
									AddUniqueEdge(currentNode, targetNode, "inherits_from");
								}
								else
								{
									string relation = "references";
									if (lastIdentifier.Contains("BehaviorTree", StringComparison.OrdinalIgnoreCase) || ext == ".bt") relation = "runs_behavior";
									else if (lastIdentifier.Contains("Anim", StringComparison.OrdinalIgnoreCase) || ext == ".anm") relation = "plays_anim";
									else if (lastIdentifier.Contains("Mesh", StringComparison.OrdinalIgnoreCase) || ext == ".xob") relation = "uses_mesh";
									else if (ext == ".emat") relation = "uses_material";
									else if (ext == ".edds") relation = "binds_texture";
									else if (lastIdentifier.Contains("Layout", StringComparison.OrdinalIgnoreCase) || ext == ".layout") relation = "uses_layout";

									AddUniqueEdge(currentNode, targetNode, relation);
								}
							}
						}
					}
				}
			}
		}

		private static void ParseStringTableConf(string fileId, ReadOnlySpan<byte> contentSpan, string fullPath, Dictionary<string, AssetNode> localNodes, string projectId, string origin)
		{
			var tz = new TokenizerByte(contentSpan);
			var fileNode = GetOrAddLocalNode(localNodes, fileId, Path.GetFileName(fullPath), "L10N", projectId, origin);
			List<string> idsList = [];
			List<string> textsList = [];
			string currentArrayContext = "";

			while (true)
			{
				var t = tz.Next();
				if (t.Type == AssetTokenType.End) break;
				if (t.Type == AssetTokenType.Identifier)
				{
					if (t.EqualsStringIgnoreCase("Ids")) currentArrayContext = "IDS";
					else if (t.EqualsStringIgnoreCase("Texts")) currentArrayContext = "TEXTS";
				}
				else if (t.Type == AssetTokenType.String)
				{
					string rawStr = t.GetString();
					if (currentArrayContext == "IDS") idsList.Add(rawStr);
					else if (currentArrayContext == "TEXTS") textsList.Add(rawStr);
				}
			}

			int count = Math.Min(idsList.Count, textsList.Count);
			for (int i = 0; i < count; i++)
			{
				string key = idsList[i];
				string val = textsList[i];
				string stringNodeId = $"{fileId}|{key}".ToLowerInvariant();
				var stringNode = GetOrAddLocalNode(localNodes, stringNodeId, $"{key} (\"{val}\")", "NODE", projectId, origin);
				AddUniqueEdge(fileNode, stringNode, "contains_string");
			}
		}

		private static void ParseAcp(string fileId, ReadOnlySpan<byte> contentSpan, string fullPath, Dictionary<string, AssetNode> localNodes, string projectId, string origin)
		{
			var tz = new TokenizerByte(contentSpan);
			var fileNode = GetOrAddLocalNode(localNodes, fileId, Path.GetFileName(fullPath), "ACP", projectId, origin);

			Dictionary<string, List<int>> soundLinks = [];
			Dictionary<int, string> bankIdToWav = [];

			string? currentSoundNodeId = null;
			int? currentBankId = null;
			bool inSoundClass = false;
			bool inBankLocalClass = false;

			while (true)
			{
				var t = tz.Next();
				if (t.Type == AssetTokenType.End) break;
				if (t.Type == AssetTokenType.Identifier)
				{
					if (t.EqualsStringIgnoreCase("SoundClass")) { inSoundClass = true; currentSoundNodeId = null; }
					else if (t.EqualsStringIgnoreCase("BankLocalClass")) { inBankLocalClass = true; currentBankId = null; }
					else if (t.EqualsStringIgnoreCase("selectors") || t.EqualsStringIgnoreCase("mixers")) { inSoundClass = false; inBankLocalClass = false; }

					if (inSoundClass)
					{
						if (t.EqualsStringIgnoreCase("name"))
						{
							var val = tz.Next();
							if (val.Type == AssetTokenType.String)
							{
								string soundName = val.GetString();
								currentSoundNodeId = (fileId + "|" + soundName).ToLowerInvariant();
								var soundNode = GetOrAddLocalNode(localNodes, currentSoundNodeId, soundName, "SOUND", projectId, origin);
								AddUniqueEdge(fileNode, soundNode, "contains_sound");
							}
						}
						else if (t.EqualsStringIgnoreCase("id") && currentSoundNodeId != null)
						{
							var val = tz.Next();
							if (val.Type == AssetTokenType.Identifier && int.TryParse(val.GetString(), out int linkId))
							{
								if (!soundLinks.TryGetValue(currentSoundNodeId, out var list)) { list = []; soundLinks[currentSoundNodeId] = list; }
								list.Add(linkId);
							}
						}
					}
					else if (inBankLocalClass)
					{
						if (t.EqualsStringIgnoreCase("id"))
						{
							var val = tz.Next();
							if (val.Type == AssetTokenType.Identifier && int.TryParse(val.GetString(), out int bId)) currentBankId = bId;
						}
						else if (t.EqualsStringIgnoreCase("Filename") && currentBankId != null)
						{
							var val = tz.Next();
							if (val.Type == AssetTokenType.String) bankIdToWav[currentBankId.Value] = val.GetString();
						}
					}
				}
			}

			foreach (var pair in soundLinks)
			{
				string soundNodeId = pair.Key;
				if (!localNodes.TryGetValue(soundNodeId, out var soundNode)) continue;
				foreach (int bankId in pair.Value)
				{
					if (bankIdToWav.TryGetValue(bankId, out string? wavPath))
					{
						string wavId = Path.GetFileName(wavPath).ToLowerInvariant();
						SoundMap[soundNodeId] = wavPath;
						var wavNode = GetOrAddLocalNode(localNodes, wavId, Path.GetFileName(wavPath), "WAV", projectId, origin);
						AddUniqueEdge(soundNode, wavNode, "plays_wav");
					}
				}
			}
		}

		private static void ParseEmat(string fileId, ReadOnlySpan<byte> contentSpan, string fullPath, Dictionary<string, AssetNode> localNodes, string projectId, string origin)
		{
			var tz = new TokenizerByte(contentSpan);
			var matNode = GetOrAddLocalNode(localNodes, fileId, Path.GetFileName(fullPath), "EMAT", projectId, origin);
			EmatNodes[fileId] = matNode;

			while (true)
			{
				var t = tz.Next();
				if (t.Type == AssetTokenType.End) break;
				if (t.Type == AssetTokenType.Identifier)
				{
					if (t.EqualsStringIgnoreCase("Shader"))
					{
						var val = tz.Next();
						if (val.Type == AssetTokenType.String)
						{
							string shader = val.GetString();
							ShaderMap[fullPath] = shader;
							string shaderId = Path.GetFileName(shader).ToLowerInvariant();
							var shaderNode = GetOrAddLocalNode(localNodes, shaderId, Path.GetFileName(shader), "SHADER", projectId, origin);
							AddUniqueEdge(matNode, shaderNode, "uses_shader");
						}
					}
					else if (t.EqualsStringIgnoreCase("Texture"))
					{
						var val = tz.Next();
						if (val.Type == AssetTokenType.String)
						{
							string tex = StripEnfusionGuid(val.GetString());
							string eddsId = Path.GetFileName(tex).ToLowerInvariant();
							var eddsNode = GetOrAddLocalNode(localNodes, eddsId, Path.GetFileName(tex), "EDDS", projectId, origin);
							AddUniqueEdge(matNode, eddsNode, "binds_texture");
						}
					}
				}
			}
		}

		private static void ParseImageset(string fileId, ReadOnlySpan<byte> contentSpan, string fullPath, Dictionary<string, AssetNode> localNodes, string projectId, string origin)
		{
			var tz = new TokenizerByte(contentSpan);
			var imgNode = GetOrAddLocalNode(localNodes, fileId, Path.GetFileName(fullPath), "IMAGESET", projectId, origin);

			while (true)
			{
				var t = tz.Next();
				if (t.Type == AssetTokenType.End) break;
				if (t.Type == AssetTokenType.Identifier && t.EqualsStringIgnoreCase("SourceValue"))
				{
					var val = tz.Next();
					if (val.Type == AssetTokenType.String)
					{
						string cleanTex = StripEnfusionGuid(val.GetString());
						string eddsId = Path.GetFileName(cleanTex).ToLowerInvariant();
						var eddsNode = GetOrAddLocalNode(localNodes, eddsId, Path.GetFileName(cleanTex), "EDDS", projectId, origin);
						AddUniqueEdge(imgNode, eddsNode, "source_texture");
					}
				}
			}
		}

		private static void ParseEnforceScript(string fileId, ReadOnlySpan<byte> contentSpan, string fullPath, Dictionary<string, AssetNode> localNodes, string projectId, string origin)
		{
			string content = Encoding.UTF8.GetString(contentSpan);
			var fileNode = GetOrAddLocalNode(localNodes, fileId, Path.GetFileName(fullPath), "C", projectId, origin);

			var pathMatches = GetScriptPathRegex().Matches(content);
			foreach (Match m in pathMatches)
			{
				string cleanPath = StripEnfusionGuid(m.Groups[1].Value);
				string ext = Path.GetExtension(cleanPath).ToLowerInvariant();

				if (IsTrackedExtension(ext))
				{
					string targetId = Path.GetFileName(cleanPath).ToLowerInvariant();
					string targetType = ext.TrimStart('.').ToUpperInvariant();
					var targetNode = GetOrAddLocalNode(localNodes, targetId, Path.GetFileName(cleanPath), targetType, projectId, origin);
					AddUniqueEdge(fileNode, targetNode, "scripts_reference");
				}
			}
		}

		private static void ParseLayoutWidgets(string fileId, ReadOnlySpan<byte> contentSpan, string fullPath, Dictionary<string, AssetNode> localNodes, string projectId, string origin)
		{
			string normalizedFileId = fileId.ToLowerInvariant();
			var layoutNode = GetOrAddLocalNode(localNodes, normalizedFileId, Path.GetFileName(fullPath), "LAYOUT", projectId, origin);
			var tz = new TokenizerByte(contentSpan);

			string currentDetectedClass = "";
			string lastProperty = "";
			Stack<string> classStack = new();

			while (true)
			{
				var t = tz.Next();
				if (t.Type == AssetTokenType.End) break;

				if (t.Type == AssetTokenType.Identifier)
				{
					string raw = t.GetString();

					if (raw.EndsWith("WidgetClass", StringComparison.OrdinalIgnoreCase))
					{
						currentDetectedClass = raw;
					}
					else if (!raw.Equals("Slot", StringComparison.OrdinalIgnoreCase) &&
							 !raw.EndsWith("WidgetSlot", StringComparison.OrdinalIgnoreCase))
					{
						lastProperty = raw;
					}
				}
				else if (t.Type == AssetTokenType.BraceOpen)
				{
					string classToPush = !string.IsNullOrEmpty(currentDetectedClass)
						? currentDetectedClass
						: (classStack.Count > 0 ? classStack.Peek() : "");

					classStack.Push(classToPush);

					currentDetectedClass = "";
					lastProperty = "";
				}
				else if (t.Type == AssetTokenType.BraceClose)
				{
					if (classStack.Count > 0) classStack.Pop();
					currentDetectedClass = "";
					lastProperty = "";
				}
				else if (t.Type == AssetTokenType.String)
				{
					string rawValue = t.GetString();

					if (lastProperty.Equals("Name", StringComparison.OrdinalIgnoreCase) && classStack.Count > 0)
					{
						string activeClass = classStack.Peek();

						if (!string.IsNullOrEmpty(activeClass) && !activeClass.Contains("Slot", StringComparison.OrdinalIgnoreCase))
						{
							string entry = $"{rawValue} ({activeClass})";
							layoutNode.EmbeddedElements.Add(entry);
						}
					}

					string cleanPath = StripEnfusionGuid(rawValue);
					if (cleanPath.Contains('.') && !cleanPath.Contains(' '))
					{
						string ext = Path.GetExtension(cleanPath).ToLowerInvariant();
						if (IsTrackedExtension(ext))
						{
							string targetId = Path.GetFileName(cleanPath).ToLowerInvariant();
							string targetType = ext.TrimStart('.').ToUpperInvariant();
							var targetNode = GetOrAddLocalNode(localNodes, targetId, Path.GetFileName(cleanPath), targetType, projectId, origin);
							AddUniqueEdge(layoutNode, targetNode, "references");
						}
					}

					lastProperty = "";
				}
			}
		}

		#endregion

		#region Helper Methods

		private static string StripEnfusionGuid(string input)
		{
			int start = input.IndexOf('{');
			if (start < 0) return input;

			int end = input.IndexOf('}', start);
			if (end > start)
				return string.Concat(input.AsSpan(0, start), input.AsSpan(end + 1));

			return input;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void AddUniqueEdge(AssetNode source, AssetNode target, string relation, bool checkRelation = false)
		{
			var edges = source.Edges;
			int count = edges.Count;
			for (int i = 0; i < count; i++)
			{
				var e = edges[i];
				if (e.Target.Id == target.Id)
				{
					if (!checkRelation || e.Relation == relation) return;
				}
			}
			edges.Add(new() { Target = target, Relation = relation });
		}

		private static AssetNode GetOrAddLocalNode(Dictionary<string, AssetNode> localNodes, string id, string name, string type, string projectId, string origin = "")
		{
			if (!localNodes.TryGetValue(id, out var node))
			{
				node = new() { Id = id, Name = name, Type = type, ProjectId = projectId, Origin = origin };
				localNodes[id] = node;
				return node;
			}

			bool isCurrentProjectUnknown = string.IsNullOrEmpty(node.ProjectId) || node.ProjectId == "unknown";
			bool isNewProjectValid = !string.IsNullOrEmpty(projectId) && projectId != "unknown";

			if (isCurrentProjectUnknown && isNewProjectValid)
			{
				node.ProjectId = projectId;
			}

			if (string.IsNullOrEmpty(node.Origin) && !string.IsNullOrEmpty(origin))
			{
				node.Origin = origin;
			}

			return node;
		}

		private static void MergeLocalNodeToGlobal(AssetNode globalNode, AssetNode localNode, string key)
		{
			if (globalNode == localNode) return;

			if (!string.IsNullOrEmpty(localNode.Type) && localNode.Type != "NODE")
				globalNode.Type = localNode.Type;

			if (!string.IsNullOrEmpty(localNode.Name) && localNode.Name != key)
				globalNode.Name = localNode.Name;

			if (localNode.Category != EnfusionAssetCategory.Unknown)
				globalNode.Category = localNode.Category;

			bool isGlobalProjectUnknown = string.IsNullOrEmpty(globalNode.ProjectId) || globalNode.ProjectId == "unknown";
			bool isLocalProjectValid = !string.IsNullOrEmpty(localNode.ProjectId) && localNode.ProjectId != "unknown";

			if (isGlobalProjectUnknown && isLocalProjectValid)
			{
				globalNode.ProjectId = localNode.ProjectId;
			}

			if (string.IsNullOrEmpty(globalNode.Origin) && !string.IsNullOrEmpty(localNode.Origin))
			{
				globalNode.Origin = localNode.Origin;
			}

			if (localNode.Tags != null && localNode.Tags.Count > 0)
			{
				lock (globalNode.Tags)
				{
					foreach (var tag in localNode.Tags)
						if (!globalNode.Tags.Contains(tag)) globalNode.Tags.Add(tag);
				}
			}

			if (localNode.EmbeddedElements.Count > 0)
			{
				lock (globalNode.EmbeddedElements)
					globalNode.EmbeddedElements.UnionWith(localNode.EmbeddedElements);
			}
		}

		private static void ResolveNodeCrossLinks(string nodeId)
		{
			if (GlobalGraph.Nodes.TryGetValue(nodeId, out var node) && node.Type == "WAV")
			{
				string cleanWavName = Path.GetFileNameWithoutExtension(node.Name).ToLowerInvariant();
				foreach (var targetNode in EmatNodes.Values)
				{
					if (targetNode.Id.Contains(cleanWavName))
					{
						AddUniqueEdge(node, targetNode, "suggested_material");
					}
				}
			}
		}

		private static bool IsSignificantBlock(string blockName)
		{
			if (blockName.EndsWith("WidgetClass", StringComparison.OrdinalIgnoreCase) || blockName.EndsWith("Widget", StringComparison.OrdinalIgnoreCase))
				return true;
			return SignificantBlocks.Contains(blockName);
		}

		private static bool IsTrackedExtension(string ext) => TrackedExtensions.Contains(ext);

		public static bool FilterByQueryUpgrade(string entryName, string query, out string cleanQuery)
		{
			cleanQuery = query;
			string ext = Path.GetExtension(entryName).ToLowerInvariant();

			if (query.StartsWith("sound:", StringComparison.OrdinalIgnoreCase))
			{
				cleanQuery = query[6..];
				return ext == ".acp";
			}
			if (query.StartsWith("mat:", StringComparison.OrdinalIgnoreCase) || query.StartsWith("shader:", StringComparison.OrdinalIgnoreCase))
			{
				bool isShaderQuery = query.StartsWith("shader:", StringComparison.OrdinalIgnoreCase);
				cleanQuery = isShaderQuery ? query[7..] : query[4..];

				if (ext != ".emat") return false;
				if (isShaderQuery && ShaderMap.TryGetValue(entryName, out string? shader))
					return shader.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase);

				return true;
			}
			if (query.StartsWith("anim:", StringComparison.OrdinalIgnoreCase))
			{
				cleanQuery = query[5..];
				return ext == ".pap" || ext == ".asi" || ext == ".bt" || ext == ".siga" || ext == ".ptc" || ext == ".ct" || ext == ".anm";
			}
			if (query.StartsWith("widget:", StringComparison.OrdinalIgnoreCase))
			{
				cleanQuery = query[7..];
				return ext == ".layout" || ext == ".styles" || ext == ".imageset";
			}
			if (query.StartsWith("prefab:", StringComparison.OrdinalIgnoreCase))
			{
				cleanQuery = query[7..];
				return ext == ".et";
			}
			if (query.StartsWith("script:", StringComparison.OrdinalIgnoreCase))
			{
				cleanQuery = query[7..];
				return ext == ".c";
			}
			if (query.StartsWith("texture:", StringComparison.OrdinalIgnoreCase))
			{
				cleanQuery = query[8..];
				return ext == ".edds";
			}
			if (query.StartsWith("model:", StringComparison.OrdinalIgnoreCase) || query.StartsWith("mesh:", StringComparison.OrdinalIgnoreCase))
			{
				int length = query.StartsWith("model:", StringComparison.OrdinalIgnoreCase) ? 6 : 5;
				cleanQuery = query[length..];
				return ext == ".xob";
			}
			return true;
		}

		#endregion
	}
}