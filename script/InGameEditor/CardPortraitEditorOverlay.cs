using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace CustomCardPortraits.script.InGameEditor;

public sealed partial class CardPortraitEditorOverlay : CanvasLayer
{
	private const string OverlayNodeName = "CustomCardPortraits_PortraitEditorOverlay";
	private const string OverrideRootVirtual = "user://CustomCardPortraits/images/packed/card_portraits";
	private const string OriginalRootRes = "res://images/packed/card_portraits";
	private const string AtlasSpritesRootRes = "res://images/atlases/card_atlas.sprites";

	private Control _root = null!;
	private Label _status = null!;
	private OptionButton _poolSelect = null!;
	private OptionButton _cardSelect = null!;
	private TextureRect _originalPreview = null!;
	private TextureRect _workPreview = null!;
	private CropOverlay _cropOverlay = null!;
	private FileDialog _fileDialog = null!;

	private readonly Dictionary<string, List<string>> _cardsByPool = new(StringComparer.OrdinalIgnoreCase);
	private string? _currentPool;
	private string? _currentCard;
	private bool _openOnReady;

	public static void ShowEditor(SceneTree tree)
	{
		if (tree?.Root == null)
			return;

		var existing = tree.Root.GetNodeOrNull<CardPortraitEditorOverlay>(OverlayNodeName);
		if (existing == null)
		{
			existing = new CardPortraitEditorOverlay
			{
				Name = OverlayNodeName,
				Layer = 200,
				Visible = true,
				_openOnReady = true,
			};
			tree.Root.AddChild(existing);
			return;
		}

		if (existing.IsNodeReady())
			existing.Open();
		else
			existing._openOnReady = true;
	}

