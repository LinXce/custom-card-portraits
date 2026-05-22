using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace CustomCardPortraits.script.InGameEditor;

public sealed partial class CardPortraitEditorOverlay : CanvasLayer
{
	private const string OverlayNodeName = "CustomCardPortraits_PortraitEditorOverlay";
	private const string OverrideRootVirtual = "user://CustomCardPortraits/images/packed/card_portraits";
	private const string OriginalRootRes = "res://images/packed/card_portraits";
	private const string AtlasSpritesRootRes = "res://images/atlases/card_atlas.sprites";

	private Control _root = null!;
	private Control _panel = null!;
	private HFlowContainer _topBar = null!;
	private Label _status = null!;
	private OptionButton _poolSelect = null!;
	private OptionButton _cardSelect = null!;
	private NCard _originalCardPreview = null!;
	private NCard _overrideCardPreview = null!;
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
		UpdateLayout();
		GetViewport().Connect(Viewport.SignalName.SizeChanged, Callable.From(UpdateLayout));
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
		UpdateLayout();
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
			Color = StsColors.screenBackdrop,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		bg.SetOffsetsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(bg);

		_panel = new Control { Name = "Panel" };
		_panel.SetAnchorsPreset(Control.LayoutPreset.Center);
		_panel.Size = GetPanelSize(GetViewport().GetVisibleRect().Size);
		_panel.Position = -_panel.Size / 2;
        // _panel.Position = new Vector2(0, 0);
		_panel.MouseFilter = Control.MouseFilterEnum.Stop;
		_root.AddChild(_panel);

