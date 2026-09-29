using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using System.Threading.Tasks;

namespace PakWorkbench.src.ext.Workbench;

#region Win32 Window Focus & Restore Helper

internal static partial class Win32FocusHelper
{
	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool SetForegroundWindow(IntPtr hWnd);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool IsIconic(IntPtr hWnd);

	private const int SW_RESTORE = 9;

	public static void BringToFront(IntPtr handle)
	{
		if (handle == IntPtr.Zero) return;

		if (IsIconic(handle))
		{
			ShowWindow(handle, SW_RESTORE);
		}
		SetForegroundWindow(handle);
	}
}

#endregion

#region Core Interfaces & Models

public interface IEditorJumpProvider
{
	string Name { get; }
	int Priority { get; }
	bool CanHandle(Process? editorProcess, string executableName);
	Task<bool> JumpAsync(Process? editorProcess, string executablePath, string filePath, int line, int column);
}

public sealed class EditorCliConfig
{
	public string Name { get; set; } = "";
	public string Executable { get; set; } = "";
	public string Args { get; set; } = "";
}

#endregion

#region Universal Jump Engine

public sealed class UniversalJumpEngine
{
	private readonly List<IEditorJumpProvider> _providers;
	private readonly SmartCliJumpProvider _smartCliProvider;
	private readonly FallbackJumpProvider _fallbackProvider;

	public UniversalJumpEngine()
	{
		_smartCliProvider = new SmartCliJumpProvider();
		_fallbackProvider = new FallbackJumpProvider();

		_providers =
		[
			new VisualStudioJumpProvider(), // Priority 10: Nativ VS DTE/COM
			new UrlSchemeJumpProvider(),    // Priority 15: Windows OS URL Protocol (vscode://, idea://)
			_smartCliProvider,              // Priority 20: Smart Dynamic CLI
			new SendKeysJumpProvider(),     // Priority 30: Win32 Focus + SendKeys (Ctrl+G)
			_fallbackProvider               // Priority 99: Default Open
		];

		_providers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
	}

	public void UpdateCliConfigs(IEnumerable<EditorCliConfig> configs)
	{
		_smartCliProvider.UpdateConfigs(configs);
		EditorDiscovery.RegisterKnownExecutables(configs);
	}

	private static Process? FindRunningEditorProcess(string executablePath)
	{
		try
		{
			string processName = Path.GetFileNameWithoutExtension(executablePath);
			Process[] processes = Process.GetProcessesByName(processName);
			return processes.FirstOrDefault(p => !p.HasExited && p.MainWindowHandle != IntPtr.Zero);
		}
		catch
		{
			return null;
		}
	}

	public async Task<bool> ExecuteJumpAsync(string executablePath, string filePath, int line, int column)
	{
		if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(filePath))
			return false;

		line = Math.Max(1, line);
		column = Math.Max(1, column);
		string exeName = Path.GetFileName(executablePath);

		using Process? runningProcess = FindRunningEditorProcess(executablePath);

		foreach (IEditorJumpProvider provider in _providers)
		{
			try
			{
				if (!provider.CanHandle(runningProcess, exeName))
					continue;

				if (await provider.JumpAsync(runningProcess, executablePath, filePath, line, column).ConfigureAwait(false))
					return true;
			}
			catch
			{
			}
		}
		return false;
	}
}

public sealed class EditorJumpModule
{
	private readonly UniversalJumpEngine _engine = new();

	public void UpdateCliConfigs(IEnumerable<EditorCliConfig> configs) => _engine.UpdateCliConfigs(configs);

	public Task<bool> JumpAsync(string executablePath, string filePath, int line, int column) =>
		_engine.ExecuteJumpAsync(executablePath, filePath, line, column);

	public bool Jump(string executablePath, string filePath, int line, int column) =>
		JumpAsync(executablePath, filePath, line, column).GetAwaiter().GetResult();
}

#endregion

#region URL Scheme Provider

public sealed class UrlSchemeJumpProvider : IEditorJumpProvider
{
	public string Name => "Windows Protocol Handler (URL Scheme)";
	public int Priority => 15;

