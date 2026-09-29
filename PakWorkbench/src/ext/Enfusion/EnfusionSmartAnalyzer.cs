using PakWorkbench.src;
using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;

namespace PakWorkbench.src.ext.Enfusion
{
	#region Helper Structures and Classes
	internal struct ByteLine { public int Start; public int Length; }

	internal class LazyStringContext
	{
		public byte[] Data;
		public System.Collections.Generic.List<ByteLine> Lines = [];
		public string?[] Cache;
		[ThreadStatic] private static char[]? CharBuffer;

		public LazyStringContext(byte[] data)
		{
			Data = data;
			int start = 0;
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i] == '\n')
				{
					int len = i - start;
					if (len > 0 && data[i - 1] == '\r') len--;
					Lines.Add(new ByteLine { Start = start, Length = len });
					start = i + 1;
				}
			}
			if (start < data.Length) Lines.Add(new ByteLine { Start = start, Length = data.Length - start });
			Cache = new string?[Lines.Count];
		}

		public string GetString(int index)
		{
			if (index < 0 || index >= Lines.Count) return string.Empty;
			Cache[index] ??= Encoding.UTF8.GetString(Data, Lines[index].Start, Lines[index].Length);
			return Cache[index]!;
		}

		public ReadOnlySpan<char> GetSpan(int index)
		{
			if (index < 0 || index >= Lines.Count) return [];
			var line = Lines[index];
			int maxChars = Encoding.UTF8.GetMaxCharCount(line.Length);

			CharBuffer ??= new char[8192];
			if (CharBuffer.Length < maxChars) CharBuffer = new char[maxChars * 2];

			int charCount = Encoding.UTF8.GetChars(Data, line.Start, line.Length, CharBuffer, 0);
			return new ReadOnlySpan<char>(CharBuffer, 0, charCount);
		}
	}
	#endregion

	#region EnfusionSmartAnalyzer Main Class
	internal static class EnfusionSmartAnalyzer
	{
		#region Static Caches
		private static readonly Dictionary<string, string> _nodeById = new(256);
		private static readonly Dictionary<string, string> _nodeByName = new(256);
		private static readonly Dictionary<string, List<string>> _connectionsFrom = new(256);
		private static readonly Dictionary<string, List<string>> _connectionsTo = new(256);
		private static readonly List<int> _matchLineIndices = new(32);
		#endregion

		public static bool TryAnalyze(
			string ext,
			byte[] data,
			string actualQuery1,
			StringComparison fastComp,
			Pak pak,
			PakEntryFile entry,
			Func<Pak, PakEntryFile, int, int, string, string[], ListViewItem> createResultItem,
			Action<ListViewItem> onMatchFound,
			CancellationToken token = default)
		{
			#region 🔊 .ACP - AUDIO CONTROL PROJECT
			// ==========================================
			// 🔊 .ACP - AUDIO CONTROL PROJECT
			// ==========================================
			if (ext == ".acp" && actualQuery1.StartsWith("SOUND_", StringComparison.OrdinalIgnoreCase))
			{
				LazyStringContext context = new(data);
				for (int i = 0; i < context.Lines.Count; i++)
				{
					if (token.IsCancellationRequested) return true;
					string lineStr = context.GetString(i);
					if (lineStr.Contains("name \"", StringComparison.OrdinalIgnoreCase) && lineStr.Contains(actualQuery1, StringComparison.OrdinalIgnoreCase))
					{
						string targetID = "";
						for (int j = i + 1; j < Math.Min(i + 60, context.Lines.Count); j++)
						{
							string innerLine = context.GetString(j);
							if (innerLine.Contains("ConnectionClass connection {"))
							{
								for (int sub = j + 1; sub < Math.Min(j + 6, context.Lines.Count); sub++)
								{
									string subLine = context.GetString(sub);
									if (subLine.Contains("id "))
									{
										targetID = subLine.Replace("id", "").Replace(";", "").Trim();
										break;
									}
								}
								if (!string.IsNullOrEmpty(targetID)) break;
							}
							if (innerLine.Contains("SoundClass {")) break;
						}

						string resolvedWav = "[ No source WAV link found ]";
						if (!string.IsNullOrEmpty(targetID))
						{
							for (int k = 0; k < context.Lines.Count; k++)
							{
								string checkLine = context.GetString(k);
								if (checkLine.Contains("BankLocalClass {") || checkLine.Contains("BankClass {"))
								{
									if (k + 1 < context.Lines.Count && context.GetString(k + 1).Contains("id " + targetID))
									{
										for (int m = k + 2; m < Math.Min(k + 30, context.Lines.Count); m++)
										{
											string mLine = context.GetString(m);
											if (mLine.Contains("Filename \""))
											{
												int fStart = mLine.IndexOf("Filename \"") + 10;
												int fEnd = mLine.IndexOf('"', fStart);
												if (fEnd > fStart) { resolvedWav = mLine[fStart..fEnd]; break; }
											}
										}
										break;
									}
								}
							}
						}
						onMatchFound(createResultItem(pak, entry, i + 1, -1, actualQuery1, ["🔊 " + resolvedWav]));
					}
				}
				return true;
			}
			#endregion

			#region 📡 .SIGA - SIGNAL GRAPH
			// ============================================================================
			// 📡 .SIGA
			// ============================================================================
			if (ext == ".siga")
			{
				LazyStringContext context = new(data);
				int lineCount = context.Lines.Count;

				var nodeById = new Dictionary<string, string>(256);
				var nodeByName = new Dictionary<string, string>(256);
				var connectionsFrom = new Dictionary<string, List<string>>(256);
				var connectionsTo = new Dictionary<string, List<string>>(256);
				var matchLineIndices = new List<int>(32);

				var forwardCache = new Dictionary<string, string>(32);
				var reverseCache = new Dictionary<string, string>(32);

				for (int i = 0; i < lineCount; i++)
				{
					string line = context.GetString(i);

					if (line.Contains("Name \""))
					{
						int s = line.IndexOf("Name \"") + 6;
						int e = line.IndexOf('"', s);
						if (e > s)
						{
							string name = line[s..e];
							int maxJ = Math.Min(i + 10, lineCount);
							for (int j = i + 1; j < maxJ; j++)
							{
								string sub = context.GetString(j);
								if (sub.Contains("id "))
								{
									string id = sub.Replace("id", "").Trim(' ', ';', '\r', '\n');
									nodeById[id] = name;
									nodeByName[name] = id;
									break;
								}
							}
						}
					}
					else if (line.Contains("ConnectionClass") || line.Contains("Connection "))
					{
						string fromId = "";
						string toId = "";
						int maxJ = Math.Min(i + 15, lineCount);
						for (int j = i + 1; j < maxJ; j++)
						{
							string sub = context.GetString(j);
							if (sub.Contains("from ")) fromId = sub.Replace("from", "").Trim(' ', ';', '\r', '\n');
							if (sub.Contains("to ")) toId = sub.Replace("to", "").Trim(' ', ';', '\r', '\n');
						}

						if (!string.IsNullOrEmpty(fromId) && !string.IsNullOrEmpty(toId))
						{
							if (!connectionsFrom.TryGetValue(fromId, out var fromList))
								connectionsFrom[fromId] = fromList = new List<string>(4);
							fromList.Add(toId);

							if (!connectionsTo.TryGetValue(toId, out var toList))
								connectionsTo[toId] = toList = new List<string>(4);
							toList.Add(fromId);
						}
					}

					if (line.Contains(actualQuery1, fastComp))
					{
						matchLineIndices.Add(i);
					}
				}

				int matchCount = matchLineIndices.Count;
				if (matchCount > 0)
				{
					const int MAX_DEPTH = 8;

					var dfsStack = new Stack<(string id, int edgeIdx)>(MAX_DEPTH + 1);
					var currentPathSet = new HashSet<string>(MAX_DEPTH + 1);
					string[] pathBuffer = new string[MAX_DEPTH + 1];
					string[] bestPathBuffer = new string[MAX_DEPTH + 1];

					string BuildChain(string startId, Dictionary<string, List<string>> adjList, bool forward)
					{
						var cache = forward ? forwardCache : reverseCache;
						if (cache.TryGetValue(startId, out var cachedPath)) return cachedPath;

						dfsStack.Clear();
						currentPathSet.Clear();

						int bestPathLength = 0;

						dfsStack.Push((startId, 0));
						currentPathSet.Add(startId);
						pathBuffer[0] = startId;

						while (dfsStack.Count > 0)
						{
							var (currId, edgeIdx) = dfsStack.Pop();
							int currentDepth = dfsStack.Count;

							if (!adjList.TryGetValue(currId, out var neighbors) || edgeIdx >= neighbors.Count || currentDepth >= MAX_DEPTH)
							{
								int currentPathLen = currentDepth + 1;
								if (currentPathLen > bestPathLength)
								{
									bestPathLength = currentPathLen;
									Array.Copy(pathBuffer, bestPathBuffer, currentPathLen);
								}

								currentPathSet.Remove(currId);
								continue;
							}

							dfsStack.Push((currId, edgeIdx + 1));

							string nextId = neighbors[edgeIdx];
							if (currentPathSet.Add(nextId))
							{
								pathBuffer[currentDepth + 1] = nextId;
								dfsStack.Push((nextId, 0));
							}
						}

						if (bestPathLength <= 1)
						{
							string identity = nodeById.TryGetValue(startId, out var name) ? name : startId;
							cache[startId] = identity;
							return identity;
						}

						var localSb = new System.Text.StringBuilder(128);
						string separator = forward ? " → " : " ← ";

						for (int k = 0; k < bestPathLength; k++)
						{
							if (k > 0) localSb.Append(separator);
							string nId = bestPathBuffer[k];
							localSb.Append(nodeById.TryGetValue(nId, out var name) ? name : nId);
						}

						string result = localSb.ToString();
						cache[startId] = result;
						return result;
					}

					var sb = new System.Text.StringBuilder(256);

					for (int m = 0; m < matchCount; m++)
					{
						if (token.IsCancellationRequested) return true;

						int i = matchLineIndices[m];

						string nodeName = "UnknownNode";
						string? nodeId = null;
						string signalType = "NODE";

						int minJ = Math.Max(0, i - 25);
						for (int j = i; j >= minJ; j--)
						{
							string prev = context.GetString(j);

							if (prev.Contains("SignalInputClass") || prev.Contains("InputClass")) signalType = "INPUT";
							else if (prev.Contains("SignalOutputClass") || prev.Contains("OutputClass")) signalType = "OUTPUT";
							else if (prev.Contains("ConstantClass")) signalType = "CONSTANT";

							if (prev.Contains("Name \""))
							{
								int s = prev.IndexOf("Name \"") + 6;
								int e = prev.IndexOf('"', s);
								if (e > s)
								{
									nodeName = prev[s..e];
									nodeByName.TryGetValue(nodeName, out nodeId);
									break;
								}
							}
						}

						sb.Clear();
						string chainInfo = "[no connections]";

						if (!string.IsNullOrEmpty(nodeId))
						{
							string forwardChain = BuildChain(nodeId, connectionsFrom, forward: true);
							sb.Append(forwardChain);

							string reverseChain = BuildChain(nodeId, connectionsTo, forward: false);

							if (reverseChain.Length > nodeName.Length)
							{
								string cleanReverse = reverseChain[nodeName.Length..];
								sb.Append(" (Sources:").Append(cleanReverse).Append(')');
							}

							chainInfo = sb.ToString();
						}

						string display = $"📡 [{signalType} {nodeName}] {chainInfo}";

						onMatchFound(createResultItem(
							pak,
							entry,
							i + 1,
							-1,
							actualQuery1,
							[display]
						));
					}
				}

				return true;
			}
			#endregion

			#region 🖼️ .IMAGESET - IMAGE ATLAS
			// ==========================================
			// 🖼️ .IMAGESET
			// ==========================================
			if (ext == ".imageset")
			{
				LazyStringContext context = new(data);
				string sourceTexture = "Unknown EDDS Source";

				for (int h = 0; h < Math.Min(30, context.Lines.Count); h++)
				{
					string hLine = context.GetString(h);
					if (hLine.Contains("SourceValue \""))
					{
						int tStart = hLine.IndexOf("SourceValue \"") + 13;
						int tEnd = hLine.IndexOf('"', tStart);
						if (tEnd > tStart) { sourceTexture = System.IO.Path.GetFileName(hLine[tStart..tEnd]); break; }
					}
				}

				for (int i = 0; i < context.Lines.Count; i++)
				{
					if (token.IsCancellationRequested) return true;
					string lineStr = context.GetString(i);
					if (lineStr.Contains(actualQuery1, fastComp))
					{
						onMatchFound(createResultItem(pak, entry, i + 1, -1, actualQuery1, [$"🖼️ [Atlas: {sourceTexture}] -> {lineStr.Trim()}"]));
					}
				}
				return true;
			}
			#endregion

			#region 📦 .CONF, .CT, .ET - CONFIG / PREFAB
			// ==========================================
			// 📦 .CONF, .CT, .ET - CONFIG / PREFAB
			// ==========================================
			if (ext == ".conf" || ext == ".ct" || ext == ".et")
			{
				LazyStringContext context = new(data);
				for (int i = 0; i < context.Lines.Count; i++)
				{
					if (token.IsCancellationRequested) return true;
					string lineStr = context.GetString(i);
					if (lineStr.Contains(actualQuery1, fastComp))
					{
						string resolvedParent = "Base Entity Structure";
						string inheritedFrom = "";

						for (int j = i - 1; j >= Math.Max(0, i - 40); j--)
						{
							string upperLine = context.GetString(j);

							if (upperLine.Contains(" : \""))
							{
								int sStart = upperLine.IndexOf(" : \"") + 4;
								int sEnd = upperLine.IndexOf('"', sStart);
								if (sEnd > sStart) {
									string fullPath = upperLine[sStart..sEnd];
									inheritedFrom = $" (Base: {System.IO.Path.GetFileName(fullPath)})";
								}
							}

							if (upperLine.Contains("TemplateClass {") || upperLine.Contains("Component {") ||
								upperLine.Contains("EntityPrefab {") || upperLine.Contains("Component ") ||
								upperLine.Contains("Class ") || upperLine.Contains("GenericEntity") ||
								upperLine.Contains("GameEntity") ||
								(upperLine.Contains('{') && upperLine.Contains('_')))
							{
								int braceIdx = upperLine.IndexOf('{');
								if (braceIdx > 0)
								{
									resolvedParent = upperLine[..braceIdx].Replace(":", "").Trim();
								}
								else
								{
									resolvedParent = upperLine.Replace("{", "").Trim();
								}
								break;
							}
						}

						string smartDisplay = $"📦 [{resolvedParent}{inheritedFrom}] ➔ {lineStr.Trim()}";
						onMatchFound(createResultItem(pak, entry, i + 1, -1, actualQuery1, [smartDisplay]));
					}
				}
				return true;
			}
			#endregion

			#region 🎨 .EMAT - MATERIAL
			// ==========================================
			// 🎨 .EMAT - MATERIAL
			// ==========================================
			if (ext == ".emat")
			{
				LazyStringContext context = new(data);
				string currentShader = "DefaultShader";
				for (int j = 0; j < Math.Min(15, context.Lines.Count); j++)
				{
					string topLine = context.GetString(j);
					if (topLine.Contains("Shader \""))
					{
						int sStart = topLine.IndexOf("Shader \"") + 8;
						int sEnd = topLine.IndexOf('"', sStart);
						if (sEnd > sStart) currentShader = System.IO.Path.GetFileName(topLine[sStart..sEnd]);
						break;
					}
				}

				for (int i = 0; i < context.Lines.Count; i++)
				{
					if (token.IsCancellationRequested) return true;
					string lineStr = context.GetString(i);
					if (lineStr.Contains(actualQuery1, fastComp))
					{
						string smartDisplay = $"🎨 [Shader: {currentShader}] ➔ {lineStr.Trim()}";
						onMatchFound(createResultItem(pak, entry, i + 1, -1, actualQuery1, [smartDisplay]));
					}
				}
				return true;
			}
			#endregion

			#region ✨ .PTC - PARTICLE EFFECT
			// ==========================================
			// ✨ .PTC - PARTICLE EFFECT
			// ==========================================
			if (ext == ".ptc")
			{
				LazyStringContext context = new(data);
				for (int i = 0; i < context.Lines.Count; i++)
				{
					if (token.IsCancellationRequested) return true;
					string lineStr = context.GetString(i);
					if (lineStr.Contains(actualQuery1, fastComp))
					{
						string emitterName = "Root Emitter";
						for (int j = i - 1; j >= Math.Max(0, i - 30); j--)
						{
							string upperLine = context.GetString(j);
							if (upperLine.Contains("EmitterClass ") || upperLine.Contains("Name \""))
							{
								emitterName = upperLine.Trim();
								break;
							}
						}

						string smartDisplay = $"✨ [{emitterName}] ➔ {lineStr.Trim()}";
						onMatchFound(createResultItem(pak, entry, i + 1, -1, actualQuery1, [smartDisplay]));
					}
				}
				return true;
			}
			#endregion

			#region 🎬 .PAP, .ASI, .BT - ANIMATION & BEHAVIOR
			// ==========================================
			// 🎬 .PAP, .ASI, .BT - ANIMATION & BEHAVIOR
			// ==========================================
			if (ext == ".pap" || ext == ".asi" || ext == ".bt")
			{
				LazyStringContext context = new(data);
				for (int i = 0; i < context.Lines.Count; i++)
				{
					if (token.IsCancellationRequested) return true;
					string lineStr = context.GetString(i);
					if (lineStr.Contains(actualQuery1, fastComp))
					{
						string resolvedAnmFile = "Local Structure";
						string nodeScope = "Root Graph Block";

						for (int j = i; j >= Math.Max(0, i - 15); j--)
						{
							string prevLine = context.GetString(j);
							if (prevLine.Contains("State ") || prevLine.Contains("Node ") || prevLine.Contains("Event "))
							{
								nodeScope = prevLine.Trim();
								break;
							}
						}

						for (int j = Math.Max(0, i - 20); j < Math.Min(context.Lines.Count, i + 20); j++)
						{
							string checkLine = context.GetString(j);
							if (checkLine.Contains("AnimationPath \"") || checkLine.Contains("SourcePath \"") || checkLine.Contains(".anm"))
							{
								int keywordIdx = checkLine.IndexOf("Path \"");
								if (keywordIdx == -1) keywordIdx = checkLine.IndexOf(".anm");

								int aStart = checkLine.IndexOf('"', keywordIdx == -1 ? 0 : keywordIdx) + 1;
								if (aStart > 0)
								{
									int aEnd = checkLine.IndexOf('"', aStart);
									if (aEnd > aStart)
									{
										resolvedAnmFile = System.IO.Path.GetFileName(checkLine[aStart..aEnd]);
										break;
									}
								}
							}
						}

						string smartDisplay = $"🎬 [Anim: {resolvedAnmFile} | {nodeScope}] ➔ {lineStr.Trim()}";
						onMatchFound(createResultItem(pak, entry, i + 1, -1, actualQuery1, [smartDisplay]));
					}
				}
				return true;
			}
			#endregion

			return false;
		}
	}
	#endregion
}