		var panelBg = new TextureRect
		{
			Name = "PanelBg",
			Texture = GD.Load<Texture2D>("res://asset/image/bg.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = Control.MouseFilterEnum.Ignore,
            Scale = new Vector2(1.2f, 1.2f)
		};
		panelBg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		panelBg.SetOffsetsPreset(Control.LayoutPreset.FullRect);
        panelBg.Position = new Vector2(-160, -100);
		_panel.AddChild(panelBg);

		var vbox = new VBoxContainer { Name = "VBox" };
		vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		vbox.SetOffsetsPreset(Control.LayoutPreset.FullRect);
		vbox.AddThemeConstantOverride("separation", 10);
		_panel.AddChild(vbox);

		_topBar = new HFlowContainer { Name = "TopBar" };
		_topBar.AddThemeConstantOverride("separation", 10);
		_topBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_topBar.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		vbox.AddChild(_topBar);

		var poolLabel = new Label { Text = "Pool:" };
		ApplyGameLabelStyle(poolLabel, 22);
		_topBar.AddChild(poolLabel);
		_poolSelect = new OptionButton { Name = "PoolSelect", CustomMinimumSize = new Vector2(220, 0) };
		ApplyGameOptionStyle(_poolSelect, 22);
		_topBar.AddChild(_poolSelect);

		var cardLabel = new Label { Text = "Card:" };
		ApplyGameLabelStyle(cardLabel, 22);
		_topBar.AddChild(cardLabel);
		_cardSelect = new OptionButton { Name = "CardSelect", CustomMinimumSize = new Vector2(220, 0) };
		ApplyGameOptionStyle(_cardSelect, 22);
		_topBar.AddChild(_cardSelect);

		var pickBtn = CreateGameButton("选择图片…", "PickImage");
		var resetBtn = CreateGameButton("重置", "Reset");
		var saveBtn = CreateGameButton("裁切并保存", "Save");
		var closeBtn = CreateGameButton("关闭(ESC)", "Close");
		_topBar.AddChild(pickBtn);
		_topBar.AddChild(resetBtn);
		_topBar.AddChild(saveBtn);
		_topBar.AddChild(closeBtn);

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

		var originalTitle = new Label { Text = "原版卡图（图鉴样式）" };
		ApplyGameLabelStyle(originalTitle, 22);
		left.AddChild(originalTitle);

		_originalCardPreview = CreateCardPreview();
		left.AddChild(WrapCardPreview(_originalCardPreview));

		var overrideTitle = new Label { Text = "替换预览（图鉴样式）" };
		ApplyGameLabelStyle(overrideTitle, 22);
		right.AddChild(overrideTitle);

		_overrideCardPreview = CreateCardPreview();
		right.AddChild(WrapCardPreview(_overrideCardPreview));

		var workTitle = new Label { Text = "工作图（拖拽框选裁切）" };
		ApplyGameLabelStyle(workTitle, 22);
		right.AddChild(workTitle);

		var workPanel = CreateRoundedPanel();
		right.AddChild(workPanel);

		var workStack = new Control
		{
			Name = "WorkStack",
			CustomMinimumSize = new Vector2(0, 280),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		workStack.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		workStack.SetOffsetsPreset(Control.LayoutPreset.FullRect);
		workPanel.AddChild(workStack);

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
		ApplyGameLabelStyle(_status, 20);
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
		resetBtn.Pressed += ResetWorkImage;
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
		_workPreview.Texture = null;
		_cropOverlay.SetWorkImage(null);
		_originalCardPreview.Model = null;
		_overrideCardPreview.Model = null;

		if (string.IsNullOrWhiteSpace(_currentPool) || string.IsNullOrWhiteSpace(_currentCard))
			return;

		string pool = _currentPool!.ToLowerInvariant();
		string id = _currentCard!.ToLowerInvariant();
		CardModel? cardModel = ResolveCardModel(pool, id);
		if (cardModel == null)
		{
			SetStatus($"未找到卡牌模型: {id}", isError: true);
			return;
		}

		_originalCardPreview.Model = cardModel;
		_overrideCardPreview.Model = cardModel;
		// Force visuals update so text/description refreshes using current localization
		try
		{
			_originalCardPreview.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
			_overrideCardPreview.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
		}
		catch
		{
			// Ignore if methods unavailable in older engine builds
		}

		Texture2D? originalPortrait = LoadOriginalPortrait(cardModel);
		ApplyPortraitOverride(_originalCardPreview, originalPortrait);
		ApplyPortraitOverride(_overrideCardPreview, originalPortrait);

		if (TryLoadOverrideImage(pool, id, out Image? img))
		{
			_cropOverlay.SetWorkImage(img);
			var tex = ImageTexture.CreateFromImage(img);
			_workPreview.Texture = tex;
			ApplyPortraitOverride(_overrideCardPreview, tex);
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
			ApplyPortraitOverride(_overrideCardPreview, _workPreview.Texture as Texture2D);
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

	private void ResetWorkImage()
	{
		if (string.IsNullOrWhiteSpace(_currentPool) || string.IsNullOrWhiteSpace(_currentCard))
		{
			SetStatus("请先选择卡牌。", isError: true);
			return;
		}

		_cropOverlay.ClearSelection();
		_workPreview.Texture = null;
		_cropOverlay.SetWorkImage(null);
		ApplyPortraitOverride(_overrideCardPreview, LoadOriginalPortrait(_overrideCardPreview.Model));
		SetStatus("已重置为原版预览。", isError: false);
	}

	private static CardModel? ResolveCardModel(string pool, string id)
	{
		string entry = id.Trim();
		string poolKey = pool.Trim();
		CardModel? match = ModelDb.AllCards.FirstOrDefault((CardModel c) =>
			string.Equals(c.Id.Entry, entry, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(c.Pool.Title, poolKey, StringComparison.OrdinalIgnoreCase));
		return match ?? ModelDb.AllCards.FirstOrDefault((CardModel c) =>
			string.Equals(c.Id.Entry, entry, StringComparison.OrdinalIgnoreCase));
	}

	private static Vector2 GetPanelSize(Vector2 viewportSize)
	{
		float margin = 60f;
		float maxWidth = MathF.Max(0f, viewportSize.X - margin);
		float maxHeight = MathF.Max(0f, viewportSize.Y - margin);
		float width = MathF.Min(1540f, maxWidth);
		float height = MathF.Min(940f, maxHeight);
		if (width <= 0f)
			width = viewportSize.X;
		if (height <= 0f)
			height = viewportSize.Y;
		return new Vector2(width, height);
	}

	private void UpdateLayout()
	{
		if (_panel == null || _topBar == null)
			return;
		_panel.Size = GetPanelSize(GetViewport().GetVisibleRect().Size);
		// _panel.Position = -_panel.Size / 2;
        _panel.Position = new Vector2(200, 50);
		float topWidth = MathF.Max(0f, _panel.Size.X - 40f);
		_topBar.CustomMinimumSize = new Vector2(topWidth, 0f);
	}

	private static Texture2D? LoadOriginalPortrait(CardModel? model)
	{
		if (model == null)
			return null;
		string path = model.HasPortrait ? model.PortraitPath : CardModel.MissingPortraitPath;
		return ResourceLoader.Load<Texture2D>(path);
	}

	private static void ApplyPortraitOverride(NCard card, Texture2D? portrait)
	{
		if (portrait == null)
			return;
		var standard = card.GetNodeOrNull<TextureRect>("%Portrait");
		if (standard != null)
			standard.Texture = portrait;
		var ancient = card.GetNodeOrNull<TextureRect>("%AncientPortrait");
		if (ancient != null)
			ancient.Texture = portrait;
	}

	private static bool TryLoadOverrideImage(string pool, string id, out Image? image)
	{
		image = null;
		string overrideVirtual = $"{OverrideRootVirtual}/{pool}/{id}.png";
		string overrideAbs = ProjectSettings.GlobalizePath(overrideVirtual);
		if (!File.Exists(overrideAbs))
			return false;

		var img = new Image();
		var err = img.Load(overrideAbs);
		if (err != Error.Ok)
			return false;
		image = img;
		return true;
	}

	private static Control WrapCardPreview(NCard card)
	{
		var panel = CreateRoundedPanel();
		var center = new CenterContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		card.Scale = Vector2.One * 1.6f;
		card.MouseFilter = Control.MouseFilterEnum.Ignore;
		center.AddChild(card);
		panel.AddChild(center);
		return panel;
	}

	private static PanelContainer CreateRoundedPanel()
	{
		var panel = new PanelContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0f, 0f, 0f, 0.35f),
			CornerRadiusTopLeft = 16,
			CornerRadiusTopRight = 16,
			CornerRadiusBottomLeft = 16,
			CornerRadiusBottomRight = 16
		};
		style.ContentMarginLeft = 12;
		style.ContentMarginRight = 12;
		style.ContentMarginTop = 12;
		style.ContentMarginBottom = 12;
		panel.AddThemeStyleboxOverride("panel", style);
		return panel;
	}

	private static NCard CreateCardPreview()
	{
		var scene = GD.Load<PackedScene>("res://scenes/cards/card.tscn");
		return scene.Instantiate<NCard>();
	}

	private static TextureButton CreateGameButton(string text, string name)
	{
		var button = new TextureButton
		{
			Name = name,
			FocusMode = Control.FocusModeEnum.All,
			CustomMinimumSize = new Vector2(180, 44)
		};

		var normalTex = GD.Load<Texture2D>("res://asset/image/button.png");
		var outlineTex = GD.Load<Texture2D>("res://asset/image/button_outline.png");
		if (normalTex != null)
		{
			button.TextureNormal = normalTex;
		}
		if (outlineTex != null)
		{
			button.TextureHover = outlineTex;
			button.TexturePressed = outlineTex;
		}

		var label = new Label
		{
			Text = text,
			FocusMode = Control.FocusModeEnum.None,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		label.SetOffsetsPreset(Control.LayoutPreset.FullRect);
		ApplyGameLabelStyle(label, 22);
		button.AddChild(label);
		return button;
	}

	private static void ApplyGameLabelStyle(Label label, int fontSize)
	{
		label.ApplyLocaleFontSubstitution(FontType.Regular, "font");
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", StsColors.cream);
		label.AddThemeColorOverride("font_outline_color", StsColors.cardTitleOutlineCommon);
		label.AddThemeConstantOverride("outline_size", 6);
	}

	private static void ApplyGameOptionStyle(OptionButton option, int fontSize)
	{
		option.AddThemeFontSizeOverride("font_size", fontSize);
		option.AddThemeColorOverride("font_color", StsColors.cream);
		option.AddThemeColorOverride("font_outline_color", StsColors.cardTitleOutlineCommon);
		option.AddThemeConstantOverride("outline_size", 6);
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
