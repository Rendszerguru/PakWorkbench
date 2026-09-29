using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace PakWorkbench.src.ext
{
	public static class PakIndex
	{
		#region Fields & Database Configuration
		private static readonly string dbPath = Path.Combine(AppContext.BaseDirectory, "PakWorkbenchCache.db");
		private static readonly string connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Pooling=False;";
		private static readonly object dbLock = new();
		#endregion

		#region Initialization
		public static void Initialize()
		{
			lock (dbLock)
			{
				using var connection = new SqliteConnection(connectionString);
				connection.Open();
				using var command = connection.CreateCommand();

				command.CommandText = @"
					PRAGMA journal_mode=WAL;
					PRAGMA synchronous=NORMAL;

					CREATE TABLE IF NOT EXISTS paks (
						id INTEGER PRIMARY KEY AUTOINCREMENT,
						path TEXT UNIQUE NOT NULL,
						file_size INTEGER NOT NULL,
						last_modified INTEGER NOT NULL,
						form_size INTEGER NOT NULL,
						data_size INTEGER NOT NULL,
						entries_size INTEGER NOT NULL
					);

					CREATE TABLE IF NOT EXISTS files (
						id INTEGER PRIMARY KEY AUTOINCREMENT,
						pak_id INTEGER NOT NULL,
						name TEXT NOT NULL,
						offset INTEGER NOT NULL,
						size INTEGER NOT NULL,
						original_size INTEGER NOT NULL,
						compression INTEGER NOT NULL,
						FOREIGN KEY (pak_id) REFERENCES paks(id) ON DELETE CASCADE
					);

					CREATE INDEX IF NOT EXISTS idx_files_pak_id ON files(pak_id);
					CREATE INDEX IF NOT EXISTS idx_paks_path ON paks(path);
				";
				command.ExecuteNonQuery();
			}
		}
		#endregion

		#region Cache Operations
		public static bool NeedsUpdate(string pakPath, long currentSize, long currentMtime)
		{
			lock (dbLock)
			{
				using var connection = new SqliteConnection(connectionString);
				connection.Open();
				using var command = connection.CreateCommand();
				command.CommandText = "SELECT file_size, last_modified FROM paks WHERE path = $path";
				command.Parameters.AddWithValue("$path", pakPath);

				using var reader = command.ExecuteReader();
				if (reader.Read())
				{
					long cachedSize = reader.GetInt64(0);
					long cachedMtime = reader.GetInt64(1);
					return cachedSize != currentSize || cachedMtime != currentMtime;
				}
				return true;
			}
		}

		public static void SavePakToDb(Pak pak, long currentSize, long currentMtime)
		{
			lock (dbLock)
			{
				using var connection = new SqliteConnection(connectionString);
				connection.Open();

				using var transaction = connection.BeginTransaction();

				using (var cmdDeleteFiles = connection.CreateCommand())
				{
					cmdDeleteFiles.Transaction = transaction;
					cmdDeleteFiles.CommandText = "DELETE FROM files WHERE pak_id IN (SELECT id FROM paks WHERE path = $path)";
					cmdDeleteFiles.Parameters.AddWithValue("$path", pak.name);
					cmdDeleteFiles.ExecuteNonQuery();
				}

				using (var cmdDeletePak = connection.CreateCommand())
				{
					cmdDeletePak.Transaction = transaction;
					cmdDeletePak.CommandText = "DELETE FROM paks WHERE path = $path";
					cmdDeletePak.Parameters.AddWithValue("$path", pak.name);
					cmdDeletePak.ExecuteNonQuery();
				}

				long pakId;
				using (var cmdInsertPak = connection.CreateCommand())
				{
					cmdInsertPak.Transaction = transaction;
					cmdInsertPak.CommandText = @"
						INSERT INTO paks (path, file_size, last_modified, form_size, data_size, entries_size)
						VALUES ($path, $fsize, $mtime, $form, $data, $entries);
						SELECT last_insert_rowid();";
					cmdInsertPak.Parameters.AddWithValue("$path", pak.name);
					cmdInsertPak.Parameters.AddWithValue("$fsize", currentSize);
					cmdInsertPak.Parameters.AddWithValue("$mtime", currentMtime);
					cmdInsertPak.Parameters.AddWithValue("$form", pak.formSize);
					cmdInsertPak.Parameters.AddWithValue("$data", pak.dataSize);
					cmdInsertPak.Parameters.AddWithValue("$entries", pak.entriesSize);

					var scalarResult = cmdInsertPak.ExecuteScalar();
					pakId = scalarResult != null ? Convert.ToInt64(scalarResult) : 0L;
				}

				using (var cmdInsertFile = connection.CreateCommand())
				{
					cmdInsertFile.Transaction = transaction;
					cmdInsertFile.CommandText = @"
						INSERT INTO files (pak_id, name, offset, size, original_size, compression)
						VALUES ($pakId, $name, $offset, $size, $orig, $comp)";

					var pPakId = cmdInsertFile.Parameters.Add("$pakId", SqliteType.Integer);
					var pName = cmdInsertFile.Parameters.Add("$name", SqliteType.Text);
					var pOffset = cmdInsertFile.Parameters.Add("$offset", SqliteType.Integer);
					var pSize = cmdInsertFile.Parameters.Add("$size", SqliteType.Integer);
					var pOrig = cmdInsertFile.Parameters.Add("$orig", SqliteType.Integer);
					var pComp = cmdInsertFile.Parameters.Add("$comp", SqliteType.Integer);

					foreach (var entry in pak.entries)
					{
						pPakId.Value = pakId;
						pName.Value = entry.name;
						pOffset.Value = entry.offset;
						pSize.Value = entry.size;
						pOrig.Value = entry.originalSize;
						pComp.Value = (int)entry.compression;
						cmdInsertFile.ExecuteNonQuery();
					}
				}

				transaction.Commit();
			}
		}

		public static Pak LoadPakFromDb(string pakPath)
		{
			Pak pak = new()
			{
				name = pakPath
			};
			long pakId = -1;

			lock (dbLock)
			{
				using var connection = new SqliteConnection(connectionString);
				connection.Open();

				using (var cmdPak = connection.CreateCommand())
				{
					cmdPak.CommandText = "SELECT id, form_size, data_size, entries_size FROM paks WHERE path = $path";
					cmdPak.Parameters.AddWithValue("$path", pakPath);
					using var reader = cmdPak.ExecuteReader();
					if (reader.Read())
					{
						pakId = reader.GetInt64(0);
						pak.formSize = reader.GetInt32(1);
						pak.dataSize = reader.GetInt32(2);
						pak.entriesSize = reader.GetInt32(3);
					}
				}

				if (pakId != -1)
				{
					using var cmdFiles = connection.CreateCommand();
					cmdFiles.CommandText = "SELECT name, offset, size, original_size, compression FROM files WHERE pak_id = $pakId";
					cmdFiles.Parameters.AddWithValue("$pakId", pakId);
					using var reader = cmdFiles.ExecuteReader();
					while (reader.Read())
					{
						PakEntryFile entry = new()
						{
							name = reader.GetString(0),
							offset = reader.GetInt32(1),
							size = reader.GetInt32(2),
							originalSize = reader.GetInt32(3),
							compression = (PakEntryFile.CompressionType)reader.GetInt32(4)
						};
						pak.entries.Add(entry);
					}
				}
			}
			return pak;
		}
		#endregion
	}
}