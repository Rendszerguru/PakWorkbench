using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using PakWorkbench.src;

namespace PakWorkbench.src.ext.Preview
{
	#region Enums
	public enum PreviewEngine
	{
		XFusion
	}
	#endregion

	#region Buffered Text Writer
	public abstract class BufferedTextWriter : TextWriter
	{
		protected readonly ConcurrentQueue<string> _buffer = new();
		private readonly System.Windows.Forms.Timer _timer;

		public override Encoding Encoding => Encoding.UTF8;

		protected BufferedTextWriter()
		{
			_timer = new System.Windows.Forms.Timer { Interval = 30 };
			_timer.Tick += Timer_Tick;
			_timer.Start();
		}

		public override void Write(char value)
		{
			_buffer.Enqueue(value.ToString());
		}

		public override void Write(string? value)
		{
			if (!string.IsNullOrEmpty(value))
			{
				_buffer.Enqueue(value);
			}
		}

		private void Timer_Tick(object? sender, EventArgs e)
		{
			if (_buffer.IsEmpty) return;
			FlushBuffer();
		}

		protected abstract void FlushBuffer();

		protected override void Dispose(bool disposing)
		{
			if (disposing) _timer.Dispose();
			base.Dispose(disposing);
		}
	}
	#endregion

	#region IPreviewController Interface
	public interface IPreviewController : IDisposable
	{
		Control UIControl { get; }
		bool WordWrap { get; set; }
		bool EnableSyntaxHighlighting { get; set; }

		void Initialize();
		void Clear();
		void AppendLine(string text);
		void AppendBatchText(string text);
		void Refresh();
		void ResetScroll();
		void ScrollToBottom();
		TextWriter CreateTextWriter();

		void ShowEmptyState();
		void ShowLogHeader(string filter, bool isSearchActive, bool logEnabled);
		void ShowLogFooter(int totalFilesCount, int totalMatchedCount, bool isSearchActive, bool logEnabled);
		void ShowExtractionLogHeader(string actionName);
		void ShowFilePreview(Pak sourcePak, PakEntryFile entry, byte[] data, string? textPreview, int maxReasonableSize);

		void ConfigureForSearchLog(bool logEnabled);
		void ConfigureForSyncLog();
		void ShowContextWindow(DeepSearchControl.SearchResultInfo info, string rawContent, bool matchCase, bool multiLine, bool regexMode, bool wholeWord, bool useSyntaxHighlighting);
	}
	#endregion

	#region BasePreviewController Abstract Class
	public abstract class BasePreviewController : IPreviewController
	{
		#region Constants & Fields
		private static readonly string[] NewLineSeparators = ["\r\n", "\r", "\n"];

		protected const string MetaHeaderTop    = "╔══════════════════════════════════════════════════════════════════════════╗";
		protected const string MetaHeaderMiddle = "║  SELECTED FILE METADATA                                                  ║";
		protected const string MetaHeaderBottom = "╚══════════════════════════════════════════════════════════════════════════╝";
		#endregion

		#region Abstract Properties & Methods
		public abstract Control UIControl { get; }
		public abstract bool WordWrap { get; set; }
		public abstract bool EnableSyntaxHighlighting { get; set; }
		public abstract void Initialize();
		public abstract void Clear();
		public abstract void AppendLine(string text);
		public abstract void AppendBatchText(string text);
		public abstract void Refresh();
		public abstract void ResetScroll();
		public abstract void ScrollToBottom();
		public abstract TextWriter CreateTextWriter();
		public abstract void Dispose();

		protected abstract void RenderRawText(string text, bool isTextPreview);
		protected abstract void RenderHexView(byte[] data);

		public abstract void ConfigureForSearchLog(bool logEnabled);
		public abstract void ConfigureForSyncLog();
		public abstract void ShowContextWindow(DeepSearchControl.SearchResultInfo info, string rawContent, bool matchCase, bool multiLine, bool regexMode, bool wholeWord, bool useSyntaxHighlighting);
		#endregion

