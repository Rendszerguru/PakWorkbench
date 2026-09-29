namespace PakWorkbench;

using PakWorkbench.src;
using PakWorkbench.src.ext;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;

internal static class Program
{
	#region Main Entry Point

	[STAThread]
	static void Main(string[] args)
	{
		Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);

		AppSettings.Load();

		string version = AppInfo.Version;

		if (args.Length > 0)
		{
			if (!NativeMethods.AttachConsole(-1))
			{
				NativeMethods.AllocConsole();
			}

			Console.WriteLine($"PakWorkbench CLI: {version} (PakInspector Mode)");

			string command = args[0].ToLower();

			#region 1. Help Command
			// =========================================================================
			// 1. HELP COMMAND
			// =========================================================================
			if (command == "--help" || command == "-h" || command == "help")
			{
				Console.WriteLine("\nUsage:");
				Console.WriteLine("  [files/directories]                     -> Batch extraction (Original behavior)");
				Console.WriteLine("  inspect <file.pak>                      -> Basic archive information and statistics");
				Console.WriteLine("  inspect <file.pak> --tree               -> List all archive files in a tree-like view");
				Console.WriteLine("  inspect <file.pak> --chunks             -> Display raw IFF chunk data (FORM, DATA, etc.)");
				Console.WriteLine("  extract <file.pak>                      -> Extract the entire PAK archive");
				Console.WriteLine("  extract <file.pak> --output <dir>       -> Extract to a custom output directory");
				Console.WriteLine("  extract <file.pak> --file <internal_path> -> Extract only ONE specific file from the archive");
				Console.WriteLine("\nDone! Exiting...");
				return;
			}
			#endregion

			#region 2. Inspect Commands
			// =========================================================================
			// 2. INSPECT COMMANDS (Diagnostics & Layout)
			// =========================================================================
			if (command == "inspect" && args.Length > 1)
			{
				string pakPath = args[1];
				if (!File.Exists(pakPath))
				{
					Console.WriteLine($"ERROR: File not found: {pakPath}");
					return;
				}

				try
				{
					using Pak pak = new(pakPath);
					bool isTree = args.Contains("--tree");
					bool isChunks = args.Contains("--chunks");

					if (isChunks)
					{
						Console.WriteLine($"\n[Chunk Map] - {Path.GetFileName(pakPath)}");
						Console.WriteLine(new string('-', 50));
						Console.WriteLine($"  FORM Header Size: {pak.formSize} bytes");
						Console.WriteLine($"  DATA Section Size: {pak.dataSize} bytes");
						Console.WriteLine($"  ENTRIES Index Size: {pak.entriesSize} bytes");
						Console.WriteLine($"  Total File Entries: {pak.entries.Count} items");
						Console.WriteLine(new string('-', 50));
					}
					else if (isTree)
					{
						Console.WriteLine($"\n[File Tree] - {Path.GetFileName(pakPath)} ({pak.entries.Count} files):");
						Console.WriteLine(new string('-', 60));
						var sortedEntries = pak.entries.OrderBy(e => e.name).ToList();
						foreach (var entry in sortedEntries)
						{
							Console.WriteLine($"  {entry.name} ({entry.size} bytes)");
						}
						Console.WriteLine(new string('-', 60));
					}
					else
					{
						Console.WriteLine($"\n[Archive Info] - {Path.GetFileName(pakPath)}");
						Console.WriteLine($"  Total Files: {pak.entries.Count}");
						Console.WriteLine($"  Compressed Size: {new FileInfo(pakPath).Length / 1024 / 1024} MB");
						Console.WriteLine("\nTip: Use 'inspect <file.pak> --tree' or '--chunks' for detailed breakdowns.");
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"ERROR inspecting archive: {ex.Message}");
				}
				return;
			}
			#endregion

			#region 3. Extract Commands
			// =========================================================================
			// 3. EXTRACT COMMANDS (Unpacking & Single File Extraction)
			// =========================================================================
			if (command == "extract" && args.Length > 1)
			{
				string pakPath = args[1];
				if (!File.Exists(pakPath))
				{
					Console.WriteLine($"ERROR: File not found: {pakPath}");
					return;
				}

				try
				{
					using Pak pak = new(pakPath);

					int fileIdx = Array.IndexOf(args, "--file");
					if (fileIdx != -1 && args.Length > fileIdx + 1)
					{
						string targetInternalPath = args[fileIdx + 1];
						var entry = pak.entries.FirstOrDefault(e => string.Equals(e.name, targetInternalPath, StringComparison.OrdinalIgnoreCase));

						if (entry != null)
						{
							string outFileName = Path.GetFileName(targetInternalPath);
							string outPath = Path.Combine(Directory.GetCurrentDirectory(), outFileName);

							Console.WriteLine($"[CLI] Extracting single file: {entry.name} -> {outPath}");
							byte[] fileData = pak.GetFileBytes(entry);
							File.WriteAllBytes(outPath, fileData);
							Console.WriteLine("Extraction successful!");
						}
						else
						{
							Console.WriteLine($"ERROR: File '{targetInternalPath}' not found inside the PAK archive.");
						}
						return;
					}

					string outputDir = Path.ChangeExtension(pakPath, null);
					int outputIdx = Array.IndexOf(args, "--output");
					if (outputIdx != -1 && args.Length > outputIdx + 1)
					{
						outputDir = args[outputIdx + 1];
					}

					Console.WriteLine($"Unpacking: {Path.GetFileName(pakPath)} -> {outputDir}...");
					pak.ExtractDataBlock(outputDir);
					Console.WriteLine("Success!");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"ERROR extracting archive: {ex.Message}");
				}
				return;
			}
			#endregion

