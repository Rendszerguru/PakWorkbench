using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PakWorkbench.src.ext.Workbench;

public sealed class WorkbenchNetApiClient(string host = "127.0.0.1", int port = 5775)
{
	public string Host { get; } = host;
	public int Port { get; } = port;

	public async Task<JsonDocument> CallAsync(object request, CancellationToken cancellationToken = default)
	{
		string json = JsonSerializer.Serialize(request);
		byte[] response = await SendAsync("PakWorkbenchSyncTest", "JsonRPC", json, cancellationToken);

		if (response.Length == 0)
			return JsonDocument.Parse("{}");

		return JsonDocument.Parse(response);
	}

	public Task<JsonDocument> IsWorkbenchRunningAsync(CancellationToken ct = default) =>
		CallAsync(new { APIFunc = "IsWorkbenchRunning" }, ct);

	public Task<JsonDocument> OpenResourceAsync(string resourceName, CancellationToken ct = default) =>
		CallAsync(new { APIFunc = "OpenResource", ResourceName = resourceName }, ct);

	public Task<JsonDocument> BringModuleWindowToFrontAsync(string moduleName, CancellationToken ct = default) =>
		CallAsync(new { APIFunc = "BringModuleWindowToFront", ModuleName = moduleName }, ct);

	public Task<JsonDocument> ValidateScriptsAsync(string configuration = "WORKBENCH", CancellationToken ct = default) =>
		CallAsync(new { APIFunc = "ValidateScripts", Configuration = configuration }, ct);

	private async Task<byte[]> SendAsync(
		string clientId,
		string contentType,
		string payload,
		CancellationToken cancellationToken)
	{
		using TcpClient client = new();
		client.NoDelay = true;

		await client.ConnectAsync(Host, Port, cancellationToken);

		using NetworkStream stream = client.GetStream();

		await WriteInt32Async(stream, 1, cancellationToken);
		await WriteStringAsync(stream, clientId, cancellationToken);
		await WriteStringAsync(stream, contentType, cancellationToken);
		await WriteStringAsync(stream, payload, cancellationToken);

		string errorCode = await ReadStringAsync(stream, cancellationToken);
		string responsePayload = await ReadStringAsync(stream, cancellationToken);

		if (!string.IsNullOrWhiteSpace(errorCode) && !string.Equals(errorCode, "Ok", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"Workbench NET API error: {errorCode}\nPayload: {responsePayload}");
		}

		return Encoding.UTF8.GetBytes(responsePayload);
	}

	private static async Task WriteInt32Async(
		NetworkStream stream,
		int value,
		CancellationToken cancellationToken)
	{
		byte[] bytes = BitConverter.GetBytes(value);
		await stream.WriteAsync(bytes, cancellationToken);
	}

	private static async Task WriteStringAsync(
		NetworkStream stream,
		string value,
		CancellationToken cancellationToken)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		await WriteInt32Async(stream, bytes.Length, cancellationToken);
		await stream.WriteAsync(bytes, cancellationToken);
	}

	private static async Task<string> ReadStringAsync(
		NetworkStream stream,
		CancellationToken cancellationToken)
	{
		int length = await ReadInt32Async(stream, cancellationToken);

		if (length < 0 || length > 64 * 1024 * 1024)
			throw new InvalidDataException($"Invalid Workbench string length: {length}");

		byte[] bytes = new byte[length];
		int offset = 0;

		while (offset < bytes.Length)
		{
			int read = await stream.ReadAsync(
				bytes.AsMemory(offset, bytes.Length - offset),
				cancellationToken);

			if (read == 0)
				throw new EndOfStreamException("Workbench closed the NET API connection.");

			offset += read;
		}

		return Encoding.UTF8.GetString(bytes);
	}

	private static async Task<int> ReadInt32Async(
		NetworkStream stream,
		CancellationToken cancellationToken)
	{
		byte[] bytes = new byte[4];
		int offset = 0;

		while (offset < 4)
		{
			int read = await stream.ReadAsync(
				bytes.AsMemory(offset, 4 - offset),
				cancellationToken);

			if (read == 0)
				throw new EndOfStreamException("Workbench closed the NET API connection.");

			offset += read;
		}

		return BitConverter.ToInt32(bytes, 0);
	}
}