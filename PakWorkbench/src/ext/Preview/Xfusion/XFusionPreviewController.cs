#define USE_XFUSION
#if USE_XFUSION

using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DiffPlex.DiffBuilder.Model;
using DiffPlex.DiffBuilder;
using DiffPlex;
using PakWorkbench.src;

namespace PakWorkbench.src.ext.Preview.Xfusion
{
	#region XFusionHelper
	// ==========================================
	// XFUSION EDITOR FUNCTIONS
	// ==========================================
	public static class XFusionHelper
	{
		#region Public Methods
		public static void ConfigureEnfusionLexer(XFusion previewBox)
		{
			previewBox.Parser.LoadKeywordCaches();
			previewBox.Clear();
		}

		public static void ResetEditorToBasic(XFusion box)
		{
			box.Clear();
		}

		public static void ComputeDiffToEditor(XFusion scLeft, XFusion scRight, string oldText, string newText)
		{
			scLeft.Clear();
			scRight.Clear();

			using var scopeLeft = scLeft.CreateForceHighlightScope();
			using var scopeRight = scRight.CreateForceHighlightScope();

			var diffBuilder = new SideBySideDiffBuilder(new Differ());
			var result = diffBuilder.BuildDiffModel(oldText, newText);

			ProcessPane(scLeft, result.OldText, true);
			ProcessPane(scRight, result.NewText, false);
		}
		#endregion

		#region Private Processing Helpers
		private static void ProcessPane(XFusion xf, DiffPaneModel textSide, bool isOld)
		{
			Color bgAddedLine = Color.FromArgb(40, UITheme.ToggleActive);
			Color bgAddedWord = Color.FromArgb(100, UITheme.ToggleActive);
			Color bgDeletedLine = Color.FromArgb(50, UITheme.LogError);
			Color bgDeletedWord = Color.FromArgb(120, UITheme.LogError);
			Color bgEmpty = UITheme.BgMain;

			int lineIndex = 0;

			foreach (var line in textSide.Lines)
			{
				bool isChanged = (line.Type == ChangeType.Inserted || line.Type == ChangeType.Deleted || line.Type == ChangeType.Modified);
				Color? bgColor = null;

				if (line.Type == ChangeType.Inserted) bgColor = bgAddedLine;
				else if (line.Type == ChangeType.Deleted) bgColor = bgDeletedLine;
				else if (line.Type == ChangeType.Modified) bgColor = isOld ? bgDeletedLine : bgAddedLine;
				else if (line.Type == ChangeType.Imaginary) bgColor = bgEmpty;

				xf.AppendLine(line.Text ?? string.Empty, bgColor);

				if (isChanged && line.SubPieces != null && line.SubPieces.Count > 0)
				{
					int currentPos = 0;
					foreach (var piece in line.SubPieces)
					{
						if (piece.Type != ChangeType.Unchanged && piece.Type != ChangeType.Imaginary)
						{
							Color wordColor;
							if (piece.Type == ChangeType.Inserted) wordColor = bgAddedWord;
							else if (piece.Type == ChangeType.Deleted) wordColor = bgDeletedWord;
							else wordColor = isOld ? bgDeletedWord : bgAddedWord;

							if (!string.IsNullOrEmpty(piece.Text))
							{
								xf.AddHighlight(lineIndex, currentPos, piece.Text.Length, wordColor);
							}
						}
						currentPos += piece.Text?.Length ?? 0;
					}
				}
				lineIndex++;
			}
		}
		#endregion
	}
	#endregion

	#region EditorTextWriter
	public class EditorTextWriter(XFusion editor) : BufferedTextWriter
	{
		private static readonly string[] NewLineSeparators = ["\r\n", "\r", "\n"];

