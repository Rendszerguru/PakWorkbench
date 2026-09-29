namespace PakWorkbench.src;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading.Tasks;

public class Pak : IDisposable
{
	#region Fields, Types & Initialization

	public enum EntryType
	{
		Directory, File1
	}

	public string name = string.Empty;
	public int formSize;
	public int dataSize;
	public int entriesSize;
	public List<PakEntryFile> entries = [];

	private FileStream? _fileStream;
	private MemoryMappedFile? _mmf;
	private bool _isDisposed;
	private readonly object _lockObject = new();

	public Pak() { }

	public Pak(string src)
	{
		name = src;
		InitializeMmf();

		using MemoryMappedViewStream viewStream = _mmf!.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
		using PakReader pr = new(viewStream);

		ReadForm(pr);
		ReadHead(pr);
		ReadData(pr);
		ReadEntries(pr);
	}

	private void InitializeMmf()
	{
		lock (_lockObject)
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);

			if (_mmf == null)
			{
				_fileStream = new FileStream(name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
				_mmf = MemoryMappedFile.CreateFromFile(_fileStream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
			}
		}
	}

	#endregion

	#region Header & Index Parsing

	public void ReadForm(PakReader pr)
	{
		pr.SkipSignature();
		formSize = pr.ReadInt32BE();
		pr.SkipSignature();
	}

	public static void ReadHead(PakReader pr)
	{
		pr.SkipSignature();
		Console.WriteLine("Head of the Pak File: " + Convert.ToBase64String(pr.ReadBytes(32)));
	}

	public void ReadData(PakReader pr)
	{
		pr.SkipSignature();
		dataSize = pr.ReadInt32BE();
		pr.Skip(dataSize);
	}

