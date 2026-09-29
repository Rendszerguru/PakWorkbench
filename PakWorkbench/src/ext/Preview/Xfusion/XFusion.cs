using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PakWorkbench.src.ext.Preview.Xfusion
{
	#region Core Data Structures & Enums

	public enum LexerLanguage : byte
	{
		EnfusionScript,
		EnfusionData,
		XML
	}

	public enum TokenType : byte
	{
		Default,
		Comment,
		Number,
		String,
		Preprocessor,
		Operator,
		Keyword,
		Type,
		Function,
		Attribute,
		Constant,
		Enum,
		Member,
		LogPrefix
	}

	public struct Token
	{
		public int Start;
		public int Length;
		public TokenType Type;
	}

	public struct HighlightDef
	{
		public int StartCol;
		public int Length;
		public Color BgColor;
		public bool IsUnderlined;
	}

	public struct LineState : IEquatable<LineState>
	{
		public bool InComment;
		public bool InsideMethod;
		public string? ContextType;
		public bool InAttribute;
		public bool ExpectClassName;
		public bool ExpectInheritance;
		public bool InsideEnum;
		public int TypedefState;
		public bool ExpectConstant;
		public int GenericLevel;

		public int BraceLevel;
		public int ParenLevel;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public readonly bool Equals(LineState other) =>
			InComment == other.InComment &&
			InsideMethod == other.InsideMethod &&
			ContextType == other.ContextType &&
			InAttribute == other.InAttribute &&
			ExpectClassName == other.ExpectClassName &&
			ExpectInheritance == other.ExpectInheritance &&
			InsideEnum == other.InsideEnum &&
			TypedefState == other.TypedefState &&
			ExpectConstant == other.ExpectConstant &&
			GenericLevel == other.GenericLevel &&
			BraceLevel == other.BraceLevel &&
			ParenLevel == other.ParenLevel;

		public override readonly bool Equals(object? obj) => obj is LineState other && Equals(other);

		public override readonly int GetHashCode()
		{
			var hash = new HashCode();
			hash.Add(InComment);
			hash.Add(InsideMethod);
			hash.Add(ContextType);
			hash.Add(InAttribute);
			hash.Add(ExpectClassName);
			hash.Add(ExpectInheritance);
			hash.Add(InsideEnum);
			hash.Add(TypedefState);
			hash.Add(ExpectConstant);
			hash.Add(GenericLevel);
			hash.Add(BraceLevel);
			hash.Add(ParenLevel);
			return hash.ToHashCode();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool operator ==(LineState left, LineState right) => left.Equals(right);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool operator !=(LineState left, LineState right) => !left.Equals(right);
	}

	public class LineDescriptor
	{
		public string LineText = string.Empty;
		public List<Token> Tokens = new(8);
		public List<HighlightDef> Highlights = [];
		public int CachedWidth = -1;
		public bool IsDirty = true;
		public bool IsWrapDirty = true;

		public Color? LineBackColor = null;
		public int? OriginalLineNumber = null;

		public LineState StateBefore;
		public LineState StateAfter;

		public bool StateInComment = false;
		public string? CurrentTypeContext = null;
		public bool InsideMethodContext = false;

		public bool IsCollapsed = false;

		public int[]? WrapOffsets = null;
		public int VisualLineCount = 1;
		public bool IsImaginary = false;
	}

	#endregion

	public partial class XFusion : Control
	{
		#region Native Imports (GDI32)

		[LibraryImport("gdi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static unsafe partial bool ExtTextOutW(IntPtr hdc, int x, int y, uint options, IntPtr lprect, char* lpString, uint c, int* lpDx);

		[LibraryImport("gdi32.dll")]
		private static partial IntPtr SelectObject(IntPtr hdc, IntPtr h);

		[LibraryImport("gdi32.dll")]
		private static partial uint SetTextColor(IntPtr hdc, int crColor);

		[LibraryImport("gdi32.dll")]
		private static partial int SetBkMode(IntPtr hdc, int iBkMode);

		[LibraryImport("gdi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool DeleteObject(IntPtr hObject);

		[LibraryImport("gdi32.dll", EntryPoint = "CreateFontIndirectW", SetLastError = true)]
		private static partial IntPtr CreateFontIndirectW(in LOGFONT lplf);

		[LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool GetTextExtentPoint32W(IntPtr hdc, string lpString, int c, out SIZE sz);

		[StructLayout(LayoutKind.Sequential)]
		private unsafe struct LOGFONT
		{
			public int lfHeight;
			public int lfWidth;
			public int lfEscapement;
			public int lfOrientation;
			public int lfWeight;
			public byte lfItalic;
			public byte lfUnderline;
			public byte lfStrikeOut;
			public byte lfCharSet;
			public byte lfOutPrecision;
			public byte lfClipPrecision;
			public byte lfQuality;
			public byte lfPitchAndFamily;

			public fixed char lfFaceName[32];

			public void SetFaceName(string name)
			{
				if (string.IsNullOrEmpty(name)) return;
				fixed (char* p = lfFaceName)
				{
					int len = Math.Min(name.Length, 31);
					for (int i = 0; i < len; i++) p[i] = name[i];
					p[len] = '\0';
				}
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct SIZE
		{
			public int cx;
			public int cy;
		}

		#endregion

		#region Fields & State Variables

#if DEBUG
		private static readonly bool SAFE_MODE = true;
#else
		private static readonly bool SAFE_MODE = false;
#endif

		private const int MAX_RENDER_CHARS = 8192;
		private static readonly char[] HexLookup = "0123456789ABCDEF".ToCharArray();
		private static readonly Color[] TokenColors = new Color[14];

		private readonly VScrollBar vScrollBar;
		private readonly HScrollBar hScrollBar;
		private int lineHeight;
		private int _cachedCharWidth;
		private IntPtr _cachedHFont = IntPtr.Zero;
		private int visibleLinesCount;

		private readonly List<LineDescriptor> _lines = [];
		private readonly Stack<LineDescriptor> _linePool = new();

		private char[] _renderBuffer = new char[MAX_RENDER_CHARS * 4];
		private int[] _dxBuffer = new int[MAX_RENDER_CHARS * 4];
		private int[] _wrapBuffer = new int[4096];

		private readonly int[] _nativeColors = new int[14];

		private int _updateLock = 0;
		private byte[]? _hexData = null;

		private int _selStartLine = -1, _selStartCol = -1;
		private int _selEndLine = -1, _selEndCol = -1;
		private bool _isSelecting = false;

		private int _clickCount = 0;
		private DateTime _lastClickTime = DateTime.MinValue;
		private Point _lastClickPos = Point.Empty;

		private readonly ContextMenuStrip _contextMenu;
		private readonly ToolStripMenuItem _copyMenuItem;

		private readonly SolidBrush _marginBrush;
		private readonly Pen _boxPen;
		private readonly SolidBrush _textBrush;
		private readonly SolidBrush _selBrush;
		private readonly StringFormat _sf;
		private readonly StringFormat _marginSf;
		private readonly Dictionary<Color, SolidBrush> _brushCache = [];

		private readonly Color _targetLineBg = Color.FromArgb(50, 54, 60);
		private readonly Color _arrowColor = Color.FromArgb(224, 108, 117);

		private int _scrollLine = 0;
		private int _scrollCharX = 0;

		private bool _enableSyntaxHighlighting;
		private int _wrapMaxCols = 100;
		private bool _wordWrap = true;
		private bool _suppressScrollEvent = false;

		#endregion

		#region Properties & Events

		public LexerLanguage CurrentLanguage { get; set; } = LexerLanguage.EnfusionScript;
		public bool ReadOnly { get; set; }
		public BorderStyle BorderStyle { get; set; }
		public string[] Lines => [.. _lines.Select(l => l.LineText)];
		public WorkbenchParserEngine Parser { get; } = new WorkbenchParserEngine();

		public bool IsHexMode { get; set; } = false;
		public int StartLineNumber { get; set; } = 1;
		public int HeaderLinesCount { get; set; } = 0;
		public int TargetLine { get; set; } = -1;
		public int TargetLineEnd { get; set; } = -1;
		public bool IsSearchLogMode { get; set; } = false;
		public int LineCount => _lines.Count;

		public bool ForceHighlight { get; set; } = false;
		public bool HighlightTextInsteadOfBackground { get; set; } = false;

		public int TopMargin { get; set; } = 4;
		public int BottomMargin { get; set; } = 4;

		public bool WordWrap
		{
			get => _wordWrap;
			set
			{
				if (_wordWrap != value)
				{
					bool wasAtBottom = false;
					int topLogicalLine = 0;

					if (vScrollBar != null && vScrollBar.Enabled)
					{
						int allowedMax = Math.Max(0, vScrollBar.Maximum - vScrollBar.LargeChange + 1);
						wasAtBottom = (_scrollLine >= allowedMax);
						topLogicalLine = GetLineFromVisualOffset(Math.Max(0, _scrollLine), out _);
					}

					_wordWrap = value;

					if (_updateLock == 0)
					{
						UpdateAllWordWraps();
						UpdateScrollbar();

						if (wasAtBottom)
						{
							ScrollToBottom();
						}
						else
						{
							ScrollToLine(topLogicalLine);
						}

						Invalidate();
					}
				}
			}
		}

		public bool EnableSyntaxHighlighting
		{
			get
			{
				if (AppSettings.Current.LiteMode || !AppSettings.Current.EnableSyntaxHighlighting)
					return false;

				return ForceHighlight;
			}
			set => _enableSyntaxHighlighting = value;
		}

		private int CurrentMarginWidth
		{
			get
			{
				int totalLines = _lines.Count + ((IsHexMode && _hexData != null) ? (_hexData.Length + 15) / 16 : 0);
				int maxLineNum = StartLineNumber + totalLines;
				int charW = _cachedCharWidth > 0 ? _cachedCharWidth : 8;
				int requiredWidth = (maxLineNum.ToString().Length * charW) + 40;
				return Math.Max(60, requiredWidth);
			}
		}

		public event EventHandler<int>? VisualScrollPositionChanged;

		#endregion

		#region Nested Helper Types

		public readonly struct HighlightScope : IDisposable
		{
			private readonly XFusion _xf;

			public HighlightScope(XFusion xf)
			{
				_xf = xf;
				_xf.ForceHighlight = true;
			}

			public void Dispose()
			{
				_xf.ForceHighlight = false;
			}
		}

		public class DarkMenuRenderer : ToolStripProfessionalRenderer
		{
			public DarkMenuRenderer() : base(new DarkColorTable()) { }

			protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
			{
				Color textColor = e.Item.Selected ? Color.White : UITheme.TextMain;
				TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, textColor, e.TextFormat);
			}
		}

		#endregion

		#region Constructor & Initialization

		public XFusion()
		{
			SetStyle(ControlStyles.OptimizedDoubleBuffer |
					 ControlStyles.AllPaintingInWmPaint |
					 ControlStyles.UserPaint |
					 ControlStyles.ResizeRedraw |
					 ControlStyles.Selectable |
					 ControlStyles.StandardClick, true);
			UpdateStyles();

			DoubleBuffered = true;
			BackColor = UITheme.BgPreview;

			TokenColors[(int)TokenType.Default] = ColorTranslator.FromHtml("#B6BDCC");
			TokenColors[(int)TokenType.Comment] = ColorTranslator.FromHtml("#59AA59");
			TokenColors[(int)TokenType.Number] = ColorTranslator.FromHtml("#D5DFF0");
			TokenColors[(int)TokenType.String] = ColorTranslator.FromHtml("#C178DD");
			TokenColors[(int)TokenType.Preprocessor] = ColorTranslator.FromHtml("#D4FD95");
			TokenColors[(int)TokenType.Operator] = ColorTranslator.FromHtml("#D5DFF0");
			TokenColors[(int)TokenType.Keyword] = ColorTranslator.FromHtml("#59A6E9");
			TokenColors[(int)TokenType.Type] = ColorTranslator.FromHtml("#40B5AC");
			TokenColors[(int)TokenType.Function] = ColorTranslator.FromHtml("#F3AD58");
			//TokenColors[(int)TokenType.Attribute] = ColorTranslator.FromHtml("#40B5AC");
			TokenColors[(int)TokenType.Attribute] = ColorTranslator.FromHtml("#F3AD58");
			TokenColors[(int)TokenType.Constant] = ColorTranslator.FromHtml("#B6BDCC");
			TokenColors[(int)TokenType.Enum] = ColorTranslator.FromHtml("#40B5AC");
			TokenColors[(int)TokenType.Member] = ColorTranslator.FromHtml("#B6BDCC");
			TokenColors[(int)TokenType.LogPrefix] = UITheme.LogInfo;

			ForeColor = TokenColors[(int)TokenType.Default];

			for (int i = 0; i < TokenColors.Length; i++)
			{
				Color c = TokenColors[i];
				_nativeColors[i] = c.R | (c.G << 8) | (c.B << 16);
			}

			_marginBrush = new SolidBrush(Color.FromArgb(45, 45, 48));
			_boxPen = new Pen(Color.Gray);
			_textBrush = new SolidBrush(Color.White);
			_selBrush = new SolidBrush(Color.FromArgb(70, 90, 130));
			_sf = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

			_marginSf = new() {
				Alignment = StringAlignment.Far,
				LineAlignment = StringAlignment.Center,
				Trimming = StringTrimming.None,
				FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
			};

			vScrollBar = new() { Dock = DockStyle.Right };
			vScrollBar.ValueChanged += (s, e) => {
				if (_updateLock == 0)
				{
					_scrollLine = vScrollBar.Value;
					Invalidate();
					if (!_suppressScrollEvent)
						VisualScrollPositionChanged?.Invoke(this, _scrollLine);
				}
			};
			Controls.Add(vScrollBar);

			hScrollBar = new() { Dock = DockStyle.Bottom };
			hScrollBar.ValueChanged += (s, e) => {
				if (_updateLock == 0)
				{
					_scrollCharX = hScrollBar.Value;
					Invalidate();
				}
			};
			Controls.Add(hScrollBar);

			_contextMenu = new ContextMenuStrip
			{
				BackColor = UITheme.BgDark,
				ForeColor = UITheme.TextMain,
				ShowImageMargin = false,
				Renderer = new DarkMenuRenderer()
			};

			_copyMenuItem = new ToolStripMenuItem("Copy", null, (s, e) => CopySelection())
			{
				ShortcutKeyDisplayString = "Ctrl+C",
				ForeColor = UITheme.TextMain
			};

			_contextMenu.Items.Add(_copyMenuItem);
			_contextMenu.Opening += ContextMenu_Opening;
			ContextMenuStrip = _contextMenu;

			RecalculateFontMetrics();
		}

		public IDisposable CreateForceHighlightScope() => new HighlightScope(this);

		public void ConfigureForSearchLog(bool logEnabled)
		{
			IsSearchLogMode = logEnabled;
			BackColor = logEnabled ? Color.FromArgb(15, 15, 18) : Color.FromArgb(38, 40, 43);
			if (_updateLock == 0)
			{
				UpdateScrollbar();
				Invalidate();
			}
		}

		#endregion

		#region Memory & Line Management

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private LineDescriptor GetLine()
		{
			if (_linePool.Count > 0)
			{
				var line = _linePool.Pop();
				line.Tokens.Clear();
				line.Highlights.Clear();
				line.LineBackColor = null;
				line.IsDirty = true;
				line.IsWrapDirty = true;
				line.IsCollapsed = false;
				line.WrapOffsets = null;
				line.VisualLineCount = 1;
				line.IsImaginary = false;
				return line;
			}
			return new LineDescriptor();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void ReleaseLine(LineDescriptor line)
		{
			line.LineText = string.Empty;
			line.Tokens.Clear();
			line.Highlights.Clear();
			line.WrapOffsets = null;
			_linePool.Push(line);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void BeginUpdate()
		{
			_updateLock++;
		}

		public void EndUpdate()
		{
			_updateLock--;
			if (_updateLock <= 0)
			{
				_updateLock = 0;
				UpdateAllWordWraps();
				UpdateScrollbar();
				if (IsSearchLogMode) ScrollToBottom();
				Invalidate();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void EnsureBufferSize(int requiredLength)
		{
			if (requiredLength > _renderBuffer.Length)
			{
				int newSize = Math.Max(requiredLength + 1024, _renderBuffer.Length * 2);
				Array.Resize(ref _renderBuffer, newSize);
				Array.Resize(ref _dxBuffer, newSize);
			}
		}

		#endregion

		#region Word Wrapping & Text Calculations

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void EnsureWrap(LineDescriptor desc)
		{
			if (!desc.IsWrapDirty) return;
			desc.IsWrapDirty = false;
			ComputeWrap(desc);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private int GetLineVisualCountFast(LineDescriptor line)
		{
			if (!line.IsWrapDirty) return line.VisualLineCount;
			if (!_wordWrap || _wrapMaxCols <= 0 || IsHexMode) return 1;

			int len = line.LineText.Length;
			if (len <= _wrapMaxCols) return 1;

			return (len / _wrapMaxCols) + 1;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private unsafe void ComputeWrap(LineDescriptor desc)
		{
			if (!_wordWrap || _wrapMaxCols <= 0 || IsHexMode)
			{
				desc.WrapOffsets = null;
				desc.VisualLineCount = 1;
				return;
			}

			int maxCols = _wrapMaxCols;
			string text = desc.LineText;
			int len = text.Length;

			const int MAX_VISUAL_LINES = 1000;

			if (len <= maxCols)
			{
				bool hasTabs = false;
				fixed (char* pText = text)
				{
					for (int i = 0; i < len; i++)
					{
						if (pText[i] == '\t')
						{
							hasTabs = true;
							break;
						}
					}
				}
				if (!hasTabs)
				{
					desc.WrapOffsets = null;
					desc.VisualLineCount = 1;
					return;
				}
			}

			int offsetCount = 0;
			int currentLineCols = 0;
			int lastBreakIdx = -1;

			fixed (char* pText = text)
			{
				for (int i = 0; i < len; i++)
				{
					char c = pText[i];
					int colWidth = (c == '\t') ? 4 : 1;

					if (c == ' ' || c == '\t' || c == ',' || c == '-' || c == ';' || c == '(' || c == ')')
					{
						lastBreakIdx = i;
					}

					currentLineCols += colWidth;

					if (currentLineCols > maxCols && i > 0)
					{
						if (offsetCount >= _wrapBuffer.Length) Array.Resize(ref _wrapBuffer, _wrapBuffer.Length * 2);

						if (lastBreakIdx != -1 && lastBreakIdx > (offsetCount > 0 ? _wrapBuffer[offsetCount - 1] : 0))
						{
							_wrapBuffer[offsetCount++] = lastBreakIdx + 1;
							i = lastBreakIdx;
						}
						else
						{
							_wrapBuffer[offsetCount++] = i;
							i--;
						}
						currentLineCols = 0;
						lastBreakIdx = -1;

						if (offsetCount >= MAX_VISUAL_LINES) {
							break;
						}
					}
				}
			}

			if (offsetCount > 0)
			{
				if (desc.WrapOffsets == null || desc.WrapOffsets.Length != offsetCount)
					desc.WrapOffsets = new int[offsetCount];

				Array.Copy(_wrapBuffer, desc.WrapOffsets, offsetCount);
				desc.VisualLineCount = offsetCount + 1;
			}
			else
			{
				desc.WrapOffsets = null;
				desc.VisualLineCount = 1;
			}
		}

		private void UpdateAllWordWraps()
		{
			if (_lines == null || _cachedCharWidth <= 0 || Width <= 0) return;

			int availablePixels = Width - CurrentMarginWidth - 10;
			_wrapMaxCols = Math.Max(5, availablePixels / _cachedCharWidth);

			var linesSpan = CollectionsMarshal.AsSpan(_lines);
			for (int i = 0; i < linesSpan.Length; i++)
			{
				linesSpan[i].IsWrapDirty = true;
				EnsureWrap(linesSpan[i]);
			}
		}

		private int GetTotalVisualLines()
		{
			int total = 0;
			var linesSpan = CollectionsMarshal.AsSpan(_lines);
			for (int i = 0; i < linesSpan.Length; i++)
			{
				if (linesSpan[i].IsCollapsed)
				{
					total++;
					int foldLevel = linesSpan[i].StateBefore.BraceLevel;
					while (i + 1 < linesSpan.Length && linesSpan[i + 1].StateBefore.BraceLevel > foldLevel) i++;
				}
				else
				{
					total += GetLineVisualCountFast(linesSpan[i]);
				}
			}
			if (IsHexMode && _hexData != null) total += (_hexData.Length + 15) / 16;
			return total;
		}

		private int GetLineFromVisualOffset(int visualOffset, out int visualSubLine)
		{
			visualSubLine = 0;
			int currentVis = 0;
			var linesSpan = CollectionsMarshal.AsSpan(_lines);

			for (int i = 0; i < linesSpan.Length; i++)
			{
				var line = linesSpan[i];
				int vCount = line.IsCollapsed ? 1 : GetLineVisualCountFast(line);

				if (currentVis + vCount > visualOffset)
				{
					EnsureWrap(line);
					visualSubLine = visualOffset - currentVis;
					if (visualSubLine >= line.VisualLineCount) visualSubLine = Math.Max(0, line.VisualLineCount - 1);
					return i;
				}
				currentVis += vCount;

				if (line.IsCollapsed)
				{
					int foldLevel = line.StateBefore.BraceLevel;
					while (i + 1 < linesSpan.Length && linesSpan[i + 1].StateBefore.BraceLevel > foldLevel) i++;
				}
			}
			if (IsHexMode && _hexData != null)
			{
				int hexLines = (_hexData.Length + 15) / 16;
				if (visualOffset < currentVis + hexLines)
				{
					visualSubLine = visualOffset - currentVis;
					return _lines.Count + visualSubLine;
				}
			}
			return Math.Max(0, _lines.Count - 1);
		}

		public void GetLocationFromPoint(int x, int y, out int lineIdx, out int charIdx)
		{
			lineIdx = 0;
			charIdx = 0;
			if (lineHeight <= 0 || _lines.Count == 0) return;

			const int yPosStart = 40;
			int visualOffset = _scrollLine + ((y - yPosStart) / lineHeight);
			if (y < yPosStart) visualOffset = _scrollLine - 1;

			lineIdx = GetLineFromVisualOffset(Math.Max(0, visualOffset), out int subLine);

			if (lineIdx >= _lines.Count)
			{
				if (IsHexMode) { lineIdx = 0; return; }
				lineIdx = _lines.Count - 1;
			}
			if (lineIdx < 0) { lineIdx = 0; return; }

			var line = _lines[lineIdx];
			EnsureWrap(line);

			int startChar = (subLine <= 0 || line.WrapOffsets == null) ? 0 : line.WrapOffsets[subLine - 1];
			int endChar = (line.WrapOffsets == null || subLine >= line.VisualLineCount - 1)
				? line.LineText.Length
				: line.WrapOffsets[subLine];

			startChar = Math.Clamp(startChar, 0, line.LineText.Length);
			endChar = Math.Clamp(endChar, startChar, line.LineText.Length);

			int marginWidth = CurrentMarginWidth;
			int relativeX = x - (marginWidth + 5);
			int targetRenderCol = _scrollCharX + (relativeX / Math.Max(1, _cachedCharWidth));

			if (targetRenderCol <= 0)
			{
				charIdx = startChar;
				return;
			}

			ReadOnlySpan<char> textSpan = line.LineText.AsSpan();
			int currentCol = 0;

			for (int i = startChar; i < endChar; i++)
			{
				int charWidth = (textSpan[i] == '\t') ? 4 : 1;

				if (currentCol + (charWidth / 2) >= targetRenderCol)
				{
					charIdx = i;
					return;
				}

				currentCol += charWidth;
			}

			charIdx = endChar;
		}

		private static int GetRenderColumnForIndex(ReadOnlySpan<char> text, int startIdx, int targetIdx)
		{
			int col = 0;
			int end = Math.Min(targetIdx, text.Length);
			for (int i = startIdx; i < end; i++)
			{
				col += (text[i] == '\t') ? 4 : 1;
			}
			return col;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private unsafe int GetRenderColWidth(string text, int start, int end)
		{
			if (start >= end || start >= text.Length) return 0;
			int limit = Math.Min(end, text.Length);
			int cols = 0;

			fixed (char* pText = text)
			{
				char* p = pText + start;
				char* pEnd = pText + limit;
				while (p < pEnd)
				{
					cols += (*p == '\t') ? 4 : 1;
					p++;
				}
			}
			return cols;
		}

		#endregion

		#region Public Content & Highlighting APIs

		public void Clear()
		{
			BeginUpdate();
			var linesSpan = CollectionsMarshal.AsSpan(_lines);
			for (int i = 0; i < linesSpan.Length; i++) ReleaseLine(linesSpan[i]);
			_lines.Clear();
			_hexData = null;
			IsHexMode = false;
			_selStartLine = _selEndLine = -1;
			_scrollLine = 0;
			_scrollCharX = 0;
			TargetLine = -1;
			TargetLineEnd = -1;
			HeaderLinesCount = 0;
			EndUpdate();
		}

		public void ClearHighlights()
		{
			BeginUpdate();
			var linesSpan = CollectionsMarshal.AsSpan(_lines);
			for (int i = 0; i < linesSpan.Length; i++)
			{
				linesSpan[i].Highlights.Clear();
			}
			EndUpdate();
		}

		public void AddHighlight(int lineIdx, int startCol, int length, Color color, bool underline = false)
		{
			if (lineIdx >= 0 && lineIdx < _lines.Count)
			{
				_lines[lineIdx].Highlights.Add(new HighlightDef
				{
					StartCol = startCol,
					Length = length,
					BgColor = color,
					IsUnderlined = underline
				});
				if (_updateLock == 0) Invalidate();
			}
		}

		public void HighlightPatternGlobal(string pattern, bool isRegex, bool matchCase, Color color)
		{
			if (string.IsNullOrEmpty(pattern)) return;

			string regexPattern = isRegex ? pattern : Regex.Escape(pattern);

			if (!isRegex && (pattern.Contains('*') || pattern.Contains('?')))
			{
				regexPattern = regexPattern.Replace("\\*", ".*").Replace("\\?", ".");
			}

			try
			{
				var opt = RegexOptions.Compiled | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
				var r = new Regex(regexPattern, opt, TimeSpan.FromMilliseconds(500));

				var linesSpan = CollectionsMarshal.AsSpan(_lines);
				for (int i = 0; i < linesSpan.Length; i++)
				{
					var line = linesSpan[i];
					if (string.IsNullOrEmpty(line.LineText)) continue;

					foreach (ValueMatch m in r.EnumerateMatches(line.LineText.AsSpan()))
					{
						line.Highlights.Add(new HighlightDef { StartCol = m.Index, Length = m.Length, BgColor = color });
					}
				}

				if (_updateLock == 0) Invalidate();
			}
			catch
			{
			}
		}

		public void LoadHexData(byte[] data)
		{
			_hexData = data;
			IsHexMode = true;
			_selStartLine = _selEndLine = -1;

			UpdateScrollbar();
			Invalidate();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void TokenizeLineByContext(LineDescriptor desc, int lineIndex, LineState stateBefore, ref LineState stateAfter)
		{
			if (lineIndex < HeaderLinesCount)
			{
				WorkbenchParserEngine.TokenizeMetadataLine(desc);
				stateAfter = stateBefore;
			}
			else if (IsHexMode)
			{
				WorkbenchParserEngine.TokenizeHexLine(desc);
				stateAfter = stateBefore;
			}
			else if (IsSearchLogMode || !EnableSyntaxHighlighting)
			{
				desc.Tokens.Clear();
				if (desc.LineText.Length > 0)
				{
					TokenType type = IsSearchLogMode ? TokenType.LogPrefix : TokenType.Default;
					desc.Tokens.Add(new Token { Start = 0, Length = desc.LineText.Length, Type = type });
				}
				stateAfter = stateBefore;
			}
			else
			{
				stateAfter = stateBefore;
				if (CurrentLanguage == LexerLanguage.EnfusionData)
				{
					WorkbenchParserEngine.TokenizeDataLine(desc, ref stateAfter);
				}
				else if (CurrentLanguage == LexerLanguage.XML)
				{
					WorkbenchParserEngine.TokenizeXmlLine(desc, ref stateAfter);
				}
				else
				{
					Parser.TokenizeLine(desc, ref stateAfter);
				}
			}
		}

		public void AppendLines(IEnumerable<string> lines)
		{
			BeginUpdate();
			LineState currentState = _lines.Count > 0 ? _lines[^1].StateAfter : new LineState();

			foreach (var rawLine in lines)
			{
				var lineStr = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
				var desc = GetLine();
				desc.LineText = lineStr;
				desc.StateBefore = currentState;
				desc.StateInComment = currentState.InComment;
				desc.CurrentTypeContext = currentState.ContextType;
				desc.InsideMethodContext = currentState.InsideMethod;

				LineState nextState = currentState;
				TokenizeLineByContext(desc, _lines.Count, currentState, ref nextState);
				desc.StateAfter = nextState;
				currentState = nextState;

				desc.IsWrapDirty = true;
				_lines.Add(desc);
			}
			EndUpdate();
		}

		public void AppendLine(string text, Color? backColor = null, int? originalLineNumber = null)
		{
			var desc = GetLine();
			desc.LineText = text;
			desc.LineBackColor = backColor;
			desc.OriginalLineNumber = originalLineNumber;

			LineState prevState = _lines.Count > 0 ? _lines[^1].StateAfter : new LineState();
			desc.StateBefore = prevState;
			desc.StateInComment = prevState.InComment;

			LineState nextState = prevState;
			TokenizeLineByContext(desc, _lines.Count, prevState, ref nextState);
			desc.StateAfter = nextState;

			desc.IsWrapDirty = true;
			_lines.Add(desc);

			if (_updateLock == 0)
			{
				UpdateScrollbar();
				if (IsSearchLogMode) ScrollToBottom();
				Invalidate();
			}
		}

		public void AppendImaginaryLine(Color? backColor = null)
		{
			var desc = GetLine();
			desc.IsImaginary = true;
			desc.LineText = string.Empty;
			desc.LineBackColor = backColor;

			desc.StateBefore = _lines.Count > 0 ? _lines[^1].StateAfter : new LineState();
			desc.StateAfter = desc.StateBefore;
			desc.IsWrapDirty = false;

			_lines.Add(desc);

			if (_updateLock == 0)
			{
				UpdateScrollbar();
				Invalidate();
			}
		}

		public string TextContent
		{
			set
			{
				BeginUpdate();
				var linesSpan = CollectionsMarshal.AsSpan(_lines);
				for (int i = 0; i < linesSpan.Length; i++) ReleaseLine(linesSpan[i]);
				_lines.Clear();
				_selStartLine = _selEndLine = -1;

				string[] rawLines = value.Split('\n');
				if (_lines.Capacity < rawLines.Length) _lines.Capacity = rawLines.Length;

				int autoHeaderCount = 0;
				bool hasPreviewMarker = false;

				for (int i = 0; i < rawLines.Length; i++)
				{
					if (rawLines[i].Contains("TEXT PREVIEW ]") ||
						rawLines[i].StartsWith("Code Context Preview:") ||
						rawLines[i].StartsWith("// == CONTEXT WINDOW:"))
					{
						autoHeaderCount = Math.Min(rawLines.Length, i + 2);
						hasPreviewMarker = true;
						break;
					}
				}

				if (!hasPreviewMarker)
				{
					for (int i = 0; i < Math.Min(3, rawLines.Length); i++)
					{
						if (rawLines[i].Contains("SELECTED FILE METADATA"))
						{
							autoHeaderCount = rawLines.Length;
							break;
						}
					}
				}

				HeaderLinesCount = autoHeaderCount;
				LineState currentState = new();

				for (int i = 0; i < rawLines.Length; i++)
				{
					var lineStr = rawLines[i].EndsWith('\r') ? rawLines[i][..^1] : rawLines[i];

					var desc = GetLine();
					desc.LineText = lineStr;
					desc.StateBefore = currentState;
					desc.StateInComment = currentState.InComment;
					desc.CurrentTypeContext = currentState.ContextType;
					desc.InsideMethodContext = currentState.InsideMethod;

					LineState nextState = currentState;
					TokenizeLineByContext(desc, i, currentState, ref nextState);
					desc.StateAfter = nextState;
					currentState = nextState;

					desc.IsWrapDirty = true;
					_lines.Add(desc);
				}

				EndUpdate();
			}
		}

		public void UpdateLine(int lineIndex, string newText)
		{
			if (lineIndex < 0 || lineIndex >= _lines.Count) return;

			var currentLine = _lines[lineIndex];
			currentLine.LineText = newText;
			currentLine.IsDirty = true;

			LineState prevState = lineIndex > 0 ? _lines[lineIndex - 1].StateAfter : new LineState();
			currentLine.StateBefore = prevState;
			currentLine.StateInComment = prevState.InComment;

			LineState nextState = prevState;
			TokenizeLineByContext(currentLine, lineIndex, prevState, ref nextState);
			currentLine.StateAfter = nextState;
			currentLine.IsWrapDirty = true;

			int next = lineIndex + 1;
			while (next < _lines.Count)
			{
				var nextLine = _lines[next];
				if (nextLine.StateBefore.Equals(currentLine.StateAfter)) break;

				nextLine.StateBefore = currentLine.StateAfter;
				nextLine.StateInComment = currentLine.StateAfter.InComment;
				nextLine.IsDirty = true;

				LineState afterState = nextLine.StateAfter;
				TokenizeLineByContext(nextLine, next, nextLine.StateBefore, ref afterState);
				nextLine.StateAfter = afterState;
				nextLine.IsWrapDirty = true;

				currentLine = nextLine;
				next++;
			}

			if (_updateLock == 0)
			{
				UpdateScrollbar();
				Invalidate();
			}
		}

		#endregion

		#region Scrolling & Layout Management

		public void SetScrollPosition(int visualLine)
		{
			if (vScrollBar == null || _lines.Count == 0) return;

			_suppressScrollEvent = true;

			int maxScroll = Math.Max(0, vScrollBar.Maximum - vScrollBar.LargeChange + 1);
			int targetScroll = Math.Max(0, Math.Min(visualLine, maxScroll));

			if (_scrollLine != targetScroll)
			{
				_scrollLine = targetScroll;
				if (vScrollBar.Enabled)
				{
					vScrollBar.Value = _scrollLine;
				}
				Invalidate();
			}

			_suppressScrollEvent = false;
		}

		public void ScrollToBottom()
		{
			if (lineHeight == 0) return;

			UpdateScrollbar();

			int totalLines = GetTotalVisualLines();
			int maxScroll = Math.Max(0, totalLines - visibleLinesCount);

			_scrollLine = maxScroll;

			if (vScrollBar.Enabled)
			{
				int allowedMax = Math.Max(0, vScrollBar.Maximum - vScrollBar.LargeChange + 1);
				vScrollBar.Value = Math.Min(_scrollLine, allowedMax);
			}

			Invalidate();
		}

		public void ScrollToLine(int lineIndex)
		{
			if (vScrollBar == null || _lines.Count == 0) return;

			UpdateScrollbar();

			int visualOffset = 0;
			for (int i = 0; i < lineIndex && i < _lines.Count; i++)
			{
				if (_lines[i].IsCollapsed)
				{
					visualOffset++;
					int foldLevel = _lines[i].StateBefore.BraceLevel;
					while (i + 1 < _lines.Count && _lines[i + 1].StateBefore.BraceLevel > foldLevel) i++;
				}
				else
				{
					visualOffset += GetLineVisualCountFast(_lines[i]);
				}
			}

			int maxScroll = Math.Max(0, vScrollBar.Maximum - vScrollBar.LargeChange + 1);
			int targetScroll = Math.Max(0, Math.Min(visualOffset, maxScroll));

			if (vScrollBar.Enabled)
			{
				if (vScrollBar.Value != targetScroll)
				{
					vScrollBar.Value = targetScroll;
				}
				else if (_updateLock == 0)
				{
					_scrollLine = targetScroll;
					Invalidate();
				}
			}
			else
			{
				_scrollLine = targetScroll;
				if (_updateLock == 0) Invalidate();
			}
		}

		private void UpdateScrollbar()
		{
			if (lineHeight == 0) return;

			int marginWidth = CurrentMarginWidth;
			int rawWidthChars = _cachedCharWidth > 0 ? (Width - marginWidth - (vScrollBar.Visible ? vScrollBar.Width : 0)) / _cachedCharWidth : 1;
			int availableWidthChars = Math.Max(1, rawWidthChars);

			int maxChars = 0;
			if (!_wordWrap && _lines.Count > 0)
			{
				var linesSpan = CollectionsMarshal.AsSpan(_lines);
				for (int i = 0; i < linesSpan.Length; i++)
				{
					int c = GetRenderColWidth(linesSpan[i].LineText, 0, linesSpan[i].LineText.Length);
					if (c > maxChars) maxChars = c;
				}
				if (IsHexMode && _hexData != null)
				{
					maxChars = Math.Max(maxChars, 80);
				}
			}

			if (!_wordWrap && rawWidthChars > 0 && maxChars > availableWidthChars)
			{
				hScrollBar.Enabled = true;
				hScrollBar.Visible = true;
				hScrollBar.LargeChange = availableWidthChars;
				hScrollBar.Maximum = maxChars - 1;
				hScrollBar.SmallChange = 1;

				int maxHScroll = Math.Max(0, maxChars - availableWidthChars);
				if (_scrollCharX > maxHScroll) _scrollCharX = maxHScroll;

				int allowedHMax = Math.Max(0, hScrollBar.Maximum - hScrollBar.LargeChange + 1);
				hScrollBar.Value = Math.Min(_scrollCharX, allowedHMax);
			}
			else
			{
				hScrollBar.Enabled = false;
				hScrollBar.Visible = false;
				hScrollBar.Maximum = 0;
				hScrollBar.Value = 0;
				_scrollCharX = 0;
			}

			int availableHeight = Height - TopMargin - BottomMargin - (hScrollBar.Visible ? hScrollBar.Height : 0);
			visibleLinesCount = Math.Max(1, availableHeight / lineHeight);

			int totalLines = GetTotalVisualLines();

			int scrollableTotalLines = (totalLines > visibleLinesCount) ? totalLines + 1 : totalLines;

			if (totalLines <= visibleLinesCount)
			{
				vScrollBar.Enabled = false;
				vScrollBar.Maximum = 0;
				vScrollBar.Value = 0;
				_scrollLine = 0;
			}
			else
			{
				vScrollBar.Enabled = true;
				vScrollBar.LargeChange = visibleLinesCount;
				vScrollBar.Maximum = Math.Max(0, scrollableTotalLines - 1);
				vScrollBar.SmallChange = 1;

				int maxScroll = Math.Max(0, scrollableTotalLines - visibleLinesCount);
				if (_scrollLine > maxScroll)
				{
					_scrollLine = maxScroll;
				}

				int allowedMax = Math.Max(0, vScrollBar.Maximum - vScrollBar.LargeChange + 1);
				int targetValue = Math.Min(_scrollLine, allowedMax);
				vScrollBar.Value = Math.Max(0, targetValue);
			}
		}

		#endregion

		#region Font Metrics & Control Overrides

		protected override void OnFontChanged(EventArgs e)
		{
			base.OnFontChanged(e);
			RecalculateFontMetrics();
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			RecalculateFontMetrics();
		}

		private void RecalculateFontMetrics()
		{
			if (_cachedHFont != IntPtr.Zero)
			{
				_ = DeleteObject(_cachedHFont);
				_cachedHFont = IntPtr.Zero;
			}

			LOGFONT lf = new()
			{
				lfHeight = -HdcLogicalHeightFromFont(Font),
				lfWeight = Font.Bold ? 700 : 400,
				lfItalic = Font.Italic ? (byte)1 : (byte)0,
				lfCharSet = 1,
				lfOutPrecision = 3,
				lfClipPrecision = 2,
				lfQuality = 5,
				lfPitchAndFamily = 49
			};
			lf.SetFaceName(Font.Name);

			_cachedHFont = CreateFontIndirectW(in lf);

			if (IsHandleCreated)
			{
				using var g = CreateGraphics();
				IntPtr hdc = g.GetHdc();
				IntPtr old = SelectObject(hdc, _cachedHFont);

				if (GetTextExtentPoint32W(hdc, "0", 1, out SIZE sz))
				{
					_cachedCharWidth = Math.Max(1, sz.cx);
					lineHeight = Math.Max(1, sz.cy);
				}
				else
				{
					_cachedCharWidth = Math.Max(1, (int)Math.Round(Font.Size * 0.6f));
					lineHeight = Math.Max(1, TextRenderer.MeasureText("Wg", Font).Height);
				}

				_ = SelectObject(hdc, old);
				g.ReleaseHdc(hdc);
			}
			else
			{
				_cachedCharWidth = Math.Max(1, (int)Math.Round(Font.Size * 0.6f));
				lineHeight = Math.Max(1, TextRenderer.MeasureText("Wg", Font).Height);
			}

			if (_updateLock == 0)
			{
				UpdateAllWordWraps();
				UpdateScrollbar();
				Invalidate();
			}
		}

		private int HdcLogicalHeightFromFont(Font font)
		{
			using var g = CreateGraphics();
			return (int)Math.Round((font.Size * g.DpiY) / 72f);
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			if (_updateLock == 0)
			{
				UpdateAllWordWraps();
				UpdateScrollbar();
				Invalidate();
			}
		}

		#endregion

		#region Mouse & Selection Event Handlers

		private void ContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
		{
			_copyMenuItem.Enabled = (_selStartLine != -1 && _selEndLine != -1 &&
									(_selStartLine != _selEndLine || _selStartCol != _selEndCol));
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);

			if ((ModifierKeys & Keys.Shift) == Keys.Shift)
			{
				if (hScrollBar != null && hScrollBar.Visible && hScrollBar.Enabled)
				{
					int charsToScroll = 5;
					int delta = -(int)Math.Round((double)e.Delta / 120.0 * charsToScroll);

					int maxScroll = Math.Max(0, hScrollBar.Maximum - hScrollBar.LargeChange + 1);
					int target = Math.Max(0, Math.Min(hScrollBar.Value + delta, maxScroll));

					if (hScrollBar.Value != target)
					{
						hScrollBar.Value = target;
						_scrollCharX = target;
						Invalidate();
					}
				}
			}
			else if (vScrollBar != null && vScrollBar.Visible && vScrollBar.Enabled)
			{
				int linesToScroll = SystemInformation.MouseWheelScrollLines;
				if (linesToScroll <= 0) linesToScroll = 3;

				int deltaLines = -(int)Math.Round((double)e.Delta / 120.0 * linesToScroll);

				int maxScroll = Math.Max(0, vScrollBar.Maximum - vScrollBar.LargeChange + 1);
				int target = Math.Max(0, Math.Min(vScrollBar.Value + deltaLines, maxScroll));

				if (vScrollBar.Value != target)
				{
					vScrollBar.Value = target;
					_scrollLine = target;
					Invalidate();

					if (!_suppressScrollEvent)
					{
						VisualScrollPositionChanged?.Invoke(this, _scrollLine);
					}
				}
			}
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			Focus();
			if (_updateLock > 0 || _lines.Count == 0 || lineHeight == 0) return;

			int marginWidth = CurrentMarginWidth;

			TimeSpan clickDelta = DateTime.Now - _lastClickTime;
			int maxDist = SystemInformation.DoubleClickSize.Width;
			bool isNear = Math.Abs(e.X - _lastClickPos.X) <= maxDist && Math.Abs(e.Y - _lastClickPos.Y) <= maxDist;

			if (clickDelta.TotalMilliseconds <= SystemInformation.DoubleClickTime && isNear)
			{
				_clickCount++;
			}
			else
			{
				_clickCount = 1;
			}

			_lastClickTime = DateTime.Now;
			_lastClickPos = e.Location;

			if (e.Button == MouseButtons.Left && e.X > marginWidth)
			{
				GetLocationFromPoint(e.X, e.Y, out int currentLine, out int currentChar);

				if (_clickCount == 2)
				{
					_isSelecting = false;
					SelectWordAt(currentLine, currentChar);
				}
				else if (_clickCount >= 3)
				{
					_isSelecting = false;
					_selStartLine = currentLine;
					_selStartCol = 0;
					_selEndLine = currentLine;
					_selEndCol = _lines[currentLine].LineText.Length;
					Invalidate();
				}
				else
				{
					_isSelecting = true;
					_selStartLine = currentLine;
					_selStartCol = currentChar;
					_selEndLine = _selStartLine;
					_selEndCol = _selStartCol;
					Invalidate();
				}
			}
			else if (e.Button == MouseButtons.Right && e.X > marginWidth)
			{
				GetLocationFromPoint(e.X, e.Y, out int clickLine, out int clickCol);

				if (!IsInsideSelection(clickLine, clickCol))
				{
					_selStartLine = _selEndLine = clickLine;
					_selStartCol = _selEndCol = clickCol;
					Invalidate();
				}
			}
		}

		private void SelectWordAt(int lineIdx, int charIdx)
		{
			if (lineIdx < 0 || lineIdx >= _lines.Count) return;
			string text = _lines[lineIdx].LineText;
			if (text.Length == 0) return;

			int start = Math.Max(0, Math.Min(charIdx, text.Length - 1));
			int end = start;

			if (start >= text.Length) return;

			if (char.IsLetterOrDigit(text[start]) || text[start] == '_')
			{
				while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
				while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
			}
			else
			{
				end = Math.Min(end + 1, text.Length);
			}

			_selStartLine = lineIdx;
			_selStartCol = start;
			_selEndLine = lineIdx;
			_selEndCol = end;
			Invalidate();
		}

		private bool IsInsideSelection(int line, int col)
		{
			if (_selStartLine == -1 || _selEndLine == -1) return false;

			int sLine = _selStartLine, sCol = _selStartCol;
			int eLine = _selEndLine, eCol = _selEndCol;

			if (sLine > eLine || (sLine == eLine && sCol > eCol))
			{
				sLine = _selEndLine; sCol = _selEndCol;
				eLine = _selStartLine; eCol = _selStartCol;
			}

			if (line < sLine || line > eLine) return false;
			if (line > sLine && line < eLine) return true;
			if (sLine == eLine) return col >= sCol && col <= eCol;
			if (line == sLine) return col >= sCol;
			if (line == eLine) return col <= eCol;

			return false;
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (_isSelecting && _lines.Count > 0 && lineHeight > 0)
			{
				GetLocationFromPoint(e.X, e.Y, out _selEndLine, out _selEndCol);

				if (e.Y < 40)
				{
					_scrollLine = Math.Max(0, _scrollLine - 1);
					if (vScrollBar.Maximum >= _scrollLine) vScrollBar.Value = _scrollLine;
				}
				else if (e.Y > Height)
				{
					_scrollLine = Math.Min(_scrollLine + 1, vScrollBar.Maximum - vScrollBar.LargeChange + 1);
					if (vScrollBar.Maximum >= _scrollLine) vScrollBar.Value = _scrollLine;
				}

				if (e.X > Width - 20 && hScrollBar.Visible)
				{
					_scrollCharX = Math.Min(_scrollCharX + 1, hScrollBar.Maximum - hScrollBar.LargeChange + 1);
					if (hScrollBar.Maximum >= _scrollCharX) hScrollBar.Value = _scrollCharX;
				}
				else if (e.X < CurrentMarginWidth + 20 && hScrollBar.Visible)
				{
					_scrollCharX = Math.Max(0, _scrollCharX - 1);
					if (hScrollBar.Maximum >= _scrollCharX) hScrollBar.Value = _scrollCharX;
				}

				Invalidate();
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			if (e.Button == MouseButtons.Left)
			{
				_isSelecting = false;
			}
		}

		protected override void OnMouseClick(MouseEventArgs e)
		{
			base.OnMouseClick(e);
			if (_updateLock > 0 || _lines.Count == 0 || lineHeight == 0) return;

			int marginWidth = CurrentMarginWidth;
			if (e.X <= marginWidth)
			{
				GetLocationFromPoint(e.X, e.Y, out int clickedLineIdx, out _);
				if (clickedLineIdx >= 0 && clickedLineIdx < _lines.Count)
				{
					var line = _lines[clickedLineIdx];
					bool isFoldStart = line.StateAfter.BraceLevel > line.StateBefore.BraceLevel;

					if (isFoldStart)
					{
						line.IsCollapsed = !line.IsCollapsed;
						UpdateScrollbar();
						Invalidate();
					}
				}
			}
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == (Keys.Control | Keys.C))
			{
				CopySelection();
				return true;
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}

		private void CopySelection()
		{
			if (_selStartLine == -1 || _selEndLine == -1) return;
			int sLine = _selStartLine, sCol = _selStartCol;
			int eLine = _selEndLine, eCol = _selEndCol;

			if (sLine > eLine || (sLine == eLine && sCol > eCol))
			{
				sLine = _selEndLine; sCol = _selEndCol;
				eLine = _selStartLine; eCol = _selStartCol;
			}

			StringBuilder sb = new();
			for (int i = sLine; i <= eLine; i++)
			{
				if (i >= _lines.Count) break;
				string text = _lines[i].LineText;
				int start = (i == sLine) ? sCol : 0;
				int end = (i == eLine) ? Math.Min(eCol, text.Length) : text.Length;

				start = Math.Max(0, Math.Min(start, text.Length));
				end = Math.Max(0, Math.Min(end, text.Length));

				if (end >= start)
				{
					sb.Append(text.AsSpan(start, end - start));
				}

				if (i < eLine) sb.AppendLine();
			}

			if (sb.Length > 0)
			{
				try { Clipboard.SetText(sb.ToString()); }
				catch { }
			}
		}

		public void SetSelection(int startLine, int startCol, int endLine, int endCol)
		{
			if (_lines.Count == 0) return;
			_selStartLine = Math.Max(0, Math.Min(startLine, _lines.Count - 1));
			_selStartCol = Math.Max(0, startCol);
			_selEndLine = Math.Max(0, Math.Min(endLine, _lines.Count - 1));
			_selEndCol = Math.Max(0, endCol);
			Invalidate();
		}

		#endregion

		#region Rendering & Painting Logic

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private int GetNativeColor(TokenType type)
		{
			return _nativeColors[(int)type];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static Color GetManagedColor(TokenType type)
		{
			return TokenColors[(int)type];
		}

		protected override unsafe void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);

			if (_updateLock > 0 || lineHeight <= 0 || _cachedCharWidth <= 0) return;
			if (_cachedHFont == IntPtr.Zero || _renderBuffer == null || _dxBuffer == null) return;

			int startVis = _scrollLine;
			int yPosStart = TopMargin;
			int bottomMargin = BottomMargin;
			int hScrollHeight = hScrollBar.Visible ? hScrollBar.Height : 0;
			int maxY = Height - bottomMargin - hScrollHeight;
			int marginWidth = CurrentMarginWidth;

			int totalLines = _lines.Count + ((IsHexMode && _hexData != null) ? (_hexData.Length + 15) / 16 : 0);

			var oldClip = e.Graphics.Clip;
			e.Graphics.SetClip(new Rectangle(0, yPosStart, Width, Math.Max(0, maxY - yPosStart)));

			if (totalLines > 0)
			{
				int currentLogicalLine = GetLineFromVisualOffset(startVis, out int visualSubLineOffset);

				int baseLineNumber = StartLineNumber;
				for (int k = 0; k < currentLogicalLine; k++)
				{
					if (k >= HeaderLinesCount) baseLineNumber++;
				}

				int i1 = currentLogicalLine;
				int y1 = yPosStart;
				int curLineNum1 = baseLineNumber;

				while (y1 < maxY && i1 < totalLines)
				{
					if (i1 < _lines.Count)
					{
						var line = _lines[i1];
						EnsureWrap(line);
						bool isHeader = i1 < HeaderLinesCount;
						int displayLine = isHeader ? 0 : curLineNum1;
						bool isTargetRow = !IsHexMode && TargetLine != -1 && i1 >= TargetLine && i1 <= (TargetLineEnd >= TargetLine ? TargetLineEnd : TargetLine);

						int startSub = (i1 == currentLogicalLine) ? visualSubLineOffset : 0;
						int endSub = line.IsCollapsed ? 1 : line.VisualLineCount;

						for (int subLine = startSub; subLine < endSub; subLine++)
						{
							e.Graphics.FillRectangle(_marginBrush, 0, y1, marginWidth, lineHeight);

							if (subLine == 0)
							{
								if (isTargetRow && !isHeader)
								{
									using SolidBrush bg = new(Color.FromArgb(80, 85, 90));
									e.Graphics.FillRectangle(bg, 0, y1, marginWidth, lineHeight);

									using SolidBrush arrowBrush = new(_arrowColor);
									e.Graphics.DrawString("►", Font, arrowBrush, new RectangleF(4, y1, marginWidth, lineHeight), new() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });

									using SolidBrush fg = new(_arrowColor);
									e.Graphics.DrawString(displayLine.ToString(), Font, fg, new RectangleF(0, y1, marginWidth - 12, lineHeight), _marginSf);
								}
								else if (!isHeader && !line.IsImaginary)
								{
									using SolidBrush fg = new(Color.FromArgb(100, 105, 115));
									e.Graphics.DrawString(displayLine.ToString(), Font, fg, new RectangleF(0, y1, marginWidth - 12, lineHeight), _marginSf);
								}
							}

							Color separatorColor = (isTargetRow && !isHeader) ? _arrowColor : Color.FromArgb(60, 60, 65);
							using Pen sep = new(separatorColor);
							e.Graphics.DrawLine(sep, marginWidth - 1, y1, marginWidth - 1, y1 + lineHeight);

							if (subLine == 0 && line.StateAfter.BraceLevel > line.StateBefore.BraceLevel)
							{
								int boxSize = 9;
								int boxX = marginWidth - 10;
								int boxY = y1 + (lineHeight - boxSize) / 2;

								e.Graphics.DrawRectangle(_boxPen, boxX, boxY, boxSize, boxSize);
								string symbol = line.IsCollapsed ? "+" : "-";
								e.Graphics.DrawString(symbol, Font, _textBrush, new RectangleF(boxX, boxY + 1, boxSize, boxSize), _sf);
							}

							if (line.LineBackColor.HasValue)
							{
								Color bgColor = line.LineBackColor.Value;
								if (!_brushCache.TryGetValue(bgColor, out SolidBrush? cachedBgBrush) || cachedBgBrush == null)
								{
									cachedBgBrush = new(bgColor);
									_brushCache[bgColor] = cachedBgBrush;
								}
								e.Graphics.FillRectangle(cachedBgBrush, marginWidth, y1, Width - marginWidth, lineHeight);
							}
							else if (isTargetRow)
							{
								using SolidBrush matchBg = new(_targetLineBg);
								e.Graphics.FillRectangle(matchBg, marginWidth, y1, Width - marginWidth, lineHeight);
							}

							int startChar = subLine == 0 ? 0 : line.WrapOffsets![subLine - 1];
							int endChar = subLine == line.VisualLineCount - 1 ? line.LineText.Length : line.WrapOffsets![subLine];

							if (line.Highlights.Count > 0 && !HighlightTextInsteadOfBackground)
							{
								foreach (var hl in line.Highlights)
								{
									int hlStartChar = Math.Max(hl.StartCol, startChar);
									int hlEndChar = Math.Min(hl.StartCol + hl.Length, endChar);

									if (hlStartChar < hlEndChar)
									{
										int hlColStart = GetRenderColWidth(line.LineText, startChar, hlStartChar);
										int hlColEnd = GetRenderColWidth(line.LineText, startChar, hlEndChar);

										if (hlColEnd > _scrollCharX)
										{
											int visStartCol = Math.Max(hlColStart, _scrollCharX);
											int visEndCol = hlColEnd;

											int drawX = marginWidth + 5 + (visStartCol - _scrollCharX) * _cachedCharWidth;
											int drawW = (visEndCol - visStartCol) * _cachedCharWidth;

											if (drawW > 0)
											{
												Color bg = hl.BgColor;
												using SolidBrush hb = new(Color.FromArgb(70, bg));
												e.Graphics.FillRectangle(hb, drawX, y1, drawW, lineHeight);

												using Pen hp = new(Color.FromArgb(255, bg));
												e.Graphics.DrawRectangle(hp, drawX, y1, drawW - 1, lineHeight - 1);

												if (hl.IsUnderlined)
												{
													int underlineY = y1 + lineHeight - 1;
													using Pen underlinePen = new(hl.BgColor, 2);
													e.Graphics.DrawLine(underlinePen, drawX, underlineY, drawX + drawW, underlineY);
												}
											}
										}
									}
								}
							}

							if (_selStartLine != -1 && _selEndLine != -1 && !IsHexMode)
							{
								int sLine = _selStartLine, sCol = _selStartCol;
								int eLine = _selEndLine, eCol = _selEndCol;
								if (sLine > eLine || (sLine == eLine && sCol > eCol))
								{
									sLine = _selEndLine; sCol = _selEndCol;
									eLine = _selStartLine; eCol = _selStartCol;
								}

								if (i1 >= sLine && i1 <= eLine)
								{
									int drawStartStringCol = (i1 == sLine) ? sCol : 0;
									int drawEndStringCol = (i1 == eLine) ? eCol : line.LineText.Length;

									int selStartChar = Math.Max(drawStartStringCol, startChar);
									int selEndChar = Math.Min(drawEndStringCol, endChar);

									bool drawTrailing = (i1 < eLine && subLine == endSub - 1 && selEndChar == endChar && drawEndStringCol >= endChar);

									if (selStartChar < selEndChar || drawTrailing)
									{
										int selColStart = GetRenderColWidth(line.LineText, startChar, selStartChar);
										int selColEnd = GetRenderColWidth(line.LineText, startChar, selEndChar);
										if (drawTrailing && selColEnd == selColStart) selColEnd = selColStart + 1;

										if (selColEnd > _scrollCharX)
										{
											int visStartCol = Math.Max(selColStart, _scrollCharX);
											int visEndCol = selColEnd;

											int drawX = marginWidth + 5 + (visStartCol - _scrollCharX) * _cachedCharWidth;
											int selWidth = (visEndCol - visStartCol) * _cachedCharWidth;
											if (drawTrailing && selWidth == 0) selWidth = _cachedCharWidth;

											if (selWidth > 0)
											{
												e.Graphics.FillRectangle(_selBrush, drawX, y1, selWidth, lineHeight);
											}
										}
									}
								}
							}

							y1 += lineHeight;
							if (y1 >= maxY) break;
						}

						if (!isHeader && !line.IsImaginary) curLineNum1++;

						if (line.IsCollapsed)
						{
							int foldLevel = line.StateBefore.BraceLevel;
							while (i1 + 1 < _lines.Count && _lines[i1 + 1].StateBefore.BraceLevel > foldLevel)
							{
								i1++;
								if (!isHeader && !_lines[i1].IsImaginary) curLineNum1++;
							}
						}
					}
					else
					{
						if (IsHexMode)
						{
							using SolidBrush fg = new(Color.FromArgb(100, 105, 115));
							e.Graphics.DrawString(curLineNum1.ToString(), Font, fg, new RectangleF(0, y1, marginWidth - 12, lineHeight), _marginSf);
							curLineNum1++;
						}
						using Pen sep = new(Color.FromArgb(60, 60, 65));
						e.Graphics.DrawLine(sep, marginWidth - 1, y1, marginWidth - 1, y1 + lineHeight);

						y1 += lineHeight;
					}
					i1++;
				}

				e.Graphics.SetClip(new Rectangle(marginWidth, yPosStart, Width - marginWidth, Math.Max(0, maxY - yPosStart)));

				IntPtr hdc = IntPtr.Zero;
				IntPtr hOldFont = IntPtr.Zero;

				if (!SAFE_MODE)
				{
					hdc = e.Graphics.GetHdc();
					hOldFont = SelectObject(hdc, _cachedHFont);
					_ = SetBkMode(hdc, 1);
				}

				try
				{
					void DrawChunk(int xPos, int renderYPos, int writeIdx, TokenType type, Color? overrideColor = null)
					{
						if (writeIdx <= 0 || writeIdx > _renderBuffer.Length) return;

						if (SAFE_MODE)
						{
							Color safeCol;
							if (overrideColor.HasValue)
							{
								safeCol = overrideColor.Value;
							}
							else
							{
								int nativeCol = GetNativeColor(type);
								safeCol = Color.FromArgb(nativeCol & 0xFF, (nativeCol >> 8) & 0xFF, (nativeCol >> 16) & 0xFF);
							}

							string safeText = new(_renderBuffer, 0, writeIdx);
							TextRenderer.DrawText(e.Graphics, safeText, Font, new Point(xPos, renderYPos), safeCol, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
						}
						else
						{
							if (writeIdx > _dxBuffer.Length) return;

							int nativeCol;
							if (overrideColor.HasValue)
							{
								nativeCol = overrideColor.Value.R | (overrideColor.Value.G << 8) | (overrideColor.Value.B << 16);
							}
							else
							{
								nativeCol = GetNativeColor(type);
							}

							_ = SetTextColor(hdc, nativeCol);

							for (int d = 0; d < writeIdx; d++)
							{
								_dxBuffer[d] = _cachedCharWidth;
							}

							fixed (char* pText = _renderBuffer)
							fixed (int* pDx = _dxBuffer)
							{
								ExtTextOutW(hdc, xPos, renderYPos, 0, IntPtr.Zero, pText, (uint)writeIdx, pDx);
							}
						}
					}

					int i2 = currentLogicalLine;
					int y2 = yPosStart;

					while (y2 < maxY && i2 < totalLines)
					{
						if (i2 < _lines.Count)
						{
							var line = _lines[i2];
							EnsureWrap(line);
							int startSub = (i2 == currentLogicalLine) ? visualSubLineOffset : 0;
							int endSub = line.IsCollapsed ? 1 : line.VisualLineCount;
							ReadOnlySpan<char> lineSpan = line.LineText.AsSpan();

							for (int subLine = startSub; subLine < endSub; subLine++)
							{
								int startChar = subLine == 0 ? 0 : line.WrapOffsets![subLine - 1];
								int endChar = subLine == line.VisualLineCount - 1 ? line.LineText.Length : line.WrapOffsets![subLine];

								var tokensSpan = CollectionsMarshal.AsSpan(line.Tokens);
								foreach (var token in tokensSpan)
								{
									if (token.Length == 0) continue;
									if ((uint)token.Start >= (uint)lineSpan.Length) continue;

									int tStart = Math.Max(token.Start, startChar);
									int tEnd = Math.Min(token.Start + token.Length, endChar);
									if (tStart >= tEnd) continue;

									int tokenColStart = GetRenderColWidth(line.LineText, startChar, tStart);
									int tokenColEnd = GetRenderColWidth(line.LineText, startChar, tEnd);

									if (tokenColEnd <= _scrollCharX) continue;

									int writeIdx = 0;
									EnsureBufferSize(token.Length * 4);

									int currCol = tokenColStart;
									int firstVisibleCol = -1;
									Color? currentHighlightColor = null;

									fixed (char* pLine = lineSpan)
									fixed (char* pBuf = _renderBuffer)
									{
										for (int idx = tStart; idx < tEnd; idx++)
										{
											Color? charHighlightColor = null;
											if (HighlightTextInsteadOfBackground && line.Highlights.Count > 0)
											{
												foreach (var hl in line.Highlights)
												{
													if (idx >= hl.StartCol && idx < hl.StartCol + hl.Length)
													{
														charHighlightColor = hl.BgColor;
														break;
													}
												}
											}

											if (charHighlightColor != currentHighlightColor && writeIdx > 0)
											{
												if (firstVisibleCol >= _scrollCharX)
												{
													int drawXPos = marginWidth + 5 + (firstVisibleCol - _scrollCharX) * _cachedCharWidth;
													DrawChunk(drawXPos, y2, writeIdx, token.Type, currentHighlightColor);
												}
												writeIdx = 0;
												firstVisibleCol = -1;
											}
											currentHighlightColor = charHighlightColor;

											char ch = pLine[idx];
											int cWidth = (ch == '\t') ? 4 : 1;
											int nextCol = currCol + cWidth;

											if (nextCol <= _scrollCharX)
											{
												currCol = nextCol;
												continue;
											}

											if (currCol < _scrollCharX)
											{
												if (ch == '\t')
												{
													int visibleSpaces = nextCol - _scrollCharX;
													for (int s = 0; s < visibleSpaces; s++)
													{
														pBuf[writeIdx++] = ' ';
													}
												}
												else
												{
													pBuf[writeIdx++] = ch;
												}
											}
											else
											{
												if (ch == '\t')
												{
													pBuf[writeIdx++] = ' ';
													pBuf[writeIdx++] = ' ';
													pBuf[writeIdx++] = ' ';
													pBuf[writeIdx++] = ' ';
												}
												else
												{
													pBuf[writeIdx++] = ch;
												}
											}

											if (firstVisibleCol == -1)
											{
												firstVisibleCol = Math.Max(_scrollCharX, currCol);
											}

											currCol = nextCol;
										}
									}

									if (writeIdx > 0 && firstVisibleCol >= _scrollCharX)
									{
										int drawXPos = marginWidth + 5 + (firstVisibleCol - _scrollCharX) * _cachedCharWidth;
										DrawChunk(drawXPos, y2, writeIdx, token.Type, currentHighlightColor);

										if (HighlightTextInsteadOfBackground && line.Highlights.Count > 0)
										{
											foreach (var hl in line.Highlights)
											{
												if (hl.IsUnderlined)
												{
													int hlStartChar = Math.Max(hl.StartCol, startChar);
													int hlEndChar = Math.Min(hl.StartCol + hl.Length, endChar);

													if (hlStartChar < hlEndChar)
													{
														int hlColStart = GetRenderColWidth(line.LineText, startChar, hlStartChar);
														int hlColEnd = GetRenderColWidth(line.LineText, startChar, hlEndChar);

														if (hlColEnd > _scrollCharX)
														{
															int visStartCol = Math.Max(hlColStart, _scrollCharX);
															int visEndCol = hlColEnd;

															int uDrawX = marginWidth + 5 + (visStartCol - _scrollCharX) * _cachedCharWidth;
															int uDrawW = (visEndCol - visStartCol) * _cachedCharWidth;

															if (uDrawW > 0)
															{
																int underlineY = y2 + lineHeight - 1;
																using Pen underlinePen = new(hl.BgColor, 1.5f);

																if (!SAFE_MODE)
																{
																	using var gHdc = Graphics.FromHdc(hdc);
																	gHdc.DrawLine(underlinePen, uDrawX, underlineY, uDrawX + uDrawW, underlineY);
																}
																else
																{
																	e.Graphics.DrawLine(underlinePen, uDrawX, underlineY, uDrawX + uDrawW, underlineY);
																}
															}
														}
													}
												}
											}
										}
									}
								}

								y2 += lineHeight;
								if (y2 >= maxY) break;
							}

							if (line.IsCollapsed)
							{
								int foldLevel = line.StateBefore.BraceLevel;
								while (i2 + 1 < _lines.Count && _lines[i2 + 1].StateBefore.BraceLevel > foldLevel)
								{
									i2++;
								}
							}
						}
						else if (IsHexMode && _hexData != null)
						{
							int hexIdx = i2 - _lines.Count;
							int offset = hexIdx * 16;
							int bytesToRead = Math.Min(16, _hexData.Length - offset);
							int xPos = Math.Max(marginWidth + 5, marginWidth + 5 - (_scrollCharX * _cachedCharWidth));

							EnsureBufferSize(8);
							for (int j = 7; j >= 0; j--)
							{
								_renderBuffer[j] = HexLookup[(offset >> ((7 - j) * 4)) & 0xF];
							}
							DrawChunk(xPos, y2, 8, TokenType.Type);
							xPos += (8 + 2) * _cachedCharWidth;

							EnsureBufferSize(48);
							int hexWriteIdx = 0;
							for (int j = 0; j < 16; j++)
							{
								if (j < bytesToRead)
								{
									byte b = _hexData[offset + j];
									_renderBuffer[hexWriteIdx++] = HexLookup[b >> 4];
									_renderBuffer[hexWriteIdx++] = HexLookup[b & 0xF];
								}
								else
								{
									_renderBuffer[hexWriteIdx++] = ' ';
									_renderBuffer[hexWriteIdx++] = ' ';
								}
								_renderBuffer[hexWriteIdx++] = ' ';
							}
							DrawChunk(xPos, y2, hexWriteIdx, TokenType.Default);
							xPos += (hexWriteIdx + 1) * _cachedCharWidth;

							EnsureBufferSize(16);
							int asciiWriteIdx = 0;
							for (int j = 0; j < bytesToRead; j++)
							{
								byte b = _hexData[offset + j];
								_renderBuffer[asciiWriteIdx++] = (b < 32 || b > 126) ? '.' : (char)b;
							}
							DrawChunk(xPos, y2, asciiWriteIdx, TokenType.Comment);

							y2 += lineHeight;
						}
						i2++;
					}
				}
				finally
				{
					if (!SAFE_MODE)
					{
						SelectObject(hdc, hOldFont);
						e.Graphics.ReleaseHdc(hdc);
					}
				}
			}

			e.Graphics.Clip = oldClip;
		}

		#endregion

		#region Resource Cleanup & Disposal

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (_cachedHFont != IntPtr.Zero)
				{
					_ = DeleteObject(_cachedHFont);
					_cachedHFont = IntPtr.Zero;
				}

				_contextMenu?.Dispose();

				_marginBrush?.Dispose();
				_boxPen?.Dispose();
				_textBrush?.Dispose();
				_selBrush?.Dispose();
				_sf?.Dispose();
				_marginSf?.Dispose();

				foreach (var brush in _brushCache.Values)
				{
					brush.Dispose();
				}
				_brushCache.Clear();
			}
			base.Dispose(disposing);
		}

		#endregion
	}

	public class WorkbenchParserEngine
	{
		#region Nested Models & Data Sets

		public class Scope
		{
			public string? Name { get; set; }
			public bool IsMethod { get; set; }
		}

		public class SpanHashSet
		{
			private readonly Dictionary<int, List<string>> _buckets = [];

			public void Add(string value)
			{
				int hash = GetSpanHashCode(value.AsSpan());
				if (!_buckets.TryGetValue(hash, out var list))
				{
					list = [];
					_buckets[hash] = list;
				}
				if (!list.Contains(value)) list.Add(value);
			}

			public bool Contains(ReadOnlySpan<char> span)
			{
				int hash = GetSpanHashCode(span);
				if (!_buckets.TryGetValue(hash, out var list)) return false;

				var listSpan = CollectionsMarshal.AsSpan(list);
				for (int i = 0; i < listSpan.Length; i++)
				{
					if (span.SequenceEqual(listSpan[i].AsSpan())) return true;
				}
				return false;
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private static unsafe int GetSpanHashCode(ReadOnlySpan<char> span)
			{
				unchecked
				{
					int hash = 5381;
					fixed (char* ptr = span)
					{
						char* current = ptr;
						char* end = ptr + span.Length;
						while (current < end)
						{
							hash = ((hash << 5) + hash) ^ (*current);
							current++;
						}
					}
					return hash;
				}
			}
		}

		public class SemanticModel
		{
			public SpanHashSet Types { get; } = new();
			public Dictionary<string, string> Inheritance { get; } = new(StringComparer.Ordinal);
			public Dictionary<string, List<string>> Members { get; } = new(StringComparer.Ordinal);

			public void RegisterType(ReadOnlySpan<char> nameSpan)
			{
				if (nameSpan.IsEmpty) return;
				string name = new(nameSpan);
				if (!Members.ContainsKey(name)) Members[name] = [];
				Types.Add(name);
			}

			public void RegisterInheritance(string childType, string parentType)
			{
				if (childType != null && parentType != null) Inheritance[childType] = parentType;
			}

			public void RegisterMember(string type, ReadOnlySpan<char> memberSpan)
			{
				if (type == null || memberSpan.IsEmpty) return;
				string member = new(memberSpan);
				if (!Members.ContainsKey(type)) Members[type] = [];
				if (!Members[type].Contains(member)) Members[type].Add(member);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public bool IsType(ReadOnlySpan<char> name) => Types.Contains(name);
		}

		#endregion

		#region Parser Fields & Caches

		private readonly SpanHashSet _keywords = new();
		private readonly SpanHashSet _knownTypes = new();
		private readonly SpanHashSet _knownFuncs = new();
		private readonly SpanHashSet _knownConsts = new();
		private readonly SpanHashSet _knownEnums = new();
		private readonly SpanHashSet _knownAttrs = new();
		private readonly SpanHashSet _knownMembers = new();
		private readonly SpanHashSet _knownTypedefs = new();

		private readonly SemanticModel _globalSemantic = new();
		private readonly Stack<Scope> _scopeStack = new();

		private static readonly char[] WhitespaceTokens = [' ', '\r', '\n', '\t'];

		#endregion

		#region Cache Population & Utilities

		public void LoadKeywordCaches()
		{
			string kw0 = "class namespace struct typedef typename new null void static bool float int " +
						 "override proto modded event autoptr autoref ref super notnull out in local gvar " +
						 "const private protected public return break continue if else for foreach while " +
						 "switch case default true false func this";

			string complexTypes = "string vector array map set";

			Populate(_keywords, kw0);
			Populate(_knownTypes, complexTypes);
			Populate(_knownTypes, Safe("kw1_types.txt"));
			Populate(_knownFuncs, "Print " + Safe("kw2_funcs.txt"));
			Populate(_knownConsts, Safe("kw3_consts.txt"));
			Populate(_knownEnums, Safe("kw4_enums.txt"));
			Populate(_knownMembers, Safe("kw5_members.txt"));
			Populate(_knownTypedefs, Safe("kw6_typedefs.txt"));
		}

		private static void Populate(SpanHashSet set, string raw)
		{
			if (string.IsNullOrWhiteSpace(raw)) return;
			foreach (var w in raw.Split(WhitespaceTokens, StringSplitOptions.RemoveEmptyEntries)) set.Add(w);
		}

		private static string Safe(string resource)
		{
			try
			{
				var asm = Assembly.GetExecutingAssembly();
				var name = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(resource));
				if (name == null) return "";

				using var s = asm.GetManifestResourceStream(name);
				if (s == null) return "";
				using var r = new StreamReader(s);
				return r.ReadToEnd();
			}
			catch { return ""; }
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe bool IsAllUpper(ReadOnlySpan<char> s)
		{
			if (s.Length <= 1) return false;
			fixed (char* ptr = s)
			{
				char* current = ptr;
				char* end = ptr + s.Length;
				while (current < end)
				{
					char c = *current;
					if (!(c == '_' || char.IsUpper(c) || char.IsDigit(c))) return false;
					current++;
				}
			}
			return true;
		}

		#endregion

		#region Specialized Tokenizers (Hex, Metadata, XML, Data)

		public static void TokenizeHexLine(LineDescriptor line)
		{
			line.Tokens.Clear();
			int len = line.LineText.Length;
			if (len == 0) return;

			int offsetLen = Math.Min(8, len);
			line.Tokens.Add(new Token { Start = 0, Length = offsetLen, Type = TokenType.Type });

			if (len > 8)
			{
				int hexEnd = Math.Min(56, len);
				line.Tokens.Add(new Token { Start = 8, Length = hexEnd - 8, Type = TokenType.Default });

				if (len > 56)
				{
					line.Tokens.Add(new Token { Start = 56, Length = len - 56, Type = TokenType.Comment });
				}
			}
		}

		public static void TokenizeMetadataLine(LineDescriptor line)
		{
			line.Tokens.Clear();
			string text = line.LineText;
			if (string.IsNullOrEmpty(text)) return;

			if (text.Contains("BINARY HEX DUMP ]") || text.Contains("TEXT PREVIEW ]") || text.Contains("Code Context Preview:"))
			{
				line.Tokens.Add(new Token { Start = 0, Length = text.Length, Type = TokenType.String });
				return;
			}

			if (text.StartsWith('╔') || text.StartsWith('║') || text.StartsWith('╚') || text.StartsWith("---"))
			{
				line.Tokens.Add(new Token { Start = 0, Length = text.Length, Type = TokenType.Keyword });
				return;
			}

			int colonIdx = text.IndexOf(':');
			if (colonIdx != -1)
			{
				line.Tokens.Add(new Token { Start = 0, Length = colonIdx + 1, Type = TokenType.Type });

				if (colonIdx + 1 < text.Length)
				{
					line.Tokens.Add(new Token { Start = colonIdx + 1, Length = text.Length - (colonIdx + 1), Type = TokenType.Preprocessor });
				}
			}
			else
			{
				line.Tokens.Add(new Token { Start = 0, Length = text.Length, Type = TokenType.Default });
			}
		}

		public static unsafe void TokenizeXmlLine(LineDescriptor line, ref LineState state)
		{
			line.Tokens.Clear();
			ReadOnlySpan<char> span = line.LineText.AsSpan();
			int len = span.Length;
			int i = 0;

			bool inComment = state.InComment;
			bool inTag = state.InAttribute;
			bool inCData = state.TypedefState == 3;

			fixed (char* pSpan = span)
			{
				if (inComment)
				{
					int startComment = i;
					while (i < len)
					{
						if (i + 2 < len && pSpan[i] == '-' && pSpan[i + 1] == '-' && pSpan[i + 2] == '>')
						{
							i += 3;
							inComment = false;
							state.InComment = false;
							line.Tokens.Add(new Token { Start = startComment, Length = i - startComment, Type = TokenType.Comment });
							break;
						}
						i++;
					}
					if (inComment)
					{
						line.Tokens.Add(new Token { Start = startComment, Length = len - startComment, Type = TokenType.Comment });
						return;
					}
				}

				if (inCData)
				{
					int startCData = i;
					while (i < len)
					{
						if (i + 2 < len && pSpan[i] == ']' && pSpan[i + 1] == ']' && pSpan[i + 2] == '>')
						{
							i += 3;
							inCData = false;
							state.TypedefState = 0;
							line.Tokens.Add(new Token { Start = startCData, Length = i - startCData, Type = TokenType.String });
							break;
						}
						i++;
					}
					if (inCData)
					{
						line.Tokens.Add(new Token { Start = startCData, Length = len - startCData, Type = TokenType.String });
						return;
					}
				}

				while (i < len)
				{
					char c = pSpan[i];

					if (char.IsWhiteSpace(c))
					{
						int s = i;
						while (i < len && char.IsWhiteSpace(pSpan[i])) i++;
						line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Default });
						continue;
					}

					if (!inTag)
					{
						if (c == '<')
						{
							if (i + 3 < len && pSpan[i + 1] == '!' && pSpan[i + 2] == '-' && pSpan[i + 3] == '-')
							{
								int s = i;
								i += 4;
								inComment = true;
								state.InComment = true;
								while (i < len)
								{
									if (i + 2 < len && pSpan[i] == '-' && pSpan[i + 1] == '-' && pSpan[i + 2] == '>')
									{
										i += 3;
										inComment = false;
										state.InComment = false;
										break;
									}
									i++;
								}
								line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Comment });
								continue;
							}
							if (i + 8 < len && span.Slice(i, 9).SequenceEqual("<![CDATA[".AsSpan()))
							{
								int s = i;
								i += 9;
								inCData = true;
								state.TypedefState = 3;
								while (i < len)
								{
									if (i + 2 < len && pSpan[i] == ']' && pSpan[i + 1] == ']' && pSpan[i + 2] == '>')
									{
										i += 3;
										inCData = false;
										state.TypedefState = 0;
										break;
									}
									i++;
								}
								line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.String });
								continue;
							}

							line.Tokens.Add(new Token { Start = i, Length = 1, Type = TokenType.Operator });
							i++;
							inTag = true;
							state.InAttribute = true;

							if (i < len && (pSpan[i] == '/' || pSpan[i] == '?' || pSpan[i] == '!'))
							{
								line.Tokens.Add(new Token { Start = i, Length = 1, Type = TokenType.Operator });
								i++;
							}

							int nameStart = i;
							while (i < len && (char.IsLetterOrDigit(pSpan[i]) || pSpan[i] == '-' || pSpan[i] == '_' || pSpan[i] == ':')) i++;
							if (i > nameStart)
							{
								line.Tokens.Add(new Token { Start = nameStart, Length = i - nameStart, Type = TokenType.Type });
							}
							continue;
						}

						int textStart = i;
						while (i < len && pSpan[i] != '<') i++;
						line.Tokens.Add(new Token { Start = textStart, Length = i - textStart, Type = TokenType.Default });
						continue;
					}
					else
					{
						if (c == '>')
						{
							line.Tokens.Add(new Token { Start = i, Length = 1, Type = TokenType.Operator });
							i++;
							inTag = false;
							state.InAttribute = false;
							continue;
						}

						if (c == '/' || c == '?')
						{
							line.Tokens.Add(new Token { Start = i, Length = 1, Type = TokenType.Operator });
							i++;
							continue;
						}

						if (c == '=')
						{
							line.Tokens.Add(new Token { Start = i, Length = 1, Type = TokenType.Operator });
							i++;
							continue;
						}

						if (c == '"' || c == '\'')
						{
							char q = c;
							int s = i++;
							while (i < len)
							{
								if (pSpan[i] == q) { i++; break; }
								i++;
							}
							line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.String });
							continue;
						}

						if (char.IsLetter(c) || c == '_' || c == ':')
						{
							int s = i;
							while (i < len && (char.IsLetterOrDigit(pSpan[i]) || pSpan[i] == '-' || pSpan[i] == '_' || pSpan[i] == ':')) i++;
							line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Attribute });
							continue;
						}

						line.Tokens.Add(new Token { Start = i, Length = 1, Type = TokenType.Default });
						i++;
					}
				}
			}
		}

		public static unsafe void TokenizeDataLine(LineDescriptor line, ref LineState state)
		{
			line.Tokens.Clear();
			ReadOnlySpan<char> span = line.LineText.AsSpan();
			int len = span.Length;
			int i = 0;

			if (span.StartsWith("[SYSTEM]"))
			{
				line.Tokens.Add(new Token { Start = 0, Length = len, Type = TokenType.LogPrefix });
				return;
			}

			bool inComment = state.InComment;

			fixed (char* pSpan = span)
			{
				if (inComment)
				{
					int startComment = i;
					while (i < len)
					{
						if (i + 1 < len && pSpan[i] == '*' && pSpan[i + 1] == '/')
						{
							i += 2;
							inComment = false;
							state.InComment = false;
							line.Tokens.Add(new Token { Start = startComment, Length = i - startComment, Type = TokenType.Comment });
							break;
						}
						i++;
					}
					if (inComment)
					{
						line.Tokens.Add(new Token { Start = startComment, Length = len - startComment, Type = TokenType.Comment });
						return;
					}
				}

				while (i < len)
				{
					char c = pSpan[i];
					char n = i + 1 < len ? pSpan[i + 1] : '\0';

					if (char.IsWhiteSpace(c))
					{
						int s = i;
						while (i < len && char.IsWhiteSpace(pSpan[i])) i++;
						line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Default });
						continue;
					}

					if (c == '/' && n == '/')
					{
						line.Tokens.Add(new Token { Start = i, Length = len - i, Type = TokenType.Comment });
						break;
					}

					if (c == '/' && n == '*')
					{
						int s = i;
						i += 2;
						inComment = true;
						state.InComment = true;
						while (i < len)
						{
							if (i + 1 < len && pSpan[i] == '*' && pSpan[i + 1] == '/')
							{
								i += 2;
								inComment = false;
								state.InComment = false;
								break;
							}
							i++;
						}
						line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Comment });
						continue;
					}

					if (c == '"' || c == '\'')
					{
						char q = c;
						int s = i++;
						while (i < len)
						{
							if (pSpan[i] == '\\' && i + 1 < len) i += 2;
							else if (pSpan[i] == q) { i++; break; }
							else i++;
						}

						int lookAhead = i;
						while (lookAhead < len && char.IsWhiteSpace(pSpan[lookAhead])) lookAhead++;

						TokenType stringType = TokenType.String;
						if (lookAhead < len && (pSpan[lookAhead] == ':' || pSpan[lookAhead] == '{'))
						{
							stringType = TokenType.Keyword;
						}

						line.Tokens.Add(new Token { Start = s, Length = i - s, Type = stringType });
						continue;
					}

					if ((uint)(c - '0') <= 9 || (c == '-' && (uint)(n - '0') <= 9))
					{
						int s = i;
						if (c == '-') i++;
						if (i < len && pSpan[i] == '0' && (i + 1 < len) && (pSpan[i + 1] == 'x' || pSpan[i + 1] == 'X')) i += 2;
						while (i < len)
						{
							char d = pSpan[i];
							if (char.IsLetterOrDigit(d) || d == '.') i++;
							else break;
						}
						line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Number });
						continue;
					}

					if (char.IsLetter(c) || c == '_')
					{
						int s = i;
						while (i < len && (char.IsLetterOrDigit(pSpan[i]) || pSpan[i] == '-' || pSpan[i] == '_')) i++;

						ReadOnlySpan<char> wSpan = span[s..i];
						TokenType type = TokenType.Default;

						bool isGuid = (wSpan.Length == 36 && wSpan[8] == '-' && wSpan[13] == '-' && wSpan[18] == '-' && wSpan[23] == '-');

						if (isGuid)
						{
							type = TokenType.Type;
						}
						else
						{
							int lb = s - 1;
							while (lb >= 0 && char.IsWhiteSpace(pSpan[lb])) lb--;

							int la = i;
							while (la < len && char.IsWhiteSpace(pSpan[la])) la++;

							bool isFirstInLine = (lb < 0);
							bool precedesBlockOrGuid = (la < len && (pSpan[la] == '{' || pSpan[la] == ':' || pSpan[la] == '"'));
							bool hasPreviousWord = (lb >= 0 && (char.IsLetterOrDigit(pSpan[lb]) || pSpan[lb] == '_'));
							bool hasNextWord = (la < len && (char.IsLetter(pSpan[la]) || pSpan[la] == '_'));

							if (wSpan.StartsWith("m_"))
							{
								type = TokenType.Type;
							}
							else if (hasPreviousWord && (hasNextWord || precedesBlockOrGuid))
							{
								type = TokenType.Keyword;
							}
							else if (hasPreviousWord && !hasNextWord)
							{
								type = TokenType.Type;
							}
							else if (isFirstInLine || precedesBlockOrGuid || hasNextWord)
							{
								type = TokenType.Keyword;
							}
							else
							{
								type = TokenType.Keyword;
							}
						}

						line.Tokens.Add(new Token { Start = s, Length = i - s, Type = type });
						continue;
					}

					line.Tokens.Add(new Token { Start = i, Length = 1, Type = TokenType.Operator });
					i++;
				}
			}
		}

		#endregion

		#region Script Syntax Tokenizer Engine

		public unsafe void TokenizeLine(LineDescriptor line, ref LineState state)
		{
			line.Tokens.Clear();
			ReadOnlySpan<char> span = line.LineText.AsSpan();
			int len = span.Length;
			int i = 0;

			if (line.LineText.StartsWith("[SYSTEM]"))
			{
				line.Tokens.Add(new Token { Start = 0, Length = len, Type = TokenType.LogPrefix });
				return;
			}

			if (len > 5)
			{
				int pipeIdx = span.IndexOf('│');
				if (pipeIdx != -1 && pipeIdx < 15)
				{
					if (span.StartsWith(" ► ".AsSpan()))
					{
						line.Tokens.Add(new Token { Start = 0, Length = 3, Type = TokenType.LogPrefix });

						if (pipeIdx > 3)
						{
							line.Tokens.Add(new Token { Start = 3, Length = pipeIdx - 3, Type = TokenType.Default });
						}

						line.Tokens.Add(new Token { Start = pipeIdx, Length = 1, Type = TokenType.Default });
						i = pipeIdx + 1;
					}
					else if (span.StartsWith("   ".AsSpan()))
					{
						line.Tokens.Add(new Token { Start = 0, Length = pipeIdx + 1, Type = TokenType.Default });
						i = pipeIdx + 1;
					}
				}
			}

			bool inComment = state.InComment;

			fixed (char* pSpan = span)
			{
				if (inComment)
				{
					int startComment = i;
					while (i < len)
					{
						if (i + 1 < len && pSpan[i] == '*' && pSpan[i + 1] == '/')
						{
							i += 2;
							inComment = false;
							state.InComment = false;
							line.Tokens.Add(new Token { Start = startComment, Length = i - startComment, Type = TokenType.Comment });
							break;
						}
						i++;
					}
					if (inComment)
					{
						line.Tokens.Add(new Token { Start = startComment, Length = len - startComment, Type = TokenType.Comment });
						return;
					}
				}

				while (i < len)
				{
					char c = pSpan[i];
					char n = i + 1 < len ? pSpan[i + 1] : '\0';

					if (char.IsWhiteSpace(c))
					{
						int s = i;
						while (i < len && char.IsWhiteSpace(pSpan[i])) i++;
						line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Default });
						continue;
					}

					if (c == '#')
					{
						int startHash = i;
						i++;

						while (i < len && char.IsLetter(pSpan[i]))
						{
							i++;
						}

						line.Tokens.Add(new Token { Start = startHash, Length = i - startHash, Type = TokenType.Preprocessor });
						continue;
					}

					if (c == '/' && n == '/')
					{
						line.Tokens.Add(new Token { Start = i, Length = len - i, Type = TokenType.Comment });
						break;
					}

					if (c == '/' && n == '*')
					{
						int s = i;
						i += 2;
						inComment = true;
						state.InComment = true;
						while (i < len)
						{
							if (i + 1 < len && pSpan[i] == '*' && pSpan[i + 1] == '/')
							{
								i += 2;
								inComment = false;
								state.InComment = false;
								break;
							}
							i++;
						}
						line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Comment });
						continue;
					}

					if (c == '[') { state.InAttribute = true; line.Tokens.Add(new Token { Start = i++, Length = 1, Type = TokenType.Operator }); continue; }
					if (c == ']') { state.InAttribute = false; line.Tokens.Add(new Token { Start = i++, Length = 1, Type = TokenType.Operator }); continue; }
					if (c == '<') { state.GenericLevel++; line.Tokens.Add(new Token { Start = i++, Length = 1, Type = TokenType.Operator }); continue; }
					if (c == '>') { if (state.GenericLevel > 0) state.GenericLevel--; line.Tokens.Add(new Token { Start = i++, Length = 1, Type = TokenType.Operator }); continue; }
					if (c == '(') { state.ParenLevel++; line.Tokens.Add(new Token { Start = i++, Length = 1, Type = TokenType.Operator }); continue; }
					if (c == ')') { if (state.ParenLevel > 0) state.ParenLevel--; line.Tokens.Add(new Token { Start = i++, Length = 1, Type = TokenType.Operator }); continue; }

					if (c == ';') { state.GenericLevel = 0; state.ExpectConstant = false; line.Tokens.Add(new Token { Start = i++, Length = 1, Type = TokenType.Operator }); continue; }

					if (c == '{')
					{
						state.BraceLevel++;
						state.ExpectInheritance = false;
						state.ExpectClassName = false;
						state.GenericLevel = 0;
						if (!state.InAttribute)
						{
							_scopeStack.Push(new Scope { Name = state.ContextType, IsMethod = state.InsideMethod });
						}
						line.Tokens.Add(new Token { Start = i++, Length = 1, Type = TokenType.Operator });
						continue;
					}

					if (c == '}')
					{
						if (state.BraceLevel > 0) state.BraceLevel--;
						state.InsideEnum = false;
						if (!state.InAttribute && _scopeStack.Count > 0)
						{
							var popped = _scopeStack.Pop();
							state.ContextType = popped.Name;
							state.InsideMethod = popped.IsMethod;
						}
						line.Tokens.Add(new Token { Start = i++, Length = 1, Type = TokenType.Operator });
						continue;
					}

					if (c == '"' || c == '\'')
					{
						char q = c;
						int s = i++;
						while (i < len)
						{
							if (pSpan[i] == '\\' && i + 1 < len) i += 2;
							else if (pSpan[i] == q) { i++; break; }
							else i++;
						}
						line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.String });
						continue;
					}

					if ((uint)(c - '0') <= 9)
					{
						int s = i;
						if (c == '0' && (n == 'x' || n == 'X')) i += 2;
						while (i < len)
						{
							char d = pSpan[i];
							if (char.IsLetterOrDigit(d) || d == '.') i++;
							else break;
						}
						line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Number });
						continue;
					}

					if (char.IsLetter(c) || c == '_')
					{
						int s = i;
						while (i < len && (char.IsLetterOrDigit(pSpan[i]) || pSpan[i] == '_')) i++;

						ReadOnlySpan<char> wSpan = span[s..i];

						int la = i;
						while (la < len && pSpan[la] <= ' ') la++;
						bool isFunction = la < len && pSpan[la] == '(';
						bool isFollowedByIdentifier = la < len && (char.IsLetter(pSpan[la]) || pSpan[la] == '_');

						int nextDotIdx = i;
						while (nextDotIdx < len && char.IsWhiteSpace(pSpan[nextDotIdx])) nextDotIdx++;
						bool isFollowedByDot = nextDotIdx < len && pSpan[nextDotIdx] == '.';
						bool isFollowedByDoubleColon = nextDotIdx + 1 < len && pSpan[nextDotIdx] == ':' && pSpan[nextDotIdx + 1] == ':';

						int lb = s - 1;
						while (lb >= 0 && pSpan[lb] <= ' ') lb--;
						char prevChar = lb >= 0 ? pSpan[lb] : '\0';

						int p = lb;
						while (p >= 0 && (char.IsLetterOrDigit(pSpan[p]) || pSpan[p] == '_')) p--;

						int startPrev = p + 1;
						int lenPrev = lb - p;

						bool isAfterNew = false;
						if (lenPrev > 0)
						{
							ReadOnlySpan<char> prevSpan = span[startPrev..(lb + 1)];
							isAfterNew = prevSpan switch
							{
								"new" or "ref" or "out" or "autoptr" => true,
								_ => false
							};
						}

						if (wSpan.SequenceEqual("class".AsSpan()) || wSpan.SequenceEqual("struct".AsSpan()) || wSpan.SequenceEqual("interface".AsSpan()))
						{
							state.ExpectClassName = true;
							line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Keyword });
							continue;
						}

						if (state.ExpectClassName)
						{
							_globalSemantic.RegisterType(wSpan);
							_knownTypes.Add(new string(wSpan));
							state.ContextType = new string(wSpan);
							state.ExpectClassName = false;
							state.ExpectInheritance = true;
							line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Type });
							continue;
						}

						if (wSpan.SequenceEqual("enum".AsSpan()))
						{
							state.InsideEnum = true;
							line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Keyword });
							continue;
						}

						if (wSpan.SequenceEqual("typedef".AsSpan()))
						{
							state.TypedefState = 1;
							line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Keyword });
							continue;
						}

						if (wSpan.SequenceEqual("const".AsSpan()))
						{
							state.ExpectConstant = true;
							line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Keyword });
							continue;
						}

						if ((prevChar == ':' && !state.InAttribute && !state.InsideMethod) || state.ExpectInheritance)
						{
							if (state.ContextType != null)
							{
								_globalSemantic.RegisterInheritance(state.ContextType, new string(wSpan));
								state.ExpectInheritance = false;
								line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Type });
								continue;
							}
						}

						if (state.ContextType != null && !state.InsideMethod && !state.InAttribute)
						{
							if (isFunction)
							{
								state.InsideMethod = true;
								_globalSemantic.RegisterMember(state.ContextType, wSpan);
								line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Function });
								continue;
							}

							if ((uint)(wSpan[0] - 'a') <= 25 && prevChar != '.' && !_keywords.Contains(wSpan) && !_knownTypes.Contains(wSpan))
							{
								_globalSemantic.RegisterMember(state.ContextType, wSpan);
								line.Tokens.Add(new Token { Start = s, Length = i - s, Type = TokenType.Member });
								continue;
							}
						}

						bool isMemberAccess = prevChar == '.';
						bool looksLikeType = char.IsUpper(wSpan[0]) && !IsAllUpper(wSpan);
						bool looksLikeConst = IsAllUpper(wSpan);
						bool looksLikeEnum = wSpan.Length > 2 && wSpan[0] == 'E' && char.IsUpper(wSpan[1]);
						bool isGlobalConst = IsAllUpper(wSpan);

						TokenType type = TokenType.Default;

						if (_keywords.Contains(wSpan))
						{
							type = TokenType.Keyword;
						}
						else if (looksLikeType && isFollowedByDot)
						{
							type = TokenType.Type;
						}
						else if (isFunction)
						{
							type = TokenType.Function;
						}
						else if (isAfterNew)
						{
							type = TokenType.Type;
							_knownTypes.Add(new string(wSpan));
						}
						else if (_knownTypes.Contains(wSpan) || _knownTypedefs.Contains(wSpan) || _globalSemantic.IsType(wSpan))
						{
							type = TokenType.Type;
						}
						else if (isMemberAccess)
						{
							if (isFunction) type = TokenType.Function;
							else if (looksLikeConst) type = TokenType.Constant;
							else type = TokenType.Member;
						}
						else if (isGlobalConst)
						{
							type = TokenType.Constant;
							state.ExpectConstant = false;
						}
						else if (_knownConsts.Contains(wSpan))
						{
							type = TokenType.Constant;
						}
						else if (wSpan.EndsWith("Attribute".AsSpan()))
						{
							type = TokenType.Attribute;
						}
						else if (state.InsideEnum && !_keywords.Contains(wSpan))
						{
							type = TokenType.Enum;
							_knownEnums.Add(new string(wSpan));
						}
						else if (state.TypedefState == 1)
						{
							type = TokenType.Type;
							state.TypedefState = 2;
						}
						else if (state.TypedefState == 2)
						{
							type = TokenType.Type;
							_knownTypedefs.Add(new string(wSpan));
							_globalSemantic.RegisterType(wSpan);
							state.TypedefState = 0;
						}
						else if (state.ExpectConstant && !_keywords.Contains(wSpan))
						{
							type = TokenType.Constant;
							state.ExpectConstant = false;
							_knownConsts.Add(new string(wSpan));
						}
						else if (state.GenericLevel > 0 && (char.IsUpper(wSpan[0]) || _knownTypes.Contains(wSpan)))
						{
							type = TokenType.Type;
						}
						else if (isFollowedByDoubleColon)
						{
							type = TokenType.Type;
							_knownTypes.Add(new string(wSpan));
						}
						else if (_knownFuncs.Contains(wSpan))
						{
							type = TokenType.Function;
						}
						else if (_knownEnums.Contains(wSpan) || looksLikeEnum)
						{
							type = TokenType.Enum;
						}
						else if (looksLikeConst)
						{
							type = TokenType.Constant;
						}
						else if (looksLikeType && isFollowedByIdentifier)
						{
							type = TokenType.Type;
							_knownTypes.Add(new string(wSpan));
						}
						else if (state.InAttribute)
						{
							if (IsAllUpper(wSpan))
								type = TokenType.Constant;
							else if (wSpan.EndsWith("Attribute".AsSpan()))
								type = TokenType.Attribute;
							else
								type = TokenType.Default;
						}

						line.Tokens.Add(new Token { Start = s, Length = i - s, Type = type });
						continue;
					}

					line.Tokens.Add(new Token { Start = i, Length = 1, Type = TokenType.Operator });
					i++;
				}
			}
		}

		#endregion
	}
}