		protected override void FlushBuffer()
		{
			if (editor == null || editor.IsDisposed || !editor.IsHandleCreated) return;

			StringBuilder sb = new();

			while (_buffer.TryDequeue(out string? line))
			{
				sb.Append(line);
			}

			if (sb.Length > 0)
			{
				string[] lines = sb.ToString().Split(NewLineSeparators, StringSplitOptions.None);

				if (lines.Length > 0)
				{
					editor.AppendLines(lines);

					if (editor.LineCount > 0)
					{
						editor.ScrollToLine(editor.LineCount - 1);
					}
				}
			}
		}
	}
	#endregion

	#region XFusionPreviewController
	public partial class XFusionPreviewController(Font font) : BasePreviewController
	{
		#region Regex Definitions
		[GeneratedRegex(@"(?:[a-zA-Z]:\\[^:\n\r\(\)""]+|[a-zA-Z0-9_\-\\/]+\.[a-zA-Z0-9]+)", RegexOptions.IgnoreCase)]
		private static partial Regex MyPathRegex();

		[GeneratedRegex(@"@""[^""]+,\d+""")]
		private static partial Regex MyEnfMatchRegex();

		private static readonly Regex PathRegex = MyPathRegex();
		#endregion

		#region Private Fields
		private readonly XFusion _editor = new()
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Font = font
		};

		private bool _isLogMode = false;
		private Color _origBg;
		private Color _origFg;
		private bool _colorsSaved = false;
		#endregion

		#region Base Properties
		public override bool EnableSyntaxHighlighting
		{
			get => _editor.EnableSyntaxHighlighting;
			set => _editor.EnableSyntaxHighlighting = value;
		}

		public override Control UIControl => _editor;

		public override bool WordWrap
		{
			get => _editor?.WordWrap ?? false;
			set
			{
				if (_editor != null)
				{
					_editor.WordWrap = value;
				}
			}
		}
		#endregion

		#region Lifecycle and Basic Controls
		public override void Initialize() => _editor.Parser.LoadKeywordCaches();

		public override void Clear()
		{
			_editor.Clear();
			_editor.IsHexMode = false;
		}

		public override void Refresh() => _editor.Refresh();
		public override void ResetScroll() => _editor.ScrollToLine(0);
		public override void ScrollToBottom() => _editor.ScrollToBottom();

		public override TextWriter CreateTextWriter() => new EditorTextWriter(_editor);
		#endregion

		#region Text Appending & Log Parsing
		public override void AppendLine(string text)
		{
			AppendBatchText(text + "\r\n");
		}

		public override void AppendBatchText(string text)
		{
			if (_editor == null || _editor.IsDisposed)
				return;

			int startLine = _editor.LineCount;
			string[] lines = SplitLines(text);

			_editor.AppendLines(lines);

			if (_isLogMode)
			{
				Color purpleColor = ColorTranslator.FromHtml("#C178DD");
				Color orangeColor = ColorTranslator.FromHtml("#FF9900");
				Color errorColor = UITheme.LogError;

				for (int i = 0; i < lines.Length; i++)
				{
					string line = lines[i];

					if (string.IsNullOrEmpty(line))
						continue;

					int currentLine = startLine + i;

					Match enfMatch = MyEnfMatchRegex().Match(line);

					if (enfMatch.Success)
					{
						_editor.AddHighlight(
							currentLine,
							enfMatch.Index,
							enfMatch.Length,
							purpleColor,
							true);
					}

					int scriptIdx = line.IndexOf("SCRIPT", StringComparison.Ordinal);
					int errorIdx = line.IndexOf("(E):", StringComparison.Ordinal);
					int warningIdx = line.IndexOf("(W):", StringComparison.Ordinal);

					bool isError = errorIdx >= 0;
					bool isWarning = !isError && warningIdx >= 0;

					int severityIdx = isError ? errorIdx : warningIdx;
					Color severityColor = isError ? errorColor : orangeColor;

					if (scriptIdx >= 0)
					{
						int scriptLength = 6;

						if (severityIdx >= 0 && severityIdx >= scriptIdx)
						{
							scriptLength = (severityIdx + 4) - scriptIdx;
						}

						_editor.AddHighlight(
							currentLine,
							scriptIdx,
							scriptLength,
							severityColor);
					}

					if (isError || isWarning)
					{
						_editor.AddHighlight(
							currentLine,
							severityIdx,
							4,
							severityColor);

						int messageSeparator = line.LastIndexOf("\":");

						if (messageSeparator >= 0 &&
							messageSeparator + 2 < line.Length)
						{
							int messageStart = messageSeparator + 1;

							_editor.AddHighlight(
								currentLine,
								messageStart,
								line.Length - messageStart,
								severityColor);
						}
					}
					else
					{
						int compileIdx = line.IndexOf(
							"Can't compile",
							StringComparison.OrdinalIgnoreCase);

						if (compileIdx >= 0)
						{
							_editor.AddHighlight(
								currentLine,
								compileIdx,
								line.Length - compileIdx,
								orangeColor);
						}
					}

					int keywordIdx = line.IndexOf(
						"ERROR",
						StringComparison.OrdinalIgnoreCase);

					if (keywordIdx >= 0)
					{
						_editor.AddHighlight(
							currentLine,
							keywordIdx,
							5,
							errorColor);
					}
				}
			}

			if (_editor.LineCount > 0)
			{
				_editor.ScrollToLine(_editor.LineCount - 1);
			}

			_editor.Refresh();
		}