			#region 4. Original Batch Processing
			// =========================================================================
			// 4. ORIGINAL BATCH PROCESSING (Drag and Drop / Folder mapping)
			// =========================================================================
			HashSet<string> filesToProcess = [];
			foreach (var arg in args)
			{
				if (File.Exists(arg) && string.Equals(Path.GetExtension(arg), ".pak", StringComparison.OrdinalIgnoreCase))
				{
					filesToProcess.Add(arg);
				}
				else if (Directory.Exists(arg))
				{
					foreach (string file in Directory.GetFiles(arg, "*.pak", SearchOption.AllDirectories))
					{
						filesToProcess.Add(file);
					}
				}
			}

			if (filesToProcess.Count > 0)
			{
				Console.WriteLine($"\n[CLI Mode] Unpacking {filesToProcess.Count} archive(s)...");
				Parallel.ForEach(filesToProcess, pakPath =>
				{
					ProcessFile(pakPath);
				});
				Console.WriteLine("\nDone! Exiting...");
				return;
			}
			#endregion
		}

		#region 5. Original GUI Initialization
		// =========================================================================
		// 5. ORIGINAL GUI INITIALIZATION (No arguments provided)
		// =========================================================================
		List<string> localFiles = [.. Directory.GetFiles(Directory.GetCurrentDirectory(), "*.pak")];

		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.Run(new ViewerForm(localFiles));
		#endregion
	}

	#endregion

	#region CLI Helper Methods

	static void ProcessFile(string path)
	{
		try
		{
			if (!string.Equals(Path.GetExtension(path), ".pak", StringComparison.OrdinalIgnoreCase)) return;

			Console.WriteLine($"Unpacking: {Path.GetFileName(path)}...");
			using Pak pak = new(path);
			string outputDir = Path.ChangeExtension(path, null);
			pak.ExtractDataBlock(outputDir);
			Console.WriteLine($"Success: {Path.GetFileName(path)} -> {outputDir}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"ERROR processing {Path.GetFileName(path)}: {ex.Message}");
		}
	}

	#endregion
}

#region Native Methods Interop

internal static partial class NativeMethods
{
	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static partial bool AttachConsole(int dwProcessId);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static partial bool AllocConsole();
}

#endregion

#region Application Information

public static class AppInfo
{
	public static string Version { get; } =
		Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

	public static string Title { get; } = $"PakWorkbench {Version}";
}

#endregion