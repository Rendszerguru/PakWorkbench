namespace PakWorkbench.src;

using System;
using System.IO;
using System.IO.Compression;
using System.Buffers;

public static class Zlib
{
	#region Full Buffer Decompression

	public static byte[] Decompress(byte[] data)
	{
		if (data == null || data.Length < 2)
		{
			throw new InvalidDataException("The input data is too short for decompression.");
		}

		if (data[0] != 0x78 && (data[0] & 0x0F) != 0x08)
		{
			throw new InvalidDataException("Invalid Zlib header or compression method.");
		}

		try
		{
			using var compressStream = new MemoryStream(data, 2, data.Length - 2);
			using var decompressedStream = new MemoryStream();
			using var deflateStream = new DeflateStream(compressStream, CompressionMode.Decompress);

			deflateStream.CopyTo(decompressedStream);
			return decompressedStream.ToArray();
		}
		catch (InvalidDataException ex)
		{
			Console.WriteLine($"Invalid data during decompression: {ex.Message}");
			throw;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error during decompression: {ex.Message}");
			throw;
		}
	}

	#endregion

	#region Stream Decompression

	public static void Decompress(PakReader pr, int expectedSize, string dst)
	{
		if (!pr.CanRead(2))
		{
			Console.WriteLine("Error: Insufficient data to skip Zlib header.");
			return;
		}

		long originalPosition = pr.Pos();
		pr.Skip(2);

		try
		{
			using FileStream decompressedStream = new(
				dst,
				FileMode.Create,
				FileAccess.Write,
				FileShare.None,
				bufferSize: 65536,
				FileOptions.SequentialScan);

			using DeflateStream deflateStream = new(pr.BaseStream, CompressionMode.Decompress, leaveOpen: true);

			byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
			int bytesReadTotal = 0;

			try
			{
				int bytesRead;
				while (bytesReadTotal < expectedSize &&
					   (bytesRead = deflateStream.Read(buffer, 0, Math.Min(buffer.Length, expectedSize - bytesReadTotal))) > 0)
				{
					decompressedStream.Write(buffer, 0, bytesRead);
					bytesReadTotal += bytesRead;
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}

			if (bytesReadTotal < expectedSize)
			{
				Console.WriteLine($"Warning: Decompressed size ({bytesReadTotal} bytes) is smaller than expected ({expectedSize} bytes).");
			}

			Console.WriteLine($"The file was successfully decompressed: {dst}");
		}
		catch (InvalidDataException ex)
		{
			Console.WriteLine($"Invalid data during decompression: {ex.Message}");
			pr.BaseStream.Position = originalPosition;
			SaveCorruptedData(pr, expectedSize, dst);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error during Zlib decompression: {ex.Message}");
			pr.BaseStream.Position = originalPosition;
			SaveCorruptedData(pr, expectedSize, dst);
		}
	}

	public static byte[] DecompressPartial(PakReader pr, int _, int maxBytes)
	{
		if (!pr.CanRead(2)) return [];

		long originalPosition = pr.Pos();
		pr.Skip(2);

		try
		{
			using MemoryStream ms = new();
			using DeflateStream deflateStream = new(pr.BaseStream, CompressionMode.Decompress, leaveOpen: true);

			byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
			int bytesReadTotal = 0;

			try
			{
				int bytesRead;
				while (bytesReadTotal < maxBytes &&
					   (bytesRead = deflateStream.Read(buffer, 0, Math.Min(buffer.Length, maxBytes - bytesReadTotal))) > 0)
				{
					ms.Write(buffer, 0, bytesRead);
					bytesReadTotal += bytesRead;
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}

			return ms.ToArray();
		}
		catch
		{
			pr.BaseStream.Position = originalPosition;
			return [];
		}
	}

	#endregion

	#region Error Handling & Recovery

	private static void SaveCorruptedData(PakReader pr, int expectedSize, string dst)
	{
		try
		{
			using FileStream fs = File.Create(dst + "_Error");
			byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
			int remaining = expectedSize;

			try
			{
				while (remaining > 0)
				{
					int toRead = Math.Min(buffer.Length, remaining);
					int read = pr.Read(buffer, 0, toRead);
					if (read <= 0) break;
					fs.Write(buffer, 0, read);
					remaining -= read;
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}

			Console.WriteLine($"Corrupted data saved: {dst}_Error");
		}
		catch (Exception innerEx)
		{
			Console.WriteLine($"Error while saving corrupted data: {innerEx.Message}");
		}
	}

	#endregion
}