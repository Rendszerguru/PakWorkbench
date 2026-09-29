using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace PakWorkbench.src.ext.Workbench;

#region Models & Interfaces

public readonly struct EditorInfo(string name, string path)
{
	public string Name { get; } = name;
	public string Path { get; } = path;

	public override string ToString() => Name;
}

public interface IEditorDiscoveryProvider
{
	string Name { get; }
	int Priority { get; }
	void Discover(Action<string, string> addCandidate, HashSet<string> knownExes);
}

#endregion

#region Discovery Providers

public sealed class VsWhereDiscoveryProvider : IEditorDiscoveryProvider
{
	public string Name => "Visual Studio vswhere Discovery";
	public int Priority => 10;

	public void Discover(Action<string, string> addCandidate, HashSet<string> knownExes)
	{
		try
		{
			string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
			string vswherePath = Path.Combine(pf86, "Microsoft Visual Studio", "Installer", "vswhere.exe");

			if (File.Exists(vswherePath))
			{
				var psi = new ProcessStartInfo
				{
					FileName = vswherePath,
					Arguments = "-products * -requires Microsoft.Component.MSBuild -property installationPath",
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};

				using var proc = Process.Start(psi);
				if (proc != null)
				{
					string output = proc.StandardOutput.ReadToEnd();
					proc.WaitForExit();

					using var reader = new StringReader(output);
					string? line;
					while ((line = reader.ReadLine()) != null)
					{
						string installPath = line.Trim();
						if (!string.IsNullOrEmpty(installPath))
						{
							string devEnvPath = Path.Combine(installPath, "Common7", "IDE", "devenv.exe");
							addCandidate(devEnvPath, "Visual Studio");
						}
					}
				}
			}
		}
		catch { }
	}
}

public sealed class UninstallRegistryDiscoveryProvider : IEditorDiscoveryProvider
{
	public string Name => "Windows Uninstall Registry Discovery";
	public int Priority => 20;

	public void Discover(Action<string, string> addCandidate, HashSet<string> knownExes)
	{
		ScanRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", addCandidate, knownExes);
		ScanRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", addCandidate, knownExes);
		ScanRegistry(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", addCandidate, knownExes);
	}

	private static void ScanRegistry(RegistryKey rootKey, string uninstallPath, Action<string, string> addCandidate, HashSet<string> knownExes)
	{
		try
		{
			using var key = rootKey.OpenSubKey(uninstallPath);
			if (key == null) return;

			foreach (var subKeyName in key.GetSubKeyNames())
			{
				using var subKey = key.OpenSubKey(subKeyName);
				if (subKey == null) continue;

				string? displayName = subKey.GetValue("DisplayName") as string;
				string? installLocation = subKey.GetValue("InstallLocation") as string;
				string? displayIcon = subKey.GetValue("DisplayIcon") as string;

				if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
				{
					try
					{
						var exes = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.AllDirectories);
						foreach (var exe in exes)
						{
							if (knownExes.Contains(Path.GetFileName(exe)))
							{
								addCandidate(exe, displayName ?? "");
							}
						}
					}
					catch { }
				}

				if (!string.IsNullOrWhiteSpace(displayIcon))
				{
					string exePath = ExtractExeFromCommand(displayIcon);
					if (File.Exists(exePath) && knownExes.Contains(Path.GetFileName(exePath)))
					{
						addCandidate(exePath, displayName ?? "");
					}
				}
			}
		}
		catch { }
	}

	private static string ExtractExeFromCommand(string command)
	{
		if (string.IsNullOrWhiteSpace(command)) return "";
		command = command.Trim();
		if (command.StartsWith('"'))
		{
			int nextQuote = command.IndexOf('\"', 1);
			if (nextQuote > 1) return command[1..nextQuote];
		}
		int commaIndex = command.IndexOf(',');
		if (commaIndex > 0) command = command[..commaIndex];

		int spaceIndex = command.IndexOf(' ');
		return spaceIndex > 0 ? command[..spaceIndex] : command;
	}
}

public sealed class JetBrainsDirectoryDiscoveryProvider : IEditorDiscoveryProvider
{
	public string Name => "JetBrains Default Directories Discovery";
	public int Priority => 30;

