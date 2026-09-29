using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace PakWorkbench.src.ext
{	
	#region TreeNode Data Structure
	public class TreeNode
	{
		private string _text = string.Empty;
		public string Text
		{
			get => _text;
			set
			{
				_text = value;
				CacheProperties();
			}
		}

		public string Name { get; set; } = string.Empty;
		public object? Tag { get; set; }
		public TreeNode? Parent { get; internal set; }
		public TreeNodeCollection Nodes { get; }
		public bool IsExpanded { get; set; }

		public string DisplayText { get; private set; } = string.Empty;
		public string IconPart { get; private set; } = string.Empty;
		public bool IsFolder { get; private set; } = false;
		public Color? CustomIconColor { get; private set; }
		public ICustomIcon? CustomIconRenderer { get; private set; }

		public int Level
		{
			get
			{
				int lvl = 0;
				var p = Parent;
				while (p != null) { lvl++; p = p.Parent; }
				return lvl;
			}
		}

		public TreeNode()
		{
			Nodes = new TreeNodeCollection(this);
		}

		public TreeNode(string text) : this()
		{
			Text = text;
		}

		private void CacheProperties()
		{
			IconControl.ParseNodeData(_text, out string disp, out string icn, out Color? col, out ICustomIcon? renderer, out bool isFld);

			DisplayText = disp;
			IconPart = icn;
			CustomIconColor = col;
			CustomIconRenderer = renderer;
			IsFolder = isFld;
		}

		public void Expand()
		{
			if (Nodes._tree != null && Nodes._tree.InvokeRequired)
			{
				Nodes._tree.Invoke(new Action(Expand));
				return;
			}
			if (!IsExpanded)
			{
				IsExpanded = true;
				Nodes._tree?.UpdateFlattenedNodes();
				Nodes._tree?.OnAfterExpand(new TreeViewEventArgs(this, TreeViewAction.Unknown));
			}
		}

		public void Collapse()
		{
			if (Nodes._tree != null && Nodes._tree.InvokeRequired)
			{
				Nodes._tree.Invoke(new Action(Collapse));
				return;
			}
			if (IsExpanded)
			{
				IsExpanded = false;
				Nodes._tree?.UpdateFlattenedNodes();
				Nodes._tree?.OnAfterCollapse(new TreeViewEventArgs(this, TreeViewAction.Unknown));
			}
		}

		public void ExpandAll()
		{
			if (Nodes._tree != null && Nodes._tree.InvokeRequired)
			{
				Nodes._tree.Invoke(new Action(ExpandAll));
				return;
			}
			ExpandAllInternal(this);
			Nodes._tree?.UpdateFlattenedNodes();
		}

		private void ExpandAllInternal(TreeNode node)
		{
			if (!node.IsExpanded)
			{
				node.IsExpanded = true;
				Nodes._tree?.OnAfterExpand(new TreeViewEventArgs(node, TreeViewAction.Unknown));
			}
			foreach (TreeNode child in node.Nodes)
			{
				ExpandAllInternal(child);
			}
		}
	}
	#endregion

	#region Collections
	public class TreeNodeCollection(TreeNode? owner) : System.Collections.IEnumerable
	{
		private readonly List<TreeNode> _list = [];
		private readonly TreeNode? _owner = owner;
		internal CustomTreeView? _tree;
		private bool _lastSortByExt = false;
		private bool _sortAscending = true;

		internal void SetTree(CustomTreeView? tree)
		{
			_tree = tree;
			foreach (var item in _list)
			{
				item.Nodes.SetTree(tree);
			}
		}

		public int Count => _list.Count;
		public TreeNode this[int index] => _list[index];

		public void Clear()
		{
			if (_tree != null && _tree.InvokeRequired)
			{
				_tree.Invoke(new Action(Clear));
				return;
			}
			_list.Clear();
			_tree?.UpdateFlattenedNodes();
		}

		public void Add(TreeNode node)
		{
			if (_tree != null && _tree.InvokeRequired)
			{
				_tree.Invoke(new Action<TreeNode>(Add), node);
				return;
			}
			node.Parent = _owner;
			_list.Add(node);
			if (_tree != null)
			{
				node.Nodes.SetTree(_tree);
				_tree.UpdateFlattenedNodes();
			}
		}

		public void AddRange(TreeNode[] nodes)
		{
			if (_tree != null && _tree.InvokeRequired)
			{
				_tree.Invoke(new Action<TreeNode[]>(AddRange), [nodes]);
				return;
			}
			foreach (var node in nodes)
			{
				node.Parent = _owner;
				_list.Add(node);
				if (_tree != null) node.Nodes.SetTree(_tree);
			}
			_tree?.UpdateFlattenedNodes();
		}

		public void Sort(bool byExtension)
		{
			if (_tree != null && _tree.InvokeRequired)
			{
				_tree.Invoke(new Action<bool>(Sort), byExtension);
				return;
			}

			if (_lastSortByExt == byExtension)
			{
				_sortAscending = !_sortAscending;
			}
			else
			{
				_sortAscending = true;
				_lastSortByExt = byExtension;
			}

			_list.Sort((a, b) =>
			{
				if (a.IsFolder != b.IsFolder)
				{
					return a.IsFolder ? -1 : 1;
				}

				if (a.IsFolder && b.IsFolder)
				{
					return string.Compare(a.DisplayText, b.DisplayText, StringComparison.OrdinalIgnoreCase);
				}

				int result = 0;
				if (byExtension)
				{
					string extA = System.IO.Path.GetExtension(a.DisplayText);
					string extB = System.IO.Path.GetExtension(b.DisplayText);
					result = string.Compare(extA, extB, StringComparison.OrdinalIgnoreCase);
				}

				if (result == 0)
				{
					string nameA = System.IO.Path.GetFileNameWithoutExtension(a.DisplayText);
					string nameB = System.IO.Path.GetFileNameWithoutExtension(b.DisplayText);
					result = string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
				}

				return _sortAscending ? result : -result;
			});

			_tree?.UpdateFlattenedNodes();
		}

		public System.Collections.IEnumerator GetEnumerator() => _list.GetEnumerator();
	}
	#endregion

	#region Event Arguments
	public class TreeViewEventArgs(TreeNode? node, TreeViewAction action = TreeViewAction.Unknown) : EventArgs
	{
		public TreeNode? Node { get; } = node;
		public TreeViewAction Action { get; } = action;
	}

	public class TreeNodeMouseClickEventArgs(TreeNode? node, MouseButtons button, int clicks, int x, int y, int delta)
		: MouseEventArgs(button, clicks, x, y, delta)
	{
		public TreeNode? Node { get; } = node;
	}

	public class TreeViewCancelEventArgs(TreeNode? node, TreeViewAction action = TreeViewAction.Unknown) : System.ComponentModel.CancelEventArgs
	{
		public TreeNode? Node { get; } = node;
		public TreeViewAction Action { get; } = action;
	}
	#endregion

	#region CustomTreeView Control
	public partial class CustomTreeView : Control
	{
		#region Interop & Shared Constants
		// Shared colors
		private static readonly Color _colorIconDefault = UITheme.SideIcon;
		private static readonly Color _colorTextDefault = Color.FromArgb(164, 169, 179);

		[LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool SendMessage(IntPtr hWnd, int wMsg, [MarshalAs(UnmanagedType.Bool)] bool wParam, int lParam);
		private const int WM_SETREDRAW = 11;
		private const int WM_RBUTTONDOWN = 0x0204;
		#endregion

		#region Public Properties & Events
		public TreeViewDrawMode DrawMode { get; set; } = TreeViewDrawMode.OwnerDrawAll;
		public bool ShowLines { get; set; } = false;
		public bool HideSelection { get; set; } = false;
		public BorderStyle BorderStyle { get; set; } = BorderStyle.None;

		public event EventHandler<TreeNodeMouseClickEventArgs>? NodeMouseDoubleClick;
		public event EventHandler<TreeViewEventArgs>? AfterSelect;
		public event EventHandler<TreeNodeMouseClickEventArgs>? NodeMouseClick;
		public event EventHandler<TreeViewCancelEventArgs>? BeforeExpand;
		public event EventHandler<TreeViewCancelEventArgs>? BeforeCollapse;
		public event EventHandler<TreeViewEventArgs>? AfterExpand;
		public event EventHandler<TreeViewEventArgs>? AfterCollapse;
		public event EventHandler? SelectionChanged;

		public int ItemHeight
		{
			get => _itemHeight;
			set
			{
				if (_itemHeight != value)
				{
					_itemHeight = value;
					if (this.IsHandleCreated)
					{
						UpdateScrollbar();
						Invalidate();
					}
				}
			}
		}

		public TreeNodeCollection Nodes { get; }
		public List<TreeNode> SelectedNodes => [.. _selectedNodes];

		public TreeNode? SelectedNode
		{
			get
			{
				if (_selectedNodes.Count == 0) return null;
				foreach (var n in _selectedNodes) return n;
				return null;
			}
			set
			{
				_selectedNodes.Clear();
				if (value != null)
				{
					_selectedNodes.Add(value);
					EnsureVisible(value);
				}
				SelectionChanged?.Invoke(this, EventArgs.Empty);
				Invalidate();
				AfterSelect?.Invoke(this, new TreeViewEventArgs(value, TreeViewAction.ByMouse));
			}
		}
		#endregion

		#region Private Fields
		private readonly SolidBrush _bgSelectedBrush = new(UITheme.BgSelected);
		private readonly Pen _borderSelectedPen = new(UITheme.Accent);
		private readonly SolidBrush _bgNormalBrush = new(UITheme.BgPanel);
		private Pen _arrowPen = new(_colorIconDefault, 1.5f);
		private SolidBrush _arrowBrush = new(_colorIconDefault);

		private Font? _folderFont;

		private float _dpiScale = 1.0f;
		private int _itemHeight = 24;
		private int _indentWidth = 20;
		private int _baseIndent = 15;
		private int _folderIconWidth = 18;
		private int _fileIconWidth = 18;

		private TreeNode? _hoveredNode;

		private readonly List<TreeNode> _flatNodes = [];
		private readonly VScrollBar _vScrollBar;
		private int _updateCount = 0;

		private readonly HashSet<TreeNode> _selectedNodes = [];
		#endregion

		#region Constructor & Initialization
		public CustomTreeView()
		{
			this.BackColor = UITheme.BgPanel;
			this.ForeColor = _colorTextDefault;
			this.Font = UITheme.MainFont;

			this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.StandardDoubleClick, true);

			Nodes = new TreeNodeCollection(null);
			Nodes.SetTree(this);

			_vScrollBar = new VScrollBar { Dock = DockStyle.Right };
			_vScrollBar.Scroll += (s, e) => Invalidate();
			this.Controls.Add(_vScrollBar);

			RecalculateScaling();
		}

		protected override void OnFontChanged(EventArgs e)
		{
			base.OnFontChanged(e);
			RecalculateScaling();
		}

		private void RecalculateScaling()
		{
			_dpiScale = this.DeviceDpi / 96f;
			if (_dpiScale < 1.0f) _dpiScale = 1.0f;

			_itemHeight = (int)(24 * _dpiScale);
			_indentWidth = (int)(20 * _dpiScale);
			_baseIndent = (int)(15 * _dpiScale);
			_folderIconWidth = (int)(18 * _dpiScale);
			_fileIconWidth = (int)(18 * _dpiScale);

			_arrowPen.Dispose();
			_arrowPen = new Pen(_colorIconDefault, 1.5f * _dpiScale);

			_arrowBrush.Dispose();
			_arrowBrush = new SolidBrush(_colorIconDefault);

			_folderFont?.Dispose();
			_folderFont = new Font(UITheme.EmojiFontFamily, UITheme.MainFont.Size, FontStyle.Regular);

			IconControl.UpdateDpiScale(_dpiScale);

			UpdateScrollbar();
			Invalidate();
		}
		#endregion

		#region Tree State Management (Update, Expand, Scroll)
		public void BeginUpdate()
		{
			if (_updateCount == 0 && this.IsHandleCreated)
			{
				_ = SendMessage(this.Handle, WM_SETREDRAW, false, 0);
			}
			_updateCount++;
		}

		public void EndUpdate()
		{
			_updateCount--;
			if (_updateCount <= 0)
			{
				_updateCount = 0;
				UpdateFlattenedNodes();
				if (this.IsHandleCreated)
				{
					_ = SendMessage(this.Handle, WM_SETREDRAW, true, 0);
					this.Invalidate();
				}
			}
		}

		internal void OnAfterExpand(TreeViewEventArgs e)
		{
			AfterExpand?.Invoke(this, e);
		}

		internal void OnAfterCollapse(TreeViewEventArgs e)
		{
			AfterCollapse?.Invoke(this, e);
		}

		public void ExpandAll()
		{
			if (this.InvokeRequired)
			{
				this.Invoke(new Action(ExpandAll));
				return;
			}
			void ExpandNode(TreeNode n)
			{
				if (!n.IsExpanded)
				{
					n.IsExpanded = true;
					OnAfterExpand(new TreeViewEventArgs(n, TreeViewAction.Unknown));
				}
				foreach (TreeNode child in n.Nodes) ExpandNode(child);
			}
			foreach (TreeNode n in Nodes) ExpandNode(n);
			UpdateFlattenedNodes();
		}

		public void CollapseAll()
		{
			if (this.InvokeRequired)
			{
				this.Invoke(new Action(CollapseAll));
				return;
			}
			void CollapseNode(TreeNode n)
			{
				if (n.IsExpanded)
				{
					n.IsExpanded = false;
					OnAfterCollapse(new TreeViewEventArgs(n, TreeViewAction.Unknown));
				}
				foreach (TreeNode child in n.Nodes) CollapseNode(child);
			}
			foreach (TreeNode n in Nodes) CollapseNode(n);
			UpdateFlattenedNodes();
		}

		internal void UpdateFlattenedNodes()
		{
			if (_updateCount > 0) return;
			if (this.InvokeRequired)
			{
				this.BeginInvoke(new Action(UpdateFlattenedNodes));
				return;
			}

			_flatNodes.Clear();
			void AddVisible(TreeNode n)
			{
				_flatNodes.Add(n);
				if (n.IsExpanded)
				{
					foreach (TreeNode child in n.Nodes) AddVisible(child);
				}
			}

			foreach (TreeNode n in Nodes) AddVisible(n);

			UpdateScrollbar();
			Invalidate();
		}

		private void UpdateScrollbar()
		{
			if (_itemHeight <= 0 || !this.IsHandleCreated) return;

			int visibleCount = this.Height / _itemHeight;
			bool shouldBeVisible = _flatNodes.Count > visibleCount;

			bool visibilityChanged = (_vScrollBar.Visible != shouldBeVisible);

			if (visibilityChanged)
			{
				_ = SendMessage(this.Handle, WM_SETREDRAW, false, 0);
				_vScrollBar.Visible = shouldBeVisible;
			}

			if (shouldBeVisible)
			{
				_vScrollBar.Maximum = Math.Max(0, _flatNodes.Count - 1);
				_vScrollBar.LargeChange = Math.Max(1, visibleCount);
				_vScrollBar.SmallChange = 1;

				if (_vScrollBar.Value > _vScrollBar.Maximum - _vScrollBar.LargeChange + 1)
				{
					_vScrollBar.Value = Math.Max(0, _vScrollBar.Maximum - _vScrollBar.LargeChange + 1);
				}
			}
			else
			{
				_vScrollBar.Value = 0;
			}

			if (visibilityChanged)
			{
				_ = SendMessage(this.Handle, WM_SETREDRAW, true, 0);
			}

			this.Invalidate();
		}

		public void EnsureVisible(TreeNode? node)
		{
			if (node == null) return;
			int index = _flatNodes.IndexOf(node);
			if (index >= 0)
			{
				int visibleCount = this.Height / _itemHeight;
				if (index < _vScrollBar.Value)
				{
					_vScrollBar.Value = index;
				}
				else if (index >= _vScrollBar.Value + visibleCount)
				{
					int target = index - visibleCount + 1;
					int maxVal = _vScrollBar.Maximum - _vScrollBar.LargeChange + 1;
					_vScrollBar.Value = Math.Min(target, Math.Max(0, maxVal));
				}
			}
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			UpdateScrollbar();
		}
		#endregion

		#region Input Events (Keyboard & Mouse)
		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);
			if (_vScrollBar.Visible)
			{
				int newValue = _vScrollBar.Value - (e.Delta / 120) * 3;
				int maxVal = _vScrollBar.Maximum - _vScrollBar.LargeChange + 1;
				if (maxVal < 0) maxVal = 0;

				if (newValue < 0) newValue = 0;
				if (newValue > maxVal) newValue = maxVal;

				_vScrollBar.Value = newValue;
				Invalidate();
			}
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (e.Control && e.KeyCode == Keys.A)
			{
				_selectedNodes.Clear();
				foreach (var node in _flatNodes)
				{
					_selectedNodes.Add(node);
				}
				SelectionChanged?.Invoke(this, EventArgs.Empty);
				Invalidate();
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			int visibleWidth = this.Width - (_vScrollBar.Visible ? _vScrollBar.Width : 0);
			if (e.X > visibleWidth) return;

			int hoveredRow = _vScrollBar.Value + (e.Y / _itemHeight);
			if (hoveredRow >= 0 && hoveredRow < _flatNodes.Count)
			{
				TreeNode node = _flatNodes[hoveredRow];
				if (_hoveredNode != node)
				{
					_hoveredNode = node;
					Invalidate();
				}
			}
			else
			{
				if (_hoveredNode != null)
				{
					_hoveredNode = null;
					Invalidate();
				}
			}
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			if (_hoveredNode != null)
			{
				_hoveredNode = null;
				Invalidate();
			}
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			base.OnMouseDoubleClick(e);
			int visibleWidth = this.Width - (_vScrollBar.Visible ? _vScrollBar.Width : 0);
			if (e.X > visibleWidth) return;

			int clickedRow = _vScrollBar.Value + (e.Y / _itemHeight);
			if (clickedRow >= 0 && clickedRow < _flatNodes.Count)
			{
				TreeNode clickedNode = _flatNodes[clickedRow];

				if (!clickedNode.IsFolder)
				{
					int indent = clickedNode.Level * _indentWidth + _baseIndent + _fileIconWidth;
					string baseName = System.IO.Path.GetFileNameWithoutExtension(clickedNode.DisplayText);

					{
						using Graphics g = this.CreateGraphics();
						Size nameSize = TextRenderer.MeasureText(g, baseName, this.Font, new Size(int.MaxValue, _itemHeight), TextFormatFlags.NoPadding);
						bool doubleClickedExt = (e.X > indent + nameSize.Width);

						TreeNodeCollection parentCollection = clickedNode.Parent != null ? clickedNode.Parent.Nodes : this.Nodes;
						parentCollection.Sort(doubleClickedExt);
					}
				}

				if (clickedNode.Nodes.Count > 0 || clickedNode.IsFolder)
				{
					if (!clickedNode.IsExpanded)
					{
						var args = new TreeViewCancelEventArgs(clickedNode);
						BeforeExpand?.Invoke(this, args);
						if (!args.Cancel)
						{
							clickedNode.IsExpanded = true;
							UpdateFlattenedNodes();
							OnAfterExpand(new TreeViewEventArgs(clickedNode, TreeViewAction.ByMouse));
						}
					}
					else
					{
						var args = new TreeViewCancelEventArgs(clickedNode);
						BeforeCollapse?.Invoke(this, args);
						if (!args.Cancel)
						{
							clickedNode.IsExpanded = false;
							UpdateFlattenedNodes();
							OnAfterCollapse(new TreeViewEventArgs(clickedNode, TreeViewAction.ByMouse));
						}
					}
				}

				NodeMouseDoubleClick?.Invoke(this, new TreeNodeMouseClickEventArgs(clickedNode, e.Button, e.Clicks, e.X, e.Y, e.Delta));
			}
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			this.Focus();
			int visibleWidth = this.Width - (_vScrollBar.Visible ? _vScrollBar.Width : 0);
			if (e.X > visibleWidth) return;

			int clickedRow = _vScrollBar.Value + (e.Y / _itemHeight);
			if (clickedRow >= 0 && clickedRow < _flatNodes.Count)
			{
				TreeNode clickedNode = _flatNodes[clickedRow];

				int indent = clickedNode.Level * _indentWidth + _baseIndent;
				int clickZoneWidth = (int)(20 * _dpiScale);

				if (e.X >= indent - clickZoneWidth && e.X <= indent && clickedNode.Nodes.Count > 0)
				{
					if (!clickedNode.IsExpanded)
					{
						var args = new TreeViewCancelEventArgs(clickedNode);
						BeforeExpand?.Invoke(this, args);
						if (!args.Cancel)
						{
							clickedNode.IsExpanded = true;
							UpdateFlattenedNodes();
							OnAfterExpand(new TreeViewEventArgs(clickedNode, TreeViewAction.ByMouse));
						}
					}
					else
					{
						var args = new TreeViewCancelEventArgs(clickedNode);
						BeforeCollapse?.Invoke(this, args);
						if (!args.Cancel)
						{
							clickedNode.IsExpanded = false;
							UpdateFlattenedNodes();
							OnAfterCollapse(new TreeViewEventArgs(clickedNode, TreeViewAction.ByMouse));
						}
					}
					base.OnMouseDown(e);
					return;
				}

				if (e.Button == MouseButtons.Right)
				{
					if (!_selectedNodes.Contains(clickedNode))
					{
						_selectedNodes.Clear();
						_selectedNodes.Add(clickedNode);
						SelectionChanged?.Invoke(this, EventArgs.Empty);
						Invalidate();
						AfterSelect?.Invoke(this, new TreeViewEventArgs(clickedNode, TreeViewAction.ByMouse));
					}

					NodeMouseClick?.Invoke(this, new TreeNodeMouseClickEventArgs(clickedNode, e.Button, e.Clicks, e.X, e.Y, e.Delta));
					return;
				}

				if (e.Button == MouseButtons.Left)
				{

					if (e.Clicks == 2)
					{
						base.OnMouseDown(e);
						return;
					}

					bool ctrlPressed = (ModifierKeys & Keys.Control) == Keys.Control;
					bool shiftPressed = (ModifierKeys & Keys.Shift) == Keys.Shift;

					if (shiftPressed && _flatNodes.Count > 0)
					{
						int anchorIndex = 0;
						if (_selectedNodes.Count > 0)
						{
							TreeNode[] currentSelected = new TreeNode[_selectedNodes.Count];
							_selectedNodes.CopyTo(currentSelected, 0);
							anchorIndex = _flatNodes.IndexOf(currentSelected[0]);
						}

						if (anchorIndex < 0) anchorIndex = 0;
						int startRow = Math.Min(anchorIndex, clickedRow);
						int endRow = Math.Max(anchorIndex, clickedRow);

						if (!ctrlPressed) _selectedNodes.Clear();

						for (int r = startRow; r <= endRow; r++)
						{
							_selectedNodes.Add(_flatNodes[r]);
						}
					}
					else if (ctrlPressed)
					{
						if (!_selectedNodes.Remove(clickedNode))
						{
							_selectedNodes.Add(clickedNode);
						}
					}
					else
					{
						_selectedNodes.Clear();
						_selectedNodes.Add(clickedNode);
					}

					SelectionChanged?.Invoke(this, EventArgs.Empty);
					Invalidate();
					AfterSelect?.Invoke(this, new TreeViewEventArgs(clickedNode, TreeViewAction.ByMouse));
					NodeMouseClick?.Invoke(this, new TreeNodeMouseClickEventArgs(clickedNode, e.Button, e.Clicks, e.X, e.Y, e.Delta));
				}
			}

			base.OnMouseDown(e);
		}
		#endregion

		#region Rendering & Painting
		protected override void WndProc(ref Message m)
		{
			if (m.Msg == WM_RBUTTONDOWN)
			{
				this.Focus();
			}
			base.WndProc(ref m);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics g = e.Graphics;
			g.FillRectangle(_bgNormalBrush, this.ClientRectangle);

			if (_itemHeight <= 0) return;
			int visibleCount = (this.Height / _itemHeight) + 1;
			int start = _vScrollBar.Value;
			int end = Math.Min(_flatNodes.Count, start + visibleCount);
			int viewWidth = this.Width - (_vScrollBar.Visible ? _vScrollBar.Width : 0);

			bool useIcons = true;
			try { useIcons = !AppSettings.Current.LiteMode && AppSettings.Current.EnableCustomIcons; } catch { }

			for (int i = start; i < end; i++)
			{
				TreeNode node = _flatNodes[i];
				int y = (i - start) * _itemHeight;
				Rectangle rowRect = new(0, y, viewWidth, _itemHeight);

				bool isSelected = _selectedNodes.Contains(node);
				bool isHovered = (_hoveredNode == node);
				Color bgFill = _bgNormalBrush.Color;

				if (isSelected)
				{
					g.FillRectangle(_bgSelectedBrush, rowRect);
					g.DrawRectangle(_borderSelectedPen, rowRect.X, rowRect.Y, rowRect.Width - 1, rowRect.Height - 1);
					bgFill = _bgSelectedBrush.Color;
				}

				int indent = node.Level * _indentWidth + _baseIndent;

				if (node.Nodes.Count > 0)
				{
					DrawCollapseExpandArrow(g, indent, y + (_itemHeight / 2), node.IsExpanded);
				}

				int currentX = indent + (int)(1 * _dpiScale);
				int iconYOffset = (y + (_itemHeight / 2)) - (int)(8 * _dpiScale);
				var originalTransform = g.Transform;

				if (useIcons && node.CustomIconRenderer != null)
				{
					g.TranslateTransform(currentX, iconYOffset);
					g.ScaleTransform(_dpiScale, _dpiScale);

					node.CustomIconRenderer.Draw(g, 0, 0, bgFill);

					g.Transform = originalTransform;
					currentX += (int)(node.CustomIconRenderer.BaseAdvanceWidth * _dpiScale);
				}
				else if (node.IsFolder)
				{
					Font fontToUse = _folderFont ?? this.Font;
					string folderIconStr = !string.IsNullOrEmpty(node.IconPart) ? node.IconPart : "📁";

					Size emojiSize = TextRenderer.MeasureText(g, folderIconStr, fontToUse,
						new Size(int.MaxValue, _itemHeight),
						TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

					int folderWidth = emojiSize.Width;
					int folderY = y + (_itemHeight - emojiSize.Height) / 2;
					folderY -= (int)(2 * _dpiScale);

					Rectangle folderRect = new(currentX, folderY, folderWidth, emojiSize.Height);

					TextRenderer.DrawText(g, folderIconStr, fontToUse, folderRect, UITheme.FolderIcon, bgFill,
						TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping);

					currentX += folderWidth;
				}
				else if (useIcons && !string.IsNullOrEmpty(node.IconPart))
				{
					Color iconColor = node.CustomIconColor ?? this.ForeColor;
					Rectangle iconRect = new(currentX, y, (int)(30 * _dpiScale), _itemHeight);

					TextRenderer.DrawText(g, node.IconPart, this.Font, iconRect, iconColor, bgFill,
						TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping);

					currentX += _fileIconWidth;
				}

				Rectangle textRect = new(currentX, y, Math.Max(10, viewWidth - currentX), _itemHeight);
				Color textColor = (isSelected || isHovered) ? Color.White : this.ForeColor;

				TextRenderer.DrawText(g, node.DisplayText, this.Font, textRect, textColor, bgFill,
					TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoClipping);
			}
		}

		private void DrawCollapseExpandArrow(Graphics g, int indent, int arrowY, bool isExpanded)
		{
			int arrowX = indent - (int)(10 * _dpiScale);
			int size = (int)(4f * _dpiScale);

			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

			if (isExpanded)
			{
				Point[] points =
				[
					new Point(arrowX - size, arrowY),
					new Point(arrowX + size, arrowY),
					new Point(arrowX, arrowY + size)
				];

				g.FillPolygon(_arrowBrush, points);
			}
			else
			{
				int rightX = arrowX + size;

				g.DrawLine(_arrowPen, arrowX, arrowY - size, rightX, arrowY);
				g.DrawLine(_arrowPen, rightX, arrowY, arrowX, arrowY + size);
			}

			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
		}
		#endregion

		#region Cleanup
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_bgSelectedBrush?.Dispose();
				_borderSelectedPen?.Dispose();
				_bgNormalBrush?.Dispose();
				_arrowPen?.Dispose();
				_arrowBrush?.Dispose();
				_folderFont?.Dispose();
			}
			base.Dispose(disposing);
		}
		#endregion
	}
	#endregion
}