	public bool CanHandle(Process? editorProcess, string executableName)
	{
		string name = executableName.ToLowerInvariant();
		return name.Contains("code") || name.Contains("idea") || name.Contains("rider");
	}

	public Task<bool> JumpAsync(Process? editorProcess, string executablePath, string filePath, int line, int column)
	{
		string exeName = Path.GetFileName(executablePath).ToLowerInvariant();
		string? url = null;

		if (exeName.Contains("code"))
		{
			// VS Code protocol: vscode://file/C:/path/file.cs:line:col
			url = $"vscode://file/{filePath.Replace('\\', '/')}:{line}:{column}";
		}
		else if (exeName.Contains("idea") || exeName.Contains("rider"))
		{
			// JetBrains protocol: idea://open?file=C:/path/file.cs&line=10&column=2
			url = $"idea://open?file={Uri.EscapeDataString(filePath)}&line={line}&column={column}";
		}

		if (url != null)
		{
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = url,
					UseShellExecute = true
				});
				return Task.FromResult(true);
			}
			catch { }
		}

		return Task.FromResult(false);
	}
}

#endregion

#region Win32 Keyboard Automation Provider (SendKeys)

public sealed class SendKeysJumpProvider : IEditorJumpProvider
{
	public string Name => "Win32 Keyboard Focus & SendKeys";
	public int Priority => 30;

	public bool CanHandle(Process? editorProcess, string executableName)
	{
		return editorProcess != null && editorProcess.MainWindowHandle != IntPtr.Zero;
	}

	public async Task<bool> JumpAsync(Process? editorProcess, string executablePath, string filePath, int line, int column)
	{
		if (editorProcess == null || editorProcess.MainWindowHandle == IntPtr.Zero)
			return false;

		try
		{
			Win32FocusHelper.BringToFront(editorProcess.MainWindowHandle);
			await Task.Delay(150);

			System.Windows.Forms.SendKeys.SendWait("^g");
			await Task.Delay(100);

			System.Windows.Forms.SendKeys.SendWait($"{line}");
			await Task.Delay(50);
			System.Windows.Forms.SendKeys.SendWait("~");

			return true;
		}
		catch
		{
			return false;
		}
	}
}

#endregion

#region Smart Dynamic CLI Provider

public sealed class SmartCliJumpProvider : IEditorJumpProvider
{
	public string Name => "Smart Dynamic CLI";
	public int Priority => 20;

