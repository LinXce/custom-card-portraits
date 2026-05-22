using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
	private string? _pendingPool;
	private string? _pendingCard;
	private bool _pendingResetToOriginal;
	private bool _openOnReady;
	private Tween? _openTween;
	private Tween? _closeTween;

	public static void ShowEditor(SceneTree tree)
	{
		ShowEditor(tree, null);
	}

	public static void ShowEditor(SceneTree tree, CardModel? selectedCard)
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
				_pendingPool = selectedCard?.Pool?.Title,
				_pendingCard = selectedCard?.Id?.Entry,
			};
			tree.Root.AddChild(existing);
			return;
		}

		if (existing.IsNodeReady())
			existing.Open(selectedCard);
		else
		{
			existing._pendingPool = selectedCard?.Pool?.Title;
			existing._pendingCard = selectedCard?.Id?.Entry;
			existing._openOnReady = true;
		}
	}

	public override void _Ready()
	{
		BuildUi();
		UpdateLayout();
		GetViewport().Connect(Viewport.SignalName.SizeChanged, Callable.From(UpdateLayout));
		ReloadAtlasIndex();
		SetStatus("选择卡牌 → 选择图片 → 拖拽框选裁切 → 保存（ESC 关闭、右键清除选区）", isError: false);
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

	private void Open(CardModel? selectedCard = null)
	{
		_closeTween?.Kill();
		if (selectedCard != null)
		{
			_pendingPool = selectedCard.Pool?.Title;
			_pendingCard = selectedCard.Id?.Entry;
		}

		Visible = true;
		_root.Visible = true;
		UpdateLayout();

		if (!TryApplyPendingSelection())
			RefreshPreviews();

		StartOpenAnimation();
		(_cardSelect ?? _poolSelect)?.GrabFocus();
	}

	private void Close()
	{
		_openTween?.Kill();
		if (!Visible || !_root.Visible)
		{
			_closeTween?.Kill();
			_root.Visible = false;
			Visible = false;
			return;
		}

		_closeTween?.Kill();
		Vector2 targetPosition = _panel.Position + Vector2.Up * 36f;
		_closeTween = CreateTween().SetParallel();
		_closeTween.TweenProperty(_panel, "position", targetPosition, 0.2f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		_closeTween.TweenProperty(_panel, "modulate:a", 0f, 0.16f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
		_closeTween.Chain().TweenCallback(Callable.From(delegate
		{
			_root.Visible = false;
			Visible = false;
			_panel.Modulate = Colors.White;
		}));
	}

	private void StartOpenAnimation()
	{
		_openTween?.Kill();
		Vector2 targetPosition = _panel.Position;
		_panel.Position = targetPosition + Vector2.Up * 36f;
		_panel.Modulate = new Color(1f, 1f, 1f, 0f);
		_openTween = CreateTween().SetParallel();
		_openTween.TweenProperty(_panel, "position", targetPosition, 0.24f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		_openTween.TweenProperty(_panel, "modulate:a", 1f, 0.18f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
	}

	private bool TryApplyPendingSelection()
	{
		if (string.IsNullOrWhiteSpace(_pendingPool) || string.IsNullOrWhiteSpace(_pendingCard))
			return false;

		int poolIndex = FindOptionIndex(_poolSelect, _pendingPool);
		if (poolIndex < 0)
			return false;

		_pendingPool = _pendingPool.Trim();
		_pendingCard = _pendingCard.Trim();
		_poolSelect.Select(poolIndex);
		OnPoolSelected(poolIndex);

		int cardIndex = FindOptionIndex(_cardSelect, _pendingCard);
		if (cardIndex >= 0)
		{
			_cardSelect.Select(cardIndex);
			OnCardSelected(cardIndex);
		}

		_pendingPool = null;
		_pendingCard = null;
		return true;
	}

	private static int FindOptionIndex(OptionButton option, string value)
	{
		for (int i = 0; i < option.ItemCount; i++)
		{
			if (string.Equals(option.GetItemText(i), value, StringComparison.OrdinalIgnoreCase))
				return i;
		}
		return -1;
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
        var cropBtn = CreateGameButton("裁切", "Crop");
        var saveBtn = CreateGameButton("保存", "Save");
		var closeBtn = CreateGameButton("关闭(ESC)", "Close");
		_topBar.AddChild(pickBtn);
        _topBar.AddChild(resetBtn);
		_topBar.AddChild(cropBtn);
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

		var originalTitle = new Label { Text = "替换预览（图鉴样式）" };
		ApplyGameLabelStyle(originalTitle, 22);
		left.AddChild(originalTitle);

		_overrideCardPreview = CreateCardPreview();
		left.AddChild(WrapCardPreview(_overrideCardPreview, 1.2f));

		var overrideTitle = new Label { Text = "原版卡图（图鉴样式）" };
		ApplyGameLabelStyle(overrideTitle, 22);
		right.AddChild(overrideTitle);

		_originalCardPreview = CreateCardPreview();
		right.AddChild(WrapCardPreview(_originalCardPreview, 0.8f));

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
		cropBtn.Pressed += CropWorkImage;
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

		// // Ensure custom preview scales are applied after visuals update
		// _overrideCardPreview.SetDeferred("scale", Vector2.One * 1.2f);
		// _originalCardPreview.SetDeferred("scale", Vector2.One * 0.8f);

		Texture2D? originalPortrait = LoadOriginalPortrait(cardModel);
		ApplyPortraitOverride(_originalCardPreview, originalPortrait);
		ApplyPortraitOverride(_overrideCardPreview, originalPortrait);

		if (ConfigStore.IsCardOverrideEnabled(pool, id) && TryLoadOverrideImage(pool, id, out Image? img))
		{
			_cropOverlay.SetWorkImage(img);
			var tex = ImageTexture.CreateFromImage(img);
			_workPreview.Texture = tex;
			ApplyPortraitOverride(_overrideCardPreview, tex);
		}
		_pendingResetToOriginal = false;
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
				_pendingResetToOriginal = false;
			SetStatus("已加载工作图：先裁切，再保存。", isError: false);
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

		string pool = _currentPool!.ToLowerInvariant();
		string id = _currentCard!.ToLowerInvariant();

		if (_pendingResetToOriginal)
		{
			ConfigStore.SetCardOverrideEnabled(pool, id, false);
			RefreshVisibleCardViews(GetTree());
			_pendingResetToOriginal = false;
			SetStatus("已保存为原版状态，并关闭该卡的替换。", isError: false);
			return;
		}

		Image? work = _cropOverlay.GetWorkImage();
		if (work == null)
		{
			SetStatus("没有工作图。请先“选择图片…”。", isError: true);
			return;
		}

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

			_applySavedTextureToPreview(toSave);
			ConfigStore.SetCardOverrideEnabled(pool, id, true);
			_pendingResetToOriginal = false;
			RefreshVisibleCardViews(GetTree());

			SetStatus($"已保存: {outVirtual}", isError: false);
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

		_pendingResetToOriginal = true;
		ApplyPortraitOverride(_overrideCardPreview, LoadOriginalPortrait(_overrideCardPreview.Model));
		SetStatus("已切回原版预览，保存后会关闭该卡的替换。", isError: false);
	}

	private void CropWorkImage()
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

		Rect2I? crop = _cropOverlay.GetCropRect();
		if (!crop.HasValue)
		{
			SetStatus("请先框选一个裁剪区域。", isError: true);
			return;
		}

		Rect2I r = ClampRect(crop.Value, work.GetWidth(), work.GetHeight());
		if (r.Size.X <= 0 || r.Size.Y <= 0)
		{
			SetStatus("裁剪区域无效。", isError: true);
			return;
		}

		Image cropped = work.GetRegion(r);
		ApplyPortraitOverride(_overrideCardPreview, ImageTexture.CreateFromImage(cropped));
		_pendingResetToOriginal = false;
		SetStatus("已应用当前裁切预览，可继续调整后再保存。", isError: false);
	}

	private void _applySavedTextureToPreview(Image savedImage)
	{
		ApplyPortraitOverride(_overrideCardPreview, ImageTexture.CreateFromImage(savedImage));
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

	private static void RefreshVisibleCardViews(SceneTree tree)
	{
		if (tree?.Root == null)
			return;

		RefreshVisibleCardViewsRecursive(tree.Root);
	}

	private static void RefreshVisibleCardViewsRecursive(Node node)
	{
		if (node is NCard card && card.IsNodeReady())
		{
			var reloadMethod = typeof(NCard).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic);
			reloadMethod?.Invoke(card, Array.Empty<object>());
		}

		foreach (Node child in node.GetChildren())
			RefreshVisibleCardViewsRecursive(child);
	}

}