	public void Discover(Action<string, string> addCandidate, HashSet<string> knownExes)
	{
		try
		{
			string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

			string[] searchPaths =
			[
				Path.Combine(localAppData, "Programs", "JetBrains"),
				Path.Combine(localAppData, "JetBrains", "Toolbox", "apps"),
				Path.Combine(programFiles, "JetBrains")
			];

			foreach (var basePath in searchPaths)
			{
				if (!Directory.Exists(basePath)) continue;

				var executables = Directory.EnumerateFiles(basePath, "*.exe", SearchOption.AllDirectories);
				foreach (var exe in executables)
				{
					string fileName = Path.GetFileName(exe);
					if (knownExes.Contains(fileName))
					{
						addCandidate(exe, "");
					}
				}
			}
		}
		catch { }
	}
}

public sealed class FileAssociationDiscoveryProvider : IEditorDiscoveryProvider
{
	public string Name => "File Associations Discovery";
	public int Priority => 40;

	public void Discover(Action<string, string> addCandidate, HashSet<string> knownExes)
	{
		string[] extensions = [".c", ".cpp", ".cs", ".txt"];

		foreach (var ext in extensions)
		{
			try
			{
				using var key = Registry.ClassesRoot.OpenSubKey($@"SystemFileAssociations\{ext}\OpenWithList");
				if (key != null)
				{
					foreach (var subKeyName in key.GetSubKeyNames())
					{
						using var appKey = Registry.ClassesRoot.OpenSubKey($@"Applications\{subKeyName}\shell\open\command");
						if (appKey?.GetValue("") is string cmd)
						{
							string exePath = ExtractExeFromCommand(cmd);
							if (knownExes.Contains(Path.GetFileName(exePath)))
							{
								addCandidate(exePath, "");
							}
						}
					}
				}
			}
			catch { }
		}
	}

	private static string ExtractExeFromCommand(string command)
	{
		if (string.IsNullOrWhiteSpace(command)) return "";
		command = command.Trim();
		if (command.StartsWith('"'))
		{
			int nextQuote = command.IndexOf('\"', 1);
			if (nextQuote > 1) return command[1..nextQuote];
		}
		int spaceIndex = command.IndexOf(' ');
		return spaceIndex > 0 ? command[..spaceIndex] : command;
	}
}

public sealed class AppPathsDiscoveryProvider : IEditorDiscoveryProvider
{
	public string Name => "Registry App Paths Discovery";
	public int Priority => 50;

	public void Discover(Action<string, string> addCandidate, HashSet<string> knownExes)
	{
		ScanRegistryAppPaths(Registry.LocalMachine, addCandidate, knownExes);
		ScanRegistryAppPaths(Registry.CurrentUser, addCandidate, knownExes);
	}

	private static void ScanRegistryAppPaths(RegistryKey rootKey, Action<string, string> addCandidate, HashSet<string> knownExes)
	{
		try
		{
			string registryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
			using var key = rootKey.OpenSubKey(registryPath);
			if (key == null) return;

			foreach (var subKeyName in key.GetSubKeyNames())
			{
				if (!knownExes.Contains(subKeyName)) continue;

				using var subKey = key.OpenSubKey(subKeyName);
				if (subKey?.GetValue("") is string exePath)
				{
					addCandidate(exePath, "");
				}
			}
		}
		catch { }
	}
}

#endregion

#region Core Universal Discovery Engine

public static class EditorDiscovery
{
	public static readonly HashSet<string> KnownEditorExes = new(StringComparer.OrdinalIgnoreCase)
	{
		// JetBrains IDEs
		"rider.exe", "rider64.exe", "clion.exe", "clion64.exe", "idea.exe", "idea64.exe", "webstorm.exe", "webstorm64.exe", "pycharm.exe", "pycharm64.exe", "goland.exe", "goland64.exe", "phpstorm.exe", "phpstorm64.exe",
		// General / Popular editors
		"code.exe", "notepad++.exe", "sublime_text.exe", "devenv.exe", "vscodium.exe", "cursor.exe", "fleet.exe", "zed.exe", "neovim.exe", "nvim.exe"
	};

	private static readonly List<IEditorDiscoveryProvider> Providers =
	[
		new VsWhereDiscoveryProvider(),
		new UninstallRegistryDiscoveryProvider(),
		new JetBrainsDirectoryDiscoveryProvider(),
		new FileAssociationDiscoveryProvider(),
		new AppPathsDiscoveryProvider()
	];

	public static void RegisterKnownExecutable(string executable)
	{
		if (!string.IsNullOrWhiteSpace(executable))
		{
			KnownEditorExes.Add(Path.GetFileName(executable));
		}
	}

	public static void RegisterKnownExecutables(IEnumerable<string> executables)
	{
		if (executables == null) return;
		foreach (var exe in executables)
		{
			RegisterKnownExecutable(exe);
		}
	}