		#region Log & UI State Presentation
		public void ShowEmptyState()
		{
			Clear();
			AppendLine("\n  > Select a file from the list to view its contents.");
		}

		public void ShowLogHeader(string filter, bool isSearchActive, bool logEnabled)
		{
			Clear();
			if (logEnabled)
			{
				AppendLine("======================================================================");
				AppendLine(" 🚀 LOADING ENGINE :: FILE INDEXING ACTIVE");
				if (isSearchActive) AppendLine($" 📍 APPLIED FILTER: '{filter}'");
				AppendLine("======================================================================");
				AppendLine("");
			}
			else
			{
				AppendLine("\n File hierarchy generation complete.");
			}
		}

		public void ShowLogFooter(int totalFilesCount, int totalMatchedCount, bool isSearchActive, bool logEnabled)
		{
			if (!logEnabled) return;
			AppendLine("");
			AppendLine("======================================================================");
			AppendLine("[INFO] INDEXING COMPLETED SUCCESSFULLY.");
			AppendLine($"[INFO] TOTAL FILES LOADED: {totalFilesCount}");
			if (isSearchActive) AppendLine($"[INFO] TOTAL FILTER MATCHES FOUND: {totalMatchedCount}");
			AppendLine("======================================================================");

			ResetScroll();
		}

		public void ShowExtractionLogHeader(string actionName)
		{
			Clear();
			AppendLine($"--- [ 🚀 EXTRACTION LOG: {actionName} ] ---");
			AppendLine("");
		}
		#endregion

		#region Metadata Helpers
		protected static IEnumerable<(string Key, string Value)> GetFileMetadata(Pak sourcePak, PakEntryFile entry, int maxReasonableSize)
		{
			yield return ("Source File:", Path.GetFileName(sourcePak.name));
			yield return ("File Path:", entry.name);
			yield return ("File Offset:", $"0x{entry.offset:X8} ({entry.offset:N0} bytes)");
			yield return ("Compressed Size:", $"{entry.size:N0} bytes");
			yield return ("Uncompressed Size:", $"{entry.originalSize:N0} bytes");
			yield return ("Compression Type:", entry.compression.ToString());

			if (entry.originalSize > maxReasonableSize)
			{
				yield return ("Size Limit:", $"Truncated to {maxReasonableSize / (1024 * 1024)}MB to maintain stable performance.");
			}
		}

		protected static string BuildMetadataString(Pak sourcePak, PakEntryFile entry, int maxReasonableSize)
		{
			StringBuilder sb = new();
			sb.AppendLine(MetaHeaderTop);
			sb.AppendLine(MetaHeaderMiddle);
			sb.AppendLine(MetaHeaderBottom);

			foreach (var (key, value) in GetFileMetadata(sourcePak, entry, maxReasonableSize))
			{
				sb.AppendLine($" {key.PadRight(20)}{value}");
			}

			sb.AppendLine();
			return sb.ToString();
		}

		public virtual void ShowFilePreview(Pak sourcePak, PakEntryFile entry, byte[] data, string? textPreview, int maxReasonableSize)
		{
			if (entry == null || string.IsNullOrEmpty(entry.name) || entry.name.EndsWith('/') || entry.name.EndsWith('\\'))
			{
				ShowEmptyState();
				return;
			}
		}
		#endregion

		#region Utility Helpers
		protected static string[] SplitLines(string text)
		{
			return text.Split(NewLineSeparators, StringSplitOptions.None);
		}

		protected static void GetContextWindowBounds(DeepSearchControl.SearchResultInfo info, int totalLines, out int targetStart, out int targetEnd, out int windowStart, out int windowEnd)
		{
			targetStart = info.LineNumber - 1;
			targetEnd = info.LineNumberEnd > info.LineNumber ? info.LineNumberEnd - 1 : targetStart;

			windowStart = Math.Max(0, targetStart - 5);
			windowEnd = Math.Min(totalLines - 1, targetEnd + 5);
		}
		#endregion
	}
	#endregion
}