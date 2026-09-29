using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PakWorkbench.src.ext.Workbench
{
	#region Context Model

	public class WorkbenchContext
	{
		public string InternalPath { get; set; } = "";
		public string SandboxPath { get; set; } = "";
		public string TargetFilePath { get; set; } = "";
		public string Ext { get; set; } = "";
		public string GUID_PROJECT { get; set; } = "A1B2C3D4E5F60789";
		public string ProjectName { get; set; } = "PAKViewer";
		public string WbModule { get; set; } = "resourceManager";
		public string LoadResource { get; set; } = "";
	}

	#endregion

	public static partial class WorkbenchLauncherModule
	{
		#region Fields, Constants & State

		public static bool EnableLogging { get; set; } = false;

		private const string WORKBENCH_EXE_NAME = "ArmaReforgerWorkbenchSteamDiag.exe";
		private const string WORKBENCH_FALLBACK_EXE = "ArmaReforgerWorkbenchSteam.exe";

		private static readonly string LogFilePath = GetLogPath();
		private static DateTime _lastSteamRecovery = DateTime.MinValue;

		private static readonly Dictionary<string, (string ProjectName, string WbModule)> WorkbenchFormats =
			new(StringComparer.OrdinalIgnoreCase)
		{
			// 3D & Textures
			[".xob"] = ("XOBViewer", "resourceManager"),
			[".edds"] = ("ResourceManager", "resourceManager"),

			// Scripts & Localization
			[".c"] = ("ScriptEditor", "resourceManager"),
			[".conf"] = ("ConfigViewer", "resourceManager"),
			[".st"] = ("LocalizationEditor", "localizationEditor"),

			// Entities & World
			[".et"] = ("WorldEditor", "worldEditor"),
			[".ent"] = ("WorldEditor", "worldEditor"),
			[".layer"] = ("WorldEditor", "worldEditor"),

			// UI
			[".layout"] = ("LayoutEditor", "resourceManager"),
			[".styles"] = ("LayoutEditor", "resourceManager"),
			[".imageset"] = ("LayoutEditor", "resourceManager"),

			// Materials
			[".emat"] = ("MaterialEditor", "resourceManager"),
			[".gamemat"] = ("MaterialEditor", "resourceManager"),
			[".physmat"] = ("MaterialEditor", "resourceManager"),

			// Effects & Logic
			[".ptc"] = ("ParticleEditor", "particleEditor"),
			[".bt"] = ("BehaviorEditor", "resourceManager"),

			// Audio
			[".acp"] = ("AudioEditor", "audioEditor"),
			[".sig"] = ("AudioEditor", "audioEditor"),
			[".afm"] = ("AudioEditor", "audioEditor"),
			[".wav"] = ("ResourceManager", "resourceManager"),
			[".snd"] = ("ResourceManager", "resourceManager"),

			// Animation
			[".agf"] = ("AnimationEditor", "animEditor"),
			[".agr"] = ("AnimationEditor", "animEditor"),
			[".anm"] = ("AnimationEditor", "animEditor"),
			[".asi"] = ("AnimationEditor", "animEditor"),
			[".ast"] = ("AnimationEditor", "animEditor"),
			[".aw"] = ("AnimationEditor", "animEditor"),
			[".ae"] = ("AnimationEditor", "animEditor"),
			[".asy"] = ("AnimationEditor", "animEditor"),
			[".txa"] = ("AnimationEditor", "animEditor"),

			// Navigation & Procedural Animation
			[".nmn"] = ("NavmeshGenerator", "navmeshGeneratorMain"),
			[".pap"] = ("ProcAnimEditor", "procAnimEditor"),
			[".siga"] = ("ProcAnimEditor", "procAnimEditor"),

			// Project & Configuration
			[".gproj"] = ("ConfigViewer", "resourceManager"),
			[".meta"] = ("ConfigViewer", "resourceManager"),
			[".pre"] = ("ConfigViewer", "resourceManager"),
			[".fnt"] = ("ConfigViewer", "resourceManager"),
			[".ttf"] = ("ConfigViewer", "resourceManager")
		};

		#endregion

		#region Logging

		private static string GetLogPath()
		{
			try
			{
				string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
				if (Directory.Exists(desktop))
					return Path.Combine(desktop, "WorkbenchLauncherModule_Debug.log");
			}
			catch { }

			return Path.Combine(Path.GetTempPath(), "WorkbenchLauncherModule_Debug.log");
		}

		public static void Log(string message)
		{
			if (!EnableLogging)
				return;

			try
			{
				string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
				File.AppendAllText(LogFilePath, entry);
			}
			catch { }
		}

		#endregion

		#region Validation & Registry / Path Resolution

		public static bool IsWorkbenchSupported(string extension)
		{
			return WorkbenchFormats.ContainsKey(extension);
		}

		private static string ResolveWorkbenchExe(string exe)
		{
			try
			{
				string dir = Path.GetDirectoryName(exe) ?? "";

				string diag = Path.Combine(dir, WORKBENCH_EXE_NAME);
				string steam = Path.Combine(dir, WORKBENCH_FALLBACK_EXE);
				string legacy = Path.Combine(dir, "Workbench", WORKBENCH_FALLBACK_EXE);

				if (File.Exists(diag)) return diag;
				if (File.Exists(steam)) return steam;
				if (File.Exists(legacy)) return legacy;
			}
			catch { }

			return exe;
		}

		public static string GetWorkbenchPathFromRegistry()
		{
			string exePath = "";
			string subkey = @"SOFTWARE\Bohemia Interactive\Arma Reforger Tools";

			RegistryKey[] roots = [Registry.LocalMachine, Registry.CurrentUser];
			RegistryView[] views = [RegistryView.Registry64, RegistryView.Registry32, RegistryView.Default];

			foreach (var root in roots)
			{
				foreach (var view in views)
				{
					using var baseKey = RegistryKey.OpenBaseKey(
						root.Name == "HKEY_LOCAL_MACHINE"
							? RegistryHive.LocalMachine
							: RegistryHive.CurrentUser,
						view);

					using var key = baseKey.OpenSubKey(subkey);

					if (key != null)
					{
						var exeVal = key.GetValue("exe") as string;

						if (!string.IsNullOrEmpty(exeVal))
						{
							exePath = exeVal;
						}
						else
						{
							var pathVal = key.GetValue("path") as string;

							if (!string.IsNullOrEmpty(pathVal))
							{
								exePath = Path.Combine(pathVal, WORKBENCH_EXE_NAME);
							}
						}
					}

					if (!string.IsNullOrEmpty(exePath))
						break;
				}

				if (!string.IsNullOrEmpty(exePath))
					break;
			}

			if (string.IsNullOrEmpty(exePath))
			{
				using var key = Registry.ClassesRoot.OpenSubKey(
					@"enfusion\shell\open\command");

				if (key != null)
				{
					var val = key.GetValue(null) as string;

					if (!string.IsNullOrEmpty(val))
					{
						int firstQuote = val.IndexOf('"');
						int secondQuote = val.IndexOf('"', firstQuote + 1);

						if (firstQuote != -1 && secondQuote != -1)
						{
							exePath = val.Substring(
								firstQuote + 1,
								secondQuote - firstQuote - 1);
						}
					}
				}
			}

			if (!string.IsNullOrEmpty(exePath))
			{
				List<string> candidates = [exePath];

				string? dir = Path.GetDirectoryName(exePath);

				if (!string.IsNullOrEmpty(dir))
				{
					candidates.Add(Path.Combine(dir, WORKBENCH_EXE_NAME));
					candidates.Add(Path.Combine(dir, "Workbench", WORKBENCH_EXE_NAME));
					candidates.Add(Path.Combine(dir, WORKBENCH_FALLBACK_EXE));
					candidates.Add(Path.Combine(dir, "Workbench", WORKBENCH_FALLBACK_EXE));
				}

				bool found = false;

				foreach (var cand in candidates)
				{
					if (File.Exists(cand))
					{
						exePath = cand;
						found = true;
						break;
					}
				}

				if (!found)
					exePath = "";
			}

			return exePath;
		}

		public static string GetReforgerVanillaPath()
		{
			string subkey = @"SOFTWARE\Bohemia Interactive\Arma Reforger";
			RegistryView[] views = [RegistryView.Registry64, RegistryView.Registry32];

			foreach (var view in views)
			{
				using var baseKey = RegistryKey.OpenBaseKey(
					RegistryHive.LocalMachine,
					view);

				using var key = baseKey.OpenSubKey(subkey);

				if (key != null)
				{
					string installPath = key.GetValue("path") as string ?? "";

					if (!string.IsNullOrEmpty(installPath) &&
						Directory.Exists(installPath))
					{
						return installPath;
					}
				}
			}

			using var fallbackBaseKey = RegistryKey.OpenBaseKey(
				RegistryHive.LocalMachine,
				RegistryView.Registry32);

			using var fallbackKey = fallbackBaseKey.OpenSubKey(
				@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1874880");

			if (fallbackKey != null)
			{
				string installPath =
					fallbackKey.GetValue("InstallLocation") as string ?? "";

				if (!string.IsNullOrEmpty(installPath) &&
					Directory.Exists(installPath))
				{
					return installPath;
				}
			}

			return string.Empty;
		}

		#endregion

		#region Steam & Process Management

		private static void RecoverSteamIfNeeded()
		{
			try
			{
				if (IsSteamRunning())
					return;

				if ((DateTime.Now - _lastSteamRecovery).TotalSeconds < 15)
					return;

				_lastSteamRecovery = DateTime.Now;

				Process.Start(new ProcessStartInfo("steam://open/main")
				{
					UseShellExecute = true
				});
			}
			catch { }
		}

		private static bool IsWorkbenchDeadlocked(Process p)
		{
			try
			{
				return p.HasExited || !p.Responding;
			}
			catch
			{
				return true;
			}
		}

		private static int CountProcesses(string exeName)
		{
			return Process.GetProcessesByName(
				Path.GetFileNameWithoutExtension(exeName)).Length;
		}

		private static bool IsSteamRunning()
		{
			return CountProcesses("steam") > 0;
		}

		private static bool IsSteamIpcReady()
		{
			try
			{
				using var _ = new FileStream(
					@"\\.\pipe\SteamClient",
					FileMode.Open,
					FileAccess.ReadWrite,
					FileShare.None);

				return true;
			}
			catch
			{
				return false;
			}
		}

		private static bool IsSteamLoggedIn()
		{
			return Process.GetProcessesByName("steam")
				.Any(p => !string.IsNullOrEmpty(p.MainWindowTitle));
		}

		public static bool WaitForSteamReady(int maxWaitSeconds)
		{
			var sw = Stopwatch.StartNew();

			while (sw.Elapsed.TotalSeconds < maxWaitSeconds)
			{
				if (IsSteamRunning() &&
					IsSteamIpcReady() &&
					IsSteamLoggedIn())
				{
					return true;
				}

				Thread.Sleep(500);
			}

			return false;
		}

		public static bool IsWorkbenchRunning()
		{
			return CountProcesses(WORKBENCH_EXE_NAME) > 0 ||
				   CountProcesses(WORKBENCH_FALLBACK_EXE) > 0;
		}

		public static void TerminateWorkbench()
		{
			var procs =
				Process.GetProcessesByName(
					Path.GetFileNameWithoutExtension(WORKBENCH_EXE_NAME))
				.Concat(
					Process.GetProcessesByName(
						Path.GetFileNameWithoutExtension(WORKBENCH_FALLBACK_EXE)));

			foreach (var p in procs)
			{
				try
				{
					p.CloseMainWindow();

					if (!p.WaitForExit(3000))
						p.Kill();
				}
				catch { }
			}

			Thread.Sleep(300);
		}

		#endregion

		#region Open In Workbench (Public Entry Points & Internal Launch)

		public static void OpenInWorkbench(
			string internalPath,
			byte[] fileData,
			Func<string, byte[]?>? pakFileReader = null)
		{
			string sandboxDir = Path.Combine(
				Path.GetTempPath(),
				"PakWorkbench_Sandbox",
				Guid.NewGuid().ToString("N"));

			Directory.CreateDirectory(sandboxDir);

			string cleanPath = internalPath.Replace('\\', '/').TrimStart('/');
			string targetFile = Path.Combine(sandboxDir, cleanPath.Replace('/', Path.DirectorySeparatorChar));

			string? targetDir = Path.GetDirectoryName(targetFile);
			if (!string.IsNullOrEmpty(targetDir))
				Directory.CreateDirectory(targetDir);

			File.WriteAllBytes(targetFile, fileData);
			Log($"Main file written to sandbox: {targetFile}");

			HashSet<string> processedFiles = new(StringComparer.OrdinalIgnoreCase);
			Queue<string> pendingFiles = new();

			pendingFiles.Enqueue(cleanPath);
			processedFiles.Add(cleanPath);

			while (pendingFiles.Count > 0)
			{
				string currentRelPath = pendingFiles.Dequeue();
				string currentLocalFile = Path.Combine(sandboxDir, currentRelPath.Replace('/', Path.DirectorySeparatorChar));

				if (!File.Exists(currentLocalFile)) continue;

				try
				{
					string fileContent = File.ReadAllText(currentLocalFile);
					var matches = SandboxDependencyRegex().Matches(fileContent);

					foreach (Match match in matches)
					{
						string foundPath = NormalizeProjectPath(match.Value);

						if (processedFiles.Add(foundPath))
						{
							Log($"[Dependency Scanner] Discovered dependency: {foundPath}");

							if (pakFileReader != null)
							{
								byte[]? depBytes = pakFileReader(foundPath);
								if (depBytes != null && depBytes.Length > 0)
								{
									string depTargetFile = Path.Combine(sandboxDir, foundPath.Replace('/', Path.DirectorySeparatorChar));
									string? depDir = Path.GetDirectoryName(depTargetFile);
									if (!string.IsNullOrEmpty(depDir))
										Directory.CreateDirectory(depDir);

									File.WriteAllBytes(depTargetFile, depBytes);
									Log($"-> Extracted to sandbox: {depTargetFile}");

									pendingFiles.Enqueue(foundPath);
								}
								else
								{
									Log($"-> WARNING: Could not read dependency from PAK: {foundPath}");
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					Log($"Error scanning dependencies for {currentRelPath}: {ex.Message}");
				}
			}

			OpenInWorkbenchInternal(internalPath, sandboxDir);
		}

		public static bool OpenInWorkbench(
			string pakEntryPath,
			string extractedRoot)
		{
			return OpenInWorkbenchInternal(
				pakEntryPath,
				extractedRoot);
		}

		private static bool OpenInWorkbenchInternal(
			string internalPath,
			string sandboxPath)
		{
			Log("==================================================");
			Log($"START WORKBENCH OPEN: InternalPath='{internalPath}', SandboxPath='{sandboxPath}'");

			TerminateWorkbench();

			for (int i = 0; i < 20; ++i)
			{
				if (!IsWorkbenchRunning())
					break;

				Thread.Sleep(100);
			}

			string wbExe = GetWorkbenchPathFromRegistry();

			if (string.IsNullOrEmpty(wbExe))
			{
				Log("ERROR: Workbench EXE not found in registry.");
				return false;
			}

			wbExe = ResolveWorkbenchExe(wbExe);
			Log($"Workbench EXE resolved to: '{wbExe}'");

			string? binDir = Path.GetDirectoryName(wbExe);

			if (string.IsNullOrEmpty(binDir))
			{
				Log("ERROR: Workbench bin directory is null or empty.");
				return false;
			}

			string vanillaGamePath = GetReforgerVanillaPath();
			Log($"Vanilla Reforger Path: '{(string.IsNullOrEmpty(vanillaGamePath) ? "NOT FOUND" : vanillaGamePath)}'");

			string gameData =
				!string.IsNullOrEmpty(vanillaGamePath)
					? Path.Combine(
						vanillaGamePath,
						"addons",
						"data")
					: "";

			string cleanInternalPath =
				internalPath
					.Replace('\\', '/')
					.TrimStart('/');

			string targetFilePath =
				Path.Combine(
					sandboxPath,
					cleanInternalPath.Replace(
						'/',
						Path.DirectorySeparatorChar));

			WorkbenchContext ctx = new()
			{
				InternalPath = cleanInternalPath,
				SandboxPath = sandboxPath,
				TargetFilePath = targetFilePath,
				Ext = Path.GetExtension(
					targetFilePath).ToLowerInvariant()
			};

			string? ctxTargetDir =
				Path.GetDirectoryName(ctx.TargetFilePath);

			if (!string.IsNullOrEmpty(ctxTargetDir))
				Directory.CreateDirectory(ctxTargetDir);

			if (WorkbenchFormats.TryGetValue(
				ctx.Ext,
				out var format))
			{
				ctx.ProjectName = format.ProjectName;
				ctx.WbModule = format.WbModule;
			}

			Log($"Target Extension: '{ctx.Ext}', WbModule: '{ctx.WbModule}'");

			if (ctx.Ext == ".anm" || ctx.Ext == ".asi" || ctx.Ext == ".agf" ||
				ctx.Ext == ".agr" || ctx.Ext == ".ast" || ctx.Ext == ".aw" ||
				ctx.Ext == ".ae" || ctx.Ext == ".asy" || ctx.Ext == ".txa")
			{
				if (ctx.Ext == ".aw")
				{
					Log("Opening Animation Workspace (.aw) directly...");
					ctx.LoadResource =
						"{" +
						ctx.GUID_PROJECT +
						"}" +
						ctx.InternalPath;

					CreateFallbackAnimationMetaFiles(sandboxPath, ctx.GUID_PROJECT);
				}
				else
				{
					Log($"Attempting to resolve Animation Workspace (.aw) for {ctx.Ext}...");
					AnimationWorkspaceInfo? animationWorkspace = ResolveAnimationWorkspace(sandboxPath, gameData, ctx.InternalPath);

					if (animationWorkspace != null)
					{
						Log($"SUCCESSFULLY RESOLVED WORKSPACE: '{animationWorkspace.WorkspacePath}'");

						string projectGuid = animationWorkspace.IsVanilla ? "58D0FB3206B6F859" : ctx.GUID_PROJECT;

						ctx.LoadResource =
							"{" +
							projectGuid +
							"}" +
							animationWorkspace.WorkspacePath;

						if (!animationWorkspace.IsVanilla)
						{
							CreateAnimationWorkspaceMetaFiles(
								sandboxPath,
								animationWorkspace,
								ctx.GUID_PROJECT);
						}
					}
					else
					{
						Log($"WARNING: No .aw file could be resolved/found for this {ctx.Ext}! Falling back to direct load.");
						ctx.LoadResource =
							"{" +
							ctx.GUID_PROJECT +
							"}" +
							ctx.InternalPath;

						CreateFallbackAnimationMetaFiles(sandboxPath, ctx.GUID_PROJECT);
					}
				}
			}
			else if (ctx.Ext == ".pap")
			{
				Log("Attempting to parse Procedural Animation Project (.pap)...");
				ParseProceduralAnimationProject(sandboxPath, ctx.TargetFilePath);

				ctx.LoadResource =
					"{" +
					ctx.GUID_PROJECT +
					"}" +
					ctx.InternalPath;
			}
			else if (ctx.Ext == ".acp" || ctx.Ext == ".sig" || ctx.Ext == ".snd" || ctx.Ext == ".wav" || ctx.Ext == ".afm")
			{
				if (ctx.Ext != ".wav")
				{
					Log($"Attempting to parse Audio Project ({ctx.Ext})...");
					ParseAudioProject(sandboxPath, ctx.TargetFilePath);
				}

				CreateFallbackAudioMetaFiles(sandboxPath, ctx.GUID_PROJECT);

				ctx.LoadResource =
					"{" +
					ctx.GUID_PROJECT +
					"}" +
					ctx.InternalPath;
			}
			else
			{
				ctx.LoadResource =
					"{" +
					ctx.GUID_PROJECT +
					"}" +
					ctx.InternalPath;
			}

			File.WriteAllText(
				ctx.TargetFilePath + ".meta",
				$"MetaFileClass {{\n Name \"{{{ctx.GUID_PROJECT}}}{ctx.InternalPath}\"\n}}\n");

			File.WriteAllText(
				Path.Combine(
					sandboxPath,
					"addon.gproj"),
				$"GameProject {{\n ID \"{ctx.ProjectName}\"\n GUID \"{ctx.GUID_PROJECT}\"\n TITLE \"{ctx.ProjectName}\"\n Dependencies {{\n  \"58D0FB3206B6F859\"\n }}\n}}");

			string addonGproj =
				Path.Combine(
					sandboxPath,
					"addon.gproj");

			string addonsDirArg =
				!string.IsNullOrEmpty(gameData)
					? $"{sandboxPath},{gameData}"
					: sandboxPath;

			string arguments =
				$"-gproj \"{addonGproj}\" " +
				$"-addonsDir \"{addonsDirArg}\" " +
				$"-wbModule={ctx.WbModule}";

			if (ctx.WbModule == "scriptEditor")
				arguments += " -noGameScriptsOnInit";

			arguments +=
				$" -run -load \"{ctx.LoadResource}\"";

			Log($"Final Launch Arguments: {arguments}");

			return LaunchWorkbenchWithRetry(
				wbExe,
				arguments,
				binDir,
				sandboxPath);
		}

		private static bool LaunchWorkbenchWithRetry(string wbExe, string arguments, string workDir, string sandboxPath)
		{
			const int maxRetries = 5;

			for (int i = 0; i < maxRetries; ++i)
			{
				if (!IsSteamRunning())
				{
					Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true });
					WaitForSteamReady(45);
				}

				ProcessStartInfo psi = new()
				{
					FileName = wbExe,
					Arguments = arguments,
					WorkingDirectory = workDir,
					UseShellExecute = false
				};

				try
				{
					Process? wbProcess = Process.Start(psi);

					if (wbProcess != null)
					{
						bool isRunning = false;
						for (int check = 0; check < 15; ++check)
						{
							Thread.Sleep(100);
							if (!wbProcess.HasExited)
							{
								if (check >= 10)
								{
									isRunning = true;
									break;
								}
							}
							else
							{
								break;
							}
						}

						if (isRunning)
						{
							Task.Run(() =>
							{
								try
								{
									wbProcess.WaitForExit();
									Thread.Sleep(1000);
									if (wbProcess.HasExited && wbProcess.ExitCode == 0 && Directory.Exists(sandboxPath) && sandboxPath.Contains("PakWorkbench_Sandbox"))
									{
										Directory.Delete(sandboxPath, true);
									}
								}
								catch { }
							});

							return true;
						}
					}
				}
				catch (Exception ex)
				{
					Log($"Launch ERROR: {ex.Message}");
				}

				Thread.Sleep(3000);
			}

			return false;
		}

		#endregion

		#region Animation & Audio Resource Parsing

		private sealed class AnimationWorkspaceInfo
		{
			public string WorkspacePath { get; set; } = "";
			public bool IsVanilla { get; set; } = false;
			public string? WorkspaceGuid { get; set; }

			public string? AstPath { get; set; }
			public string? AstGuid { get; set; }

			public string? AgrPath { get; set; }
			public string? AgrGuid { get; set; }

			public List<string> AsiPaths { get; } = [];
			public List<string> AsiGuids { get; } = [];

			public List<string> AgfPaths { get; } = [];
			public List<string> AgfGuids { get; } = [];

			public List<(string Guid, string Path)> ReferencedResources { get; } = [];
		}

		private static readonly ConcurrentDictionary<string, AnimationWorkspaceInfo> WorkspaceCache = new();

		private static AnimationWorkspaceInfo? ResolveAnimationWorkspace(
			string sandboxPath,
			string gameDataPath,
			string selectedAnimation)
		{
			try
			{
				string normalizedSelected = NormalizeProjectPath(selectedAnimation);
				string selectedFileName = Path.GetFileName(normalizedSelected);
				string selectedAnimationName = Path.GetFileNameWithoutExtension(normalizedSelected);

				if (WorkspaceCache.TryGetValue(selectedAnimationName, out var cachedWorkspace))
				{
					Log($"Dynamic Cache Hit for: {selectedAnimationName}");
					return cachedWorkspace;
				}

				List<(string RootPath, bool IsVanilla)> searchDirs = [];
				if (Directory.Exists(sandboxPath))
					searchDirs.Add((sandboxPath, false));
				if (!string.IsNullOrEmpty(gameDataPath) && Directory.Exists(gameDataPath))
					searchDirs.Add((gameDataPath, true));

				foreach (var (rootPath, isVanilla) in searchDirs)
				{
					string[] asiFiles = Directory.GetFiles(rootPath, "*.asi", SearchOption.AllDirectories);
					List<string> matchingAsiPaths = [];

					foreach (string asiFile in asiFiles)
					{
						if (AsiReferencesAnimation(asiFile, normalizedSelected, selectedFileName, out _))
						{
							string relativeAsi = GetRelativeUnixPath(rootPath, asiFile);
							matchingAsiPaths.Add(relativeAsi);
							Log($"Found matching ASI for '{selectedFileName}': '{relativeAsi}' in {(isVanilla ? "Vanilla" : "Sandbox")}");
						}
					}

					if (matchingAsiPaths.Count > 0)
					{
						string[] awFiles = Directory.GetFiles(rootPath, "*.aw", SearchOption.AllDirectories);
						foreach (string awFile in awFiles)
						{
							string awText = File.ReadAllText(awFile);
							foreach (string targetAsi in matchingAsiPaths)
							{
								string asiFileName = Path.GetFileName(targetAsi);
								if (awText.Contains(targetAsi, StringComparison.OrdinalIgnoreCase) ||
									awText.Contains(asiFileName, StringComparison.OrdinalIgnoreCase))
								{
									AnimationWorkspaceInfo? workspace = ParseAnimationWorkspace(rootPath, awFile);
									if (workspace != null)
									{
										workspace.IsVanilla = isVanilla;
										WorkspaceCache[selectedAnimationName] = workspace;
										Log($"SUCCESSFULLY MATCHED AW (Bottom-Up): '{workspace.WorkspacePath}'");
										return workspace;
									}
								}
							}
						}
					}
				}

				string? parentDirName = Path.GetFileName(Path.GetDirectoryName(normalizedSelected));
				foreach (var (rootPath, isVanilla) in searchDirs)
				{
					string[] awFiles = Directory.GetFiles(rootPath, "*.aw", SearchOption.AllDirectories);
					foreach (string awFile in awFiles)
					{
						string awName = Path.GetFileNameWithoutExtension(awFile);

						if (string.Equals(awName, selectedAnimationName, StringComparison.OrdinalIgnoreCase) ||
							(!string.IsNullOrEmpty(parentDirName) && string.Equals(awName, parentDirName, StringComparison.OrdinalIgnoreCase)))
						{
							AnimationWorkspaceInfo? workspace = ParseAnimationWorkspace(rootPath, awFile);
							if (workspace != null)
							{
								workspace.IsVanilla = isVanilla;
								WorkspaceCache[selectedAnimationName] = workspace;
								Log($"SUCCESSFULLY MATCHED AW (Folder/Name Proximity): '{workspace.WorkspacePath}'");
								return workspace;
							}
						}
					}
				}

				Log("WARNING: No direct AW/ASI match found. Falling back to default player workspace.");
				foreach (var (rootPath, isVanilla) in searchDirs)
				{
					string[] awFiles = Directory.GetFiles(rootPath, "*.aw", SearchOption.AllDirectories);
					string? fallbackAw = awFiles.FirstOrDefault(f => f.EndsWith("player_main.aw", StringComparison.OrdinalIgnoreCase))
										?? awFiles.FirstOrDefault(f => f.EndsWith("poses.aw", StringComparison.OrdinalIgnoreCase));

					if (fallbackAw != null)
					{
						AnimationWorkspaceInfo? workspace = ParseAnimationWorkspace(rootPath, fallbackAw);
						if (workspace != null)
						{
							workspace.IsVanilla = isVanilla;
							Log($"LAST RESORT FALLBACK USED: '{workspace.WorkspacePath}'");
							return workspace;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log($"ERROR in ResolveAnimationWorkspace: {ex.Message}");
			}

			return null;
		}

		private static AnimationWorkspaceInfo? ParseAnimationWorkspace(
			string baseDir,
			string awFile)
		{
			try
			{
				string text = File.ReadAllText(awFile);
				string relativePath = GetRelativeUnixPath(baseDir, awFile);
				relativePath = DataPrefixRegex().Replace(relativePath, "");

				AnimationWorkspaceInfo workspace = new()
				{
					WorkspacePath = relativePath
				};

				Match baseGuid = BaseSourceRegex().Match(text);
				if (baseGuid.Success)
					workspace.WorkspaceGuid = baseGuid.Groups[1].Value;

				if (TryReadResourceReference(text, AnimSetTemplateRegex(), out string? astGuid, out string? astPath))
				{
					workspace.AstGuid = astGuid;
					workspace.AstPath = astPath;
				}

				if (TryReadResourceReference(text, AnimGraphRegex(), out string? agrGuid, out string? agrPath))
				{
					workspace.AgrGuid = agrGuid;
					workspace.AgrPath = agrPath;
				}

				MatchCollection asiMatches = AwAsiRegex().Matches(text);
				foreach (Match match in asiMatches)
				{
					string? guid = match.Groups[1].Success ? match.Groups[1].Value : null;
					string path = NormalizeProjectPath(match.Groups[2].Value);

					if (!workspace.AsiPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
					{
						workspace.AsiPaths.Add(path);
						if (!string.IsNullOrEmpty(guid)) workspace.AsiGuids.Add(guid);
					}
				}

				return workspace;
			}
			catch (Exception ex)
			{
				Log($"ERROR Parsing AW '{awFile}': {ex.Message}");
				return null;
			}
		}

		private static void ParseProceduralAnimationProject(string sandboxPath, string papFile)
		{
			try
			{
				if (!File.Exists(papFile)) return;

				string text = File.ReadAllText(papFile);
				HashSet<string> processed = new(StringComparer.OrdinalIgnoreCase);

				MatchCollection sigaMatches = SigaRegex().Matches(text);
				foreach (Match match in sigaMatches)
				{
					string sigaGuid = match.Groups[1].Value;
					string sigaPath = NormalizeProjectPath(match.Groups[2].Value);
					Log($"Associated SIGA found: {sigaPath} (GUID: {sigaGuid})");
					CreateResourceMetaFile(sandboxPath, sigaPath, sigaGuid, processed);
				}

				MatchCollection modelMatches = ModelRegex().Matches(text);
				foreach (Match match in modelMatches)
				{
					string modelGuid = match.Groups[1].Value;
					string modelPath = NormalizeProjectPath(match.Groups[2].Value);
					Log($"Associated XOB found: {modelPath} (GUID: {modelGuid})");
					CreateResourceMetaFile(sandboxPath, modelPath, modelGuid, processed);
				}

				MatchCollection boneMatches = BoneRegex().Matches(text);
				foreach (Match match in boneMatches)
				{
					string boneName = match.Groups[1].Value;
					Log($"Dashboard Bone found: {boneName}");
				}
			}
			catch (Exception ex)
			{
				Log($"ERROR parsing .pap file: {ex.Message}");
			}
		}

		private static void ParseAudioProject(string sandboxPath, string audioFile)
		{
			try
			{
				if (!File.Exists(audioFile)) return;

				string text = File.ReadAllText(audioFile);
				HashSet<string> processed = new(StringComparer.OrdinalIgnoreCase);

				MatchCollection matches = AudioGuidRegex().Matches(text);
				foreach (Match match in matches)
				{
					string guid = match.Groups[1].Value;
					string path = NormalizeProjectPath(match.Groups[2].Value);
					Log($"Associated Audio Resource found: {path} (GUID: {guid})");
					CreateResourceMetaFile(sandboxPath, path, guid, processed);
				}
			}
			catch (Exception ex)
			{
				Log($"ERROR parsing audio file: {ex.Message}");
			}
		}

		private static bool WorkspaceContainsAsi(AnimationWorkspaceInfo workspace, string asiRelativePath)
		{
			string normalized = NormalizeProjectPath(asiRelativePath);
			return workspace.AsiPaths.Any(p => string.Equals(NormalizeProjectPath(p), normalized, StringComparison.OrdinalIgnoreCase));
		}

		private static bool WorkspaceContainsAnimation(string sandboxPath, AnimationWorkspaceInfo workspace, string selectedAnimation, string selectedFileName)
		{
			try
			{
				Log($"Checking {workspace.AsiPaths.Count} ASI paths referenced by AW '{workspace.WorkspacePath}'...");
				foreach (string asiPath in workspace.AsiPaths)
				{
					string? fullAsi = ResolveProjectRelativeFile(sandboxPath, asiPath);
					Log($" - Resolving ASI '{asiPath}' -> Found: '{(string.IsNullOrEmpty(fullAsi) ? "NOT FOUND" : fullAsi)}'");

					if (string.IsNullOrEmpty(fullAsi) || !File.Exists(fullAsi))
						continue;

					if (AsiReferencesAnimation(fullAsi, selectedAnimation, selectedFileName, out _))
					{
						Log($"   -> ASI '{asiPath}' MATCHES animation!");
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				Log($"ERROR in WorkspaceContainsAnimation: {ex.Message}");
			}

			return false;
		}

		private static bool AsiReferencesAnimation(string asiFile, string selectedAnimation, string selectedFileName, out string? resourceGuid)
		{
			resourceGuid = null;

			try
			{
				string text = File.ReadAllText(asiFile);
				MatchCollection resources = ResourceAnmRegex().Matches(text);

				foreach (Match match in resources)
				{
					string guid = match.Groups[1].Success ? match.Groups[1].Value : "";
					string path = NormalizeProjectPath(match.Groups[2].Value);

					if (string.Equals(path, NormalizeProjectPath(selectedAnimation), StringComparison.OrdinalIgnoreCase) ||
						string.Equals(Path.GetFileName(path), selectedFileName, StringComparison.OrdinalIgnoreCase))
					{
						resourceGuid = guid;
						return true;
					}
				}
			}
			catch { }

			return false;
		}

		private static bool TryReadResourceReference(string text, Regex regex, out string? guid, out string? path)
		{
			guid = null;
			path = null;

			try
			{
				Match match = regex.Match(text);
				if (!match.Success) return false;

				if (match.Groups.Count > 1 && match.Groups[1].Success)
					guid = match.Groups[1].Value;

				if (match.Groups.Count > 2)
					path = NormalizeProjectPath(match.Groups[2].Value);

				return !string.IsNullOrEmpty(path);
			}
			catch
			{
				return false;
			}
		}

		private static bool AstContainsAnimation(string astFile, string animationName)
		{
			try
			{
				string astText = File.ReadAllText(astFile);
				return Regex.IsMatch(astText, @"(?m)^\s*""" + Regex.Escape(animationName) + @"""\s*$", RegexOptions.IgnoreCase);
			}
			catch
			{
				return false;
			}
		}

		#endregion

		#region Meta File Generation

		private static void CreateAnimationWorkspaceMetaFiles(string sandboxPath, AnimationWorkspaceInfo workspace, string projectGuid)
		{
			try
			{
				HashSet<string> processed = new(StringComparer.OrdinalIgnoreCase);

				foreach (var resource in workspace.ReferencedResources)
				{
					if (!string.IsNullOrEmpty(resource.Guid) && !string.IsNullOrEmpty(resource.Path))
						CreateResourceMetaFile(sandboxPath, resource.Path, resource.Guid, processed);
				}

				if (!string.IsNullOrEmpty(workspace.AgrPath))
				{
					string? agrFile = ResolveProjectRelativeFile(sandboxPath, workspace.AgrPath);
					if (!string.IsNullOrEmpty(agrFile) && File.Exists(agrFile))
						CreateAnimationFileReferenceMetaFiles(sandboxPath, agrFile, processed);
				}

				foreach (string asiPath in workspace.AsiPaths)
				{
					string? asiFile = ResolveProjectRelativeFile(sandboxPath, asiPath);
					if (!string.IsNullOrEmpty(asiFile) && File.Exists(asiFile))
						CreateAnimationFileReferenceMetaFiles(sandboxPath, asiFile, processed);
				}

				if (!string.IsNullOrEmpty(workspace.AstPath))
				{
					string? astFile = ResolveProjectRelativeFile(sandboxPath, workspace.AstPath);
					if (!string.IsNullOrEmpty(astFile) && File.Exists(astFile))
						CreateAnimationFileReferenceMetaFiles(sandboxPath, astFile, processed);
				}

				CreateFallbackAnimationMetaFiles(sandboxPath, projectGuid);
			}
			catch { }
		}

		private static void CreateAnimationFileReferenceMetaFiles(string sandboxPath, string sourceFile, HashSet<string> processed)
		{
			try
			{
				string text = File.ReadAllText(sourceFile);
				MatchCollection resources = AnimationRefRegex().Matches(text);

				foreach (Match match in resources)
				{
					string guid = match.Groups[1].Value;
					string path = NormalizeProjectPath(match.Groups[2].Value);
					CreateResourceMetaFile(sandboxPath, path, guid, processed);
				}
			}
			catch { }
		}

		private static void CreateResourceMetaFile(string sandboxPath, string resourcePath, string resourceGuid, HashSet<string> processed)
		{
			try
			{
				if (string.IsNullOrEmpty(resourceGuid) || string.IsNullOrEmpty(resourcePath)) return;

				string normalized = NormalizeProjectPath(resourcePath);
				string? file = ResolveProjectRelativeFile(sandboxPath, normalized);

				if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;

				string key = normalized + "|" + resourceGuid;
				if (!processed.Add(key)) return;

				string metaFile = file + ".meta";
				if (File.Exists(metaFile)) return;

				string content = $"MetaFileClass {{\n Name \"{{{resourceGuid}}}{normalized}\"\n}}\n";
				File.WriteAllText(metaFile, content);
			}
			catch { }
		}

		private static void CreateFallbackAnimationMetaFiles(string sandboxPath, string projectGuid)
		{
			try
			{
				string[] animationFiles = [.. Directory.GetFiles(sandboxPath, "*.*", SearchOption.AllDirectories)
					.Where(f =>
					{
						string ext = Path.GetExtension(f).ToLowerInvariant();
						return ext == ".aw" || ext == ".agr" || ext == ".agf" || ext == ".ast" || ext == ".asi" || ext == ".anm";
					})];

				foreach (string file in animationFiles)
				{
					string metaFile = file + ".meta";
					if (File.Exists(metaFile)) continue;

					string relative = GetRelativeUnixPath(sandboxPath, file);
					string content = $"MetaFileClass {{\n Name \"{{{projectGuid}}}{relative}\"\n}}\n";
					File.WriteAllText(metaFile, content);
				}
			}
			catch { }
		}

		private static void CreateFallbackAudioMetaFiles(string sandboxPath, string projectGuid)
		{
			try
			{
				string[] audioFiles = [.. Directory.GetFiles(sandboxPath, "*.*", SearchOption.AllDirectories)
					.Where(f =>
					{
						string ext = Path.GetExtension(f).ToLowerInvariant();
						return ext == ".acp" || ext == ".sig" || ext == ".snd" || ext == ".wav" || ext == ".afm";
					})];

				foreach (string file in audioFiles)
				{
					string metaFile = file + ".meta";
					if (File.Exists(metaFile)) continue;

					string relative = GetRelativeUnixPath(sandboxPath, file);
					string content = $"MetaFileClass {{\n Name \"{{{projectGuid}}}{relative}\"\n}}\n";
					File.WriteAllText(metaFile, content);
				}
			}
			catch { }
		}

		#endregion

		#region Path Utilities & File Resolution

		private static string? ResolveProjectRelativeFile(string resourcePath)
		{
			return ResolveProjectRelativeFile(string.Empty, string.Empty, resourcePath);
		}

		private static string? ResolveProjectRelativeFile(string sandboxPath, string resourcePath)
		{
			return ResolveProjectRelativeFile(sandboxPath, string.Empty, resourcePath);
		}

		private static string? ResolveProjectRelativeFile(string sandboxPath, string gameDataPath, string resourcePath)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(resourcePath)) return null;

				string clean = NormalizeProjectPath(resourcePath);

				string candidateSandbox = Path.Combine(sandboxPath, clean.Replace('/', Path.DirectorySeparatorChar));
				if (File.Exists(candidateSandbox)) return candidateSandbox;

				if (!string.IsNullOrEmpty(gameDataPath))
				{
					string candidateVanilla = Path.Combine(gameDataPath, clean.Replace('/', Path.DirectorySeparatorChar));
					if (File.Exists(candidateVanilla)) return candidateVanilla;
				}

				string fileName = Path.GetFileName(clean);
				if (!string.IsNullOrEmpty(fileName))
				{
					List<string> searchFolders = [sandboxPath];
					if (!string.IsNullOrEmpty(gameDataPath) && Directory.Exists(gameDataPath))
						searchFolders.Add(gameDataPath);

					foreach (string folder in searchFolders)
					{
						string[] matches = Directory.GetFiles(folder, fileName, SearchOption.AllDirectories);
						foreach (string match in matches)
						{
							string relative = GetRelativeUnixPath(folder, match);
							if (relative.EndsWith(clean, StringComparison.OrdinalIgnoreCase))
								return match;
						}
					}
				}
			}
			catch { }

			return null;
		}

		private static string NormalizeProjectPath(string path)
		{
			return path.Replace('\\', '/').Trim().TrimStart('/').Replace("//", "/");
		}

		private static string GetRelativeUnixPath(string root, string file)
		{
			string relative = Path.GetRelativePath(root, file);
			return relative.Replace('\\', '/').TrimStart('/');
		}

		#endregion

		#region Generated Regex Expressions

		[GeneratedRegex(@"^data\d*/", RegexOptions.IgnoreCase)]
		private static partial Regex DataPrefixRegex();

		[GeneratedRegex(@"BaseSource\s+""?\{([0-9A-Fa-f]+)\}""?", RegexOptions.IgnoreCase)]
		private static partial Regex BaseSourceRegex();

		[GeneratedRegex(@"AnimSetTemplate\s+""(?:\{([0-9A-Fa-f]+)\})?([^""]+\.ast)""", RegexOptions.IgnoreCase)]
		private static partial Regex AnimSetTemplateRegex();

		[GeneratedRegex(@"AnimGraph\s+""(?:\{([0-9A-Fa-f]+)\})?([^""]+\.agr)""", RegexOptions.IgnoreCase)]
		private static partial Regex AnimGraphRegex();

		[GeneratedRegex(@"AnimSetInstances\s*\{(?<body>.*?)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
		private static partial Regex AsiBlockRegex();

		[GeneratedRegex(@"""\{?([0-9A-Fa-f]+)?\}?([^""\r\n]+\.asi)""", RegexOptions.IgnoreCase)]
		private static partial Regex AsiMatchesRegex();

		[GeneratedRegex(@"res\s+""?\{([0-9A-Fa-f]+)\}([^""]+\.siga)""?", RegexOptions.IgnoreCase)]
		private static partial Regex SigaRegex();

		[GeneratedRegex(@"model\s+""?\{([0-9A-Fa-f]+)\}([^""]+\.xob)""?", RegexOptions.IgnoreCase)]
		private static partial Regex ModelRegex();

		[GeneratedRegex(@"bone\s+""([^""]+)""", RegexOptions.IgnoreCase)]
		private static partial Regex BoneRegex();

		[GeneratedRegex(@"\{([0-9A-Fa-f]+)\}([^""\r\n>]+)", RegexOptions.IgnoreCase)]
		private static partial Regex AudioGuidRegex();

		[GeneratedRegex(@"Resource\s+""(?:\{([0-9A-Fa-f]+)\})?([^""]+\.anm)""", RegexOptions.IgnoreCase)]
		private static partial Regex ResourceAnmRegex();

		[GeneratedRegex(@"""\{([0-9A-Fa-f]+)\}([^""\r\n]+\.(?:anm|asi|ast|agr|agf|aw|xob))""", RegexOptions.IgnoreCase)]
		private static partial Regex AnimationRefRegex();

		[GeneratedRegex(@"([A-Za-z0-9_\-\./\\]+\.(?:emat|edds|xob|et))", RegexOptions.IgnoreCase)]
		private static partial Regex SandboxDependencyRegex();

		[GeneratedRegex(@"""(?:\{([0-9A-Fa-f]+)\})?([^""\r\n]+\.asi)""", RegexOptions.IgnoreCase)]
		private static partial Regex AwAsiRegex();

		#endregion
	}
}