	private readonly Dictionary<string, string[]> _builtInArgs = new(StringComparer.OrdinalIgnoreCase)
	{
		// JetBrains IDEs
		["idea.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["idea64.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["rider.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["rider64.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["clion.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["clion64.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["pycharm.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["pycharm64.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["webstorm.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["webstorm64.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["goland.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["goland64.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["phpstorm.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["phpstorm64.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],
		["fleet.exe"] = [ "--line {line} --column {column} \"{file}\"", "--line {line} \"{file}\"" ],

		// VS Code family
		["code.exe"] = [ "-r -g \"{file}:{line}:{column}\"", "-g \"{file}:{line}:{column}\"" ],
		["vscodium.exe"] = [ "-r -g \"{file}:{line}:{column}\"", "-g \"{file}:{line}:{column}\"" ],
		["cursor.exe"] = [ "-r -g \"{file}:{line}:{column}\"", "-g \"{file}:{line}:{column}\"" ],

		// Other editors
		["notepad++.exe"] = [ "\"{file}\" -n{line} -c{column}" ],
		["sublime_text.exe"] = [ "\"{file}:{line}:{column}\"" ],
		["zed.exe"] = [ "\"{file}:{line}:{column}\"" ],
		["neovim.exe"] = [ "+{line} \"{file}\"" ],
		["nvim.exe"] = [ "+{line} \"{file}\"" ]
	};

	private Dictionary<string, string> _userConfigs = new(StringComparer.OrdinalIgnoreCase);

	public void UpdateConfigs(IEnumerable<EditorCliConfig> configs)
	{
		_userConfigs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (configs != null)
		{
			foreach (var c in configs)
			{
				if (!string.IsNullOrWhiteSpace(c.Executable))
				{
					_userConfigs[Path.GetFileName(c.Executable)] = c.Args;
				}
			}
		}
	}

	public bool CanHandle(Process? editorProcess, string executableName)
	{
		return _userConfigs.ContainsKey(executableName) || _builtInArgs.ContainsKey(executableName);
	}

	public Task<bool> JumpAsync(Process? editorProcess, string executablePath, string filePath, int line, int column)
	{
		string exeName = Path.GetFileName(executablePath);
		string workDir = Path.GetDirectoryName(executablePath) ?? "";

		try
		{
			if (_userConfigs.TryGetValue(exeName, out string? userArgs) && !string.IsNullOrWhiteSpace(userArgs))
			{
				string args = userArgs
					.Replace("{file}", filePath)
					.Replace("{line}", line.ToString())
					.Replace("{column}", column.ToString());

				Process.Start(new ProcessStartInfo
				{
					FileName = executablePath,
					Arguments = args,
					UseShellExecute = true,
					WorkingDirectory = workDir
				});

				return Task.FromResult(true);
			}

			if (_builtInArgs.TryGetValue(exeName, out var templates) && templates != null)
			{
				foreach (var template in templates)
				{
					try
					{
						string args = template
							.Replace("{file}", filePath)
							.Replace("{line}", line.ToString())
							.Replace("{column}", column.ToString());

						Process.Start(new ProcessStartInfo
						{
							FileName = executablePath,
							Arguments = args,
							UseShellExecute = true,
							WorkingDirectory = workDir
						});

						return Task.FromResult(true);
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}

		return Task.FromResult(false);
	}
}

#endregion

#region Fallback Provider

public sealed class FallbackJumpProvider : IEditorJumpProvider
{
	public string Name => "Basic File Open";
	public int Priority => 99;

	public bool CanHandle(Process? editorProcess, string executableName) => true;

	public Task<bool> JumpAsync(Process? editorProcess, string executablePath, string filePath, int line, int column)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = executablePath,
				Arguments = $"\"{filePath.Replace("\"", "\\\"")}\"",
				UseShellExecute = true,
				WorkingDirectory = Path.GetDirectoryName(executablePath) ?? ""
			});
			return Task.FromResult(true);
		}
		catch
		{
			return Task.FromResult(false);
		}
	}
}

#endregion

#region Visual Studio Full DTE / ROT / COM Provider

public sealed partial class VisualStudioJumpProvider : IEditorJumpProvider
{
	public string Name => "Visual Studio DTE/COM (Full ROT & Cache)";
	public int Priority => 10;

	#region Native COM Imports & Interfaces

	[LibraryImport("ole32.dll", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int CLSIDFromProgID(string lpszProgID, out Guid clsid);

	[LibraryImport("oleaut32.dll")]
	private static partial int GetActiveObject(ref Guid rclsid, IntPtr pvReserved, out IntPtr ppunk);

	[LibraryImport("ole32.dll")]
	private static partial int CreateBindCtx(uint reserved, out IntPtr ppbc);

	[GeneratedComInterface]
	[Guid("00000113-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal partial interface IEnumMoniker
	{
		[PreserveSig] int Next(int celt, out nint rgelt, out int pceltFetched);
		[PreserveSig] int Skip(int celt);
		[PreserveSig] int Reset();
		[PreserveSig] int Clone(out IEnumMoniker ppenum);
	}

	[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
	[Guid("0000000F-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal partial interface IMoniker
	{
		[PreserveSig] int GetClassID(out Guid pClassID);
		[PreserveSig] int IsDirty();
		[PreserveSig] int Load(nint pStm);
		[PreserveSig] int Save(nint pStm, [MarshalAs(UnmanagedType.Bool)] bool fClearDirty);
		[PreserveSig] int GetSizeMax(out long pcbSize);
		[PreserveSig] int BindToObject(IBindCtx pbc, IMoniker? pmkToLeft, in Guid riidResult, out nint ppvResult);
		[PreserveSig] int BindToStorage(IBindCtx pbc, IMoniker? pmkToLeft, in Guid riid, out nint ppvObj);
		[PreserveSig] int Reduce(IBindCtx pbc, int dwReduceHowFar, ref IMoniker? ppmkToLeft, out IMoniker? ppmkReduced);
		[PreserveSig] int ComposeWith(IMoniker pmkRight, [MarshalAs(UnmanagedType.Bool)] bool fOnlyIfNotGeneric, out IMoniker? ppmkComposite);
		[PreserveSig] int Enum([MarshalAs(UnmanagedType.Bool)] bool fForward, out IEnumMoniker? ppenumMoniker);
		[PreserveSig] int IsEqual(IMoniker pmkOtherMoniker);
		[PreserveSig] int Hash(out int pdwHash);
		[PreserveSig] int IsRunning(IBindCtx pbc, IMoniker? pmkToLeft, IMoniker? pmkNewlyRunning);
		[PreserveSig] int GetTimeOfLastChange(IBindCtx pbc, IMoniker? pmkToLeft, nint pFileTime);
		[PreserveSig] int Inverse(out IMoniker? ppmk);
		[PreserveSig] int CommonPrefixWith(IMoniker pmkOther, out IMoniker? ppmkPrefix);
		[PreserveSig] int RelativePathTo(IMoniker pmkOther, out IMoniker? ppmkRelPath);
		[PreserveSig] int GetDisplayName(IBindCtx pbc, IMoniker? pmkToLeft, out string ppszDisplayName);
		[PreserveSig] int ParseDisplayName(IBindCtx pbc, IMoniker? pmkToLeft, string pszDisplayName, out int pchEaten, out IMoniker? ppmkOut);
		[PreserveSig] int IsSystemMoniker(out int pdwMksys);
	}

	[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
	[Guid("0000000E-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal partial interface IBindCtx
	{
		[PreserveSig] int RegisterObjectBound(nint punk);
		[PreserveSig] int RevokeObjectBound(nint punk);
		[PreserveSig] int ReleaseBoundObjects();
		[PreserveSig] int SetBindOptions(nint pbindopts);
		[PreserveSig] int GetBindOptions(nint pbindopts);
		[PreserveSig] int GetRunningObjectTable(out IRunningObjectTable? pprot);
		[PreserveSig] int RegisterObjectParam(string pszKey, nint punk);
		[PreserveSig] int GetObjectParam(string pszKey, out nint ppunk);
		[PreserveSig] int EnumObjectParam(out nint ppenum);
		[PreserveSig] int RevokeObjectParam(string pszKey);
	}

	[GeneratedComInterface]
	[Guid("00000010-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal partial interface IRunningObjectTable
	{
		[PreserveSig] int Register(int grfFlags, nint punkObject, IMoniker pmk, out int pdwRegister);
		[PreserveSig] int Revoke(int dwRegister);
		[PreserveSig] int IsRunning(IMoniker pmk);
		[PreserveSig] int GetObject(IMoniker pmk, out nint ppunkObject);
		[PreserveSig] int NoteChangeTime(int dwRegister, nint pFileTime);
		[PreserveSig] int GetTimeOfLastChange(IMoniker pmk, nint pFileTime);
		[PreserveSig] int EnumRunning(out IEnumMoniker? ppenumMoniker);
	}

	#endregion

	public bool CanHandle(Process? editorProcess, string executableName)
	{
		return executableName.Equals("devenv.exe", StringComparison.OrdinalIgnoreCase);
	}

	public Task<bool> JumpAsync(Process? editorProcess, string executablePath, string filePath, int line, int column)
	{
		return Task.FromResult(TryOpenInVisualStudio(filePath, line, column));
	}

	#region VS Instance Cache Layer & DTE Resolution

	private static List<object>? _cachedDteInstances;

	private static bool IsDteAlive(object dteObj)
	{
		try
		{
			dynamic dte = dteObj;
			string name = dte.Name;
			return !string.IsNullOrEmpty(name);
		}
		catch
		{
			return false;
		}
	}

	private static List<object> GetValidDteInstances()
	{
		if (_cachedDteInstances != null)
		{
			_cachedDteInstances = [.. _cachedDteInstances.Where(IsDteAlive)];
			if (_cachedDteInstances.Count > 0)
			{
				return _cachedDteInstances;
			}
		}

		_cachedDteInstances = GetRunningVisualStudioInstances();
		return _cachedDteInstances;
	}

	private static object? GetActiveComObject(string progId)
	{
		try
		{
			if (CLSIDFromProgID(progId, out Guid clsid) != 0)
				return null;

			if (GetActiveObject(ref clsid, IntPtr.Zero, out IntPtr pUnk) == 0 && pUnk != IntPtr.Zero)
			{
				try
				{
					return Marshal.GetObjectForIUnknown(pUnk);
				}
				finally
				{
					Marshal.Release(pUnk);
				}
			}
		}
		catch { }
		return null;
	}

	private static unsafe List<object> GetRunningVisualStudioInstances()
	{
		List<object> result = [];

		try
		{
			int hr = CreateBindCtx(0, out IntPtr pBindCtx);
			if (hr != 0 || pBindCtx == IntPtr.Zero)
				return result;

			IBindCtx? bindCtx = ComInterfaceMarshaller<IBindCtx>.ConvertToManaged((void*)pBindCtx);
			if (bindCtx == null)
			{
				Marshal.Release(pBindCtx);
				return result;
			}

			hr = bindCtx.GetRunningObjectTable(out IRunningObjectTable? rot);
			if (hr != 0 || rot == null)
				return result;

			hr = rot.EnumRunning(out IEnumMoniker? enumMoniker);
			if (hr != 0 || enumMoniker == null)
				return result;

			while (true)
			{
				hr = enumMoniker.Next(1, out nint pMoniker, out int fetched);
				if (hr != 0 || fetched == 0 || pMoniker == 0)
					break;

				try
				{
					IMoniker? moniker = ComInterfaceMarshaller<IMoniker>.ConvertToManaged((void*)pMoniker);
					if (moniker == null)
					{
						Marshal.Release(pMoniker);
						continue;
					}

					if (moniker.GetDisplayName(bindCtx, null, out string displayName) != 0)
						continue;

					if (!displayName.Contains("VisualStudio.DTE", StringComparison.OrdinalIgnoreCase))
						continue;

					if (rot.GetObject(moniker, out nint pDteObject) == 0 && pDteObject != 0)
					{
						try
						{
							object? dteObject = Marshal.GetObjectForIUnknown(pDteObject);
							if (dteObject != null)
							{
								result.Add(dteObject);
							}
						}
						finally
						{
							Marshal.Release(pDteObject);
						}
					}
				}
				catch { }
			}
		}
		catch { }

		return result;
	}

	#endregion

	#region Document Automation & Focus Controls

	private static dynamic? FindOpenDocument(dynamic dte, string fullPath)
	{
		string wantedPath;
		try
		{
			wantedPath = Path.GetFullPath(fullPath);
		}
		catch
		{
			return null;
		}

		try
		{
			dynamic documents = dte.Documents;
			foreach (dynamic doc in documents)
			{
				try
				{
					string? docPath = null;
					try { docPath = doc.FullName; } catch { }

					if (string.IsNullOrWhiteSpace(docPath))
						continue;

					string normalizedDocPath;
					try { normalizedDocPath = Path.GetFullPath(docPath); } catch { continue; }

					if (string.Equals(normalizedDocPath, wantedPath, StringComparison.OrdinalIgnoreCase))
					{
						return doc;
					}
				}
				catch { }
			}
		}
		catch { }

		return null;
	}

	private static bool ActivateDocumentAndGoToLine(dynamic dte, dynamic document, int line, int column)
	{
		try
		{
			line = Math.Max(1, line);
			column = Math.Max(1, column);

			try { document.Activate(); } catch { }
			try { document.ActiveWindow.Activate(); } catch { }
			try { dte.MainWindow.Activate(); } catch { }

			dynamic selection = document.Selection;
			selection.GotoLine(line, true);

			if (column > 1)
			{
				try
				{
					selection.MoveToLineAndOffset(line, column, true);
				}
				catch { }
			}

			try { document.Activate(); } catch { }
			try { document.ActiveWindow.Activate(); } catch { }
			try { dte.MainWindow.Activate(); } catch { }

			return true;
		}
		catch
		{
			return false;
		}
	}

	private static object? FindVisualStudioWithOpenDocument(string fullPath)
	{
		string normalizedPath;
		try { normalizedPath = Path.GetFullPath(fullPath); } catch { return null; }

		List<object> instances = GetValidDteInstances();
		foreach (object instance in instances)
		{
			try
			{
				dynamic dte = instance;
				dynamic? document = FindOpenDocument(dte, normalizedPath);
				if (document != null)
					return instance;
			}
			catch { }
		}

		return null;
	}

	private static object? GetAnyActiveVisualStudio()
	{
		List<object> instances = GetValidDteInstances();
		if (instances.Count > 0)
			return instances[0];

		string[] progIds =
		[
			"VisualStudio.DTE.18.0",
			"VisualStudio.DTE.17.0",
			"VisualStudio.DTE.16.0",
			"VisualStudio.DTE.15.0",
			"VisualStudio.DTE"
		];

		foreach (string progId in progIds)
		{
			try
			{
				object? obj = GetActiveComObject(progId);
				if (obj != null && IsDteAlive(obj))
				{
					_cachedDteInstances ??= [];
					_cachedDteInstances.Add(obj);
					return obj;
				}
			}
			catch { }
		}

		return null;
	}

	private static bool TryOpenInVisualStudio(string fullPath, int line, int column)
	{
		string normalizedPath;
		try { normalizedPath = Path.GetFullPath(fullPath); } catch { return false; }

		line = Math.Max(1, line);
		column = Math.Max(1, column);

		object? dteObject = FindVisualStudioWithOpenDocument(normalizedPath);

		if (dteObject != null)
		{
			try
			{
				dynamic dte = dteObject;
				dynamic? document = FindOpenDocument(dte, normalizedPath);

				if (document != null && ActivateDocumentAndGoToLine(dte, document, line, column))
				{
					return true;
				}
			}
			catch { }
		}

		dteObject = GetAnyActiveVisualStudio();
		if (dteObject == null)
			return false;

		try
		{
			dynamic dte = dteObject;

			try
			{
				dte.ItemOperations.OpenFile(normalizedPath);
			}
			catch { }

			dynamic? document = null;

			for (int attempt = 0; attempt < 100; attempt++)
			{
				try
				{
					document = FindOpenDocument(dte, normalizedPath);
					if (document != null)
						break;
				}
				catch { }

				Thread.Sleep(50);
			}

			if (document == null)
				return false;

			return ActivateDocumentAndGoToLine(dte, document, line, column);
		}
		catch
		{
			return false;
		}
	}

	#endregion
}

#endregion

#region Compatibility Facade & Legacy Support

public sealed class EditorProviderRegistry(IEditorJumpProvider? fallback = null)
{
	private readonly UniversalJumpEngine _engine = new();
	private readonly IEditorJumpProvider? _fallback = fallback;

	public void UpdateCliConfigs(IEnumerable<EditorCliConfig> configs)
	{
		_engine.UpdateCliConfigs(configs);
	}

	public IEditorJumpProvider GetProvider(string executableName)
	{
		_ = executableName;
		return new RegistryProxy(_engine);
	}

	private sealed class RegistryProxy(UniversalJumpEngine engine) : IEditorJumpProvider
	{
		private readonly UniversalJumpEngine _engine = engine;

		public string Name => "Registry Engine Proxy";
		public int Priority => 1;

		public bool CanHandle(Process? editorProcess, string executableName)
			=> true;

		public Task<bool> JumpAsync(Process? editorProcess, string executablePath, string filePath, int line, int column)
			=> _engine.ExecuteJumpAsync(executablePath, filePath, line, column);
	}
}

public static class EditorProviderRegistryExtensions
{
	public static bool CanHandle(this IEditorJumpProvider provider, string executableName)
	{
		return provider.CanHandle(null, executableName);
	}

	public static bool Jump(this IEditorJumpProvider provider, string executablePath, string filePath, int line, int column)
	{
		return provider.JumpAsync(null, executablePath, filePath, line, column).GetAwaiter().GetResult();
	}
}

#endregion