using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace PakWorkbench.src.ext.Enfusion
{
	public class EnfusionSqliteCache
	{
		#region Private Fields & Constructor
		private readonly string _dbPath;
		private readonly string _connectionString;

		private static string GetDefaultDbPath()
		{
			string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
			return Path.Combine(exeDir, "EnfusionGraphCache.db");
		}

		public EnfusionSqliteCache(string? dbPath = null)
		{
			_dbPath = string.IsNullOrEmpty(dbPath) ? GetDefaultDbPath() : dbPath;
			_connectionString = $"Data Source={_dbPath}";
			InitializeDatabase();
		}
		#endregion

		#region Database Optimization & Initialization
		private static void ApplyPerformancePragmas(SqliteConnection connection)
		{
			using var command = connection.CreateCommand();
			command.CommandText = @"
					PRAGMA journal_mode = WAL;
					PRAGMA synchronous = OFF;
					PRAGMA temp_store = MEMORY;
					PRAGMA cache_size = -20000;
				";
			command.ExecuteNonQuery();
		}

		private static async Task ApplyPerformancePragmasAsync(SqliteConnection connection)
		{
			using var command = connection.CreateCommand();
			command.CommandText = @"
					PRAGMA journal_mode = WAL;
					PRAGMA synchronous = OFF;
					PRAGMA temp_store = MEMORY;
					PRAGMA cache_size = -20000;
				";
			await command.ExecuteNonQueryAsync();
		}

		private void InitializeDatabase()
		{
			using var connection = new SqliteConnection(_connectionString);
			connection.Open();
			ApplyPerformancePragmas(connection);

			using (var command = connection.CreateCommand())
			{
				command.CommandText = @"
						CREATE TABLE IF NOT EXISTS AssetNodes (
							Id INTEGER PRIMARY KEY AUTOINCREMENT,
							NodeId TEXT NOT NULL,
							Name TEXT,
							AssetType TEXT,
							EmbeddedElements TEXT,
							Origin TEXT,
							ProjectId TEXT NOT NULL DEFAULT '',
							UNIQUE(NodeId, ProjectId)
						);

						CREATE TABLE IF NOT EXISTS AssetEdges (
							Id INTEGER PRIMARY KEY AUTOINCREMENT,
							SourceNodeId TEXT NOT NULL,
							TargetNodeId TEXT NOT NULL,
							Relation TEXT
						);

						CREATE TABLE IF NOT EXISTS FileIndexCache (
							FilePath TEXT PRIMARY KEY,
							LastWriteTicks INTEGER NOT NULL
						);

						CREATE INDEX IF NOT EXISTS idx_nodeid ON AssetNodes(NodeId);
						CREATE INDEX IF NOT EXISTS idx_source ON AssetEdges(SourceNodeId);
						CREATE INDEX IF NOT EXISTS idx_target ON AssetEdges(TargetNodeId);
						CREATE INDEX IF NOT EXISTS idx_projectid ON AssetNodes(ProjectId);
					";
				command.ExecuteNonQuery();
			}

			try
			{
				using var cmd = connection.CreateCommand();
				cmd.CommandText = "ALTER TABLE AssetNodes ADD COLUMN EmbeddedElements TEXT;";
				cmd.ExecuteNonQuery();
			}
			catch { }

			try
			{
				using var cmd = connection.CreateCommand();
				cmd.CommandText = "ALTER TABLE AssetNodes ADD COLUMN Origin TEXT;";
				cmd.ExecuteNonQuery();
			}
			catch { }

			try
			{
				using var cmd = connection.CreateCommand();
				cmd.CommandText = "ALTER TABLE AssetNodes ADD COLUMN ProjectId TEXT;";
				cmd.ExecuteNonQuery();
			}
			catch { }
		}
		#endregion

		#region Graph Data Operations
		public async Task SaveGraphDataAsync(IEnumerable<AssetNode> nodes)
		{
			using var connection = new SqliteConnection(_connectionString);
			await connection.OpenAsync();
			await ApplyPerformancePragmasAsync(connection);

			using var transaction = connection.BeginTransaction();
			var insertNodeCmd = connection.CreateCommand();
			insertNodeCmd.CommandText = "INSERT OR REPLACE INTO AssetNodes (NodeId, Name, AssetType, EmbeddedElements, Origin, ProjectId) VALUES ($nodeid, $name, $type, $embedded, $origin, $projectid)";
			var nodeIdParam = insertNodeCmd.Parameters.Add("$nodeid", SqliteType.Text);
			var nameParam = insertNodeCmd.Parameters.Add("$name", SqliteType.Text);
			var typeParam = insertNodeCmd.Parameters.Add("$type", SqliteType.Text);
			var embeddedParam = insertNodeCmd.Parameters.Add("$embedded", SqliteType.Text);
			var originParam = insertNodeCmd.Parameters.Add("$origin", SqliteType.Text);
			var projectIdParam = insertNodeCmd.Parameters.Add("$projectid", SqliteType.Text);

			var deleteEdgesCmd = connection.CreateCommand();
			deleteEdgesCmd.CommandText = "DELETE FROM AssetEdges WHERE SourceNodeId = $source";
			var delSourceParam = deleteEdgesCmd.Parameters.Add("$source", SqliteType.Text);

			var insertEdgeCmd = connection.CreateCommand();
			insertEdgeCmd.CommandText = "INSERT INTO AssetEdges (SourceNodeId, TargetNodeId, Relation) VALUES ($source, $target, $relation)";
			var sourceParam = insertEdgeCmd.Parameters.Add("$source", SqliteType.Text);
			var targetParam = insertEdgeCmd.Parameters.Add("$target", SqliteType.Text);
			var relationParam = insertEdgeCmd.Parameters.Add("$relation", SqliteType.Text);

			foreach (var node in nodes)
			{
				nodeIdParam.Value = node.Id ?? string.Empty;
				nameParam.Value = node.Name ?? string.Empty;
				typeParam.Value = node.Type ?? string.Empty;
				embeddedParam.Value = node.EmbeddedElements != null && node.EmbeddedElements.Count > 0
					? JsonSerializer.Serialize(node.EmbeddedElements)
					: string.Empty;

				string nodeOrigin = string.Empty;
				string nodeProjectId = string.Empty;
				try { nodeOrigin = node.Origin ?? string.Empty; } catch {}
				try { nodeProjectId = node.ProjectId ?? string.Empty; } catch {}

				originParam.Value = nodeOrigin;
				projectIdParam.Value = nodeProjectId;

				insertNodeCmd.ExecuteNonQuery();

				delSourceParam.Value = node.Id ?? string.Empty;
				deleteEdgesCmd.ExecuteNonQuery();

				foreach (var edge in node.Edges)
				{
					if (edge.Target != null)
					{
						sourceParam.Value = node.Id ?? string.Empty;
						targetParam.Value = edge.Target.Id ?? string.Empty;
						relationParam.Value = edge.Relation ?? string.Empty;
						insertEdgeCmd.ExecuteNonQuery();
					}
				}
			}
			await transaction.CommitAsync();
		}

		public async Task<ConcurrentDictionary<string, AssetNode>> LoadGraphDataAsync(Action<string, int, int>? onStatusUpdate = null, string? targetProjectId = null)
		{
			var nodes = new ConcurrentDictionary<string, AssetNode>(StringComparer.OrdinalIgnoreCase);
			int totalNodes = 0;

			using var connection = new SqliteConnection(_connectionString);
			await connection.OpenAsync();
			await ApplyPerformancePragmasAsync(connection);

			using (var cmdCount = connection.CreateCommand())
			{
				if (string.IsNullOrEmpty(targetProjectId))
				{
					cmdCount.CommandText = "SELECT COUNT(*) FROM AssetNodes";
				}
				else
				{
					cmdCount.CommandText = "SELECT COUNT(*) FROM AssetNodes WHERE ProjectId = $projectId";
					cmdCount.Parameters.AddWithValue("$projectId", targetProjectId);
				}
				totalNodes = Convert.ToInt32(await cmdCount.ExecuteScalarAsync());
			}

			if (totalNodes == 0) return nodes;

			int loadedCount = 0;

			using (var cmd = connection.CreateCommand())
			{
				if (string.IsNullOrEmpty(targetProjectId))
				{
					cmd.CommandText = "SELECT NodeId, Name, AssetType, EmbeddedElements, Origin, ProjectId FROM AssetNodes";
				}
				else
				{
					cmd.CommandText = "SELECT NodeId, Name, AssetType, EmbeddedElements, Origin, ProjectId FROM AssetNodes WHERE ProjectId = $projectId";
					cmd.Parameters.AddWithValue("$projectId", targetProjectId);
				}

				using var reader = await cmd.ExecuteReaderAsync();
				while (await reader.ReadAsync())
				{
					var n = new AssetNode
					{
						Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
						Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
						Type = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
					};

					if (!reader.IsDBNull(3))
					{
						string embeddedStr = reader.GetString(3);
						if (!string.IsNullOrEmpty(embeddedStr))
						{
							try
							{
								n.EmbeddedElements = JsonSerializer.Deserialize<HashSet<string>>(embeddedStr)
													 ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
							}
							catch { }
						}
					}

					if (reader.FieldCount > 4 && !reader.IsDBNull(4))
					{
						try { n.Origin = reader.GetString(4); } catch {}
					}

					if (reader.FieldCount > 5 && !reader.IsDBNull(5))
					{
						try { n.ProjectId = reader.GetString(5); } catch {}
					}

					nodes[n.Id] = n;

					loadedCount++;
					if (loadedCount % 5000 == 0)
					{
						onStatusUpdate?.Invoke($"💾 Loading DB Cache: {loadedCount}/{totalNodes} nodes...", loadedCount, totalNodes);
					}
				}
			}

			onStatusUpdate?.Invoke("💾 Relinking cached graph connections...", 95, 100);

			using (var cmd = connection.CreateCommand())
			{
				cmd.CommandText = "SELECT SourceNodeId, TargetNodeId, Relation FROM AssetEdges";
				using var reader = await cmd.ExecuteReaderAsync();
				while (await reader.ReadAsync())
				{
					string src = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
					string tgt = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
					string rel = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

					if (nodes.TryGetValue(src, out var sNode) && nodes.TryGetValue(tgt, out var tNode))
					{
						sNode.Edges.Add(new AssetEdge { Target = tNode, Relation = rel });
					}
				}
			}
			return nodes;
		}
		#endregion

		#region File Index Cache Operations
		public async Task<ConcurrentDictionary<string, long>> LoadFileIndexCacheAsync()
		{
			var dates = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
			try
			{
				using var connection = new SqliteConnection(_connectionString);
				await connection.OpenAsync();
				await ApplyPerformancePragmasAsync(connection);

				using var cmd = connection.CreateCommand();
				cmd.CommandText = "SELECT FilePath, LastWriteTicks FROM FileIndexCache";
				using var reader = await cmd.ExecuteReaderAsync();
				while (await reader.ReadAsync())
				{
					dates[reader.GetString(0)] = reader.GetInt64(1);
				}
			}
			catch { /* Ignore if table doesn't exist yet */ }
			return dates;
		}

		public async Task SaveFileIndexCacheAsync(ConcurrentDictionary<string, long> fileDates)
		{
			using var connection = new SqliteConnection(_connectionString);
			await connection.OpenAsync();
			await ApplyPerformancePragmasAsync(connection);

			using var transaction = connection.BeginTransaction();
			var cmd = connection.CreateCommand();
			cmd.CommandText = "INSERT OR REPLACE INTO FileIndexCache (FilePath, LastWriteTicks) VALUES ($path, $ticks)";
			var pathParam = cmd.Parameters.Add("$path", SqliteType.Text);
			var ticksParam = cmd.Parameters.Add("$ticks", SqliteType.Integer);

			foreach (var kvp in fileDates)
			{
				pathParam.Value = kvp.Key;
				ticksParam.Value = kvp.Value;
				cmd.ExecuteNonQuery();
			}
			await transaction.CommitAsync();
		}
		#endregion

		#region Search & Query Helpers
		public async Task<List<string>> SearchFilesAsync(string keyword, string? assetType = null, string? projectId = null)
		{
			var results = new List<string>();
			using var connection = new SqliteConnection(_connectionString);
			await connection.OpenAsync();
			await ApplyPerformancePragmasAsync(connection);

			var command = connection.CreateCommand();

			string query = "SELECT NodeId FROM AssetNodes WHERE (NodeId LIKE $keyword OR Name LIKE $keyword)";
			if (!string.IsNullOrEmpty(assetType))
			{
				query += " AND AssetType = $type";
				command.Parameters.AddWithValue("$type", assetType);
			}

			if (!string.IsNullOrEmpty(projectId))
			{
				query += " AND ProjectId = $projectId";
				command.Parameters.AddWithValue("$projectId", projectId);
			}

			command.CommandText = query;
			command.Parameters.AddWithValue("$keyword", $"%{keyword}%");

			using var reader = await command.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				results.Add(reader.GetString(0));
			}
			return results;
		}

		public bool HasCache(string? projectId = null)
		{
			if (!File.Exists(_dbPath)) return false;

			try
			{
				using var connection = new SqliteConnection(_connectionString);
				connection.Open();
				ApplyPerformancePragmas(connection);

				var command = connection.CreateCommand();
				if (string.IsNullOrEmpty(projectId))
				{
					command.CommandText = "SELECT COUNT(*) FROM AssetNodes";
				}
				else
				{
					command.CommandText = "SELECT COUNT(*) FROM AssetNodes WHERE ProjectId = $projectId";
					command.Parameters.AddWithValue("$projectId", projectId);
				}
				long count = Convert.ToInt64(command.ExecuteScalar());
				return count > 0;
			}
			catch
			{
				return false;
			}
		}
		#endregion
	}
}