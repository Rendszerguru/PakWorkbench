using System.IO;
using System.Text.Json;

namespace PakWorkbench.src.ext
{
	public class AppConfig
	{
		public bool LiteMode { get; set; } = false;
		public bool EnableCustomIcons { get; set; } = true;
		public bool EnableSyntaxHighlighting { get; set; } = true;
		public bool EnableLog { get; set; } = true;
		public bool EnableSearch { get; set; } = true;
		public bool EnableMoDiscover { get; set; } = true;
		public bool EnableWorkbenchSync { get; set; } = true;

		public bool AutoLoadLast { get; set; } = false;
		public bool AutoCompareEnabled { get; set; } = false;
	}

	public static class AppSettings
	{
		public static AppConfig Current { get; set; } = new AppConfig();
		private static readonly string ConfigPath = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "PakWorkbenchConfig.json");
		private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

		static AppSettings()
		{
			Load();
		}

		public static void Load()
		{
			if (File.Exists(ConfigPath))
			{
				try
				{
					Current = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
				}
				catch
				{
					Current = new AppConfig();
				}
			}
		}

		public static void Save()
		{
			File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current, SerializerOptions));
		}
	}
}