		#endregion

		#region Configurations Mode Handling
		public override void ConfigureForSearchLog(bool logEnabled)
		{
			_isLogMode = logEnabled;

			if (!_colorsSaved)
			{
				_origBg = _editor.BackColor;
				_origFg = _editor.ForeColor;
				_colorsSaved = true;
			}

			_editor.ConfigureForSearchLog(logEnabled);
			_editor.Clear();

			if (logEnabled)
			{
				_editor.BackColor = UITheme.BgDarker;
			}
			else
			{
				_editor.BackColor = _origBg;
				_editor.ForeColor = _origFg;
			}
		}

		public override void ConfigureForSyncLog()
		{
			_isLogMode = true;

			if (!_colorsSaved)
			{
				_origBg = _editor.BackColor;
				_origFg = _editor.ForeColor;
				_colorsSaved = true;
			}

			_editor.ConfigureForSearchLog(false);

			_editor.BackColor = _origBg;
			_editor.ForeColor = _origFg;
			_editor.Clear();
		}
		#endregion

		#region Preview and Search Display Methods
		public override void ShowFilePreview(Pak sourcePak, PakEntryFile entry, byte[] data, string? textPreview, int maxReasonableSize)
		{
			ConfigureForSearchLog(false);
			Clear();

			string entryIdentifier = entry?.name ?? string.Empty;
			string ext = Path.GetExtension(entryIdentifier).ToLowerInvariant();

			string[] enfusionDataExts = [
				".ct", ".layout", ".conf", ".edds", ".et", ".emat",
				".ent", ".layer", ".desc", ".gproj", ".physmat",
				".gamemat", ".vhcsurf", ".pap", ".ragdoll", ".styles",
				".ptc", ".topo", ".afm", ".acp"
			];

			if (Array.IndexOf(enfusionDataExts, ext) >= 0)
			{
				_editor.CurrentLanguage = LexerLanguage.EnfusionData;
			}
			else if (ext == ".st" || ext == ".svg")
			{
				_editor.CurrentLanguage = LexerLanguage.XML;
			}
			else
			{
				_editor.CurrentLanguage = LexerLanguage.EnfusionScript;
			}

			if (_colorsSaved)
			{
				_editor.BackColor = _origBg;
				_editor.ForeColor = _origFg;
			}

			using var scope = _editor.CreateForceHighlightScope();

			string metadata = BuildMetadataString(sourcePak, entry!, maxReasonableSize);
			StringBuilder sb = new(metadata);

			if (textPreview != null)
			{
				_editor.IsHexMode = false;
				sb.AppendLine("--- [ 📄 TEXT PREVIEW ] ---");
				sb.AppendLine();
				sb.Append(textPreview);
				RenderRawText(sb.ToString(), isTextPreview: true);
			}
			else
			{
				_editor.IsHexMode = false;
				RenderRawText(sb.ToString(), isTextPreview: false);

				_editor.IsHexMode = true;
				RenderHexView(data);
			}
			Refresh();
			ResetScroll();
		}