	public override void _Ready()
	{
		BuildUi();
		ReloadAtlasIndex();
		SetStatus("选择卡牌 → 选择图片 → 拖拽框选裁切 → 保存。ESC 关闭。右键清除选区。", isError: false);
		if (_openOnReady)
		{
			_openOnReady = false;
			Open();
		}
		else
		{
			Close();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_root.Visible)
			return;

		if (@event is InputEventKey k && k.Pressed && k.Keycode == Key.Escape)
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	private void Open()
	{
		Visible = true;
		_root.Visible = true;
		RefreshPreviews();
		_poolSelect?.GrabFocus();
	}

	private void Close()
	{
		_root.Visible = false;
		Visible = false;
	}

	private void BuildUi()
	{
		_root = new Control { Name = "Root" };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.SetOffsetsPreset(Control.LayoutPreset.FullRect);
		_root.MouseFilter = Control.MouseFilterEnum.Stop;
		AddChild(_root);

		var bg = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.65f),
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		bg.SetOffsetsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(bg);

		var panel = new PanelContainer { Name = "Panel" };
		panel.SetAnchorsPreset(Control.LayoutPreset.Center);
		panel.Size = new Vector2(1400, 860);
		panel.Position = -panel.Size / 2;
		panel.MouseFilter = Control.MouseFilterEnum.Stop;
		_root.AddChild(panel);

		var vbox = new VBoxContainer { Name = "VBox" };
		vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		vbox.SetOffsetsPreset(Control.LayoutPreset.FullRect);
		vbox.AddThemeConstantOverride("separation", 10);
		panel.AddChild(vbox);

		var top = new HBoxContainer { Name = "TopBar" };
		top.AddThemeConstantOverride("separation", 10);
		vbox.AddChild(top);

		top.AddChild(new Label { Text = "Pool:" });
		_poolSelect = new OptionButton { Name = "PoolSelect", CustomMinimumSize = new Vector2(220, 0) };
		top.AddChild(_poolSelect);

		top.AddChild(new Label { Text = "Card:" });
		_cardSelect = new OptionButton { Name = "CardSelect", CustomMinimumSize = new Vector2(320, 0) };
		top.AddChild(_cardSelect);

		var pickBtn = new Button { Name = "PickImage", Text = "选择图片…", CustomMinimumSize = new Vector2(160, 0) };
		var saveBtn = new Button { Name = "Save", Text = "裁切并保存", CustomMinimumSize = new Vector2(160, 0) };
		var closeBtn = new Button { Name = "Close", Text = "关闭(ESC)", CustomMinimumSize = new Vector2(140, 0) };
		top.AddChild(pickBtn);
		top.AddChild(saveBtn);
		top.AddChild(closeBtn);

		var previews = new HBoxContainer { Name = "Previews" };
		previews.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		previews.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		previews.AddThemeConstantOverride("separation", 10);
		vbox.AddChild(previews);

		var left = new VBoxContainer { Name = "OriginalBox" };
		left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		left.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		previews.AddChild(left);

		var right = new VBoxContainer { Name = "WorkBox" };
		right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		right.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		previews.AddChild(right);

		left.AddChild(new Label { Text = "原图（游戏资源）" });
		right.AddChild(new Label { Text = "覆盖/工作图（拖拽框选裁切）" });

		_originalPreview = new TextureRect
		{
			Name = "OriginalPreview",
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		left.AddChild(_originalPreview);

		var workStack = new Control
		{
			Name = "WorkStack",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		right.AddChild(workStack);

		_workPreview = new TextureRect
		{
			Name = "WorkPreview",
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_workPreview.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_workPreview.SetOffsetsPreset(Control.LayoutPreset.FullRect);
		workStack.AddChild(_workPreview);

		_cropOverlay = new CropOverlay(_workPreview);
		_cropOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_cropOverlay.SetOffsetsPreset(Control.LayoutPreset.FullRect);
		workStack.AddChild(_cropOverlay);

		_status = new Label { Name = "Status", AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_status.CustomMinimumSize = new Vector2(0, 26);
		vbox.AddChild(_status);

		_fileDialog = new FileDialog
		{
			Name = "FileDialog",
			Access = FileDialog.AccessEnum.Filesystem,
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Title = "选择要导入的图片"
		};
		_fileDialog.Filters = new string[] { "*.png ; PNG", "*.jpg, *.jpeg ; JPEG" };
		_root.AddChild(_fileDialog);

		_poolSelect.ItemSelected += OnPoolSelected;
		_cardSelect.ItemSelected += OnCardSelected;
		pickBtn.Pressed += () => _fileDialog.PopupCentered(new Vector2I(900, 600));
		saveBtn.Pressed += SaveOverride;
		closeBtn.Pressed += Close;
		_fileDialog.FileSelected += OnFileSelected;
	}

	private void ReloadAtlasIndex()
	{
		_cardsByPool.Clear();

		var dir = DirAccess.Open(AtlasSpritesRootRes);
		if (dir == null)
		{
			SetStatus($"无法打开资源目录: {AtlasSpritesRootRes}", isError: true);
			return;
		}

		dir.ListDirBegin();
		while (true)
		{
			string name = dir.GetNext();
			if (string.IsNullOrEmpty(name))
				break;
			if (!dir.CurrentIsDir())
				continue;
			if (name.StartsWith('.'))
				continue;

			string pool = name;
			string poolPath = $"{AtlasSpritesRootRes}/{name}";
			var poolDir = DirAccess.Open(poolPath);
			if (poolDir == null)
				continue;

			var cards = new List<string>();
			poolDir.ListDirBegin();
			while (true)
			{
				string f = poolDir.GetNext();
				if (string.IsNullOrEmpty(f))
					break;
				if (poolDir.CurrentIsDir())
					continue;
				if (!f.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
					continue;
				string id = f[..^5];
				if (string.Equals(id, "beta", StringComparison.OrdinalIgnoreCase))
					continue;
				cards.Add(id);
			}
			poolDir.ListDirEnd();

			cards.Sort(StringComparer.OrdinalIgnoreCase);
			if (cards.Count > 0)
				_cardsByPool[pool] = cards;
		}
		dir.ListDirEnd();

		_poolSelect.Clear();
		foreach (string pool in _cardsByPool.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
			_poolSelect.AddItem(pool);

		if (_poolSelect.ItemCount > 0)
		{
			_poolSelect.Select(0);
			OnPoolSelected(0);
		}
		else
		{
			SetStatus("未扫描到任何卡牌资源。", isError: true);
		}
	}

	private void OnPoolSelected(long index)
	{
		_currentPool = _poolSelect.GetItemText((int)index);
		_cardSelect.Clear();

		if (_cardsByPool.TryGetValue(_currentPool, out List<string>? cards))
		{
			foreach (var c in cards)
				_cardSelect.AddItem(c);
		}

		if (_cardSelect.ItemCount > 0)
		{
			_cardSelect.Select(0);
			OnCardSelected(0);
		}
		else
		{
			_currentCard = null;
			RefreshPreviews();
		}
	}

	private void OnCardSelected(long index)
	{
		_currentCard = _cardSelect.GetItemText((int)index);
		RefreshPreviews();
	}

	private void RefreshPreviews()
	{
		_cropOverlay.ClearSelection();
		_originalPreview.Texture = null;
		_workPreview.Texture = null;
		_cropOverlay.SetWorkImage(null);

		if (string.IsNullOrWhiteSpace(_currentPool) || string.IsNullOrWhiteSpace(_currentCard))
			return;

		string pool = _currentPool!.ToLowerInvariant();
		string id = _currentCard!.ToLowerInvariant();

		string originalPath = $"{OriginalRootRes}/{pool}/{id}.png";
		if (ResourceLoader.Exists(originalPath))
			_originalPreview.Texture = ResourceLoader.Load<Texture2D>(originalPath);

		string overrideVirtual = $"{OverrideRootVirtual}/{pool}/{id}.png";
		string overrideAbs = ProjectSettings.GlobalizePath(overrideVirtual);
		if (File.Exists(overrideAbs))
		{
			var img = new Image();
			var err = img.Load(overrideAbs);
			if (err == Error.Ok)
			{
				_cropOverlay.SetWorkImage(img);
				_workPreview.Texture = ImageTexture.CreateFromImage(img);
			}
		}
	}

	private void OnFileSelected(string path)
	{
		try
		{
			var img = new Image();
			var err = img.Load(path);
			if (err != Error.Ok)
			{
				SetStatus($"加载图片失败: {err}", isError: true);
				return;
			}

			_cropOverlay.SetWorkImage(img);
			_workPreview.Texture = ImageTexture.CreateFromImage(img);
			SetStatus("已加载工作图：在右侧预览拖拽框选裁切，然后点击“裁切并保存”。", isError: false);
		}
		catch (Exception ex)
		{
			SetStatus($"加载失败: {ex.Message}", isError: true);
		}
	}

	private void SaveOverride()
	{
		if (string.IsNullOrWhiteSpace(_currentPool) || string.IsNullOrWhiteSpace(_currentCard))
		{
			SetStatus("请先选择卡牌。", isError: true);
			return;
		}

		Image? work = _cropOverlay.GetWorkImage();
		if (work == null)
		{
			SetStatus("没有工作图。请先“选择图片…”。", isError: true);
			return;
		}

		string pool = _currentPool!.ToLowerInvariant();
		string id = _currentCard!.ToLowerInvariant();
		string outVirtual = $"{OverrideRootVirtual}/{pool}/{id}.png";
		string outAbs = ProjectSettings.GlobalizePath(outVirtual);

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(outAbs)!);

			Rect2I? crop = _cropOverlay.GetCropRect();
			Image toSave = work;
			if (crop.HasValue)
			{
				Rect2I r = ClampRect(crop.Value, work.GetWidth(), work.GetHeight());
				if (r.Size.X > 0 && r.Size.Y > 0)
					toSave = work.GetRegion(r);
			}

			var err = toSave.SavePng(outAbs);
			if (err != Error.Ok)
			{
				SetStatus($"保存失败: {err}", isError: true);
				return;
			}

			SetStatus($"已保存: {outVirtual}", isError: false);
			RefreshPreviews();
		}
		catch (Exception ex)
		{
			SetStatus($"保存失败: {ex.Message}", isError: true);
		}
	}

	private static Rect2I ClampRect(Rect2I r, int w, int h)
	{
		int x = Math.Clamp(r.Position.X, 0, Math.Max(0, w - 1));
		int y = Math.Clamp(r.Position.Y, 0, Math.Max(0, h - 1));
		int rw = Math.Clamp(r.Size.X, 0, w - x);
		int rh = Math.Clamp(r.Size.Y, 0, h - y);
		return new Rect2I(x, y, rw, rh);
	}

	private void SetStatus(string text, bool isError)
	{
		_status.Text = text;
		_status.Modulate = isError ? new Color(1f, 0.4f, 0.4f) : Colors.White;
	}

	private sealed partial class CropOverlay : Control
	{
		private readonly TextureRect _target;
		private Image? _work;
		private bool _dragging;
		private bool _hasSelection;
		private Vector2I _start;
		private Vector2I _end;

		public CropOverlay(TextureRect target)
		{
			_target = target;
			MouseFilter = MouseFilterEnum.Stop;
		}

		public void SetWorkImage(Image? img)
		{
			_work = img;
			_hasSelection = false;
			_dragging = false;
			QueueRedraw();
		}

		public Image? GetWorkImage() => _work;

		public void ClearSelection()
		{
			_hasSelection = false;
			_dragging = false;
			QueueRedraw();
		}

		public Rect2I? GetCropRect()
		{
			if (!_hasSelection)
				return null;
			var r = Normalize(_start, _end);
			if (r.Size.X < 2 || r.Size.Y < 2)
				return null;
			return r;
		}

		public override void _GuiInput(InputEvent @event)
		{
			if (_work == null)
				return;

			if (@event is InputEventMouseButton mb)
			{
				if (mb.ButtonIndex == MouseButton.Left)
				{
					if (mb.Pressed)
					{
						if (TryMapToImage(mb.Position, out Vector2I p))
						{
							_dragging = true;
							_hasSelection = true;
							_start = p;
							_end = p;
							QueueRedraw();
						}
					}
					else
					{
						_dragging = false;
						QueueRedraw();
					}
				}
				else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
				{
					_hasSelection = false;
					QueueRedraw();
				}
			}
			else if (@event is InputEventMouseMotion mm)
			{
				if (_dragging && TryMapToImage(mm.Position, out Vector2I p))
				{
					_end = p;
					QueueRedraw();
				}
			}
		}

		public override void _Draw()
		{
			if (_work == null || !_hasSelection)
				return;

			if (!TryGetDisplayRect(out Rect2 disp, out float scale))
				return;

			Rect2I rImg = Normalize(_start, _end);
			Vector2 pos = disp.Position + new Vector2(rImg.Position.X * scale, rImg.Position.Y * scale);
			Vector2 size = new Vector2(rImg.Size.X * scale, rImg.Size.Y * scale);
			DrawRect(new Rect2(pos, size), new Color(1, 0, 0, 0.12f), filled: true);
			DrawRect(new Rect2(pos, size), Colors.Red, filled: false, width: 2);
		}

		private bool TryMapToImage(Vector2 localPos, out Vector2I p)
		{
			p = default;
			if (_work == null)
				return false;
			if (!TryGetDisplayRect(out Rect2 disp, out float scale))
				return false;
			if (!disp.HasPoint(localPos))
				return false;

			Vector2 rel = localPos - disp.Position;
			int x = (int)Mathf.Floor(rel.X / scale);
			int y = (int)Mathf.Floor(rel.Y / scale);
			x = Math.Clamp(x, 0, _work.GetWidth() - 1);
			y = Math.Clamp(y, 0, _work.GetHeight() - 1);
			p = new Vector2I(x, y);
			return true;
		}

		private bool TryGetDisplayRect(out Rect2 rect, out float scale)
		{
			rect = default;
			scale = 1;
			if (_work == null)
				return false;

			Vector2 size = _target.Size;
			if (size.X <= 1 || size.Y <= 1)
				return false;

			float imgW = _work.GetWidth();
			float imgH = _work.GetHeight();
			if (imgW <= 0 || imgH <= 0)
				return false;

			scale = MathF.Min(size.X / imgW, size.Y / imgH);
			Vector2 drawSize = new Vector2(imgW * scale, imgH * scale);
			Vector2 offset = (size - drawSize) * 0.5f;
			rect = new Rect2(offset, drawSize);
			return true;
		}

		private static Rect2I Normalize(Vector2I a, Vector2I b)
		{
			int x1 = Math.Min(a.X, b.X);
			int y1 = Math.Min(a.Y, b.Y);
			int x2 = Math.Max(a.X, b.X);
			int y2 = Math.Max(a.Y, b.Y);
			return new Rect2I(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
		}
	}
}
