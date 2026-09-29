namespace PakWorkbench.src;

using System;
using System.IO;
using System.Text;

public class PakReader(Stream stream) : BinaryReader(stream, Encoding.UTF8, leaveOpen: true)
{
	#region Stream Navigation & Validation

	public void SkipSignature()
	{
		if (CanRead(4))
		{
			BaseStream.Position += 4;
		}
		else
		{
			Console.WriteLine("Warning: Not enough data to skip the signature.");
			BaseStream.Position = BaseStream.Length;
		}
	}

	public bool CanRead(int length)
	{
		return length >= 0 && BaseStream.Position + length <= BaseStream.Length;
	}

	public void Skip(int count)
	{
		if (CanRead(count))
		{
			BaseStream.Position += count;
		}
		else
		{
			Console.WriteLine($"Warning: Attempt to skip {count} bytes, but not enough data remains. Skipping to the end of the stream.");
			BaseStream.Position = BaseStream.Length;
		}
	}

	public long Pos() => BaseStream.Position;

	#endregion

	#region Binary Reading Methods

	public int ReadInt32BE()
	{
		if (CanRead(4))
		{
			int b1 = BaseStream.ReadByte();
			int b2 = BaseStream.ReadByte();
			int b3 = BaseStream.ReadByte();
			int b4 = BaseStream.ReadByte();
			return (b1 << 24) | (b2 << 16) | (b3 << 8) | b4;
		}
		else
		{
			Console.WriteLine("Warning: Not enough data to read 4 bytes for an Int32.");
			return 0;
		}
	}

	public string ReadStringUtf8(int length)
	{
		if (length <= 0)
		{
			Console.WriteLine("Warning: Attempted to read a string with non-positive length.");
			return string.Empty;
		}

		if (!CanRead(length))
		{
			Console.WriteLine($"Warning: Not enough data to read a string of length {length}.");
			return string.Empty;
		}

		byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
		int bytesRead = BaseStream.Read(buffer, 0, length);

		try
		{
			return Encoding.UTF8.GetString(buffer, 0, bytesRead);
		}
		catch (DecoderFallbackException)
		{
			Console.WriteLine("Warning: Found invalid UTF-8 string, using replacement characters.");
			return Encoding.UTF8.GetString(buffer, 0, bytesRead);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error while reading the string: {ex.Message}");
			return "InvalidEntry";
		}
		finally
		{
			System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	#endregion
}