		public override void ShowContextWindow(DeepSearchControl.SearchResultInfo info, string rawContent, bool matchCase, bool multiLine, bool regexMode, bool wholeWord, bool useSyntaxHighlighting)
		{
			ConfigureForSearchLog(false);

			if (_colorsSaved)
			{
				_editor.BackColor = _origBg;
				_editor.ForeColor = _origFg;
			}

			using var scope = _editor.CreateForceHighlightScope();

			string[] allLines = SplitLines(rawContent);
			GetContextWindowBounds(info, allLines.Length, out int tStart, out int tEnd, out int wStartXF, out int wEndXF);

			_editor.StartLineNumber = 0;
			_editor.TargetLine = -1;
			_editor.TargetLineEnd = -1;

			StringBuilder sbXF = new();

			string header = $"// == CONTEXT WINDOW: {info.FileName} (Lines {wStartXF + 1} to {wEndXF + 1}) ==\r\n\r\n";
			sbXF.Append(header);

			for (int i = wStartXF; i <= wEndXF; i++)
			{
				bool isMatchRow = (i >= tStart && i <= tEnd);
				string numStr = (i + 1).ToString().PadRight(3);
				string prefix = isMatchRow ? $"► {numStr}│ " : $"  {numStr}│ ";
				sbXF.AppendLine(prefix + allLines[i]);
			}

			_editor.TextContent = sbXF.ToString();
			_editor.ClearHighlights();

			int headerOffset = 2;
			for (int i = wStartXF; i <= wEndXF; i++)
			{
				int currentLocalLine = (i - wStartXF) + headerOffset;
				if (currentLocalLine < 0 || currentLocalLine >= _editor.LineCount) continue;

				bool isMatchRow = (i >= tStart && i <= tEnd);
				string numStr = (i + 1).ToString().PadRight(3);

				if (isMatchRow)
				{
					_editor.AddHighlight(currentLocalLine, 0, 2 + numStr.Length, UITheme.LogError);

					if (i == tStart && !string.IsNullOrEmpty(info.SearchTerm))
					{
						int prefixLen = 2 + numStr.Length + 2;
						int col1 = allLines[tStart].IndexOf(info.SearchTerm, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
						if (col1 >= 0)
						{
							_editor.AddHighlight(currentLocalLine, col1 + prefixLen, info.SearchTerm.Length, UITheme.LogWarning);
							_editor.SetSelection(currentLocalLine, col1 + prefixLen, currentLocalLine, col1 + prefixLen + info.SearchTerm.Length);
						}
					}

					if (multiLine && !string.IsNullOrEmpty(info.SearchTermVar) && i == tEnd)
					{
						int prefixLen = 2 + numStr.Length + 2;
						int col2 = allLines[tEnd].IndexOf(info.SearchTermVar, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
						if (col2 >= 0)
						{
							_editor.AddHighlight(currentLocalLine, col2 + prefixLen, info.SearchTermVar.Length, UITheme.SyntaxType);
						}
					}
				}
			}

			int localTargetStart = (tStart - wStartXF) + headerOffset;
			_editor.ScrollToLine(localTargetStart);
		}
		#endregion

		#region Render Methods & Disposal
		protected override void RenderRawText(string text, bool isTextPreview) => _editor.TextContent = text;
		protected override void RenderHexView(byte[] data) => _editor.LoadHexData(data);

		public override void Dispose()
		{
			_editor.Dispose();
			GC.SuppressFinalize(this);
		}
		#endregion
	}
	#endregion
}
#endif