	public static void RegisterKnownExecutables(IEnumerable<EditorCliConfig> configs)
	{
		if (configs == null) return;
		foreach (var config in configs)
		{
			RegisterKnownExecutable(config.Executable);
		}
	}

	public static void RegisterProvider(IEditorDiscoveryProvider provider)
	{
		if (provider != null && !Providers.Contains(provider))
		{
			Providers.Add(provider);
			Providers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
		}
	}

	public static List<EditorInfo> DiscoverInstalledEditors()
	{
		var editors = new List<EditorInfo>();
		var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		void AddCandidate(string? path, string defaultName = "")
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

			string fullPath = Path.GetFullPath(path);
			if (seenPaths.Add(fullPath))
			{
				string friendlyName = GetFriendlyName(Path.GetFileName(fullPath), fullPath);
				if (string.IsNullOrWhiteSpace(friendlyName) || friendlyName.Equals(Path.GetFileNameWithoutExtension(fullPath), StringComparison.OrdinalIgnoreCase))
				{
					friendlyName = !string.IsNullOrWhiteSpace(defaultName) ? defaultName : Path.GetFileNameWithoutExtension(fullPath);
				}
				editors.Add(new EditorInfo(friendlyName, fullPath));
			}
		}

		var sortedProviders = Providers.OrderBy(p => p.Priority).ToList();
		foreach (var provider in sortedProviders)
		{
			try
			{
				provider.Discover(AddCandidate, KnownEditorExes);
			}
			catch { }
		}

		return editors;
	}

	public static string GetFriendlyName(string exeFileName, string fullPath)
	{
		try
		{
			var versionInfo = FileVersionInfo.GetVersionInfo(fullPath);
			if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
			{
				return versionInfo.FileDescription;
			}
		}
		catch { }

		return Path.GetFileNameWithoutExtension(exeFileName);
	}
}

#endregion

#region Configuration Classes

public class AppConfig
{
	public string Host { get; set; } = "127.0.0.1";
	public decimal Port { get; set; } = 5775;
	public int EditorIndex { get; set; } = 0;
	public string EditorPath { get; set; } = "";
	public string ProjectRoot { get; set; } = "";
	public string RelativePath { get; set; } = @"scripts\Game\PakWorkbenchSyncTest.c";
	public string SourceFile { get; set; } = "";
	public bool AutoValidateOnSave { get; set; } = true;

	[JsonPropertyName("EditorDefinitions")]
	public List<EditorCliConfig> EditorConfigs { get; set; } = [];
}

public static class EditorConfigDefaults
{
	public static List<EditorCliConfig> GetDefaultEditorConfigs()
	{
		return
		[
			new() { Name = "VS Code", Executable = "code.exe", Args = "-r -g \"{file}:{line}:{column}\"" },
			new() { Name = "VSCodium", Executable = "vscodium.exe", Args = "-r -g \"{file}:{line}:{column}\"" },
			new() { Name = "Cursor", Executable = "cursor.exe", Args = "-r -g \"{file}:{line}:{column}\"" },
			new() { Name = "Rider", Executable = "rider.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "Rider 64", Executable = "rider64.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "CLion", Executable = "clion.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "CLion 64", Executable = "clion64.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "IntelliJ IDEA", Executable = "idea.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "IntelliJ IDEA 64", Executable = "idea64.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "WebStorm", Executable = "webstorm.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "WebStorm 64", Executable = "webstorm64.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "PyCharm", Executable = "pycharm.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "PyCharm 64", Executable = "pycharm64.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "GoLand", Executable = "goland.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "GoLand 64", Executable = "goland64.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "PhpStorm", Executable = "phpstorm.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "PhpStorm 64", Executable = "phpstorm64.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "Fleet", Executable = "fleet.exe", Args = "--line {line} --column {column} \"{file}\"" },
			new() { Name = "Notepad++", Executable = "notepad++.exe", Args = "\"{file}\" -n{line} -c{column}" },
			new() { Name = "Sublime Text", Executable = "sublime_text.exe", Args = "\"{file}:{line}:{column}\"" },
			new() { Name = "Zed", Executable = "zed.exe", Args = "\"{file}:{line}:{column}\"" },
			new() { Name = "Neovim", Executable = "neovim.exe", Args = "+{line} \"{file}\"" },
			new() { Name = "Neovim (nvim)", Executable = "nvim.exe", Args = "+{line} \"{file}\"" }
		];
	}
}

#endregion