	public void ReadEntries(PakReader pr)
	{
		pr.SkipSignature();
		entriesSize = pr.ReadInt32BE();

		try
		{
			pr.Skip(2);
			pr.Skip(4);

			long posEntries = pr.Pos();
			while (pr.Pos() - posEntries < entriesSize && pr.Pos() < pr.BaseStream.Length)
			{
				long previousPos = pr.Pos();

				try
				{
					if (!pr.CanRead(2))
						break;

					EntryType entryType = (EntryType)pr.ReadByte();
					int entryNameLength = pr.ReadByte();

					if (entryNameLength <= 0 || !pr.CanRead(entryNameLength))
						break;

					string entryName = pr.ReadStringUtf8(entryNameLength);

					if (entryType == EntryType.Directory)
					{
						ReadEntriesFromDirectory(entryName, pr);
					}
					else
					{
						entries.Add(new PakEntryFile(entryName, pr));
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error while reading an entry: {ex.Message}");
					pr.BaseStream.Position = previousPos;
					break;
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error while processing the entries: {ex.Message}");
		}
	}

	public void ReadEntriesFromDirectory(string dirName, PakReader pr)
	{
		if (!pr.CanRead(4))
		{
			Console.WriteLine("Warning: Can't read directory entry as there is not enough data.");
			return;
		}

		int childCount = pr.ReadInt32();

		for (int i = 0; i < childCount; i++)
		{
			long previousPos = pr.Pos();

			try
			{
				if (!pr.CanRead(2))
					break;

				EntryType entryType = (EntryType)pr.ReadByte();
				int entryNameLength = pr.ReadByte();

				if (entryNameLength <= 0 || !pr.CanRead(entryNameLength))
					break;

				string entryName = dirName + "\\" + pr.ReadStringUtf8(entryNameLength);

				if (entryType == EntryType.File1)
				{
					entries.Add(new PakEntryFile(entryName, pr));
				}
				else if (entryType == EntryType.Directory)
				{
					ReadEntriesFromDirectory(entryName, pr);
				}
				else
				{
					Console.WriteLine("Unknown entry type.");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error while reading a directory entry: {ex.Message}");
				pr.BaseStream.Position = previousPos;
				break;
			}
		}
	}

	#endregion

	#region Disk Extraction

	public void ExtractDataBlock(string dst)
	{
		InitializeMmf();

		HashSet<string> directories = [];
		foreach (var entry in entries)
		{
			string targetPath = Path.Combine(dst, entry.name);
			string? dir = Path.GetDirectoryName(targetPath);
			if (!string.IsNullOrEmpty(dir))
			{
				directories.Add(dir);
			}
		}

		foreach (var dir in directories)
		{
			Directory.CreateDirectory(dir);
		}

		ParallelOptions options = new()
		{
			MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
		};

		Parallel.ForEach(entries, options, entry =>
		{
			if (_isDisposed) return;

			try
			{
				string targetPath = Path.Combine(dst, entry.name);

				if (entry.offset + entry.size > _fileStream!.Length)
				{
					Console.WriteLine($"Warning: Entry '{entry.name}' exceeds file size. Skipping.");
					return;
				}

				if (entry.size == 0)
				{
					File.WriteAllBytes(targetPath, []);
					return;
				}

				using MemoryMappedViewStream viewStream = _mmf!.CreateViewStream(entry.offset, entry.size, MemoryMappedFileAccess.Read);
				using PakReader pr = new(viewStream);

				if (entry.compression == PakEntryFile.CompressionType.Zlib)
				{
					try
					{
						Zlib.Decompress(pr, entry.originalSize, targetPath);
					}
					catch
					{
						Console.WriteLine($"Error during Zlib decompression: {entry.name}");
						pr.BaseStream.Position = 0;
						File.WriteAllBytes(targetPath + "_Error", pr.ReadBytes(entry.size));
					}
				}
				else
				{
					using FileStream outStream = new(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 262144, FileOptions.SequentialScan);
					byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(262144);
					try
					{
						int read;
						while ((read = pr.BaseStream.Read(buffer, 0, buffer.Length)) > 0)
						{
							outStream.Write(buffer, 0, read);
						}
					}
					finally
					{
						System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Critical error while extracting ({entry.name}): {ex.Message}");
			}
		});
	}

	public void ExtractSingleEntry(PakEntryFile entry, string baseOutputDir)
	{
		if (_isDisposed) return;

		try
		{
			string dst = Path.Combine(baseOutputDir, entry.name);
			string? dir = Path.GetDirectoryName(dst);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

			InitializeMmf();

			if (entry.offset + entry.size > _fileStream!.Length)
			{
				Console.WriteLine($"Warning: Entry '{entry.name}' exceeds file size. Skipping.");
				return;
			}

			if (entry.size == 0)
			{
				File.WriteAllBytes(dst, []);
				return;
			}

			using MemoryMappedViewStream viewStream = _mmf!.CreateViewStream(entry.offset, entry.size, MemoryMappedFileAccess.Read);
			using PakReader pr = new(viewStream);

			if (entry.compression == PakEntryFile.CompressionType.Zlib)
			{
				try
				{
					Zlib.Decompress(pr, entry.originalSize, dst);
				}
				catch
				{
					Console.WriteLine($"Error during Zlib decompression: {entry.name}");
					pr.BaseStream.Position = 0;
					File.WriteAllBytes(dst + "_Error", pr.ReadBytes(entry.size));
				}
			}
			else
			{
				using FileStream outStream = new(dst, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan);
				byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(65536);
				try
				{
					int read;
					while ((read = pr.BaseStream.Read(buffer, 0, buffer.Length)) > 0)
					{
						outStream.Write(buffer, 0, read);
					}
				}
				finally
				{
					System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
				}
			}
			Console.WriteLine($"Extracted selected file: {entry.name}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error while extracting {entry.name}: {ex.Message}");
		}
	}

	public int ExtractDirectory(string dirPrefix, string baseOutputDir)
	{
		int count = 0;
		foreach (var entry in entries)
		{
			if (string.IsNullOrEmpty(dirPrefix) || entry.name.StartsWith(dirPrefix, StringComparison.OrdinalIgnoreCase))
			{
				ExtractSingleEntry(entry, baseOutputDir);
				count++;
			}
		}
		return count;
	}

	#endregion

	#region In-Memory Data Reading

	public byte[] GetPreviewBytes(PakEntryFile entry, int maxBytes = 4096)
	{
		if (_isDisposed) return [];

		if (entry.size == 0) return [];

		try
		{
			InitializeMmf();
			using MemoryMappedViewStream viewStream = _mmf!.CreateViewStream(entry.offset, entry.size, MemoryMappedFileAccess.Read);
			using PakReader pr = new(viewStream);

			int uncompressedToRead = Math.Min(entry.originalSize, maxBytes);

			if (entry.compression == PakEntryFile.CompressionType.Zlib)
			{
				return Zlib.DecompressPartial(pr, entry.originalSize, uncompressedToRead);
			}
			else
			{
				int bytesToRead = Math.Min(entry.size, uncompressedToRead);
				return pr.ReadBytes(bytesToRead);
			}
		}
		catch
		{
			return [];
		}
	}

	public byte[] GetFileBytes(PakEntryFile entry)
	{
		if (_isDisposed) return [];

		if (entry.size == 0) return [];

		try
		{
			InitializeMmf();
			using MemoryMappedViewStream viewStream = _mmf!.CreateViewStream(entry.offset, entry.size, MemoryMappedFileAccess.Read);
			using PakReader pr = new(viewStream);

			if (entry.compression == PakEntryFile.CompressionType.Zlib)
			{
				return Zlib.DecompressPartial(pr, entry.originalSize, entry.originalSize);
			}
			else
			{
				return pr.ReadBytes(entry.size);
			}
		}
		catch
		{
			return [];
		}
	}

	#endregion

	#region IDisposable Implementation

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		lock (_lockObject)
		{
			if (!_isDisposed)
			{
				if (disposing)
				{
					_mmf?.Dispose();
					_fileStream?.Dispose();
				}
				_isDisposed = true;
			}
		}
	}

	#endregion
}